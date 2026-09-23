using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayDropHistoryTests
{
    [Fact]
    public void ClockTicksReuseFrozenSnapshotsAndRewindRestoreAndNewDropsInvalidateThem()
    {
        var history = new SessionDropHistory();
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true };
        var empty = history.Update(state);
        Assert.Same(empty, history.Update(state with { Elapsed = TimeSpan.FromSeconds(1) }));
        state = WithLoot(state, 10, 1, 1);
        var first = history.Update(state);
        Assert.Same(first, history.Update(WithLoot(state, 20, 1, 1)));
        // A manual edit updates the baseline but does not invent a drop.
        Assert.Same(first, history.Update(WithLoot(state, 25, 5, 1), manualCorrection: true));
        var second = history.Update(WithLoot(state, 30, 6, 2));
        Assert.Equal(1, second[^1].Quantity);
        Assert.Single(first);
        Assert.Equal(2, second.Count);
        var rewound = history.Update(WithLoot(state, 15, 5, 1));
        Assert.Single(rewound);
        Assert.Equal(2, second.Count);
        history.Restore(state.SessionId, state.Loot, TimeSpan.FromSeconds(10), first);
        Assert.Single(history.Update(state));
        Assert.Empty(history.Update(state with { SessionId = Guid.NewGuid() }));
    }

    [Fact]
    public void TracksRepeatedDropsWithoutDuplicatingTicksOrManualEdits()
    {
        var history = new SessionDropHistory();
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true };
        Assert.Empty(history.Update(state));
        state = WithLoot(state, 10, 1, 1);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(history.Update(state)).Elapsed);
        Assert.Single(history.Update(state));
        state = WithLoot(state, 20, 5, 1);
        Assert.Single(history.Update(state, manualCorrection: true));
        state = WithLoot(state, 30, 6, 2);
        var drops = history.Update(state);
        Assert.Equal(2, drops.Count);
        Assert.Equal(1, drops[1].Quantity);
        Assert.Equal(TimeSpan.FromSeconds(30), drops[1].Elapsed);
        Assert.Empty(history.Update(state with { SessionId = Guid.NewGuid() }));
    }

    [Fact]
    public void RestoredTotalsDoNotInventDropTimesAndRewindRemovesTail()
    {
        var history = new SessionDropHistory();
        var state = WithLoot(new() { SessionId = Guid.NewGuid(), HasSession = true }, 50, 5, 5);
        Assert.Empty(history.Update(state));
        Assert.Single(history.Update(WithLoot(state, 60, 6, 6)));
        Assert.Empty(history.Update(state));
    }

    [Fact]
    public void AutomaticRetractionAndRecoveryDoNotLeaveDuplicateMarkers()
    {
        var history = new SessionDropHistory();
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true };
        history.Update(state);
        var first = history.Update(WithLoot(state, 10, 1, 1));
        var retracted = history.Update(state with { Elapsed = TimeSpan.FromSeconds(20) });
        Assert.Empty(retracted);
        Assert.Single(first);
        var recovered = history.Update(WithLoot(state, 30, 1, 1));
        Assert.Equal(new SessionDropSample(TimeSpan.FromSeconds(30), "Black Stone", 1), Assert.Single(recovered));
        Assert.Same(recovered, history.Update(WithLoot(state, 40, 1, 1)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AutomaticQuantityReductionRemovesNewestSamplesRegardlessOfEventCount(int correctedEvents)
    {
        var history = new SessionDropHistory();
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true };
        history.Update(state);
        history.Update(WithLoot(state, 10, 1, 1));
        var before = history.Update(WithLoot(state, 20, 2, 2));
        var corrected = history.Update(WithLoot(state, 30, 1, correctedEvents));
        Assert.Equal(new SessionDropSample(TimeSpan.FromSeconds(10), "Black Stone", 1), Assert.Single(corrected));
        Assert.Equal(2, before.Count);
        Assert.Same(corrected, history.Update(WithLoot(state, 40, 1, correctedEvents)));
    }

    [Fact]
    public void AutomaticReductionCanTrimAStackWhileAnotherItemArrives()
    {
        var history = new SessionDropHistory();
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true };
        history.Update(state);
        history.Update(WithLoot(state, 10, 3, 1));
        var before = history.Update(WithLoot(state, 20, 5, 2));
        var corrected = history.Update(state with
        {
            Elapsed = TimeSpan.FromSeconds(30),
            Loot = new(new Dictionary<string, long> { ["Black Stone"] = 2, ["Ring"] = 1 }, 3, 3),
        });
        Assert.Equal(new[] { new SessionDropSample(TimeSpan.FromSeconds(10), "Black Stone", 2),
            new SessionDropSample(TimeSpan.FromSeconds(30), "Ring", 1) }, corrected);
        Assert.Equal(new long[] { 3, 2 }, before.Select(drop => drop.Quantity));
    }

    [Fact]
    public void AutomaticUpwardThenDownwardRevisionKeepsTheOriginalDropTime()
    {
        var history = new SessionDropHistory();
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true };
        history.Update(state);
        var first = history.Update(WithLoot(state, 10, 1, 1));
        var increased = history.Update(WithLoot(state, 20, 2, 1));
        Assert.Equal(new SessionDropSample(TimeSpan.FromSeconds(10), "Black Stone", 2), Assert.Single(increased));
        Assert.Equal(first, history.Update(WithLoot(state, 30, 1, 1)));
        Assert.Equal(1, Assert.Single(first).Quantity);
    }

    [Fact]
    public void ManualCorrectionPreservesTimesButLaterAutomaticCorrectionsStillRetractDrops()
    {
        var history = new SessionDropHistory();
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true };
        history.Update(state);
        var first = history.Update(WithLoot(state, 10, 1, 1));
        Assert.Same(first, history.Update(WithLoot(state, 20, 0, 1), manualCorrection: true));
        Assert.Same(first, history.Update(WithLoot(state, 25, 0, 1)));
        var added = history.Update(WithLoot(state, 30, 1, 2));
        Assert.Equal(2, added.Count);
        Assert.Equal(first, history.Update(WithLoot(state, 40, 0, 1)));
    }

    [Fact]
    public void SelectsStrictlyAboveThresholdOrFavoritesRegardlessOfPrice()
    {
        var state = new TrackerState { DropHistory = [
            new(TimeSpan.FromSeconds(10), "Expensive", 1),
            new(TimeSpan.FromSeconds(20), "Boundary", 1),
            new(TimeSpan.FromSeconds(30), "CheapStack", 1000),
            new(TimeSpan.FromSeconds(40), "Favorite", 1)] };
        var prices = new LootPriceSnapshot("eu", [
            new("Expensive", 200_000_001, 0, LootPriceOrigin.LiveMarket, null),
            new("Boundary", 200_000_000, 0, LootPriceOrigin.LiveMarket, null),
            new("CheapStack", 1_000_000, 0, LootPriceOrigin.LiveMarket, null)]);
        var actual = new OverlayMetrics().Update(state, new() { FavoriteItems = ["Favorite"] }, prices);
        Assert.Equal(new[] { "Expensive", "Favorite" }, actual.DropMarkers.Select(marker => marker.Item.CanonicalName));
        Assert.Empty(new OverlayMetrics().Update(state, new()).DropMarkers);
    }

    [Fact]
    public void MarkerCoordinatesInterpolateAndExcludeUnknownTimeRanges()
    {
        var item = new OverlayLootItem("Item", "Item", "1");
        var snapshot = new OverlaySnapshot {
            SilverHistory = [new(TimeSpan.FromSeconds(10), 100), new(TimeSpan.FromSeconds(30), 200)],
            DropMarkers = [new(TimeSpan.FromSeconds(20), item), new(TimeSpan.FromSeconds(40), item)]
        };
        var marker = Assert.Single(OverlayChartMarkers.Create(snapshot));
        Assert.Equal(.5, marker.X);
        Assert.Equal(22d / 72, marker.Y, 8);
    }

    [Fact]
    public void FirstDropBeforeTheFirstRateSampleRemainsVisible()
    {
        var snapshot = new OverlaySnapshot {
            SilverHistory = [new(TimeSpan.FromSeconds(1), 100), new(TimeSpan.FromSeconds(10), 200)],
            DropMarkers = [new(TimeSpan.Zero, new("Item", "Item", "1"))]
        };
        Assert.Equal(0, OverlayChartMarkers.FirstTick(snapshot));
        Assert.Equal(0, Assert.Single(OverlayChartMarkers.Create(snapshot)).X);
    }

    [Fact]
    public void DemoMarksEveryValuableDropOfTheExampleSessionSeparately()
    {
        // Four Twilight of the End rings and one Refined Essence of Devouring.
        Assert.Equal(5, OverlayChartMarkers.Create(OverlaySnapshot.Demo).Count);
        Assert.Equal(4, OverlaySnapshot.Demo.DropMarkers.Count(marker => marker.Item.CanonicalName == "Twilight of the End - Ring"));
        Assert.All(OverlaySnapshot.Demo.DropMarkers, marker => Assert.NotNull(marker.Item.IconPath));
    }

    private static TrackerState WithLoot(TrackerState state, int seconds, long quantity, int events) => state with {
        Elapsed = TimeSpan.FromSeconds(seconds),
        Loot = new(new Dictionary<string, long> { ["Black Stone"] = quantity }, quantity, events)
    };
}
