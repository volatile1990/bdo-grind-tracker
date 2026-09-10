using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Finds missing older rows that can distinguish competing positive scroll offsets.</summary>
internal sealed class NormalLootAlignmentReview
{
    private LootObservation[] _previous = [];
    private DateTimeOffset? _capturedAt;
    internal static readonly TimeSpan MaximumFrameGap = TimeSpan.FromSeconds(1);

    public void Reset() { _previous = []; _capturedAt = null; }

    public Plan? Prepare(IReadOnlyList<LootObservation> observations, DateTimeOffset capturedAt, int slotCount)
    {
        var current = observations.Where(r => r.Source == LootSource.Normal && !r.IsAlignmentAnchor &&
            r.RejectionReason is null && r.ItemName is not null && r.Quantity is > 0)
            .OrderBy(r => r.Slot).ToArray();
        var previous = _previous;
        var elapsed = capturedAt - _capturedAt;
        _previous = current;
        _capturedAt = capturedAt;
        if (elapsed is null || elapsed <= TimeSpan.Zero || elapsed > MaximumFrameGap ||
            !Contiguous(previous) || !Contiguous(current) || current.Length >= slotCount) return null;

        // No retry for a stationary/fading log or an unambiguous shift. At least
        // two observed rows must remain; entirely empty frames never trigger OCR.
        var shifts = Enumerable.Range(0, current.Length).Where(shift =>
            current.Length - shift <= previous.Length &&
            current.Skip(shift).Select((row, i) => Same(row, previous[i])).All(same => same)).ToArray();
        if (shifts.Length < 2 || shifts[0] == 0) return null;

        // Inspect only the next two older positions, and only if they distinguish
        // the competing explanations. Never use a recovered row as a new drop.
        var slots = Enumerable.Range(current.Length, Math.Min(2, slotCount - current.Length))
            .Where(slot => shifts.Select(shift => slot - shift < previous.Length
                ? (previous[slot - shift].ItemName, previous[slot - shift].Quantity)
                : (null, (int?)null)).Distinct().Count() > 1).ToArray();
        return slots.Length == 0 ? null : new(previous, current, shifts, slots);
    }

    private static bool Contiguous(LootObservation[] rows) => rows.Length >= 2 &&
        rows.Select((row, index) => row.Slot == index &&
            (row.QuantityBounds is null || row.Quantity >= row.QuantityBounds.Minimum &&
                (row.QuantityBounds.Maximum is null || row.Quantity <= row.QuantityBounds.Maximum))).All(same => same);

    private static bool Same(LootObservation a, LootObservation b) =>
        a.ItemName == b.ItemName && a.Quantity == b.Quantity;

    internal sealed record Plan(LootObservation[] Previous, LootObservation[] Current, int[] Shifts, int[] Slots)
    {
        public IReadOnlyList<LootObservation> Resolve(IEnumerable<LootObservation?> results)
        {
            var anchors = results.Where(r => r is { IsAlignmentAnchor: true, RejectionReason: null,
                    ItemName: not null, Quantity: > 0 } && Slots.Contains(r.Slot))
                .Cast<LootObservation>().ToArray();
            if (anchors.Length == 0 || anchors.Select(r => r.Slot).Distinct().Count() != anchors.Length) return [];
            var remaining = Shifts.Where(shift => anchors.All(row => row.Slot - shift < Previous.Length &&
                Same(row, Previous[row.Slot - shift]))).ToArray();
            // Require a unique larger shift backed by at least two old rows.
            // An anchor conflicting with every explanation leaves the primary untouched.
            if (remaining.Length != 1 || remaining[0] <= Shifts[0] ||
                Current.Length - remaining[0] + anchors.Length < 2) return [];
            return anchors.Select(row => row with { AlignmentPreviousSlot = row.Slot - remaining[0] }).ToArray();
        }
    }
}
