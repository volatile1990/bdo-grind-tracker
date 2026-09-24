namespace BdoGrindTracker.App.Overlay;

public sealed record RotationEvent(string Kind, string Label, double Seconds, int Occurrence = 1)
{
    public string Key => Kind + ":" + Occurrence;
    /// <summary>The banner was not read; its time is estimated from a message that always is (see RotationStep.Midpoint).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Inferred { get; init; }
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
/// <param name="Special">The rotation contained at least one special event.</param>
/// <param name="StartedAt">When the rotation started, for placing it on the session timeline.</param>
/// <param name="SpecialEventSeconds">Each special event's seconds from this rotation's start.</param>
/// <param name="RunId">Identifies the run across updates, so its place on the session timeline stays put.</param>
/// <param name="StartedAfter">
/// Session time of the start on the active-time axis of the drop history, recorded when the rotation first appeared.
/// Null for rotations restored or replayed without that axis; the timeline then falls back to the wall clock.
/// </param>
/// <param name="Outcome">complete, incomplete, aborted or active: only complete rotations count as statistics.</param>
/// <param name="Events">The rotation's recorded mechanics, for showing its phases on the session timeline.</param>
public sealed record SessionRotationTiming(double Duration, double? WalkBack = null, bool Special = false,
    DateTimeOffset StartedAt = default, IReadOnlyList<double>? SpecialEventSeconds = null,
    Guid RunId = default, TimeSpan? StartedAfter = null, string Outcome = "complete",
    IReadOnlyList<RotationEvent>? Events = null)
{
    public bool IsComplete => Outcome == "complete";
    public bool IsActive => Outcome == "active";

    /// <summary>Special events observed during this rotation.</summary>
    public int SpecialEvents => SpecialEventSeconds?.Count ?? 0;
}
/// <summary>Best, ideal and mechanic bests of one reference pool.</summary>
public sealed record RotationComparisonView(RotationRun? Best, RotationRun? Ideal,
    IReadOnlyDictionary<string, double> SectorBests, int Completed);
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
    /// <summary>The rotations observed at this spot in the current session, oldest first, failed ones included.</summary>
    public IReadOnlyList<SessionRotationTiming> SessionRotations { get; init; } = [];
    /// <summary>The spot knows special events: random extra or replacing mechanics a full rotation does not need.</summary>
    public bool SupportsSpecialEvents { get; init; }
    /// <summary>Special events of the current rotation.</summary>
    public int SpecialEvents { get; init; }
    /// <summary>The current phase belongs to a special event.</summary>
    public bool SpecialEventActive { get; init; }
    /// <summary>Special events in this session across all spots, including aborted and running rotations.</summary>
    public int SessionSpecialEvents { get; init; }
    /// <summary>Best, ideal, mechanic bests and count shown exclude rotations with special events.</summary>
    public bool ExcludesSpecialEvents { get; init; }
    /// <summary>
    /// Seconds into the current rotation from which its phases are certain: 0 for a rotation that began with its start;
    /// for one picked up mid-way the first message that fits exactly one phase; null while none has.
    /// </summary>
    public double? AlignedAt { get; init; }
    /// <summary>The section that message opened, to find the same moment in the reference.</summary>
    public string? AlignedSection { get; init; }
    /// <summary>Stretches of the current rotation whose required phases were never seen (ids of the steps skipped).</summary>
    public IReadOnlyList<RotationSection> MissingSections { get; init; } = [];
    /// <summary>For spots compared by their number of special events: the number the shown best and ideal have.</summary>
    public int? ComparedSpecialEvents { get; init; }
    /// <summary>The comparison without rotations that contained a special event.</summary>
    public RotationComparisonView? WithoutSpecialEvents { get; init; }
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
    // Event Horizon's banner variants and the mini-AFK midpoint mark a moment inside a phase, not its end. An estimated
    // banner time is no measurement: it never borders a mechanic best.
    public static bool IsCheckpoint(RotationEvent e) => !e.Inferred &&
        e.Kind is not "porter" and not "offer" and not "big-scarecrow" and not "reception" and not "debris" and not "distortion"
            and not "fragment" and not "away" and not "back";
    /// <summary>Short strokes above the band: pack spawns, wave events and moments inside a phase.</summary>
    public static bool IsMarker(RotationEvent e) => e.Kind is "porter" or "offer" or "hog" or "agris" or "failure" or "distortion"
        or "fragment" or "away" or "back";
    public static string Mark(RotationEvent e) => e.Kind switch {
        "drakania" => "D", "drakania-kill" => "D✓", "transfer" => "B", "mine-enter" => "M" + e.Occurrence,
        "mine-second" => "P2", "mine-cleared" => "M✓", "dragon" => "R", "afk" => "AFK", "end" => "E", _ => "" };
    public const string Legend = "D Drakania · B Buff · M Mine · P2 Phase 2 · R Drache · kleine Striche: Träger";
    public static string Sector(RotationMonitorSnapshot state, string? language = null)
    {
        var checkpoints = state.Events.Where(IsCheckpoint).ToArray();
        if (checkpoints.Length < 2) return BdoGrindTracker.App.Localization.AppText.Translate("Bestabschnitt: noch keine Mechanik abgeschlossen", language);
        var current = checkpoints[^1];
        if (!state.SectorBests.TryGetValue(current.Key, out var best)) return BdoGrindTracker.App.Localization.AppText.Translate("Bestabschnitt: noch keine passende Referenz", language);
        return BdoGrindTracker.App.Localization.AppText.Format("Abschnitt {0} · Bestzeit {1}", language, Time(current.Seconds - checkpoints[^2].Seconds), Time(best));
    }
    public static RotationRun? Reference(RotationMonitorSnapshot state, string mode) => mode == "ideal" ? state.Ideal : state.Best;
    public static double Extent(RotationMonitorSnapshot state, string mode) => Math.Max(60, Math.Max(
        state.Elapsed + RotationCurrentRow.OffsetOf(state, Reference(state, mode)) + 10, Reference(state, mode)?.Duration ?? 620));
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
