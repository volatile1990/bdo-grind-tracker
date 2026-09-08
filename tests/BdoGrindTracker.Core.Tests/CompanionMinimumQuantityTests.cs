namespace BdoGrindTracker.Core.Tests;

public sealed class CompanionMinimumQuantityTests
{
    // All minimums below are synthetic policies, not verified drop data.
    private const string Trash = "Elion Follower's Helmet";

    [Fact]
    public void AnEmptyTableRetainsTheLegacyFallbackAndNeighborOrder()
    {
        var result = Run(new CompanionFrameReconciler(new Dictionary<string, uint>()), [null, null, 6]);

        Assert.Equal(new uint[] { 1, 6 }, result.Select(entry => entry.Count));
        Assert.All(result, entry => Assert.False(entry.IsMinimumQuantityEstimate));
        Assert.Equal(Run(new CompanionFrameReconciler(), [null, null, 6]), result);
    }

    [Fact]
    public void UnresolvedNativeEstimateEmitsTheConfiguredMinimum()
    {
        var result = Run(Counter(4), [null, null]);

        var estimate = Assert.Single(result);
        Assert.Equal(4u, estimate.Count);
        Assert.True(estimate.IsMinimumQuantityEstimate);
    }

    [Fact]
    public void MinimumChangesAmountsWithoutChangingRenewalOrEventTiming()
    {
        var legacy = new CompanionFrameReconciler();
        var configured = Counter(4);
        var legacyTimeline = new List<(int Call, string Name, int Y)>();
        var configuredTimeline = new List<(int Call, string Name, int Y)>();
        var configuredEvents = new List<CompanionRecognizedEntry>();
        for (var index = 0; index < 37; index++)
        {
            legacyTimeline.AddRange(legacy.ProcessFrame(Rows(null)).Select(entry => (index, entry.Name, entry.Y)));
            var events = configured.ProcessFrame(Rows(null));
            configuredTimeline.AddRange(events.Select(entry => (index, entry.Name, entry.Y)));
            configuredEvents.AddRange(events);
        }
        legacyTimeline.AddRange(legacy.Complete().Select(entry => (37, entry.Name, entry.Y)));
        var final = configured.Complete();
        configuredTimeline.AddRange(final.Select(entry => (37, entry.Name, entry.Y)));
        configuredEvents.AddRange(final);

        Assert.True(legacyTimeline.Count > 3, "The trace must cross several renewal and batch boundaries.");
        Assert.Equal(legacyTimeline, configuredTimeline);
        Assert.All(configuredEvents, entry =>
        {
            Assert.Equal(4u, entry.Count);
            Assert.True(entry.IsMinimumQuantityEstimate);
        });
    }

    [Fact]
    public void BorrowingAnEstimatedOnePreservesItsProvenance()
    {
        var result = Run(Counter(4), [null, null, null, null, null]);

        Assert.True(result.Count > 1, "The continuing row must reach a renewed event.");
        Assert.All(result, entry =>
        {
            Assert.Equal(4u, entry.Count);
            Assert.True(entry.IsMinimumQuantityEstimate);
        });
    }

    [Fact]
    public void InsertingAMissingMiddleRowCopiesItsEstimateProvenance()
    {
        var counter = Counter(4);
        counter.ProcessFrame([new("A", 1, 250), new("C", 3, 150), new("D", 4, 100)]);
        counter.ProcessFrame([
            new("A", 1, 250), new(Trash, uint.MaxValue, 200),
            new("C", 3, 150), new("D", 4, 100),
        ]);
        counter.ProcessFrame([
            new("A", 1, 250), new(Trash, uint.MaxValue, 200),
            new("C", 3, 150), new("D", 4, 100),
        ]);
        counter.ProcessFrame([]);

        // Native missing-middle repair copies the newly found trash row into
        // the earlier frame. Losing provenance in Entry.Copy would emit 1 here.
        var estimate = Assert.Single(counter.Complete(), entry => entry.Name == Trash);
        Assert.Equal(4u, estimate.Count);
        Assert.Equal(200, estimate.Y);
        Assert.True(estimate.IsMinimumQuantityEstimate);
    }

    [Fact]
    public void AReadSixAfterMissingFramesWinsBeforeTheMinimumFallback()
    {
        var result = Run(Counter(4), [null, null, 6]);

        var actual = Assert.Single(result);
        Assert.Equal(6u, actual.Count);
        Assert.False(actual.IsMinimumQuantityEstimate);
    }

    [Fact]
    public void AReadPreviousNeighborStillWinsBeforeTheMinimumFallback()
    {
        var result = Run(Counter(4), [6, null, null]);

        var actual = Assert.Single(result);
        Assert.Equal(6u, actual.Count);
        Assert.False(actual.IsMinimumQuantityEstimate);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(6u)]
    public void AnExistingReadIsNotRaisedToOrReplacedByTheMinimum(uint quantity)
    {
        var result = Run(Counter(4), [quantity, quantity]);

        var actual = Assert.Single(result);
        Assert.Equal(quantity, actual.Count);
        Assert.False(actual.IsMinimumQuantityEstimate);
    }

    [Fact]
    public void ItemsWithoutConfiguredMinimumKeepTheirLegacyFallback()
    {
        var counter = Counter(4);
        counter.ProcessFrame([new("Ancient Spirit Dust", uint.MaxValue, 250)]);
        counter.ProcessFrame([new("Ancient Spirit Dust", uint.MaxValue, 250)]);
        counter.ProcessFrame([]);

        var actual = Assert.Single(counter.Complete());
        Assert.Equal(1u, actual.Count);
        Assert.False(actual.IsMinimumQuantityEstimate);
    }

    [Fact]
    public void TheConfiguredReadRepairDoesNotChangeUnconfiguredItems()
    {
        var counter = Counter(4);
        counter.ProcessFrame([new("Ancient Spirit Dust", uint.MaxValue, 250)]);
        counter.ProcessFrame([new("Ancient Spirit Dust", uint.MaxValue, 250)]);
        counter.ProcessFrame([new("Ancient Spirit Dust", 6, 250)]);

        Assert.Equal(new uint[] { 1, 6 }, counter.Complete().Select(entry => entry.Count));
    }

    [Fact]
    public void KnownUnitItemsKeepTheirNativeQuantityRule()
    {
        var counter = new CompanionFrameReconciler(new Dictionary<string, uint> { ["Dawn Crystal"] = 4 });
        counter.ProcessFrame([new("Dawn Crystal", uint.MaxValue, 250)]);
        counter.ProcessFrame([]);

        var actual = Assert.Single(counter.Complete());
        Assert.Equal(1u, actual.Count);
        Assert.False(actual.IsMinimumQuantityEstimate);
    }

    [Fact]
    public void AnIsolatedOrFinalUnresolvedRowIsNotTurnedIntoANewEstimate()
    {
        var counter = Counter(4);
        counter.ProcessFrame(Rows(null));

        Assert.Empty(counter.Complete());
        counter.Reset();
        counter.ProcessFrame(Rows(null));
        counter.ProcessFrame([]);
        Assert.Empty(counter.Complete());
    }

    [Fact]
    public void TheMinimumTableIsCopiedInsteadOfFollowingCallerMutations()
    {
        var values = new Dictionary<string, uint> { [Trash] = 4 };
        var counter = new CompanionFrameReconciler(values);
        values[Trash] = 99;
        values.Clear();

        Assert.Equal(4u, Assert.Single(Run(counter, [null, null])).Count);
    }

    [Theory]
    [InlineData("", 4u)]
    [InlineData(" ", 4u)]
    [InlineData(Trash, 0u)]
    [InlineData(Trash, 2147483648u)]
    [InlineData(Trash, uint.MaxValue)]
    public void InvalidMinimumTableEntriesAreRejected(string name, uint value)
    {
        Assert.Throws<ArgumentException>(() =>
            new CompanionFrameReconciler(new Dictionary<string, uint> { [name] = value }));
    }

    [Fact]
    public void TheLargestApplicationRepresentableMinimumIsAccepted()
    {
        var actual = Assert.Single(Run(Counter(int.MaxValue), [null, null]));

        Assert.Equal((uint)int.MaxValue, actual.Count);
        Assert.Equal(int.MaxValue, checked((int)actual.Count));
    }

    [Fact]
    public void ResetClearsPendingEstimateProvenanceButKeepsTheCopiedPolicy()
    {
        var counter = Counter(4);
        Assert.True(Assert.Single(Run(counter, [null, null])).IsMinimumQuantityEstimate);
        counter.Reset();

        var read = Assert.Single(Run(counter, [1, 1]));
        Assert.Equal(1u, read.Count);
        Assert.False(read.IsMinimumQuantityEstimate);
        counter.Reset();
        Assert.Equal(4u, Assert.Single(Run(counter, [null, null])).Count);
    }

    [Fact]
    public void ALaterReadDoesNotRepairAlreadyProcessedFramesAndSuppressTheNewRead()
    {
        var counter = Counter(4);
        IReadOnlyList<CompanionRecognizedEntry> alreadyEmitted = [];
        for (var index = 0; index < CompanionFrameReconciler.BatchSize; index++)
            alreadyEmitted = counter.ProcessFrame(Rows(null));
        Assert.NotEmpty(alreadyEmitted);
        Assert.All(alreadyEmitted, entry => Assert.Equal(4u, entry.Count));
        counter.ProcessFrame(Rows(6));

        // The previous batch's final missing row was not emitted. Repairing it
        // retroactively to this new six would make the new frame a duplicate
        // of a six that was never actually booked.
        var later = Assert.Single(counter.Complete());
        Assert.Equal(6u, later.Count);
        Assert.False(later.IsMinimumQuantityEstimate);
        Assert.All(alreadyEmitted, entry => Assert.Equal(4u, entry.Count));
    }

    private static CompanionFrameReconciler Counter(uint minimum) =>
        new(new Dictionary<string, uint> { [Trash] = minimum });

    private static CompanionRecognizedEntry[] Rows(uint? quantity) =>
        [new(Trash, quantity ?? uint.MaxValue, 250)];

    private static IReadOnlyList<CompanionRecognizedEntry> Run(
        CompanionFrameReconciler counter, IEnumerable<uint?> quantities)
    {
        var events = new List<CompanionRecognizedEntry>();
        foreach (var quantity in quantities) events.AddRange(counter.ProcessFrame(Rows(quantity)));
        events.AddRange(counter.ProcessFrame([]));
        events.AddRange(counter.Complete());
        return events;
    }
}
