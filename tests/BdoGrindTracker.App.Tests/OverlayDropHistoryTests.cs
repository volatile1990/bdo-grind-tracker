using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayDropHistoryTests
{
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
        Assert.Single(history.Update(state));
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
    public void DemoShowsTwoSeparateItemMarkers()
    {
        Assert.Equal(2, OverlayChartMarkers.Create(OverlaySnapshot.Demo).Count);
        Assert.All(OverlaySnapshot.Demo.DropMarkers, marker => Assert.NotNull(marker.Item.IconPath));
    }

    private static TrackerState WithLoot(TrackerState state, int seconds, long quantity, int events) => state with {
        Elapsed = TimeSpan.FromSeconds(seconds),
        Loot = new(new Dictionary<string, long> { ["Black Stone"] = quantity }, quantity, events)
    };
}
