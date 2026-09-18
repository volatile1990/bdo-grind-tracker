using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

public sealed record RotationEvent(string Kind, string Label, double Seconds, int Occurrence = 1)
{
    public string Key => Kind + ":" + Occurrence;
}
public sealed record RotationRun(double Duration, IReadOnlyList<RotationEvent> Events)
{
    public int TimingVersion { get; init; }
}
public sealed record SessionRotation(string SpotId, DateTimeOffset StartedAt, RotationRun Run);
/// <param name="Duration">Seconds from the rotation start to its end.</param>
/// <param name="WalkBack">Seconds from its end to the start of the next rotation; null until that start or after a break.</param>
public sealed record SessionRotationTiming(double Duration, double? WalkBack = null);
public sealed record RotationMonitorSnapshot
{
    public int? SmallScarecrows { get; init; }
    public string? SpotId { get; init; }
    public string SpotName { get; init; } = "Noch kein Spot erkannt";
    public bool HasProfile { get; init; }
    public double Elapsed { get; init; }
    public bool Synchronized { get; init; }
    public bool IsAfk { get; init; }
    public string Status { get; init; } = "Warte auf Rotationsstart";
    public IReadOnlyList<RotationEvent> Events { get; init; } = [];
    public RotationRun? Best { get; init; }
    public RotationRun? Ideal { get; init; }
    public IReadOnlyDictionary<string, double> SectorBests { get; init; } = new Dictionary<string, double>();
    public int Completed { get; init; }
    /// <summary>The rotations completed at this spot in the current session, oldest first.</summary>
    public IReadOnlyList<SessionRotationTiming> SessionRotations { get; init; } = [];
    public string? Error { get; init; }
}

/// <summary>Times the first event after grind start/AFK through the next AFK end.</summary>
internal sealed class HermesiaRotationTracker : IRotationEventTracker
{
    private static readonly string[] RequiredMechanics = ["drakania", "drakania-kill", "transfer", "mine-enter", "dragon", "afk"];
    // Drakania spawns after the fifth offering order of the startup.
    internal const int StartupOffers = 5;
    private readonly List<RotationEvent> _events = [];
    private readonly List<RotationRun> _runs;
    // Records saved before the startup check stay in the file but never serve as a reference.
    private readonly List<RotationRun> _incompleteStartups = [];
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
    private double _finishedElapsed;
    private bool _afk;
    private bool _awaitingOffer;
    private string? _error;
    private string _status = "Warte auf erstes Ereignis";
    private const string FailedStatus = "Rotation Failed · warte auf Opfergabe-Befehl";

    internal HermesiaRotationTracker(string? path = null)
    {
        _path = path;
        _runs = [];
        if (path is null || !File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("Rotationsdatei zu groß.");
            var runs = (JsonSerializer.Deserialize<List<RotationRun>>(File.ReadAllText(path)) ?? [])
                .Where(Valid).Select(FromFirstEvent).Where(Valid).Take(1200).ToLookup(HasCompleteStartup);
            _runs = runs[true].ToList();
            _incompleteStartups = runs[false].ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { _error = "Bestzeiten konnten nicht geladen werden: " + e.Message; }
    }

    internal static string DefaultPath => Path.Combine(AppDataPaths.Current.BaseDirectory, "hermesia-rotations.json");
    private static bool Valid(RotationRun run) => run is not null && double.IsFinite(run.Duration) && run.Duration is > 0 and < 7200 &&
        run.Events is { Count: > 2 and < 1000 } && run.Events.All(e => e is not null &&
            !string.IsNullOrWhiteSpace(e.Kind) && !string.IsNullOrWhiteSpace(e.Label) &&
            double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= run.Duration) &&
        run.Events[0].Kind == "start" && run.Events[0].Seconds == 0 && run.Events[^1].Kind == "end" &&
        RequiredMechanics.All(kind => run.Events.Any(e => e.Kind == kind)) &&
        run.Events[^1].Seconds == run.Duration && run.Events.Select(e => e.Key).Distinct().Count() == run.Events.Count &&
        run.Events.Zip(run.Events.Skip(1)).All(pair => pair.First.Seconds <= pair.Second.Seconds);

    /// <summary>Offering orders up to the first Drakania spawn, or all of them while Drakania has not spawned.</summary>
    internal static int StartupOfferCount(IReadOnlyList<RotationEvent> events)
    {
        var drakania = events.FirstOrDefault(e => e.Kind == "drakania");
        return events.Count(e => e.Kind == "offer" && (drakania is null || e.Seconds <= drakania.Seconds));
    }

    /// <summary>
    /// A rotation whose tracking began after part of the startup would look faster
    /// than it was, so it only counts with all offering orders before Drakania.
    /// </summary>
    internal static bool HasCompleteStartup(RotationRun run) =>
        run.Events.Any(e => e.Kind == "drakania") && StartupOfferCount(run.Events) >= StartupOffers;

    internal static RotationRun FromFirstEvent(RotationRun run)
    {
        if (run.TimingVersion >= 2) return run;
        var first = run.Events.FirstOrDefault(e => e.Kind != "start")?.Seconds ?? 0;
        return new(run.Duration - first, run.Events.Select(e => e with { Seconds = Math.Max(0, e.Seconds - first) }).ToArray()) { TimingVersion = 2 };
    }

    // A failed rotation stays failed in the game while tracking pauses or the image drops out.
    public void Interrupt(string status = "Warte auf erstes Ereignis")
    { _start = _lastBoundary = null; _finishedElapsed = 0; _events.Clear(); _afk = false; _status = _awaitingOffer ? FailedStatus : status; }

    public void Observe(string kind, string label, DateTimeOffset at)
    {
        if (_lastBoundary is { } boundary && at < boundary) return;
        if (kind == "failure")
        {
            // The intruder alert ends the attempt without a record. Only the
            // overseer's next offering order starts the following rotation.
            if (_start is { } failed)
            {
                _finishedElapsed = Math.Max(0, (at - failed).TotalSeconds);
                Add(kind, label, _finishedElapsed);
            }
            _start = null; _afk = false; _awaitingOffer = true;
            _lastBoundary = at;
            _status = FailedStatus;
            return;
        }
        if (_awaitingOffer)
        {
            if (kind != "offer") return;
            _awaitingOffer = false;
        }
        // The same suspension message means mine-cleared during combat and the
        // rotation boundary after crystal absorption. Text alone is insufficient.
        if (kind == "mine-cleared" && _afk && _start is { } currentStart &&
            _events.Any(e => e.Kind == "afk" && e.Seconds <= (at - currentStart).TotalSeconds))
        {
            List<(RotationEvent Event, DateTimeOffset At)> following = [];
            if (_start is { } start)
            {
                var duration = (at - start).TotalSeconds;
                // A later probe can confirm the AFK end after a new porter
                // banner. Keep that already observed event in the next run.
                following = _events.Where(e => e.Seconds > duration)
                    .Select(e => (e, start.AddSeconds(e.Seconds))).ToList();
                _events.RemoveAll(e => e.Seconds > duration);
                Add("end", "AFK-Ende", duration);
                var run = new RotationRun(duration, _events.ToArray()) { TimingVersion = 2 };
                if (Valid(run) && HasCompleteStartup(run)) { _completed.Add((start, run)); _runs.Add(run); Save(); }
            }
            _finishedElapsed = _start is { } started ? Math.Max(0, (at - started).TotalSeconds) : 0;
            _start = null; _afk = false;
            _lastBoundary = at;
            _status = DiscardsStartup(_events)
                ? $"AFK beendet · Startup unvollständig ({StartupOfferCount(_events)} / {StartupOffers} Opfergaben), Rotation nicht gezählt · warte auf erstes Ereignis"
                : "AFK beendet · warte auf erstes Ereignis";
            foreach (var next in following) Observe(next.Event.Kind, next.Event.Label, next.At);
            return;
        }
        if (kind == "afk") _afk = true;
        if (_start is null)
        {
            _start = at; _finishedElapsed = 0; _events.Clear();
            Add("start", "Rotationsstart", 0);
        }
        var seconds = (at - _start.Value).TotalSeconds;
        if (seconds < 0)
        {
            // Buffered confirmation can arrive in a later probe even though
            // its first visible frame predates an event already on the timeline.
            for (var i = 1; i < _events.Count; i++)
                _events[i] = _events[i] with { Seconds = _events[i].Seconds - seconds };
            _start = at;
            seconds = 0;
        }
        if (seconds > 7200 || _events.Any(e => e.Seconds > 7200)) { Interrupt(); return; }
        Add(kind, label, seconds);
        _status = _afk ? "AFK-Phase · Uhr läuft weiter" : _events[^1].Label;
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
        var (best, ideal, sectors) = RotationComparison.Compare(_runs);
        // An incomplete startup is dropped from the current timeline once Drakania shows it can no longer be completed.
        var drakania = _events.FindIndex(e => e.Kind == "drakania");
        var discarded = DiscardsStartup(_events);
        var status = _start is null ? _status
            : drakania < 0 ? $"Startup · {Math.Min(StartupOfferCount(_events), StartupOffers)} / {StartupOffers} Opfergaben"
            : discarded ? $"{_status} · Startup verworfen ({StartupOfferCount(_events)} / {StartupOffers} Opfergaben), zählt nicht als vollständige Rotation"
            : _status;
        return new() { Elapsed = _start is { } start ? Math.Max(0, (now - start).TotalSeconds) : _finishedElapsed,
            Synchronized = _start is not null, IsAfk = _afk, Events = (discarded ? _events.Skip(drakania) : _events).ToArray(), Best = best,
            Ideal = ideal, SectorBests = sectors, Completed = _runs.Count, Status = status, Error = _error };
    }

    private static bool DiscardsStartup(IReadOnlyList<RotationEvent> events) =>
        events.Any(e => e.Kind == "drakania") && StartupOfferCount(events) < StartupOffers;

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
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_runs.Concat(_incompleteStartups)));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { _error = "Bestzeit nicht gespeichert: " + e.Message; }
    }
    private static RotationEvent[] Checkpoints(RotationRun run) => run.Events.Where(RotationTimelinePresentation.IsCheckpoint).ToArray();
}

public static class RotationTimelinePresentation
{
    public static bool ShowSetup(RotationMonitorSnapshot state) => state.SpotId == BdoGrindTracker.Core.LootSpotCatalog.AphrodonId && !state.Synchronized;
    public const string SetupHint = "Rotation tracking startet, sobald alle 3 Scarecrows aufgestellt sind.";
    public static string SetupCount(RotationMonitorSnapshot state) => $"{Math.Clamp(state.SmallScarecrows ?? 0, 0, 3)}/3 Small Scarecrows spawned";
    // Event Horizon's banner variants and the mini-AFK midpoint mark a moment inside a phase, not its end.
    public static bool IsCheckpoint(RotationEvent e) =>
        e.Kind is not "porter" and not "offer" and not "big-scarecrow" and not "reception" and not "debris" and not "distortion";
    /// <summary>Short strokes above the band: pack spawns, wave events and moments inside a phase.</summary>
    public static bool IsMarker(RotationEvent e) => e.Kind is "porter" or "offer" or "hog" or "agris" or "failure" or "distortion";
    public static string Mark(RotationEvent e) => e.Kind switch {
        "drakania" => "D", "drakania-kill" => "D✓", "transfer" => "B", "mine-enter" => "M" + e.Occurrence,
        "mine-second" => "P2", "mine-cleared" => "M✓", "dragon" => "R", "afk" => "AFK", "end" => "E", _ => "" };
    public const string Legend = "D Drakania · B Buff · M Mine · P2 Phase 2 · R Drache · kleine Striche: Träger";
    public static string Sector(RotationMonitorSnapshot state)
    {
        var checkpoints = state.Events.Where(IsCheckpoint).ToArray();
        if (checkpoints.Length < 2) return "Bestabschnitt: noch keine Mechanik abgeschlossen";
        var current = checkpoints[^1];
        if (!state.SectorBests.TryGetValue(current.Key, out var best)) return "Bestabschnitt: noch keine passende Referenz";
        return "Abschnitt " + Time(current.Seconds - checkpoints[^2].Seconds) + " · Bestzeit " + Time(best);
    }
    public static RotationRun? Reference(RotationMonitorSnapshot state, string mode) => mode == "ideal" ? state.Ideal : state.Best;
    public static double Extent(RotationMonitorSnapshot state, string mode) => Math.Max(60, Math.Max(state.Elapsed + 10, Reference(state, mode)?.Duration ?? 620));
    public static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss\.f");
    public static string Mode(string mode) => mode switch { "ideal" => "Ideale Rotation", "sectors" => "Bestrotation + Mechanik-Bestzeiten", _ => "Beste vollständige Rotation" };
    public static string Delta(RotationMonitorSnapshot state, string mode)
    {
        var reference = Reference(state, mode);
        var current = state.Events.LastOrDefault(e => IsCheckpoint(e) && e.Kind != "start" && reference?.Events.Any(r => r.Key == e.Key) == true);
        if (current is null || reference is null) return "Noch keine vergleichbare Zwischenzeit";
        var difference = current.Seconds - reference.Events.First(e => e.Key == current.Key).Seconds;
        return current.Label + " · " + (difference >= 0 ? "+" : "−") + Math.Abs(difference).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }
}
