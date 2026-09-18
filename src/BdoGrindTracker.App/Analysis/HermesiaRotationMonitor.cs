using System.Text.RegularExpressions;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal static class HermesiaMessages
{
    internal static string Recognize(Mat pixels, CompanionWindowsOcrRecognizer engine)
    {
        var raw = engine.Recognize(pixels).Text;
        // The crop holds only the banner stack, so one enlarged black-and-white pass
        // reads messages the raw pass misses under a bright background or boss dialogue.
        using var gray = new Mat();
        using var enlarged = new Mat();
        using var binary = new Mat();
        Cv2.CvtColor(pixels, gray, ColorConversionCodes.BGR2GRAY);
        var scale = Math.Min(2.5, 1300d / pixels.Width);
        Cv2.Resize(gray, enlarged, new OpenCvSharp.Size(), scale, scale, InterpolationFlags.Cubic);
        Cv2.Threshold(enlarged, binary, 145, 255, ThresholdTypes.Binary);
        return raw + "\n" + engine.Recognize(binary).Text;
    }
    // Short phrases: skill hints beside the banners can merge into a line's first word,
    // and the AFK banner's last word can touch the crop edge.
    internal static readonly (string Kind, string Label, string Phrase)[] Definitions = [
        ("offer", "Opfergabe angeordnet", "overseer orders the black"),
        ("porter", "Träger-Spawn", "porters gather to offer"),
        ("drakania", "Drakania-Spawn", "who dares interfere"),
        ("drakania-kill", "Drakania besiegt", "father"),
        ("transfer", "Minenrechte übertragen", "authority over two mines"),
        ("mine-enter", "Mine betreten", "quarry management authority"),
        ("mine-second", "Zweite Minenphase", "quarry quota was not met"),
        ("mine-cleared", "Mine abgeschlossen", "work in the mine is suspended"),
        ("dragon", "Drachen-Spawn", "patrol descends"),
        ("afk", "AFK-Beginn", "begins absorbing nearby"),
        // Either sentence of the two-part banner is enough when OCR garbles the other.
        ("failure", "Rotation Failed", "intruder alert in effect"),
        ("failure", "Rotation Failed", "valid authorization not confirmed")
    ];

    internal static IReadOnlyList<(string Kind, string Label)> Parse(string text)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        return Definitions.Where(d => normalized.Contains(d.Phrase, StringComparison.Ordinal))
            .Select(d => (d.Kind, d.Label)).Distinct().ToArray();
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
internal class BufferedRotationSearch(Func<string, IReadOnlyList<(string Kind, string Label)>> parse)
{
    private readonly Dictionary<string, (DateTimeOffset First, DateTimeOffset Last)> _emitted = [];

    internal IReadOnlyList<(string Kind, string Label, DateTimeOffset At)> Read(
        IReadOnlyList<DateTimeOffset> times, Func<int, string> text)
    {
        List<(string Kind, string Label, DateTimeOffset At)> result = [];
        if (times.Count == 0) return result;
        var now = times[^1];
        foreach (var (kind, label) in parse(text(times.Count - 1)))
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
                if (parse(text(i)).Any(e => e.Kind == kind))
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
internal class BufferedRotationProfileMonitor : IRotationProfileMonitor
{
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly IRotationEventTracker _tracker;
    private readonly RotationMessageProfile _profile;
    private readonly Func<Bitmap, string>? _recognize;
    private readonly List<Sample> _buffer = [];
    private BufferedRotationSearch _search;
    private CompanionWindowsOcrRecognizer? _ocr;
    private DateTimeOffset? _lastFrame, _lastSample, _lastProbe;
    private long _epoch;
    private bool _busy, _disposed;
    private int _flushes;
    private Task _pending = Task.CompletedTask;
    internal Task PendingAnalysis { get { lock (_sync) return _pending; } }
    private string? _error;
    private RotationDiagnosticRecording? _diagnostics;
    private string _diagnosticSpot = "";

    private sealed class Sample(Bitmap pixels, DateTimeOffset at, System.Drawing.Size frame, Rectangle region)
    {
        private int _references = 1;
        internal Bitmap Pixels { get; } = pixels;
        internal DateTimeOffset At { get; } = at;
        internal System.Drawing.Size Frame { get; } = frame;
        internal Rectangle Region { get; } = region;
        internal string? Text { get; set; }
        internal void Retain() => Interlocked.Increment(ref _references);
        internal void Release() { if (Interlocked.Decrement(ref _references) == 0) Pixels.Dispose(); }
    }

    internal BufferedRotationProfileMonitor(IRotationEventTracker tracker, RotationMessageProfile profile, Func<Bitmap, string>? recognize = null)
    { _tracker = tracker; _profile = profile; _search = new(profile.Parse); _recognize = recognize; }
    public RotationMonitorSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_flushes == 0 && _lastFrame is { } last && now - last > TimeSpan.FromSeconds(4))
                InterruptCore("Bildsignal unterbrochen · warte auf erstes Ereignis");
            var snapshot = _tracker.Snapshot(now);
            return snapshot with { Error = _error ?? snapshot.Error };
        }
    }
    public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
    { lock (_sync) return _tracker.DrainCompleted(); }
    public void Interrupt(string status = "Tracking pausiert · warte auf erstes Ereignis")
    { lock (_sync) InterruptCore(status); }
    public void AttachDiagnostics(RotationDiagnosticRecording? recording, string spotId)
    { lock (_sync) (_diagnostics, _diagnosticSpot) = (recording, spotId); }
    private void InterruptCore(string status)
    {
        // Every frame without HUD interrupts again; only the first one ends an observation.
        if (_lastFrame is { } observed) _diagnostics?.Note(_diagnosticSpot, observed, "interrupt", status);
        _epoch++; _tracker.Interrupt(status); _search = new(_profile.Parse);
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
            var region = _profile.Crop(frame.Width, frame.Height);
            if (region.Width < 32 || region.Height < 16) return;
            _buffer.Add(new Sample(frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb), at, frame.Size, region));
            _lastSample = at;
            while (_buffer.Count > 21 || at - _buffer[0].At > TimeSpan.FromSeconds(10))
            { _buffer[0].Release(); _buffer.RemoveAt(0); }
            if (_busy || _lastProbe is { } probe && at - probe < ProbeInterval) return;
            StartProbe();
        }
    }

    // Capture has stopped before this call. Let an in-flight confirmation land,
    // then inspect the last buffered frame even if the regular probe is not due.
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        Task pending;
        long epoch;
        lock (_sync)
        {
            if (_disposed) return;
            epoch = _epoch;
            pending = _pending;
            _flushes++;
        }
        try
        {
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_disposed || epoch != _epoch) return;
                if (!_busy && _buffer.Count > 0 && _lastProbe != _buffer[^1].At) StartProbe();
                pending = _pending;
            }
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { lock (_sync) _flushes--; }
    }

    // Called only while holding _sync, with a nonempty buffer and no worker.
    private void StartProbe()
    {
            _lastProbe = _buffer[^1].At;
            var samples = _buffer.ToArray();
            foreach (var sample in samples) sample.Retain();
            var epoch = _epoch;
            var search = _search;
            var (diagnostics, spot) = (_diagnostics, _diagnosticSpot);
            _busy = true;
            _pending = Task.Run(() =>
            {
                var probedAt = samples[^1].At;
                try
                {
                    diagnostics?.Probe(spot, probedAt, samples[^1].Pixels, samples[^1].Frame, samples[^1].Region);
                    string Read(int index)
                    {
                        var sample = samples[index];
                        if (sample.Text is not null) return sample.Text;
                        if (_recognize is not null) sample.Text = _recognize(sample.Pixels);
                        else
                        {
                            _ocr ??= CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true);
                            if (_ocr is null) throw new InvalidOperationException("Englische Windows-Texterkennung fehlt.");
                            using var pixels = CompanionFrameDecoder.Decode(sample.Pixels);
                            sample.Text = _profile.Recognize(pixels, _ocr);
                        }
                        diagnostics?.Read(spot, sample.At, sample.Text, _profile.Parse(sample.Text).Select(match => match.Kind));
                        return sample.Text;
                    }
                    var events = search.Read(samples.Select(s => s.At).ToArray(), Read);
                    lock (_sync)
                    {
                        if (_disposed || epoch != _epoch) return;
                        foreach (var e in events)
                        {
                            _tracker.Observe(e.Kind, e.Label, e.At);
                            diagnostics?.Event(spot, e.Kind, e.Label, e.At, probedAt);
                            diagnostics?.State(spot, probedAt, _tracker.Snapshot(probedAt));
                        }
                        _error = null;
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    lock (_sync)
                    {
                        if (!_disposed && epoch == _epoch)
                        {
                            diagnostics?.Note(spot, probedAt, "error", e.Message);
                            InterruptCore("Erkennung unterbrochen · warte auf erstes Ereignis"); _error = "Rotation: " + e.Message;
                        }
                    }
                }
                finally
                {
                    foreach (var sample in samples) sample.Release();
                    lock (_sync) _busy = false;
                }
            });
    }
    public void Dispose()
    { lock (_sync) { _disposed = true; InterruptCore("Rotation Monitor beendet"); } }
}

internal sealed class HermesiaBufferedSearch() : BufferedRotationSearch(HermesiaMessages.Parse);

internal sealed class HermesiaRotationMonitor(string? path = null, Func<Bitmap, string>? recognize = null)
    : BufferedRotationProfileMonitor(new HermesiaRotationTracker(path), RotationMessageProfile.Hermesia, recognize);
