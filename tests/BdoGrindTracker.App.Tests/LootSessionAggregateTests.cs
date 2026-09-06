using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class LootSessionAggregateTests
{
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
