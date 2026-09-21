using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayProjectionCachingTests
{
    [Fact]
    public void ClockTicksReuseEquivalentLootAndChartsWhileTimeKeepsAdvancing()
    {
        var metrics = new OverlayMetrics();
        var state = State();
        var preferences = new TrackerPreferences { GameLanguage = "en", UiLanguage = "en" };
        var prices = Prices(300_000_000m);
        var first = metrics.Update(state, preferences, prices);
        var tick = state with
        {
            Elapsed = TimeSpan.FromSeconds(20),
            DropHistory = state.DropHistory.ToArray(),
            Loot = new(new Dictionary<string, long>(state.Loot.Totals), 1000, 1),
            Buffs = state.Buffs! with { Consumptions = state.Buffs.Consumptions.ToArray() },
        };
        var next = metrics.Update(tick, preferences, prices);
        Assert.Same(first.Drops, next.Drops);
        Assert.Same(first.RareDrops, next.RareDrops);
        Assert.Same(first.SilverDrops, next.SilverDrops);
        Assert.Same(first.DropMarkers, next.DropMarkers);
        Assert.Same(first.Consumables, next.Consumables);
        Assert.NotEqual(first.Metrics["duration"], next.Metrics["duration"]);
        Assert.Equal(TimeSpan.FromSeconds(20), next.SessionElapsed);
    }

    [Fact]
    public void ChartsRefreshForPricesTaxesFavoritesLanguagesAndRewinds()
    {
        var metrics = new OverlayMetrics();
        var state = State();
        var preferences = new TrackerPreferences { GameLanguage = "en", UiLanguage = "en" };
        var prices = Prices(300_000_000m);
        var previous = metrics.Update(state, preferences, prices);
        Assert.Single(previous.DropMarkers);

        Check();
        prices = Prices(100_000_000m);
        Check();
        Assert.Empty(previous.DropMarkers);
        preferences = preferences with { FavoriteItems = ["Caphras Stone"] };
        Check();
        Assert.Single(previous.DropMarkers);
        preferences = preferences with { ValuePack = true, MerchantRing = true, FamilyFame = 7000 };
        Check();
        preferences = preferences with { UiLanguage = "de", GameLanguage = "de" };
        Check();
        Assert.Equal("1.000", Assert.Single(previous.DropMarkers).Item.QuantityText);
        state = state with { DropHistory = [] };
        Check();
        Assert.Empty(previous.DropMarkers);
        Assert.Empty(previous.SilverDrops);
        state = State() with { DropHistory = [new(TimeSpan.FromSeconds(5), "Caphras Stone", 2)] };
        Check();
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(previous.SilverDrops).Elapsed);

        void Check()
        {
            var actual = metrics.Update(state, preferences, prices);
            var fresh = new OverlayMetrics().Update(state, preferences, prices);
            Assert.Equal(fresh.SilverDrops, actual.SilverDrops);
            Assert.Equal(fresh.DropMarkers, actual.DropMarkers);
            previous = actual;
        }
    }

    [Fact]
    public void EqualLengthDropCorrectionsAndLootQuantityChangesInvalidate()
    {
        var metrics = new OverlayMetrics();
        var state = State();
        var preferences = new TrackerPreferences();
        var prices = Prices(300_000_000m);
        var first = metrics.Update(state, preferences, prices);
        var correction = state with
        {
            DropHistory = [state.DropHistory[0] with { Quantity = 2 }],
            Loot = new(new Dictionary<string, long> { ["Caphras Stone"] = 2 }, 2, 1),
        };
        var next = metrics.Update(correction, preferences, prices);
        Assert.Equal(2, Assert.Single(next.Drops).Quantity);
        Assert.Equal(2, Assert.Single(next.DropMarkers).Item.Quantity);
        Assert.NotEqual(first.SilverDrops, next.SilverDrops);
        var localized = metrics.Update(correction, preferences with { GameLanguage = "de" }, prices);
        Assert.Equal("Caphras-Stein", Assert.Single(localized.Drops).Name);
    }

    [Fact]
    public void ConsumableObservationPriceAndLanguageChangesRefreshEvenAtEqualCounts()
    {
        var metrics = new OverlayMetrics();
        var preferences = new TrackerPreferences { UiLanguage = "en" };
        var unknown = metrics.Update(new(), preferences).Consumables;
        var empty = metrics.Update(new() { Buffs = BuffLedgerSnapshot.Empty }, preferences).Consumables;
        Assert.NotEqual(unknown.Cost, empty.Cost);
        var state = State();
        var first = metrics.Update(state, preferences).Consumables;
        var purchase = state.Buffs!.Consumptions[0];
        state = state with { Buffs = state.Buffs with
        {
            Consumptions = [purchase with { Price = purchase.Price! with { UnitPrice = 200 } }],
        } };
        var repriced = metrics.Update(state, preferences).Consumables;
        Assert.Equal(200, Assert.Single(repriced.Items).KnownCost);
        Assert.NotEqual(first.Cost, repriced.Cost);
        var translated = metrics.Update(state, preferences with { UiLanguage = "de" }).Consumables;
        Assert.NotEqual(repriced.Items[0].Name, translated.Items[0].Name);
    }

    [Fact]
    public void NativeConsumablesRedrawWhenItemsChangeAtTheSameTotalCost()
    {
        var renderer = new NativeOverlayRenderState();
        var settings = new OverlaySettings { Widgets = [OverlayCatalog.CreateWidget("consumables")] };
        var item = new ConsumableItem("one", "One", null, 1, 100, 0, "One");
        var snapshot = new OverlaySnapshot { Consumables = new([item], "100", "100", false) };
        renderer.Remember(settings, snapshot, new(360, 260));
        Assert.True(renderer.Matches(settings, snapshot with
        {
            Consumables = snapshot.Consumables with { Items = [item with { }] },
        }, new(360, 260)));
        Assert.False(renderer.Matches(settings, snapshot with
        {
            Consumables = snapshot.Consumables with { Items = [item with { Id = "two", Name = "Two" }] },
        }, new(360, 260)));
        Assert.False(renderer.Matches(settings, snapshot with
        {
            Consumables = snapshot.Consumables with { Items = [item with { Count = 2 }] },
        }, new(360, 260)));
    }

    [Fact]
    public void DailyNetReusesHistorySnapshotAndRefreshesOnRevisionAndDeletion()
    {
        var projection = new DailyNetProjection();
        var start = DateTimeOffset.Now;
        var day = DateOnly.FromDateTime(start.LocalDateTime);
        var entry = new LootHistoryEntry { SessionId = Guid.NewGuid(), StartedAt = start,
            UpdatedAt = start, Duration = TimeSpan.FromHours(1), SpotId = "hermesia",
            Totals = new Dictionary<string, long>(), SilverBeforeTax = 100, SilverAfterTax = 100, SilverIsComplete = true };
        IReadOnlyList<LootHistoryEntry> history = [entry];
        var first = projection.Update(history);
        Assert.Same(first, projection.Update(history));
        Assert.Equal(100, first[day]);
        var revised = projection.Update([entry, entry with { UpdatedAt = start.AddMinutes(1), SilverAfterTax = 250 }]);
        Assert.Equal(250, revised[day]);
        Assert.Equal(100, first[day]);
        Assert.Empty(projection.Update([]));
    }

    private static TrackerState State() => new()
    {
        HasSession = true, Elapsed = TimeSpan.FromSeconds(10),
        Loot = new(new Dictionary<string, long> { ["Caphras Stone"] = 1000 }, 1000, 1),
        DropHistory = [new(TimeSpan.FromSeconds(10), "Caphras Stone", 1000)],
        Buffs = new([new("simple-cron-meal", "Simple Cron Meal", null, DateTimeOffset.UnixEpoch,
            new(100, "eu", DateTimeOffset.UnixEpoch, false))], [], []),
    };

    private static LootPriceSnapshot Prices(decimal amount) => new("eu",
        [new("Caphras Stone", amount, 0, LootPriceOrigin.LiveMarket, null)]);
}
