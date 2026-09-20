namespace BdoGrindTracker.App.Overlay;

public sealed record RotationEvent(string Kind, string Label, double Seconds, int Occurrence = 1)
{
    public string Key => Kind + ":" + Occurrence;
}
public sealed record RotationRun(double Duration, IReadOnlyList<RotationEvent> Events)
{
    public int TimingVersion { get; init; }
    public Guid Id { get; init; }
    public string Outcome { get; init; } = "complete";
    public string? Reason { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public bool LegacyImported { get; init; }
    public IReadOnlyList<RotationSection> Sections { get; init; } = [];
    public bool EligibleForStatistics => Outcome == "complete";
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
    public string TrackingState { get; init; } = "waiting";
    public string? CurrentPhaseId { get; init; }
    public string? LastInterruptionReason { get; init; }
    public DateTimeOffset? LootStartAllowedAt { get; init; }
    public IReadOnlyList<RotationEvent> Events { get; init; } = [];
    public RotationRun? Best { get; init; }
    public RotationRun? Ideal { get; init; }
    public IReadOnlyDictionary<string, double> SectorBests { get; init; } = new Dictionary<string, double>();
    public int Completed { get; init; }
    /// <summary>The rotations completed at this spot in the current session, oldest first.</summary>
    public IReadOnlyList<SessionRotationTiming> SessionRotations { get; init; } = [];
    public string? Error { get; init; }
}

/// <summary>Compatibility name; lifecycle and recovery are owned by the shared platform.</summary>
internal sealed class HermesiaRotationTracker(string? path = null)
    : RotationPlatform(RotationDefinition.Hermesia, path)
{
    internal const int StartupOffers = 5;
    internal new static string DefaultPath => RotationPlatform.DefaultPath(BdoGrindTracker.Core.LootSpotCatalog.HermesiaId);
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
