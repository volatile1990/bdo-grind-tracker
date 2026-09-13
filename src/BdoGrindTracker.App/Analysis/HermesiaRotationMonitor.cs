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

/// <summary>Runs OCR on a cropped copy; never blocks the loot capture pipeline.</summary>
internal sealed class HermesiaRotationMonitor : IRotationProfileMonitor
{
    private readonly object _sync = new();
    private readonly HermesiaRotationTracker _tracker;
    private readonly HermesiaMessageGate _gate = new();
    private CompanionWindowsOcrRecognizer? _ocr;
    private DateTimeOffset? _lastScheduled;
    private long _epoch;
    private bool _busy, _disposed;
    private string? _error;

    internal HermesiaRotationMonitor(string? path = null) => _tracker = new(path);
    public RotationMonitorSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_lastScheduled is { } last && now - last > TimeSpan.FromSeconds(4))
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
    { _epoch++; _tracker.Interrupt(status); _gate.Reset(); _lastScheduled = null; }

    public void Observe(Bitmap frame, DateTimeOffset at)
    {
        lock (_sync)
        {
            if (_disposed || _busy || _lastScheduled is { } previous && at - previous < TimeSpan.FromMilliseconds(500)) return;
            if (_lastScheduled is { } last && at - last > TimeSpan.FromSeconds(4))
                InterruptCore("Erkennung unterbrochen · warte auf erstes Ereignis");
            _lastScheduled = at;
            // Covers both centered message rows in the supplied Hermesia recording.
            var region = new Rectangle(frame.Width / 4, (int)(frame.Height * .54), frame.Width / 2, (int)(frame.Height * .16));
            if (region.Width < 32 || region.Height < 16) return;
            var copy = frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var epoch = _epoch;
            _busy = true;
            _ = Task.Run(() =>
            {
                try
                {
                    using (copy)
                    using (var pixels = CompanionFrameDecoder.Decode(copy))
                    {
                        _ocr ??= CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true);
                        if (_ocr is null) throw new InvalidOperationException("Englische Windows-Texterkennung fehlt.");
                        var text = HermesiaMessages.Recognize(pixels, _ocr);
                        lock (_sync)
                        {
                            if (_disposed || epoch != _epoch) return;
                            foreach (var e in _gate.Read(text, at)) _tracker.Observe(e.Kind, e.Label, e.At);
                            _error = null;
                        }
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
                finally { lock (_sync) _busy = false; }
            });
        }
    }
    public void Dispose() { lock (_sync) { _disposed = true; _epoch++; } }
}
