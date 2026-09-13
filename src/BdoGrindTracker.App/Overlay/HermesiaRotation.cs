using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

public sealed record RotationEvent(string Kind, string Label, double Seconds, int Occurrence = 1)
{
    public string Key => Kind + ":" + Occurrence;
}
public sealed record RotationRun(double Duration, IReadOnlyList<RotationEvent> Events);
public sealed record RotationMonitorSnapshot
{
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
    public string? Error { get; init; }
}

/// <summary>Only complete, continuously observed AFK-end to AFK-end runs earn records.</summary>
internal sealed class HermesiaRotationTracker
{
    private static readonly string[] RequiredMechanics = ["drakania", "drakania-kill", "transfer", "mine-enter", "dragon", "afk"];
    private readonly List<RotationEvent> _events = [];
    private readonly List<RotationRun> _runs;
    private readonly string? _path;
    private DateTimeOffset? _start;
    private bool _afk;
    private string? _error;
    private string _status = "Warte auf AFK-Ende · Hermesia";

    internal HermesiaRotationTracker(string? path = null)
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

    internal static string DefaultPath => Path.Combine(AppDataPaths.Current.BaseDirectory, "hermesia-rotations.json");
    private static bool Valid(RotationRun run) => run is not null && double.IsFinite(run.Duration) && run.Duration is > 0 and < 7200 &&
        run.Events is { Count: > 2 and < 1000 } && run.Events.All(e => e is not null &&
            !string.IsNullOrWhiteSpace(e.Kind) && !string.IsNullOrWhiteSpace(e.Label) &&
            double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= run.Duration) &&
        run.Events[0].Kind == "start" && run.Events[0].Seconds == 0 && run.Events[^1].Kind == "end" &&
        RequiredMechanics.All(kind => run.Events.Any(e => e.Kind == kind)) &&
        run.Events[^1].Seconds == run.Duration && run.Events.Select(e => e.Key).Distinct().Count() == run.Events.Count &&
        run.Events.Zip(run.Events.Skip(1)).All(pair => pair.First.Seconds <= pair.Second.Seconds);

    internal void Interrupt(string status = "Unterbrochen · warte auf nächstes AFK-Ende")
    { _start = null; _events.Clear(); _afk = false; _status = status; }

    internal void Observe(string kind, string label, DateTimeOffset at)
    {
        // The same suspension message means mine-cleared during combat and the
        // rotation boundary after crystal absorption. Text alone is insufficient.
        if (kind == "mine-cleared" && _afk)
        {
            if (_start is { } start && _events.Any(e => e.Kind == "dragon") && _events.Any(e => e.Kind == "afk"))
            {
                Add("end", "AFK-Ende", (at - start).TotalSeconds);
                var run = new RotationRun((at - start).TotalSeconds, _events.ToArray());
                if (Valid(run)) { _runs.Add(run); Save(); }
            }
            _start = at; _afk = false; _events.Clear();
            Add("start", "Rotationsstart", 0);
            _status = "Rotation läuft";
            return;
        }
        if (kind == "afk") _afk = true;
        if (_start is null) { _status = _afk ? "AFK erkannt · synchronisiere am Ende" : "Warte auf AFK-Ende · Hermesia"; return; }
        var seconds = (at - _start.Value).TotalSeconds;
        if (seconds < 0 || seconds > 7200) { Interrupt(); return; }
        Add(kind, label, seconds);
        _status = _afk ? "AFK-Phase · Uhr läuft weiter" : label;
    }

    private void Add(string kind, string label, double seconds) =>
        _events.Add(new(kind, label, seconds, _events.Count(e => e.Kind == kind) + 1));

    internal RotationMonitorSnapshot Snapshot(DateTimeOffset now)
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
        return new() { Elapsed = _start is { } start ? Math.Max(0, (now - start).TotalSeconds) : 0,
            Synchronized = _start is not null, IsAfk = _afk, Events = _events.ToArray(), Best = best,
            Ideal = ideal, SectorBests = sectors, Completed = _runs.Count, Status = _status, Error = _error };
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

public static class RotationTimelinePresentation
{
    public static bool IsCheckpoint(RotationEvent e) => e.Kind is not "porter" and not "offer";
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
