namespace BdoGrindTracker.Core.Tests;

public sealed class CompanionDropQuantityBoundsTests
{
    // Synthetic bounds exercise counter policy; these are not game drop data.
    private const string Item = "Synthetic Crystal";

    [Theory]
    [InlineData(false, 1u, 7, 1)]
    [InlineData(true, 1u, 7, 1)]
    [InlineData(false, 2u, 7, 2)]
    [InlineData(true, 2u, 7, 2)]
    public void UpperBoundNormalizesBadReadBeforeMatchingTheContinuingDrop(
        bool rare, uint maximum, int firstRead, int secondRead)
    {
        var counter = new Counter(rare, new DropQuantityBounds(1, maximum));
        counter.Read(firstRead);
        counter.Read(secondRead);

        Assert.Equal(checked((int)maximum), Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false, 1u)]
    [InlineData(true, 1u)]
    [InlineData(false, 2u)]
    [InlineData(true, 2u)]
    public void SeparateDropsEachReceiveTheirOwnLimit(bool rare, uint maximum)
    {
        var counter = new Counter(rare, new DropQuantityBounds(1, maximum));
        counter.Read(7);
        // Exceed the rare counter's existing episode window as well as normal
        // row continuity: this is a second drop, never a session-wide cap.
        for (var index = 0; index < 13; index++) counter.Empty();
        counter.Read(7);

        Assert.Equal(new[] { checked((int)maximum), checked((int)maximum) }, counter.Complete());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    public void ActualReadIsClampedToTheSpotMinimum(bool rare, int quantity)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        counter.Read(quantity);
        counter.Read(quantity);

        Assert.Equal(Math.Max(4, quantity), Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LowerBoundNormalizesContinuingDropBeforeIdentityMatching(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        counter.Read(1);
        counter.Read(4);

        Assert.Equal(4, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparateDropsEachReceiveTheirOwnMinimum(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        counter.Read(1);
        for (var index = 0; index < 13; index++) counter.Empty();
        counter.Read(2);

        Assert.Equal(new[] { 4, 4 }, counter.Complete());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedRunUsesItsSpotMinimum(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        counter.Read(null);
        counter.Read(null);

        Assert.Equal(4, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false, 6, 6)]
    [InlineData(true, 6, 6)]
    [InlineData(false, 17, 8)]
    [InlineData(true, 17, 8)]
    [InlineData(false, 1, 4)]
    [InlineData(true, 1, 4)]
    public void NeighborReadWinsBeforeFallbackAndRespectsBothBounds(bool rare, int read, int expected)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        counter.Read(null);
        counter.Read(null);
        counter.Read(read);

        Assert.Equal(expected, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APreviousRealNeighborWinsBeforeFallback(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        counter.Read(6);
        counter.Read(null);
        counter.Read(null);

        Assert.Equal(6, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingletonBoundsMakeAnUnreadQuantityUnambiguous(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(1, 1));
        counter.Read(null);

        Assert.Equal(1, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingletonMissingAndMisreadQuantityStillDescribeOneDrop(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(1, 1));
        counter.Read(null);
        counter.Read(7);
        counter.Read(1);

        Assert.Equal(1, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIsolatedUnknownQuantityUsesTheExplicitSpotMinimumOnCompletion(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        counter.Read(null);

        Assert.Equal(4, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalUnknownQuantityReceivesTheFallbackWhenItsBatchFlushes(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4, 8));
        for (var index = 0; index < 9; index++) counter.Empty();
        counter.Read(null);

        Assert.Equal(4, Assert.Single(counter.Complete()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownMaximumDoesNotInventACap(bool rare)
    {
        var counter = new Counter(rare, new DropQuantityBounds(4));
        counter.Read(71);

        Assert.Equal(71, Assert.Single(counter.Complete()));
    }

    [Fact]
    public void SpotMinimumTakesPrecedenceOverLegacyItemMinimumAndRetainsProvenance()
    {
        var counter = new CompanionFrameReconciler(new Dictionary<string, uint> { [Item] = 9 });
        var bounds = new DropQuantityBounds(4, 8);
        counter.ProcessFrame([new(Item, uint.MaxValue, 250) { QuantityBounds = bounds }]);
        counter.ProcessFrame([new(Item, uint.MaxValue, 250) { QuantityBounds = bounds }]);

        var drop = Assert.Single(counter.Complete());
        Assert.Equal(4u, drop.Count);
        Assert.True(drop.IsMinimumQuantityEstimate);
        Assert.Equal(bounds, drop.QuantityBounds);
    }

    [Fact]
    public void NewlyInsertedNormalDropIsStillCountedAfterNormalization()
    {
        var counter = new CompanionFrameReconciler();
        var bounds = new DropQuantityBounds(1, 1);
        counter.ProcessFrame([
            new(Item, 7, 250) { QuantityBounds = bounds }, new("Black Stone", 1, 200),
        ]);
        counter.ProcessFrame([
            new(Item, 1, 250) { QuantityBounds = bounds },
            new(Item, 1, 200) { QuantityBounds = bounds }, new("Black Stone", 1, 150),
        ]);

        var drops = counter.Complete().Where(entry => entry.Name == Item).ToArray();
        Assert.Equal(2, drops.Length);
        Assert.All(drops, drop => Assert.Equal(1u, drop.Count));
    }

    [Fact]
    public void RareAliasCorrectionRemainsNegativeWithQuantityBounds()
    {
        var counter = new CompanionRareFrameReconciler();
        var bounds = new DropQuantityBounds(1, 1);
        counter.ProcessFrame([new("Crystal of Origin", 7) { QuantityBounds = bounds }]);
        Assert.Equal(1, Assert.Single(counter.Complete()).Count);
        counter.ProcessFrame([new("Silent Crystal of Origin", 7) { QuantityBounds = bounds }]);

        Assert.Equal(
            [new CompanionRareCountDelta("Crystal of Origin", -1), new("Silent Crystal of Origin", 1)],
            counter.Complete());
    }

    private sealed class Counter(bool rare, DropQuantityBounds bounds)
    {
        private readonly CompanionFrameReconciler normalCounter = new();
        private readonly CompanionRareFrameReconciler rareCounter = new();
        private readonly List<int> events = [];

        public void Read(int? quantity)
        {
            if (rare)
                events.AddRange(rareCounter.ProcessFrame([
                    new(Item, quantity ?? -1, 250) { QuantityBounds = bounds },
                ]).Select(entry => entry.Count));
            else
                events.AddRange(normalCounter.ProcessFrame([
                    new(Item, quantity.HasValue ? checked((uint)quantity.Value) : uint.MaxValue, 250)
                    { QuantityBounds = bounds },
                ]).Select(entry => checked((int)entry.Count)));
        }

        public void Empty()
        {
            if (rare) events.AddRange(rareCounter.ProcessFrame([]).Select(entry => entry.Count));
            else events.AddRange(normalCounter.ProcessFrame([]).Select(entry => checked((int)entry.Count)));
        }

        public IReadOnlyList<int> Complete()
        {
            if (rare) events.AddRange(rareCounter.Complete().Select(entry => entry.Count));
            else events.AddRange(normalCounter.Complete().Select(entry => checked((int)entry.Count)));
            return events;
        }
    }
}
