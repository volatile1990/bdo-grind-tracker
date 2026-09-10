namespace BdoGrindTracker.Core;

/// <summary>Read-only explanation of a finalized counter batch, never an input to counting.</summary>
public sealed record NormalLootReconciliationTrace(
    long CaptureIndex, DateTimeOffset? CapturedAt, long? PreviousCaptureIndex,
    int GeometricOverlap, int ConfirmedOverlap,
    IReadOnlyList<NormalLootOverlapAttempt> OverlapAttempts,
    IReadOnlyList<NormalLootRowTrace> Rows);

public sealed record NormalLootOverlapAttempt(int Length, bool Accepted, string Reason,
    int? PreviousSlot, int? CurrentSlot);

public sealed record NormalLootRowTrace(
    int? Slot, int? NativeY, string? ItemName, uint? InputQuantity, uint? RepairedQuantity,
    DropQuantityBounds? QuantityBounds, bool EstimatedQuantity, bool Placeholder,
    long FrameTag, Guid? TrackId, Guid? CandidatePreviousTrackId, int? CandidatePreviousSlot,
    string AlignmentReason, string Outcome, int Revision, int QuantityDelta)
{
    public Guid? MatchedPreviousTrackId { get; init; }
    public int? MatchedPreviousSlot { get; init; }
}
