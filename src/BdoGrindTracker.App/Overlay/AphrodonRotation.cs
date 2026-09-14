using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

internal sealed class AphrodonRotationTracker : IRotationEventTracker
{
    private int _smallCount;
    private readonly List<RotationEvent> _events = [];
    private readonly List<RotationRun> _runs;
    private readonly List<(DateTimeOffset StartedAt, RotationRun Run)> _completed = [];
    public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
    {
        var result = _completed.ToArray();
        _completed.Clear();
        return result;
    }
    private readonly string? _path;
    private DateTimeOffset? _start;
    private DateTimeOffset? _lastBoundary;
    private DateTimeOffset? _lastActivation;
    private double _finishedElapsed;
    private bool _afk;
    private string? _error;
    private string? _completionNotice;
    private string _status = "Warte auf erstes Ereignis";

    internal AphrodonRotationTracker(string? path = null)
    {
        _path = path;
        _runs = [];
        if (path is null || !File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("Rotationsdatei zu groß.");
            _runs = (JsonSerializer.Deserialize<List<RotationRun>>(File.ReadAllText(path)) ?? [])
                .Where(Valid).Take(1200).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { _error = "Bestzeiten konnten nicht geladen werden: " + e.Message; }
    }

    internal static string DefaultPath => Path.Combine(AppDataPaths.Current.BaseDirectory, "aphrodon-rotations.json");
    private static bool Valid(RotationRun run) => run is not null && double.IsFinite(run.Duration) && run.Duration is > 0 and < 7200 &&
        run.Events is { Count: >= 12 and < 100 } && run.Events.All(e => e is not null && !string.IsNullOrWhiteSpace(e.Kind) && !string.IsNullOrWhiteSpace(e.Label)) &&
        run.Events[0].Kind == "start" && run.Events[0].Seconds == 0 &&
        run.Events[^1].Kind == "end" && run.Events[^1].Seconds == run.Duration &&
        run.Events.Count(e => e.Kind is "hog" or "agris") == 9 &&
        run.Events.Count(e => e.Kind == "afk") == 1 &&
        run.Events.All(e => double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= run.Duration) &&
        run.Events.Select(e => e.Key).Distinct().Count() == run.Events.Count &&
        run.Events.Zip(run.Events.Skip(1)).All(p => p.First.Seconds <= p.Second.Seconds) &&
        run.Events.Where(e => e.Kind is "hog" or "agris" or "afk")
            .Select(e => e.Kind is "hog" or "agris" ? "wave" : e.Kind)
            .SequenceEqual(Enumerable.Repeat("wave", 9).Append("afk"));

    public void Interrupt(string status = "Warte auf Aphrodon-Startup")
    { _start = _lastBoundary = _lastActivation = null; _finishedElapsed = 0; _events.Clear(); _afk = false; _smallCount = 0; _status = status; }

    private void Begin(DateTimeOffset at)
    {
        _start = _lastBoundary = at; _finishedElapsed = 0; _events.Clear(); _afk = false;
        Add("start", "Rotationsstart", 0); _status = "Rotation · 0 / 9 Events";
    }

    public void Observe(string kind, string label, DateTimeOffset at)
    {
        if (_lastBoundary is { } boundary && at < boundary) return;
        if (kind == "setup") { Interrupt("Startup · 0 / 3 Vogelscheuchen"); _lastBoundary = at; return; }
        if (kind == "failure")
        {
            _smallCount = Math.Max(0, _smallCount - 1);
            _finishedElapsed = _start is { } started ? Math.Max(0, (at - started).TotalSeconds) : _finishedElapsed;
            if (_start is { } start) Add("failure", label, (at - start).TotalSeconds);
            _start = null; _afk = false; _lastBoundary = at;
            _status = $"Rotation fehlgeschlagen · {_smallCount} / 3 Vogelscheuchen aktiv";
            return;
        }
        if (kind == "small-scarecrow" && _start is null)
        {
            _smallCount = Math.Min(3, _smallCount + 1);
            _status = $"Aufbau · {_smallCount} / 3 Vogelscheuchen · warte auf Aktivierung";
            return;
        }
        if (kind == "restart")
        {
            // This banner explicitly confirms all three small scarecrows are ready.
            if (_lastActivation is { } activation && at - activation < TimeSpan.FromSeconds(8)) return;
            _lastActivation = at;
            _smallCount = 3; Begin(at);
            return;
        }
        if (kind == "end")
        {
            if (_start is { } start && _afk)
            {
                var duration = (at - start).TotalSeconds;
                var following = _events.Where(e => e.Seconds > duration)
                    .Select(e => (Event: e, At: start.AddSeconds(e.Seconds))).ToArray();
                _events.RemoveAll(e => e.Seconds > duration);
                Add("end", label, duration);
                var run = new RotationRun(duration, _events.ToArray()) { TimingVersion = 2 };
                if (Valid(run)) { _completionNotice = null; _completed.Add((start, run)); _runs.Add(run); Save(); }
                else
                {
                    _completionNotice = $"Letzte Rotation nicht übernommen: {run.Events.Count(e => e.Kind is "hog" or "agris")}/9 Hog-/Agris-Events; Ereignisfolge unvollständig.";
                    if (_path is not null)
                    {
                        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!); File.WriteAllText(_path + ".last-rejected.json", JsonSerializer.Serialize(run)); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        { _completionNotice += " Diagnose konnte nicht gespeichert werden."; }
                    }
                }
                _smallCount = 3; Begin(at);
                foreach (var next in following) Observe(next.Event.Kind, next.Event.Label, next.At);
            }
            else if (_start is null) { _smallCount = 3; Begin(at); }
            return;
        }
        if (_start is not { } currentStart || kind is not ("hog" or "agris" or "big-scarecrow" or "afk")) return;
        var seconds = (at - currentStart).TotalSeconds;
        if (seconds is < 0 or > 7200) { Interrupt(); return; }
        Add(kind, label, seconds);
        if (kind == "afk") _afk = true;
        var waves = _events.Count(e => e.Kind is "hog" or "agris");
        _status = _afk ? "AFK-Phase · Uhr läuft weiter" : $"Rotation · {waves} / 9 Events";
    }

    private void Add(string kind, string label, double seconds)
    {
        var index = _events.FindIndex(e => e.Seconds > seconds);
        _events.Insert(index < 0 ? _events.Count : index, new(kind, label, seconds));
        // Occurrence keys describe chronological order, including when OCR
        // confirms the first of two identical messages after the second.
        var occurrence = 0;
        for (var i = 0; i < _events.Count; i++)
            if (_events[i].Kind == kind) _events[i] = _events[i] with { Occurrence = ++occurrence };
    }

    public RotationMonitorSnapshot Snapshot(DateTimeOffset now)
    {
        var best = _runs.MinBy(run => run.Duration);
        var sectors = new Dictionary<string, double>();
        // Compare identical checkpoints only. Missing or extra OCR events never
        // shift a sector onto an unrelated mechanic.
        if (best is not null)
            foreach (var run in _runs.Where(run => Checkpoints(run).Select(e => e.Key).SequenceEqual(Checkpoints(best).Select(e => e.Key))))
            {
                var checkpoints = Checkpoints(run);
                for (var i = 1; i < checkpoints.Length; i++)
                {
                    var key = checkpoints[i].Key;
                    var duration = checkpoints[i].Seconds - checkpoints[i - 1].Seconds;
                    sectors[key] = Math.Min(sectors.GetValueOrDefault(key, double.MaxValue), duration);
                }
            }
        RotationRun? ideal = null;
        if (best is not null)
        {
            double total = 0;
            var checkpoints = Checkpoints(best);
            var idealTimes = checkpoints.ToDictionary(e => e.Key, e => e.Kind == "start" ? 0 : total += sectors[e.Key]);
            var events = best.Events.Select(e =>
            {
                if (idealTimes.TryGetValue(e.Key, out var time)) return e with { Seconds = time };
                var right = Array.FindIndex(checkpoints, p => p.Seconds >= e.Seconds);
                if (right <= 0) return e with { Seconds = 0 };
                var a = checkpoints[right - 1]; var b = checkpoints[right];
                var fraction = (e.Seconds - a.Seconds) / Math.Max(.001, b.Seconds - a.Seconds);
                return e with { Seconds = idealTimes[a.Key] + fraction * (idealTimes[b.Key] - idealTimes[a.Key]) };
            }).ToArray();
            ideal = new(total, events);
        }
        return new() { Elapsed = _start is { } start ? Math.Max(0, (now - start).TotalSeconds) : _finishedElapsed,
            SmallScarecrows = _smallCount, Synchronized = _start is not null, IsAfk = _afk, Events = _events.ToArray(), Best = best,
            Ideal = ideal, SectorBests = sectors, Completed = _runs.Count, Status = _status, Error = _error ?? _completionNotice };
    }

    private void Save()
    {
        // Keep sector-record donors even when their full rotation is slow.
        if (_runs.Count > 200)
        {
            var best = _runs.MinBy(r => r.Duration)!;
            var path = Checkpoints(best).Select(e => e.Key).ToArray();
            var compatible = _runs.Where(r => Checkpoints(r).Select(e => e.Key).SequenceEqual(path)).ToArray();
            var keep = _runs.OrderBy(r => r.Duration).Take(200).ToHashSet();
            for (var i = 1; i < path.Length; i++)
                keep.Add(compatible.MinBy(r => Checkpoints(r)[i].Seconds - Checkpoints(r)[i - 1].Seconds)!);
            _runs.RemoveAll(r => !keep.Contains(r));
        }
        if (_path is null || _error is not null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_runs));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { _error = "Bestzeit nicht gespeichert: " + e.Message; }
    }
    private static RotationEvent[] Checkpoints(RotationRun run) => run.Events.Where(RotationTimelinePresentation.IsCheckpoint).ToArray();
}
