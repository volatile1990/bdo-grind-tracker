using System.Text.RegularExpressions;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal static class HermesiaMessages
{
    internal static string Recognize(Mat pixels, CompanionWindowsOcrRecognizer engine)
    {
        var lines = new List<string> { engine.Recognize(pixels).Text };
        using var gray = new Mat();
        Cv2.CvtColor(pixels, gray, ColorConversionCodes.BGR2GRAY);
        using var enlarged = new Mat();
        var scale = Math.Min(2.5, 2400d / pixels.Width);
        Cv2.Resize(gray, enlarged, new OpenCvSharp.Size(), scale, scale, InterpolationFlags.Cubic);
        // Independent overlapping strips keep the red boss dialogue from causing
        // Windows OCR to omit the gold system message immediately beneath it.
        for (var row = 0; row < 3; row++)
        {
            var y = row * enlarged.Height / 4;
            using var strip = new Mat(enlarged, new Rect(0, y, enlarged.Width, enlarged.Height / 2));
            using var binary = new Mat();
            Cv2.Threshold(strip, binary, 145, 255, ThresholdTypes.Binary);
            lines.Add(engine.Recognize(binary).Text);
        }
        return string.Join("\n", lines);
    }
    internal static readonly (string Kind, string Label, string Phrase)[] Definitions = [
        ("offer", "Opfergabe angeordnet", "overseer orders the black crystals"),
        ("porter", "Träger-Spawn", "porters gather to offer"),
        ("drakania", "Drakania-Spawn", "who dares interferes with our work"),
        ("drakania-kill", "Drakania besiegt", "f father"),
        ("transfer", "Minenrechte übertragen", "authority over two mines transferred"),
        ("mine-enter", "Mine betreten", "quarry management authority confirmed"),
        ("mine-second", "Zweite Minenphase", "quarry quota was not met"),
        ("mine-cleared", "Mine abgeschlossen", "work in the mine is suspended"),
        ("dragon", "Drachen-Spawn", "patrol descends"),
        ("afk", "AFK-Beginn", "begins absorbing nearby black crystals")
    ];

    internal static IReadOnlyList<(string Kind, string Label)> Parse(string text)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        return Definitions.Where(d => normalized.Contains(d.Phrase, StringComparison.Ordinal))
            .Select(d => (d.Kind, d.Label)).ToArray();
    }
}

/// <summary>Two-read confirmation and absence rearming for the 6.5-second banners.</summary>
internal sealed class HermesiaMessageGate
{
    private readonly Dictionary<string, (DateTimeOffset First, DateTimeOffset Last, bool Emitted)> _seen = [];
    private readonly Dictionary<string, DateTimeOffset> _emitted = [];
    internal void Reset() { _seen.Clear(); _emitted.Clear(); }
    internal IReadOnlyList<(string Kind, string Label, DateTimeOffset At)> Read(string text, DateTimeOffset at)
    {
        List<(string Kind, string Label, DateTimeOffset At)> events = [];
        foreach (var (kind, label) in HermesiaMessages.Parse(text))
        {
            var previous = _seen.GetValueOrDefault(kind);
            if (previous == default || at - previous.Last > TimeSpan.FromSeconds(2))
                _seen[kind] = (at, at, false);
            else
            {
                if (!previous.Emitted && at > previous.First &&
                    (!_emitted.TryGetValue(kind, out var emitted) || previous.First - emitted >= TimeSpan.FromSeconds(8)))
                { events.Add((kind, label, previous.First)); _emitted[kind] = previous.First; }
                _seen[kind] = (previous.First, at, true);
            }
        }
        return events;
    }
}

/// <summary>Searches cached samples only when a regular probe finds a new banner.</summary>
internal sealed class HermesiaBufferedSearch
{
    private readonly Dictionary<string, (DateTimeOffset First, DateTimeOffset Last)> _emitted = [];

    internal IReadOnlyList<(string Kind, string Label, DateTimeOffset At)> Read(
        IReadOnlyList<DateTimeOffset> times, Func<int, string> text)
    {
        List<(string Kind, string Label, DateTimeOffset At)> result = [];
        if (times.Count == 0) return result;
        var now = times[^1];
        foreach (var (kind, label) in HermesiaMessages.Parse(text(times.Count - 1)))
        {
            if (_emitted.TryGetValue(kind, out var previous) &&
                (now - previous.First < TimeSpan.FromSeconds(8) || now - previous.Last <= TimeSpan.FromSeconds(4)))
            {
                _emitted[kind] = (previous.First, now);
                continue;
            }
            var first = now;
            var matches = 1;
            var misses = 0;
            for (var i = times.Count - 2; i >= 0; i--)
            {
                // A capture gap must never join separate occurrences.
                if (times[i + 1] - times[i] > TimeSpan.FromSeconds(2)) break;
                if (HermesiaMessages.Parse(text(i)).Any(e => e.Kind == kind))
                { first = times[i]; matches++; misses = 0; }
                else if (++misses >= 2) break;
            }
            if (matches < 2) continue;
            if (previous != default && first - previous.First < TimeSpan.FromSeconds(8))
            { _emitted[kind] = (previous.First, now); continue; }
            _emitted[kind] = (first, now);
            result.Add((kind, label, first));
        }
        return result.OrderBy(e => e.At).ToArray();
    }
}

/// <summary>Buffers only rotation crops; OCR never blocks the loot capture pipeline.</summary>
internal sealed class HermesiaRotationMonitor : IRotationProfileMonitor
{
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly HermesiaRotationTracker _tracker;
    private readonly Func<Bitmap, string>? _recognize;
    private readonly List<Sample> _buffer = [];
    private HermesiaBufferedSearch _search = new();
    private CompanionWindowsOcrRecognizer? _ocr;
    private DateTimeOffset? _lastFrame, _lastSample, _lastProbe;
    private long _epoch;
    private bool _busy, _disposed;
    private string? _error;

    private sealed class Sample(Bitmap pixels, DateTimeOffset at)
    {
        private int _references = 1;
        internal Bitmap Pixels { get; } = pixels;
        internal DateTimeOffset At { get; } = at;
        internal string? Text { get; set; }
        internal void Retain() => Interlocked.Increment(ref _references);
        internal void Release() { if (Interlocked.Decrement(ref _references) == 0) Pixels.Dispose(); }
    }

    internal HermesiaRotationMonitor(string? path = null, Func<Bitmap, string>? recognize = null)
    { _tracker = new(path); _recognize = recognize; }
    public RotationMonitorSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_lastFrame is { } last && now - last > TimeSpan.FromSeconds(4))
                InterruptCore("Bildsignal unterbrochen · warte auf erstes Ereignis");
            var snapshot = _tracker.Snapshot(now);
            return snapshot with { Error = _error ?? snapshot.Error };
        }
    }
    public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
    { lock (_sync) return _tracker.DrainCompleted(); }
    public void Interrupt(string status = "Tracking pausiert · warte auf erstes Ereignis")
    { lock (_sync) InterruptCore(status); }
    private void InterruptCore(string status)
    {
        _epoch++; _tracker.Interrupt(status); _search = new();
        _lastFrame = _lastSample = _lastProbe = null;
        foreach (var sample in _buffer) sample.Release();
        _buffer.Clear();
    }

    public void Observe(Bitmap frame, DateTimeOffset at)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (_lastFrame is { } last && at - last > TimeSpan.FromSeconds(4))
                InterruptCore("Bildsignal unterbrochen · warte auf erstes Ereignis");
            _lastFrame = at;
            if (_lastSample is { } sampled && at - sampled < SampleInterval) return;
            var region = new Rectangle(frame.Width / 4, (int)(frame.Height * .54), frame.Width / 2, (int)(frame.Height * .16));
            if (region.Width < 32 || region.Height < 16) return;
            _buffer.Add(new Sample(frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb), at));
            _lastSample = at;
            while (_buffer.Count > 21 || at - _buffer[0].At > TimeSpan.FromSeconds(10))
            { _buffer[0].Release(); _buffer.RemoveAt(0); }
            if (_busy || _lastProbe is { } probe && at - probe < ProbeInterval) return;
            _lastProbe = at;
            var samples = _buffer.ToArray();
            foreach (var sample in samples) sample.Retain();
            var epoch = _epoch;
            var search = _search;
            _busy = true;
            _ = Task.Run(() =>
            {
                try
                {
                    string Read(int index)
                    {
                        var sample = samples[index];
                        if (sample.Text is not null) return sample.Text;
                        if (_recognize is not null) return sample.Text = _recognize(sample.Pixels);
                        _ocr ??= CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true);
                        if (_ocr is null) throw new InvalidOperationException("Englische Windows-Texterkennung fehlt.");
                        using var pixels = CompanionFrameDecoder.Decode(sample.Pixels);
                        return sample.Text = HermesiaMessages.Recognize(pixels, _ocr);
                    }
                    var events = search.Read(samples.Select(s => s.At).ToArray(), Read);
                    lock (_sync)
                    {
                        if (_disposed || epoch != _epoch) return;
                        foreach (var e in events) _tracker.Observe(e.Kind, e.Label, e.At);
                        _error = null;
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    lock (_sync)
                    {
                        if (!_disposed && epoch == _epoch)
                        { InterruptCore("Erkennung unterbrochen · warte auf erstes Ereignis"); _error = "Rotation: " + e.Message; }
                    }
                }
                finally
                {
                    foreach (var sample in samples) sample.Release();
                    lock (_sync) _busy = false;
                }
            });
        }
    }
    public void Dispose()
    { lock (_sync) { _disposed = true; InterruptCore("Rotation Monitor beendet"); } }
}
