using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Persistence;

// Records contain caller-owned collections. Preserve their contents as well as
// their scalar fields before reusing normalized or serialized history.
internal static class LootHistoryEntrySnapshot
{
    public static LootHistoryEntry Copy(LootHistoryEntry entry) => entry with
    {
        Totals = new(entry.Totals, entry.Totals.Comparer),
        Rotations = entry.Rotations?.Select(rotation => rotation is null ? null! : rotation with
        {
            Run = rotation.Run is null ? null! : rotation.Run with
            {
                Events = rotation.Run.Events?.ToArray()!,
                Sections = rotation.Run.Sections?.ToArray()!,
            },
        }).ToArray()!,
        RotationTimeline = entry.RotationTimeline?.ToArray()!,
        DropHistory = entry.DropHistory?.ToArray(),
        Pauses = entry.Pauses?.ToArray(),
        ManualLootItems = entry.ManualLootItems?.ToArray()!,
        GarmothPendingCorrectionIntervals = entry.GarmothPendingCorrectionIntervals?.ToArray()!,
        Buffs = entry.Buffs is null ? null : entry.Buffs with
        {
            Consumptions = entry.Buffs.Consumptions?.ToArray()!,
            Usage = entry.Buffs.Usage?.ToArray()!,
            Active = entry.Buffs.Active?.ToArray()!,
        },
    };

    public static bool Matches(LootHistoryEntry entry, LootHistoryEntry snapshot)
    {
        if (entry with
            {
                Totals = snapshot.Totals,
                Rotations = snapshot.Rotations,
                RotationTimeline = snapshot.RotationTimeline,
                DropHistory = snapshot.DropHistory,
                Pauses = snapshot.Pauses,
                ManualLootItems = snapshot.ManualLootItems,
                GarmothPendingCorrectionIntervals = snapshot.GarmothPendingCorrectionIntervals,
                Buffs = snapshot.Buffs,
            } != snapshot) return false;
        if (!entry.StartedAt.EqualsExact(snapshot.StartedAt) || !entry.UpdatedAt.EqualsExact(snapshot.UpdatedAt) ||
            !SameDecimal(entry.SilverBeforeTax, snapshot.SilverBeforeTax) ||
            !SameDecimal(entry.SilverAfterTax, snapshot.SilverAfterTax) ||
            !SameDecimal(entry.ExperienceGainedPercentagePoints, snapshot.ExperienceGainedPercentagePoints) ||
            !SameDate(entry.GarmothUploadedAt, snapshot.GarmothUploadedAt) ||
            !SameDate(entry.CombatStats?.ObservedAt, snapshot.CombatStats?.ObservedAt) ||
            !Equals(entry.Totals.Comparer, snapshot.Totals.Comparer) ||
            !entry.Totals.SequenceEqual(snapshot.Totals) ||
            !Same(entry.RotationTimeline, snapshot.RotationTimeline) ||
            !Same(entry.DropHistory, snapshot.DropHistory) || !Same(entry.Pauses, snapshot.Pauses) ||
            !Same(entry.ManualLootItems, snapshot.ManualLootItems) ||
            !Same(entry.GarmothPendingCorrectionIntervals, snapshot.GarmothPendingCorrectionIntervals) ||
            !SameBuffs(entry.Buffs, snapshot.Buffs)) return false;
        if (entry.Rotations is null || snapshot.Rotations is null)
            return entry.Rotations is null && snapshot.Rotations is null;
        if (entry.Rotations.Count != snapshot.Rotations.Count) return false;
        for (var index = 0; index < entry.Rotations.Count; index++)
        {
            var left = entry.Rotations[index];
            var right = snapshot.Rotations[index];
            if (left is null || right is null)
            {
                if (left != right) return false;
                continue;
            }
            if (left.SpotId != right.SpotId || !left.StartedAt.EqualsExact(right.StartedAt)) return false;
            if (left.Run is null || right.Run is null)
            {
                if (left.Run != right.Run) return false;
                continue;
            }
            if (left.Run with { Events = right.Run.Events, Sections = right.Run.Sections } != right.Run ||
                !SameDouble(left.Run.Duration, right.Run.Duration) ||
                !left.Run.RecordedAt.EqualsExact(right.Run.RecordedAt) ||
                !Same(left.Run.Events, right.Run.Events) || !Same(left.Run.Sections, right.Run.Sections)) return false;
        }
        return true;
    }

    public static long EstimateBytes(LootHistoryEntry entry) =>
        1024L + (entry.CharacterClass?.Length ?? 0) * 2L +
        entry.Totals.Sum(pair => 64L + pair.Key.Length * 2L) +
        (entry.DropHistory?.Sum(drop => 64L + (drop?.ItemName?.Length ?? 0) * 2L) ?? 0) +
        (entry.RotationTimeline?.Sum(row => 192L + TextBytes(row?.Detail) + TextBytes(row?.Kind) +
            TextBytes(row?.ItemName) + TextBytes(row?.Type) + TextBytes(row?.SpotId)) ?? 0) +
        (entry.Rotations?.Sum(rotation => 256L + TextBytes(rotation?.Run?.Outcome) + TextBytes(rotation?.Run?.Reason) +
            (rotation?.Run?.Events?.Sum(row => 128L + TextBytes(row?.Kind) + TextBytes(row?.Label)) ?? 0) +
            (rotation?.Run?.Sections?.Sum(row => 64L + TextBytes(row?.Id)) ?? 0)) ?? 0) +
        (entry.Pauses?.Sum(row => 96L + TextBytes(row?.Kind)) ?? 0) +
        (entry.ManualLootItems?.Sum(name => 32L + TextBytes(name)) ?? 0) +
        (entry.GarmothPendingCorrectionIntervals?.Length ?? 0) * 16L +
        (entry.Buffs?.Consumptions?.Sum(row => 192L + TextBytes(row?.BuffId) + TextBytes(row?.Name) + TextBytes(row?.Price?.Region)) ?? 0) +
        (entry.Buffs?.Usage?.Sum(row => 192L + TextBytes(row?.BuffId) + TextBytes(row?.Name)) ?? 0) +
        (entry.Buffs?.Active?.Sum(row => 192L + TextBytes(row?.BuffId) + TextBytes(row?.Name) + TextBytes(row?.Price?.Region)) ?? 0);

    private static long TextBytes(string? value) => (value?.Length ?? 0) * 2L;

    private static bool Same<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index];
            var b = right[index];
            if (!EqualityComparer<T>.Default.Equals(a, b)) return false;
            var datesMatch = (a, b) switch
            {
                (RotationTimelineEntry x, RotationTimelineEntry y) => x.At.EqualsExact(y.At) && x.RecordedAt.EqualsExact(y.RecordedAt),
                (RotationEvent x, RotationEvent y) => SameDouble(x.Seconds, y.Seconds),
                (RotationSection x, RotationSection y) => SameDouble(x.Start, y.Start) && SameDouble(x.End, y.End),
                (SessionPause x, SessionPause y) => x.StartedAt.EqualsExact(y.StartedAt) && SameDate(x.EndedAt, y.EndedAt),
                (BuffConsumption x, BuffConsumption y) => x.ConsumedAt.EqualsExact(y.ConsumedAt) && SamePrice(x.Price, y.Price),
                (BuffActive x, BuffActive y) => x.ObservedAt.EqualsExact(y.ObservedAt) && SamePrice(x.Price, y.Price),
                (BuffUsage x, BuffUsage y) => SameDecimal(x.KnownProratedCost, y.KnownProratedCost),
                _ => true,
            };
            if (!datesMatch) return false;
        }
        return true;
    }

    private static bool SameDate(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null || right is null ? left is null && right is null : left.Value.EqualsExact(right.Value);

    private static bool SamePrice(BuffPrice? left, BuffPrice? right) =>
        left is null || right is null ? left is null && right is null :
            SameDate(left.FetchedAt, right.FetchedAt) && SameDecimal(left.UnitPrice, right.UnitPrice);

    private static bool SameDouble(double left, double right) =>
        BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);

    private static bool SameDecimal(decimal? left, decimal? right)
    {
        if (left is null || right is null) return left is null && right is null;
        Span<int> a = stackalloc int[4];
        Span<int> b = stackalloc int[4];
        decimal.GetBits(left.Value, a);
        decimal.GetBits(right.Value, b);
        return a.SequenceEqual(b);
    }

    private static bool SameBuffs(BuffLedgerSnapshot? left, BuffLedgerSnapshot? right) =>
        left is null || right is null ? left is null && right is null :
            Same(left.Consumptions, right.Consumptions) && Same(left.Usage, right.Usage) && Same(left.Active, right.Active);
}
