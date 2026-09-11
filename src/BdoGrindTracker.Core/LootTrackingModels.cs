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

    /// <summary>Spot-specific policy; Quantity retains the original OCR reading.</summary>
    public DropQuantityBounds? QuantityBounds { get; init; }

    /// <summary>The OCR pipeline supplied its implicit rare quantity 1 without reading a number.</summary>
    public bool UsesImplicitUnitQuantity { get; init; }

    /// <summary>The confirmed item/spot range supplies one without reading a quantity.</summary>
    public bool UsesFixedUnitQuantity { get; init; }

    /// <summary>Recovered older row used only for alignment; cannot emit loot or quantity revisions.</summary>
    public bool IsAlignmentAnchor { get; init; }
    public int? AlignmentPreviousSlot { get; init; }

    /// <summary>Optional pixel evidence of a faded row being replaced by a fresh rendering.</summary>
    public NormalLootAppearanceEvidence? AppearanceEvidence { get; init; }

    /// <summary>Physical glyph evidence only; cannot supply an item, amount, or event identity.</summary>
    public NormalLootOccupancyEvidence? OccupancyEvidence { get; init; }

}

/// <summary>A native loot delta, including signed rare corrections.</summary>
public sealed record TrackedLootEvent(
    Guid EventId,
    DateTimeOffset DetectedAt,
    string ItemName,
    int Quantity)
{
    public int Revision { get; init; }
    public int? TotalDropQuantity { get; init; }
}

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
    IReadOnlyList<LootTrackingDecision> Decisions)
{
    /// <summary>Complete fused estimate for counters with reversible event histories.</summary>
    public LootTotalsProjection? LootProjection { get; init; }
    public LifetimeParsingContext? LifetimeParsingContext { get; init; }
    public long? NormalCaptureIndex { get; init; }
    public IReadOnlyList<NormalLootReconciliationTrace> NormalReconciliation { get; init; } = [];
}
