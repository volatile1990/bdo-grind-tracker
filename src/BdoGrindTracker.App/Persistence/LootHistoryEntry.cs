using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Persistence;

internal sealed record LootHistoryEntry
{
    public required Guid SessionId { get; init; }
    public IReadOnlyList<BdoGrindTracker.App.Overlay.SessionRotation> Rotations { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(BdoGrindTracker.App.Overlay.RotationTimelineJsonConverter))]
    public IReadOnlyList<BdoGrindTracker.App.Overlay.RotationTimelineEntry> RotationTimeline { get; init; } = [];
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    // null marks sessions saved before Agris observations were recorded.
    public TimeSpan? AgrisActiveDuration { get; init; }
    public TimeSpan? AgrisObservedDuration { get; init; }
    public decimal? ExperienceGainedPercentagePoints { get; init; }
    public TimeSpan? ExperienceObservedDuration { get; init; }
    public int? ExperienceStartLevel { get; init; }
    public int? ExperienceEndLevel { get; init; }
    public required string SpotId { get; init; }
    public string? CharacterClass { get; init; }
    // Last confirmed HUD observation belonging to this session; legacy sessions remain unknown.
    public CombatStatsState? CombatStats { get; init; }
    [System.Text.Json.Serialization.JsonConverter(typeof(BuffLedgerSnapshotJsonConverter))]
    public BdoGrindTracker.Core.Buffs.BuffLedgerSnapshot? Buffs { get; init; }
    public required Dictionary<string, long> Totals { get; init; }
    public IReadOnlyList<SessionDropSample>? DropHistory { get; init; }
    public IReadOnlyList<SessionPause>? Pauses { get; init; }
    public required decimal SilverBeforeTax { get; init; }
    public required decimal SilverAfterTax { get; init; }
    public required bool SilverIsComplete { get; init; }
    public DateTimeOffset? GarmothUploadedAt { get; init; }
    public bool GarmothUploadBlocked { get; init; }
    public string[] ManualLootItems { get; init; } = [];
    public bool GarmothLocallyModified { get; init; }
    // Corrections after a request snapshot was frozen; only a possibly committed
    // matching interval turns these into a visible remote-divergence warning.
    public Guid[] GarmothPendingCorrectionIntervals { get; init; } = [];
}
