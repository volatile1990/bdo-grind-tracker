// Restored from the verified 0.5.1 assembly; recognition behavior is intentionally unchanged.
using System;

namespace BdoGrindTracker.Core;

public sealed record CompanionRecognizedEntry
{
    public string Name { get; }

    public uint Count { get; }

    public int Y { get; }

    /// <summary>Calibrated row index, newest first; optional for legacy callers.</summary>
    public int? Slot { get; init; }
    public bool IsPlaceholder { get; init; }
    public bool IsAlignmentAnchor { get; init; }
    public int? AlignmentPreviousSlot { get; init; }
    public Guid? EventId { get; init; }
    public int Revision { get; init; }
    public int? QuantityDelta { get; init; }
    public int? TotalDropQuantity { get; init; }

    /// <summary>Optional evidence used by the temporal counter; legacy counting ignores it.</summary>
    public double NameConfidence { get; init; } = 1;
    public string? RawText { get; init; }
    /// <summary>First actual observation of this drop, retained on quantity revisions.</summary>
    public DateTimeOffset? DetectedAt { get; init; }

    public DropQuantityBounds? QuantityBounds { get; init; }

    /// <summary>
    /// True when the counter emitted a configured minimum instead of its native
    /// unresolved-quantity estimate. The value is an estimate, not an OCR read.
    /// </summary>
    public bool IsMinimumQuantityEstimate { get; init; }

    public CompanionRecognizedEntry(string name, uint count, int y = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
        Name = name;
        Count = count;
        Y = y;
    }
}
