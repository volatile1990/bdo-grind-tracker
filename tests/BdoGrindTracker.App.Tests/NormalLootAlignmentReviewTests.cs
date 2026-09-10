using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class NormalLootAlignmentReviewTests
{
    private static LootObservation Row(int slot, int quantity, string name = "Helmet", bool anchor = false) =>
        new(LootSource.Normal, slot, $"{name} x {quantity}", name, quantity, 1, 1, null, null)
        { NativeY = 250 - slot * 50, IsAlignmentAnchor = anchor };
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static LootObservation[] Previous => [Row(0, 6), Row(1, 6), Row(2, 1, "Shard")];
    private static LootObservation[] Current => [Row(0, 4), Row(1, 6), Row(2, 6)];

    [Fact]
    public void MissingOlderRowDistinguishesTwoNewDropsWithoutReplacingPrimaryRows()
    {
        var review = new NormalLootAlignmentReview();
        Assert.Null(review.Prepare(Previous, Start, 6));
        var plan = Assert.IsType<NormalLootAlignmentReview.Plan>(review.Prepare(Current, Start.AddMilliseconds(450), 6));
        Assert.Equal([1, 2], plan.Shifts);
        Assert.Equal([3, 4], plan.Slots);
        var anchor = Row(3, 6, anchor: true);
        var resolved = Assert.Single(plan.Resolve([anchor, null]));
        Assert.Equal(anchor with { AlignmentPreviousSlot = 1 }, resolved);
        Assert.Equal([4, 6, 6], plan.Current.Select(r => r.Quantity));
        Assert.All(plan.Current, r => Assert.False(r.IsAlignmentAnchor));
    }

    [Fact]
    public void UnclearContradictoryAndBaselineSupportingEvidenceLeavesPrimaryUnchanged()
    {
        var review = new NormalLootAlignmentReview();
        review.Prepare(Previous, Start, 6);
        var plan = review.Prepare(Current, Start.AddMilliseconds(450), 6)!;
        Assert.Empty(plan.Resolve([null]));
        Assert.Empty(plan.Resolve([Row(3, 8, anchor: true)]));
        Assert.Empty(plan.Resolve([Row(3, 1, "Shard", anchor: true)]));
        Assert.Empty(plan.Resolve([Row(3, 6)]));
        Assert.Empty(plan.Resolve([Row(3, 6, anchor: true), Row(4, 4, anchor: true)]));
        Assert.Empty(plan.Resolve([Row(0, 6, anchor: true)]));
        Assert.Empty(plan.Resolve([Row(3, 6, anchor: true) with { RejectionReason = "outside-spot-pool" }]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1500)]
    public void StaleOrOutOfOrderFramesCannotSupplyAnchors(int gap)
    {
        var review = new NormalLootAlignmentReview();
        review.Prepare(Previous, Start, 6);
        Assert.Null(review.Prepare(Current, Start.AddMilliseconds(gap), 6));
    }

    [Fact]
    public void EmptyFrameResetAndRecoveredRowsDoNotExtendAnchorHistory()
    {
        foreach (var reset in new[] { false, true })
        {
            var review = new NormalLootAlignmentReview();
            review.Prepare(Previous, Start, 6);
            if (reset) review.Reset();
            else review.Prepare([], Start.AddMilliseconds(200), 6);
            Assert.Null(review.Prepare(Current, Start.AddMilliseconds(450), 6));
        }
        var anchorsOnly = new NormalLootAlignmentReview();
        anchorsOnly.Prepare(Previous.Select(r => r with { IsAlignmentAnchor = true }).ToArray(), Start, 6);
        Assert.Null(anchorsOnly.Prepare(Current, Start.AddMilliseconds(450), 6));
    }

    [Fact]
    public void StationaryLogsUniqueShiftsAndIncompleteRowsSkipProbes()
    {
        LootObservation[][] cases = [Previous, [], [Row(0, 6)], [Row(0, 4), Row(1, 6)],
            [Row(0, 4), Row(2, 6)], [Row(0, 4), Row(1, 6), Row(2, 6) with { Quantity = null }],
            [Row(0, 4), Row(1, 6), Row(2, 6) with { RejectionReason = "unknown" }]];
        foreach (var current in cases)
        {
            var review = new NormalLootAlignmentReview();
            review.Prepare(Previous, Start, 6);
            Assert.Null(review.Prepare(current, Start.AddMilliseconds(450), 6));
        }
    }

    [Fact]
    public void DifferentPixelCoordinatesDoNotAffectSlotAlignment()
    {
        foreach (var scale in new[] { .75, 1, 1.49, 2 })
        {
            var review = new NormalLootAlignmentReview();
            LootObservation[] Scale(LootObservation[] rows) => rows.Select(r => r with
                { NativeY = (int)Math.Round(r.NativeY!.Value * scale) }).ToArray();
            review.Prepare(Scale(Previous), Start, 6);
            var plan = review.Prepare(Scale(Current), Start.AddMilliseconds(450), 6)!;
            Assert.Equal(3, Assert.Single(plan.Resolve(Scale([Row(3, 6, anchor: true)]))).Slot);
        }
    }
}
