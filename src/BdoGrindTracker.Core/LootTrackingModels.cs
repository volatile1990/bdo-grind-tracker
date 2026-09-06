namespace BdoGrindTracker.Core;

/// <summary>Identifies the UI surface that produced a loot observation.</summary>
public enum LootSource
{
    Normal,
    Rare,
}

/// <summary>
/// A native OCR row after Companion's leading-row trim. Diagnostics only;
/// confidence fields never gate counting. Null quantity denotes the native -1 sentinel.
/// </summary>
public sealed record LootObservation(
    LootSource Source,
    int Slot,
    string RawText,
    string? ItemName,
    int? Quantity,
    double NameConfidence,
    double QuantityConfidence,
    ulong? VisualFingerprint,
    string? RejectionReason)
{
    /// <summary>Original calibrated row Y, preserved for native Companion replay.</summary>
    public int? NativeY { get; init; }

}

/// <summary>A native loot delta, including signed rare corrections.</summary>
public sealed record TrackedLootEvent(
    Guid EventId,
    DateTimeOffset DetectedAt,
    string ItemName,
    int Quantity);

/// <summary>How the tracker handled one occupied row in the current frame.</summary>
public enum LootTrackingDecisionStatus
{
    Pending,
    Counted,
    MatchedExisting,
    Rejected,
    Unresolved,
}

/// <summary>Traceable result for one input observation.</summary>
public sealed record LootTrackingDecision(
    LootObservation Observation,
    Guid? EventId,
    LootTrackingDecisionStatus Status,
    string Reason)
{
    public Guid? TrackId { get; init; }

    public LootSource Source => Observation.Source;

    public int Slot => Observation.Slot;
}

/// <summary>All newly confirmed events and per-row decisions from one frame.</summary>
public sealed record TrackerFrameResult(
    IReadOnlyList<TrackedLootEvent> NewEvents,
    IReadOnlyList<LootTrackingDecision> Decisions);
