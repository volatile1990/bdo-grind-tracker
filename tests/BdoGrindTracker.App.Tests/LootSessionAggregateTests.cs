using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LootSessionAggregateTests
{
    [Fact]
    public void ProjectionReplacesEveryItemAndCanRetractConfirmedDrops()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 2, ("Helmet", 8), ("Dust", 2)));

        var applied = aggregate.ApplyProjection(Projection(2, 1, ("Helmet", 4)));

        Assert.True(applied.TotalsChanged);
        Assert.False(applied.HasNewArrival);
        Assert.Equal(4, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
        Assert.Equal(4, Assert.Single(aggregate.Totals).Value);
    }

    [Fact]
    public void ProjectionItemReplacementChangesTotalsEvenWhenSumAndDropCountStayEqual()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 1, ("Helmet", 4)));

        var applied = aggregate.ApplyProjection(Projection(2, 1, ("Dust", 4)));

        Assert.True(applied.TotalsChanged);
        Assert.False(applied.HasNewArrival);
        Assert.Equal(4, aggregate.TotalQuantity);
        Assert.Equal("Dust", Assert.Single(aggregate.Totals).Key);
    }

    [Fact]
    public void DuplicateAndStaleProjectionsCannotRevertTotalsOrAdvanceActivity()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(2, 1, ("Helmet", 4)));
        var replayed = new LootTotalsProjection(2, new Dictionary<string, long> { ["Dust"] = 50 },
            5, DateTimeOffset.UnixEpoch.AddDays(1));

        Assert.Equal((false, false), aggregate.ApplyProjection(replayed));
        Assert.Equal((false, false), aggregate.ApplyProjection(new(1, replayed.Totals,
            replayed.ConfirmedDropCount, replayed.LatestArrivalAt)));
        Assert.Equal(4, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
        Assert.Equal(DateTimeOffset.UnixEpoch, aggregate.LatestArrivalAt);
    }

    [Fact]
    public void ActivityWatermarkSurvivesDropRetractionsAndOnlyAdvancesForLaterArrivals()
    {
        var aggregate = new LootSessionAggregate();
        var start = Projection(0, 2, ("Helmet", 8));
        Assert.True(aggregate.ApplyProjection(start).HasNewArrival);
        Assert.False(aggregate.ApplyProjection(new(1, new Dictionary<string, long>(), 0, null)).HasNewArrival);
        Assert.False(aggregate.ApplyProjection(new(2, start.Totals, 2, start.LatestArrivalAt)).HasNewArrival);
        Assert.False(aggregate.ApplyProjection(new(3, start.Totals, 2,
            DateTimeOffset.UnixEpoch.AddSeconds(-1))).HasNewArrival);
        var arrivalOnly = aggregate.ApplyProjection(new(4, start.Totals, 2,
            DateTimeOffset.UnixEpoch.AddSeconds(1)));
        Assert.True(arrivalOnly.HasNewArrival);
        Assert.False(arrivalOnly.TotalsChanged);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1), aggregate.LatestArrivalAt);
    }

    [Fact]
    public void ProjectionKeepsManualOffsetThroughReplacementAndTemporaryRetraction()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 1, ("Helmet", 4)));
        aggregate.AdjustQuantity("Helmet", 10, 4);
        aggregate.ApplyProjection(Projection(2, 1, ("Dust", 4)));
        Assert.Equal(6, aggregate.Totals["Helmet"]);
        Assert.Equal(4, aggregate.Totals["Dust"]);
        aggregate.ApplyProjection(Projection(3, 2, ("Helmet", 8), ("Dust", 4)));
        Assert.Equal(14, aggregate.Totals["Helmet"]);
        Assert.Equal(18, aggregate.TotalQuantity);
        Assert.Equal(2, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void NegativeManualOffsetSurvivesClampingAndCanBeExplicitlyEditedAgain()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 1, ("Helmet", 4)));
        aggregate.AdjustQuantity("Helmet", 0, 4);
        aggregate.ApplyProjection(Projection(2, 0));
        Assert.Equal(0, aggregate.Totals["Helmet"]);
        aggregate.ApplyProjection(Projection(3, 1, ("Helmet", 4)));
        Assert.Equal(0, aggregate.TotalQuantity);
        aggregate.ApplyProjection(Projection(4, 0));
        aggregate.AdjustQuantity("Helmet", 2, 0);
        Assert.Equal(2, aggregate.Totals["Helmet"]);
        aggregate.ApplyProjection(Projection(5, 1, ("Helmet", 4)));
        Assert.Equal(6, aggregate.TotalQuantity);
    }

    [Fact]
    public void ProjectionCorrectionPreservesDropsThatArrivedWhileEditorWasOpen()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 1, ("Helmet", 4)));
        aggregate.ApplyProjection(Projection(2, 2, ("Helmet", 8)));
        aggregate.AdjustQuantity("Helmet", 10, originalQuantity: 4);
        aggregate.ApplyProjection(Projection(3, 3, ("Helmet", 12)));
        Assert.Equal(18, aggregate.TotalQuantity);
    }

    [Fact]
    public void FailedManualSaveDoesNotChangeProjectionOffset()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 1, ("Helmet", 4)));
        Assert.Throws<IOException>(() => aggregate.AdjustQuantity("Helmet", 10, 4,
            _ => throw new IOException("Synthetic save failure")));
        aggregate.ApplyProjection(Projection(2, 2, ("Helmet", 8)));
        Assert.Equal(8, aggregate.TotalQuantity);
    }

    [Fact]
    public void InvalidProjectionCannotPartlyMutateTotalsOrConsumeRevision()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 1, ("Helmet", 4)));
        Assert.Throws<ArgumentException>(() => aggregate.ApplyProjection(
            Projection(2, 2, ("Helmet", 8), ("Dust", -1))));
        Assert.Throws<ArgumentException>(() => aggregate.ApplyProjection(
            Projection(2, 2, ("Helmet", 8), ("helmet", 4))));
        Assert.Throws<ArgumentException>(() => aggregate.ApplyProjection(
            Projection(2, 2, ("Helmet", 8), (" ", 4))));
        Assert.Throws<ArgumentException>(() => aggregate.ApplyProjection(Projection(2, -1)));
        Assert.Throws<OverflowException>(() => aggregate.ApplyProjection(
            Projection(2, 2, ("Helmet", long.MaxValue), ("Dust", 1))));
        Assert.Equal(4, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
        Assert.Equal("Helmet", Assert.Single(aggregate.Totals).Key);

        Assert.True(aggregate.ApplyProjection(Projection(2, 2, ("Helmet", 8))).TotalsChanged);
        Assert.Equal(8, aggregate.TotalQuantity);
    }

    [Fact]
    public void OverflowFromManualOffsetCannotConsumeProjectionRevision()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(1, 1, ("Helmet", 4)));
        aggregate.AdjustQuantity("Helmet", 5, 4);
        Assert.Throws<OverflowException>(() => aggregate.ApplyProjection(
            Projection(2, 1, ("Helmet", long.MaxValue))));
        Assert.Equal(5, aggregate.TotalQuantity);
        aggregate.ApplyProjection(Projection(2, 1, ("Helmet", 8)));
        Assert.Equal(9, aggregate.TotalQuantity);
    }

    [Fact]
    public void ProjectionCopiesInputAndNormalizesItemCasingAndZeroTotals()
    {
        var aggregate = new LootSessionAggregate();
        var input = new Dictionary<string, long> { ["Helmet"] = 4, ["Dust"] = 0 };
        aggregate.ApplyProjection(new(1, input, 1, null));
        input["Helmet"] = 100;
        aggregate.AdjustQuantity("helmet", 6, 4);
        aggregate.ApplyProjection(Projection(2, 1, ("HELMET", 8)));
        Assert.Equal(10, aggregate.TotalQuantity);
        Assert.Single(aggregate.Totals);
    }

    [Fact]
    public void ResetClearsProjectionRevisionOffsetsAndArrivalWatermark()
    {
        var aggregate = new LootSessionAggregate();
        var projection = Projection(5, 1, ("Helmet", 4));
        aggregate.ApplyProjection(projection);
        aggregate.AdjustQuantity("Helmet", 10, 4);
        aggregate.Reset();
        Assert.Null(aggregate.LatestArrivalAt);
        Assert.True(aggregate.ApplyProjection(new(0, projection.Totals, 1, projection.LatestArrivalAt)).HasNewArrival);
        Assert.Equal(4, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    private static LootTotalsProjection Projection(long revision, int drops,
        params (string Name, long Quantity)[] totals) =>
        new(revision, totals.ToDictionary(pair => pair.Name, pair => pair.Quantity),
            drops, DateTimeOffset.UnixEpoch);

    [Fact]
    public void QuantityRevisionUpdatesTheExistingDropExactlyOnce()
    {
        var aggregate = new LootSessionAggregate();
        var initial = Event("Helmet", 4) with { TotalDropQuantity = 4 };
        var correction = initial with { Quantity = 2, TotalDropQuantity = 6, Revision = 1 };
        aggregate.Apply(initial);
        aggregate.Apply(correction);
        aggregate.Apply(correction);
        aggregate.Apply(initial);
        Assert.Equal(6, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void RevisionsRemainIdempotentEvenWhenUiDeliveryIsOutOfOrder()
    {
        var aggregate = new LootSessionAggregate();
        var initial = Event("Helmet", 4) with { TotalDropQuantity = 4 };
        aggregate.Apply(initial with { Quantity = 2, TotalDropQuantity = 6, Revision = 1 });
        aggregate.Apply(initial);
        Assert.Equal(6, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void ManualCorrectionAndSubsequentAutomaticQuantityRevisionBothRemainApplied()
    {
        var aggregate = new LootSessionAggregate();
        var initial = Event("Helmet", 4) with { TotalDropQuantity = 4 };
        aggregate.Apply(initial);
        aggregate.AdjustQuantity("Helmet", 10, 4);
        aggregate.Apply(initial with { Quantity = 2, TotalDropQuantity = 6, Revision = 1 });
        Assert.Equal(12, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void ManualCorrectionKeepsDropsReceivedWhileTheEditorWasOpen()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Apply(Event("BON Wandering Origin Crystal", 5));
        aggregate.Apply(Event("BON Wandering Origin Crystal", 2));
        aggregate.AdjustQuantity("BON Wandering Origin Crystal", 1, originalQuantity: 5);
        Assert.Equal(3, aggregate.Totals["BON Wandering Origin Crystal"]);
        Assert.Equal(3, aggregate.TotalQuantity);
        Assert.Equal(2, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void FailedManualCommitLeavesCountersAndEventDeduplicationIntact()
    {
        var aggregate = new LootSessionAggregate();
        var drop = Event("BON Wandering Origin Crystal", 5);
        aggregate.Apply(drop);
        Assert.Throws<IOException>(() => aggregate.AdjustQuantity(drop.ItemName, 0, 5,
            _ => throw new IOException("Synthetic save failure")));
        aggregate.Apply(drop);
        Assert.Equal(5, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void ExplicitZeroCorrectionCanBeEditedAgainWithoutCountingAnotherDrop()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Apply(Event("BON Wandering Origin Crystal", 5));
        aggregate.AdjustQuantity("BON Wandering Origin Crystal", 0, 5);
        Assert.Equal(0, aggregate.Totals["BON Wandering Origin Crystal"]);
        Assert.Equal(0, aggregate.ItemTypeCount);
        aggregate.AdjustQuantity("BON Wandering Origin Crystal", 2, 0);
        Assert.Equal(2, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void LateRareReconciliationCannotMakeAnExplicitZeroCorrectionNegative()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Apply(Event("BON Wandering Origin Crystal", 5));
        aggregate.AdjustQuantity("BON Wandering Origin Crystal", 0, 5);
        aggregate.Apply(Event("BON Wandering Origin Crystal", -1));
        Assert.Equal(0, aggregate.Totals["BON Wandering Origin Crystal"]);
        Assert.Equal(0, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
        aggregate.Apply(Event("BON Wandering Origin Crystal", 1));
        Assert.Equal(1, aggregate.TotalQuantity);
        Assert.Equal(2, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void TheSameEventIdCanOnlyBeAppliedOnce()
    {
        var aggregate = new LootSessionAggregate();
        var loot = Event("BON Origin Shard", 1);
        aggregate.Apply(loot);
        aggregate.Apply(loot);
        Assert.Equal(1, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
        aggregate.Reset();
        aggregate.Apply(loot);
        Assert.Equal(1, aggregate.TotalQuantity);
    }

    [Fact]
    public void DistinctIdenticalEventsRemainSeparate()
    {
        var aggregate = new LootSessionAggregate();

        aggregate.Apply(Event("BON Origin Shard", 1));
        aggregate.Apply(Event("BON Origin Shard", 1));

        Assert.Equal(2, aggregate.TotalQuantity);
        Assert.Equal(2, aggregate.ConfirmedEventCount);
        Assert.Equal(1, aggregate.ItemTypeCount);
        Assert.Equal(2, aggregate.Totals["BON Origin Shard"]);
    }

    [Fact]
    public void ZeroEventsCannotAlterTotals()
    {
        var aggregate = new LootSessionAggregate();

        Assert.Throws<ArgumentOutOfRangeException>(() => aggregate.Apply(Event("Origin Shard", 0)));

        Assert.Equal(0, aggregate.TotalQuantity);
        Assert.Equal(0, aggregate.ConfirmedEventCount);
        Assert.Equal(0, aggregate.ItemTypeCount);
    }

    [Fact]
    public void NativeRareCorrectionChangesTotalsWithoutAddingAnEvent()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Apply(Event("BON Origin Shard", 3));
        var correction = Event("BON Origin Shard", -1);

        aggregate.Apply(correction);
        aggregate.Apply(correction);

        Assert.Equal(2, aggregate.TotalQuantity);
        Assert.Equal(2, aggregate.Totals["BON Origin Shard"]);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    [Fact]
    public void NativeRareReplacementRemovesTheZeroTotalItem()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Apply(Event("BON Origin Shard", 1));

        aggregate.Apply(Event("BON Origin Shard", -1));
        aggregate.Apply(Event("BON Wandering Origin Crystal", 1));

        Assert.Equal(1, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ItemTypeCount);
        Assert.Equal(2, aggregate.ConfirmedEventCount);
        Assert.False(aggregate.Totals.ContainsKey("BON Origin Shard"));
        Assert.Equal(1, aggregate.Totals["BON Wandering Origin Crystal"]);
    }

    [Fact]
    public void ResetClearsTotalsAndCounters()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Apply(Event("Origin Shard", 2));

        aggregate.Reset();

        Assert.Empty(aggregate.Totals);
        Assert.Equal(0, aggregate.TotalQuantity);
        Assert.Equal(0, aggregate.ConfirmedEventCount);
        Assert.Equal(0, aggregate.ItemTypeCount);
    }

    private static LootEventView Event(string itemName, int quantity) =>
        new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, itemName, quantity);
}
