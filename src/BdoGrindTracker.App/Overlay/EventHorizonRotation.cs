using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

/// <summary>
/// Event Horizon: a rotation starts with the first loot after the AFK end, clears three wormholes (each ending with
/// the removal-halted banner and, rarely, a debris mini AFK), spawns the boss after the third and ends with the
/// timeline reset after the AFK phase.
/// </summary>
internal sealed class EventHorizonRotationTracker : IRotationEventTracker
{
    internal const int Wormholes = 3;
    // Per wormhole every banner counts once, even when a teleport black screen splits it into two sightings.
    private static readonly string[] WormholeMessages = ["halted", "reception", "debris", "distortion", "spacetime", "expansion"];
    private readonly List<RotationEvent> _events = [];
    private readonly List<RotationRun> _runs = [];
    private readonly List<(DateTimeOffset StartedAt, RotationRun Run)> _completed = [];
    private readonly string? _path;
    private DateTimeOffset? _start;
    private DateTimeOffset? _lastBoundary;
    private double _finishedElapsed;
    private bool _afk;
    // Tracking can begin mid-rotation; such a partial run waits for the next AFK end.
    private bool _discarded;
    private string? _error;
    private string _status = WaitingForLoot;
    private const string WaitingForLoot = "Warte auf den ersten Loot";

    internal EventHorizonRotationTracker(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("Rotationsdatei zu groß.");
            _runs = (JsonSerializer.Deserialize<List<RotationRun>>(File.ReadAllText(path)) ?? []).Where(Valid).Take(1200).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        { _error = "Bestzeiten konnten nicht geladen werden: " + e.Message; }
    }

    internal static string DefaultPath => Path.Combine(AppDataPaths.Current.BaseDirectory, "event-horizon-rotations.json");

    public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
    {
        var result = _completed.ToArray();
        _completed.Clear();
        return result;
    }

    // The rotation's banners in their order. A covered or cut banner may be missing, but never out of order.
    private static readonly string[] MessageOrder =
        ["anomaly", "halted", "expansion", "anomaly", "halted", "expansion", "anomaly", "halted", "boss", "boss-kill"];

    /// <summary>
    /// A complete rotation: all three wormhole starts, the boss kill and the AFK end, with every recognized
    /// wormhole, activation and boss banner in its place. Only these define the timing; any of the others can be
    /// hidden by a window or a black screen without shortening the measured time.
    /// </summary>
    internal static bool Valid(RotationRun run)
    {
        if (run?.Events is not { Count: > 2 and < 1000 } events || !double.IsFinite(run.Duration) || run.Duration is <= 0 or >= 7200 ||
            events.Any(e => e is null || string.IsNullOrWhiteSpace(e.Kind) || string.IsNullOrWhiteSpace(e.Label) ||
                !double.IsFinite(e.Seconds) || e.Seconds < 0 || e.Seconds > run.Duration) ||
            events[0].Kind != "start" || events[0].Seconds != 0 || events[^1].Kind != "end" || events[^1].Seconds != run.Duration ||
            events.Select(e => e.Key).Distinct().Count() != events.Count ||
            events.Zip(events.Skip(1)).Any(pair => pair.First.Seconds > pair.Second.Seconds))
            return false;
        if (events.Count(e => e.Kind == "anomaly") != Wormholes || events.Count(e => e.Kind == "boss-kill") != 1) return false;
        var position = 0;
        foreach (var kind in events.Select(e => e.Kind).Where(MessageOrder.Contains))
        {
            while (position < MessageOrder.Length && MessageOrder[position] != kind) position++;
            if (position++ >= MessageOrder.Length) return false;
        }
        return true;
    }

    private static IEnumerable<RotationEvent> InWormhole(IReadOnlyList<RotationEvent> events, int wormhole)
    {
        var index = 0;
        foreach (var e in events)
        {
            if (e.Kind == "anomaly") index++;
            else if (e.Kind == "boss") index = Wormholes + 1;
            if (index == wormhole) yield return e;
        }
    }

    public void Interrupt(string status = WaitingForLoot)
    {
        _start = _lastBoundary = null; _finishedElapsed = 0; _events.Clear(); _afk = false; _discarded = false;
        _status = status;
    }

    public bool ObserveLoot(DateTimeOffset at)
    {
        if (_start is not null || _lastBoundary is { } boundary && at < boundary) return false;
        _start = at; _finishedElapsed = 0; _events.Clear(); _afk = false; _discarded = false;
        Add("start", "Rotationsstart", 0);
        _status = "Erstes Pack · warte auf Wurmloch 1";
        return true;
    }

    public void Observe(string kind, string label, DateTimeOffset at)
    {
        if (_lastBoundary is { } boundary && at < boundary) return;
        if (kind == "end")
        {
            // A rotation cannot end before its first wormhole: a late sighting of the previous reset banner.
            if (_start is not null && !_discarded && !_events.Any(e => e.Kind == "anomaly")) return;
            Finish(label, at);
            return;
        }
        if (_start is not { } start || _discarded) return;
        var seconds = (at - start).TotalSeconds;
        if (seconds < 0) return;
        if (seconds > 7200) { Interrupt(); return; }
        var wormhole = _events.Count(e => e.Kind == "anomaly");
        // The first banner after a loot start must open wormhole 1; anything else means tracking began mid-rotation.
        if (wormhole == 0 && kind != "anomaly")
        {
            _discarded = true;
            _status = "Mitten in der Rotation begonnen · warte auf das AFK-Ende";
            return;
        }
        if (kind == "anomaly" && (wormhole >= Wormholes || _events.Any(e => e.Kind == "anomaly" && seconds - e.Seconds < 60))) return;
        if (WormholeMessages.Contains(kind) && InWormhole(_events, wormhole).Any(e => e.Kind == kind)) return;
        // The mini AFK exists only after falling debris.
        if (kind is "distortion" or "spacetime" && !InWormhole(_events, wormhole).Any(e => e.Kind == "debris")) return;
        if (kind is "boss" or "boss-kill" && _events.Any(e => e.Kind == kind)) return;
        Add(kind, label, seconds);
        if (kind == "boss-kill") _afk = true;
        _status = Status();
    }

    private void Finish(string label, DateTimeOffset at)
    {
        if (_start is { } start && !_discarded)
        {
            var duration = Math.Max(0, (at - start).TotalSeconds);
            _events.RemoveAll(e => e.Seconds > duration);
            Add("end", label, duration);
            var run = new RotationRun(duration, _events.ToArray()) { TimingVersion = 2 };
            var valid = Valid(run);
            if (valid) { _completed.Add((start, run)); _runs.Add(run); Save(); }
            _finishedElapsed = duration;
            _status = valid ? "AFK beendet · nächste Rotation startet mit dem nächsten Loot"
                : "AFK beendet · Rotation unvollständig erkannt, nicht gezählt · warte auf den nächsten Loot";
        }
        // A partial run, or tracking that began during the AFK phase, now waits for a clean start.
        else if (_discarded || _events.Count == 0)
        {
            _events.Clear(); _finishedElapsed = 0;
            _status = "AFK beendet · nächste Rotation startet mit dem nächsten Loot";
        }
        _start = null; _afk = false; _discarded = false;
        _lastBoundary = at;
    }

    private string Status()
    {
        var last = _events[^1];
        var wormhole = _events.Count(e => e.Kind == "anomaly");
        return last.Kind switch
        {
            "anomaly" => $"Wurmloch {wormhole} · Wellen",
            "halted" or "reception" when !InWormhole(_events, wormhole).Any(e => e.Kind == "debris") =>
                wormhole < Wormholes ? $"Wurmloch {wormhole} · Mobs bis zur Aktivierung" : "Wurmloch 3 · Mobs bis zum Boss",
            "debris" or "halted" => $"Wurmloch {wormhole} · Trümmer-AFK",
            "distortion" => $"Wurmloch {wormhole} · Trümmer-AFK · Hälfte",
            "spacetime" => wormhole < Wormholes ? $"Wurmloch {wormhole} · Mobs bis zur Aktivierung" : "Wurmloch 3 · Mobs bis zum Boss",
            "expansion" => $"Wurmloch {wormhole + 1} aktiviert",
            "boss" => "Boss",
            "boss-kill" => "AFK-Phase · Uhr läuft weiter",
            _ => last.Label,
        };
    }

    private void Add(string kind, string label, double seconds)
    {
        var index = _events.FindIndex(e => e.Seconds > seconds);
        _events.Insert(index < 0 ? _events.Count : index, new(kind, label, seconds));
        var occurrence = 0;
        for (var i = 0; i < _events.Count; i++)
            if (_events[i].Kind == kind) _events[i] = _events[i] with { Occurrence = ++occurrence };
    }

    public RotationMonitorSnapshot Snapshot(DateTimeOffset now)
    {
        var (best, ideal, sectors) = RotationComparison.Compare(_runs);
        return new() { Elapsed = _discarded ? 0 : _start is { } start ? Math.Max(0, (now - start).TotalSeconds) : _finishedElapsed,
            Synchronized = _start is not null && !_discarded, IsAfk = _afk, Events = _discarded ? [] : _events.ToArray(),
            Best = best, Ideal = ideal, SectorBests = sectors, Completed = _runs.Count, Status = _status, Error = _error };
    }

    private void Save()
    {
        if (_runs.Count > 200)
        {
            var keep = _runs.OrderBy(run => run.Duration).Take(200).ToHashSet();
            _runs.RemoveAll(run => !keep.Contains(run));
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
}
