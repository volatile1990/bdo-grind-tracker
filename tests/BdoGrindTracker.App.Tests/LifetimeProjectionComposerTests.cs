using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeProjectionComposerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameItemInIndependentLogsAddsQuantitiesAndPhysicalDropCounts()
    {
        var composer = new LifetimeLootProjectionComposer();
        var result = composer.Combine(Snapshot(("Twilight Ring", 1)),
            Snapshot(("Twilight Ring", 3)) with { SupportedDropCount = 3, LatestArrivalAt = Start.AddSeconds(1) },
            Start.AddSeconds(2));
        Assert.Equal(4, result.Projection.Totals["Twilight Ring"]);
        Assert.Equal(4, result.Projection.ConfirmedDropCount);
        Assert.Equal(Start.AddSeconds(1), result.Projection.LatestArrivalAt);
        Assert.Equal(4, Assert.Single(result.Events).Quantity);
    }

    [Fact]
    public void IndependentSourceRetractionsReplaceQuantitiesCountsAndArrivalEvidence()
    {
        var composer = new LifetimeLootProjectionComposer();
        var normal = Snapshot(("Ring", 2));
        var special = Snapshot(("Ring", 3)) with { LatestArrivalAt = Start.AddSeconds(1) };
        var first = composer.Combine(normal, special, Start.AddSeconds(2));
        var corrected = composer.Combine(normal, special with { Totals = Snapshot(("Ring", 1)).Totals }, Start.AddSeconds(3));
        Assert.Equal(3, corrected.Projection.Totals["Ring"]);
        Assert.Equal(-2, Assert.Single(corrected.Events).Quantity);
        Assert.Equal(first.Projection.ConfirmedDropCount, corrected.Projection.ConfirmedDropCount);
        Assert.Equal(first.Projection.LatestArrivalAt, corrected.Projection.LatestArrivalAt);
        var retracted = composer.Combine(normal, Snapshot(), Start.AddSeconds(4));
        Assert.Equal(2, retracted.Projection.Totals["Ring"]);
        Assert.Equal(-1, Assert.Single(retracted.Events).Quantity);
        Assert.Equal(1, retracted.Projection.ConfirmedDropCount);
        Assert.Equal(Start, retracted.Projection.LatestArrivalAt);
        Assert.True(retracted.Projection.Revision > corrected.Projection.Revision);
    }

    [Fact]
    public void SeparateLifetimesAndSourceRevisionsCannotRepeatPreviouslyProjectedDrops()
    {
        var composer = new LifetimeLootProjectionComposer();
        var normal = Snapshot(("Helmet", 4));
        var special = Snapshot(("Ring", 1)) with { SelectedLifetimeMs = 6000 };
        var first = composer.Combine(normal, special, Start);
        var unchanged = composer.Combine(normal with { Revision = 20, SelectedLifetimeMs = 1500 },
            special with { Revision = 30, SelectedLifetimeMs = 9000 }, Start.AddSeconds(10));
        Assert.Empty(unchanged.Events);
        Assert.Equal(first.Projection.Revision, unchanged.Projection.Revision);
        Assert.Equal(2, unchanged.Projection.ConfirmedDropCount);
        Assert.Equal(first.Projection.LatestArrivalAt, unchanged.Projection.LatestArrivalAt);
        Assert.Equal(first.Events, new LifetimeLootProjectionComposer().Combine(normal, special, Start).Events);
    }

    [Fact]
    public void InvalidIndependentSourceCannotPartiallyMutateTheProjection()
    {
        var composer = new LifetimeLootProjectionComposer();
        var normal = Snapshot(("Ring", 2));
        var special = Snapshot(("Ring", 1));
        var first = composer.Combine(normal, special, Start);
        Assert.Throws<ArgumentException>(() => composer.Combine(normal, Snapshot(("Ring", -1)), Start));
        Assert.Throws<ArgumentException>(() => composer.Combine(normal,
            special with { SupportedDropCount = -1 }, Start));
        Assert.Throws<OverflowException>(() => composer.Combine(normal, Snapshot(("Ring", long.MaxValue)), Start));
        Assert.Throws<OverflowException>(() => composer.Combine(normal,
            special with { SupportedDropCount = int.MaxValue }, Start));
        var unchanged = composer.Combine(normal, special, Start.AddSeconds(1));
        Assert.Empty(unchanged.Events);
        Assert.Equal(first.Projection.Revision, unchanged.Projection.Revision);
        Assert.Equal(3, unchanged.Projection.Totals["Ring"]);
    }

    [Fact]
    public void ResetClearsBothIndependentSourcesAndAuditRevision()
    {
        var composer = new LifetimeLootProjectionComposer();
        var first = composer.Combine(Snapshot(("Ring", 2)), Snapshot(("Ring", 1)), Start);
        composer.Reset();
        var empty = composer.Combine(Snapshot(), Snapshot(), Start.AddSeconds(1));
        Assert.Empty(empty.Projection.Totals);
        Assert.Empty(empty.Events);
        Assert.Equal(0, empty.Projection.Revision);
        Assert.Equal(0, empty.Projection.ConfirmedDropCount);
        Assert.Null(empty.Projection.LatestArrivalAt);
        var restarted = composer.Combine(Snapshot(("Ring", 2)), Snapshot(("Ring", 1)), Start);
        Assert.Equal(first.Events, restarted.Events);
    }

    [Fact]
    public void NormalTakesPriorityAndRetractionRestoresTheIndependentRareBalance()
    {
        var composer = new LifetimeLootProjectionComposer();
        var rare = composer.Combine(Snapshot(), [new("Ring", 3)], Start);
        Assert.Equal(3, rare.Projection.Totals["Ring"]);
        var normal = composer.Combine(Snapshot(("Ring", 1)), [], Start.AddMilliseconds(200));
        Assert.Equal(1, normal.Projection.Totals["Ring"]); // Feed priority, neither sum nor maximum.
        Assert.Equal(-2, Assert.Single(normal.Events).Quantity);
        var rollback = composer.Combine(Snapshot(), [], Start.AddMilliseconds(400));
        Assert.Equal(3, rollback.Projection.Totals["Ring"]);
        Assert.Equal(2, Assert.Single(rollback.Events).Quantity);
        Assert.Equal(Start, rollback.Projection.LatestArrivalAt);
        var rareRevision = composer.Combine(Snapshot(), [new("Ring", -1)], Start.AddMilliseconds(600));
        Assert.Equal(2, rareRevision.Projection.Totals["Ring"]);
    }

    [Fact]
    public void SameTotalWithDifferentItemsStillProducesANewAtomicRevision()
    {
        var composer = new LifetimeLootProjectionComposer();
        var first = composer.Combine(Snapshot(("A", 4)), [], Start);
        var renamed = composer.Combine(Snapshot(("B", 4)), [], Start.AddMilliseconds(200));
        Assert.True(renamed.Projection.Revision > first.Projection.Revision);
        Assert.DoesNotContain("A", renamed.Projection.Totals.Keys);
        Assert.Equal(4, renamed.Projection.Totals["B"]);
        Assert.Equal(0, renamed.Events.Sum(change => change.Quantity));
        Assert.Equal(2, renamed.Events.Count);
        Assert.Equal(first.Projection.LatestArrivalAt, renamed.Projection.LatestArrivalAt);
    }

    [Fact]
    public void CompletionIsIdempotentAndAuditIdsReplayDeterministically()
    {
        var left = new LifetimeLootProjectionComposer();
        var right = new LifetimeLootProjectionComposer();
        var normal = Snapshot(("Helmet", 576));
        var first = left.Combine(normal, [new("Ring", 1)], Start);
        var replay = right.Combine(normal, [new("Ring", 1)], Start);
        Assert.Equal(first.Events, replay.Events);
        var completion = left.Combine(normal, [], Start.AddSeconds(1));
        Assert.Empty(completion.Events);
        Assert.Equal(first.Projection.Revision, completion.Projection.Revision);
        Assert.Equal(first.Projection.LatestArrivalAt, completion.Projection.LatestArrivalAt);
    }

    [Fact]
    public void InvalidRareBatchCannotPartiallyMutateTheProjection()
    {
        var composer = new LifetimeLootProjectionComposer();
        var first = composer.Combine(Snapshot(("Helmet", 4)), [new("Ring", 1)], Start);
        Assert.Throws<InvalidOperationException>(() => composer.Combine(Snapshot(),
            [new("Ring", 1), new("Unbooked", -1)], Start.AddMilliseconds(200)));
        var unchanged = composer.Combine(Snapshot(("Helmet", 4)), [], Start.AddMilliseconds(400));
        Assert.Equal(1, unchanged.Projection.Totals["Ring"]);
        Assert.Empty(unchanged.Events);
        Assert.Equal(first.Projection.Revision, unchanged.Projection.Revision);
    }

    [Fact]
    public void QuantityCorrectionDoesNotBecomeANewArrivalAndResetClearsBothSources()
    {
        var composer = new LifetimeLootProjectionComposer();
        var first = composer.Combine(Snapshot(("Helmet", 500)), [], Start);
        var corrected = composer.Combine(Snapshot(("Helmet", 4)), [], Start.AddSeconds(1));
        Assert.Equal(-496, Assert.Single(corrected.Events).Quantity);
        Assert.Equal(first.Projection.LatestArrivalAt, corrected.Projection.LatestArrivalAt);
        Assert.Equal(first.Projection.ConfirmedDropCount, corrected.Projection.ConfirmedDropCount);
        composer.Reset();
        var reset = composer.Combine(Snapshot(), [], Start.AddSeconds(2));
        Assert.Empty(reset.Projection.Totals);
        Assert.Equal(0, reset.Projection.Revision);
        Assert.Null(reset.Projection.LatestArrivalAt);
    }

    private static LifetimeSnapshot Snapshot(params (string Name, long Quantity)[] entries) =>
        new(1, 1, Start, 1350, entries.ToDictionary(entry => entry.Name, entry => entry.Quantity),
            new Dictionary<string, long>(), [], entries.Length, entries.Length == 0 ? null : Start, []);

    [Fact]
    public void RareStackQuantityIsOneArrivalAndPartialCorrectionKeepsThatArrival()
    {
        var composer = new LifetimeLootProjectionComposer();
        var stack = composer.Combine(Snapshot(), [new("Rare stack", 10)], Start);
        Assert.Equal(10, stack.Projection.Totals["Rare stack"]);
        Assert.Equal(1, stack.Projection.ConfirmedDropCount);
        var partial = composer.Combine(Snapshot(), [new("Rare stack", -1)], Start.AddMilliseconds(200));
        Assert.Equal(9, partial.Projection.Totals["Rare stack"]);
        Assert.Equal(1, partial.Projection.ConfirmedDropCount);
        var removed = composer.Combine(Snapshot(), [new("Rare stack", -9)], Start.AddMilliseconds(400));
        Assert.Empty(removed.Projection.Totals);
        Assert.Equal(0, removed.Projection.ConfirmedDropCount);
        Assert.Equal(Start, removed.Projection.LatestArrivalAt);
    }
}
