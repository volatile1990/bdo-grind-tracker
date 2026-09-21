using System.Text.Json;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class BuffCatalogTests
{
    [Fact]
    public void WhitelistContainsOnlyRequestedFamiliesAndDistinctIdentities()
    {
        var definitions = BuffPriceCatalog.Definitions;
        Assert.Equal(58, definitions.Count);
        Assert.Equal(58, definitions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, definitions.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, definitions.Count(item => item.Category == "Cron-Mahlzeiten"));
        Assert.Equal(10, definitions.Count(item => item.Category == "Harmony Draughts"));
        Assert.Equal(20, definitions.Count(item => item.Category == "Parfüms"));
        Assert.Equal(19, definitions.Count(item => item.Category == "Kostenpflichtige Zeltbuffs"));
        Assert.Equal(6, definitions.Count(item => item.Category == "Mystic-Beasts-Schriftrollen"));
        Assert.DoesNotContain(definitions, item => item.Id is "frenzy-draught" or "beasts-draught" or "giants-draught");
        Assert.All(definitions, item => Assert.True(item.MarketItemId is > 0 || item.FixedUnitPrice is > 0));
    }

    [Fact]
    public void MarketQueriesIncludeAllThirtyNineConsumablesAndNoTentItems()
    {
        var market = BuffPriceCatalog.MarketDefinitions().ToArray();
        Assert.Equal(39, market.Length);
        Assert.Equal(new[] { 9691, 9692, 9693 }, ItemIds("Cron-Mahlzeiten"));
        Assert.Equal(new[] { 1399, 1400, 1401, 1402, 1403, 1404, 1405, 1406, 1407, 1408 }, ItemIds("Harmony Draughts"));
        Assert.Equal(new[] { 767969, 767970, 767971, 767972, 767973, 790781 }, ItemIds("Mystic-Beasts-Schriftrollen"));
        Assert.All(BuffPriceCatalog.Definitions.Where(item => item.FixedUnitPrice is not null), tent =>
        {
            Assert.Null(tent.MarketItemId);
            Assert.DoesNotContain(market, item => item.ItemName == tent.Name);
        });
        Assert.DoesNotContain(market, item => item.MarketItemId is 43929 or 761880);
    }

    [Theory]
    [InlineData("tent-turning-gates-60", 60, 200_000)]
    [InlineData("tent-turning-gates-90", 90, 300_000)]
    [InlineData("tent-turning-gates-120", 120, 450_000)]
    [InlineData("tent-turning-gates-180", 180, 900_000)]
    [InlineData("tent-turning-gates-300", 300, 2_000_000)]
    [InlineData("tent-body-enhancement-60", 60, 1_000_000)]
    [InlineData("tent-body-enhancement-90", 90, 1_500_000)]
    [InlineData("tent-body-enhancement-120", 120, 2_250_000)]
    [InlineData("tent-body-enhancement-180", 180, 4_500_000)]
    [InlineData("tent-body-enhancement-300", 300, 10_000_000)]
    [InlineData("tent-adventures-boon-60", 60, 1_750_000)]
    [InlineData("tent-adventures-boon-120", 120, 3_500_000)]
    [InlineData("tent-adventures-boon-300", 300, 12_000_000)]
    [InlineData("tent-adventurers-confidence", 60, 600_000)]
    [InlineData("tent-adventurers-luck-i", 60, 10_000_000)]
    [InlineData("tent-adventurers-luck-ii", 60, 20_000_000)]
    [InlineData("tent-adventurers-luck-iii", 60, 30_000_000)]
    [InlineData("tent-adventurers-luck-iv", 60, 40_000_000)]
    [InlineData("tent-adventurers-luck-v", 60, 50_000_000)]
    public void TentUsesVerifiedPurchasePriceWithoutMarketQuote(string id, int minutes, int silver)
    {
        var buff = BuffPriceCatalog.Definitions.Single(item => item.Id == id);
        Assert.Equal(TimeSpan.FromMinutes(minutes), buff.Duration);
        var quote = BuffPriceCatalog.GetPrice(buff, new LootPriceSnapshot("eu", []));
        Assert.NotNull(quote);
        Assert.Equal((decimal)silver, quote.UnitPrice);
        Assert.Equal(BuffPriceSource.FixedNpc, quote.Source);
        Assert.Null(quote.FetchedAt);
        Assert.False(quote.IsStale);
    }

    [Fact]
    public void NormalAndImmortalRetainSeparateMarketValuesButSameRecognitionFamily()
    {
        var normal = BuffPriceCatalog.Definitions.Single(item => item.Id == "perfume-of-courage");
        var immortal = BuffPriceCatalog.Definitions.Single(item => item.Id == "immortal-perfume-of-courage");
        Assert.Equal(normal.RecognitionGroup, immortal.RecognitionGroup);
        Assert.NotEqual(normal.MarketItemId, immortal.MarketItemId);
        var at = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var snapshot = new LootPriceSnapshot("na", [
            new(normal.Name, 5_000_000m, 0, LootPriceOrigin.CachedMarket, at, true),
        ]);
        var quote = BuffPriceCatalog.GetPrice(normal, snapshot);
        Assert.Equal(new BuffPrice(5_000_000m, "na", at, true), quote);
        Assert.Equal(BuffPriceSource.CentralMarket, quote!.Source);
        Assert.Null(BuffPriceCatalog.GetPrice(immortal, snapshot));
    }

    [Fact]
    public void PartyHarmonyEffectsPermitVisibleConsumptionWithoutClaimingItsSource()
    {
        var party = BuffPriceCatalog.Definitions.Where(item => item.Name.StartsWith("[Party]", StringComparison.Ordinal)).ToArray();
        Assert.Equal(8, party.Length);
        Assert.All(party, item => Assert.False(item.RequiresConsumptionConfirmation));
        Assert.False(new BuffObservation(party[0].Id, TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(1))
            .ConsumptionAttributionConfirmed);
    }

    [Fact]
    public void HistoricalRemovedBuffsAndOldPriceJsonRemainReadable()
    {
        var legacy = BuffPriceCatalog.HistoryDefinitions.Single(item => item.Id == "frenzy-draught");
        var at = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var oldPrice = JsonSerializer.Deserialize<BuffPrice>("""{"UnitPrice":1200000,"Region":"eu","FetchedAt":null,"IsStale":false}""");
        Assert.Equal(BuffPriceSource.CentralMarket, oldPrice!.Source);
        var snapshot = new BuffLedgerSnapshot(
            [new(legacy.Id, legacy.Name, legacy.MarketItemId, at, oldPrice)],
            [new(legacy.Id, legacy.Name, legacy.MarketItemId, TimeSpan.FromMinutes(5), 300_000m, TimeSpan.Zero)],
            [new(legacy.Id, legacy.Name, legacy.MarketItemId, TimeSpan.FromMinutes(10), at, oldPrice, false)]);
        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions);
        ledger.Restore(snapshot);
        Assert.Equal(1_200_000m, ledger.Snapshot.ConsumedCost);
        Assert.Equal(300_000m, ledger.Snapshot.ProratedCost);
        Assert.Empty(ledger.Snapshot.Active);
    }

    private static int[] ItemIds(string category) => BuffPriceCatalog.Definitions
        .Where(item => item.Category == category).Select(item => item.MarketItemId!.Value).Order().ToArray();
}
