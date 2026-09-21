using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffPriceCatalogTests
{
    [Fact]
    public void CatalogUsesDistinctIdentitiesAndCurrentMarketConsumables()
    {
        Assert.Equal(BuffPriceCatalog.Definitions.Count,
            BuffPriceCatalog.Definitions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var buff in BuffPriceCatalog.Definitions.Where(item => item.FixedUnitPrice is null))
        {
            Assert.True(buff.Duration > TimeSpan.Zero);
            Assert.Contains(buff.MarketItemId!.Value, LootPriceCatalog.MarketItemIds);
            var market = Assert.Single(LootPriceCatalog.Definitions, item => item.ItemName == buff.Name);
            Assert.Equal(buff.MarketItemId, market.MarketItemId);
            Assert.Equal(LootPriceKind.Market, market.Kind);
        }
        Assert.Equal(TimeSpan.FromMinutes(20), BuffPriceCatalog.Definitions.Single(item => item.Id == "harmony-draught").Duration);
        Assert.Equal(TimeSpan.FromMinutes(120), BuffPriceCatalog.Definitions.Single(item => item.Id == "simple-cron-meal").Duration);
    }

    [Fact]
    public void ConsumptionCostUsesFullMarketValueAndRetainsOfflineMetadata()
    {
        var fetched = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var buff = BuffPriceCatalog.Definitions.Single(item => item.Id == "harmony-draught");
        var snapshot = new LootPriceSnapshot("na", [
            new(buff.Name, 2_000_000, 0, LootPriceOrigin.CachedMarket, fetched, true),
        ]);

        Assert.Equal(new BuffPrice(2_000_000, "na", fetched, true), BuffPriceCatalog.GetPrice(buff, snapshot));
        Assert.Null(BuffPriceCatalog.GetPrice(buff, LootPriceCatalog.FixedSnapshot("eu")));
        Assert.Null(BuffPriceCatalog.GetPrice(buff with { MarketItemId = null }, snapshot));
        Assert.Null(BuffPriceCatalog.GetPrice(buff with { MarketItemId = int.MaxValue }, snapshot));
    }

    [Theory]
    [InlineData("immortal-harmony-draught", "harmony-draught", 1399)]
    [InlineData("immortal-harmony-draught-human", "harmony-draught-human", 1401)]
    [InlineData("immortal-harmony-draught-demihuman", "harmony-draught-demihuman", 1403)]
    [InlineData("immortal-harmony-draught-kamasylvia", "harmony-draught-kamasylvia", 1405)]
    [InlineData("immortal-harmony-draught-edania", "harmony-draught-edania", 1407)]
    public void HarmonyRecognitionRetainsBothVariantsAndUsesTheirOwnMarketPrices(string immortalId, string normalId, int normalMarketId)
    {
        var normal = Assert.IsType<BuffDefinition>(BuffPriceCatalog.ResolveRecognitionDefinition(normalId));
        var immortal = Assert.IsType<BuffDefinition>(BuffPriceCatalog.ResolveRecognitionDefinition(immortalId));
        Assert.Equal(normalId, normal.Id);
        Assert.Equal(immortalId, immortal.Id);
        Assert.Equal(normalMarketId, normal.MarketItemId);
        Assert.Equal(normalMarketId + 1, immortal.MarketItemId);
        Assert.NotEqual(normal.RecognitionGroup, immortal.RecognitionGroup);
        Assert.Contains(BuffPriceCatalog.MarketDefinitions(), item => item.MarketItemId == immortal.MarketItemId);
        var fetched = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var snapshot = new LootPriceSnapshot("na", [
            new(normal.Name, 1_200_000m, 0, LootPriceOrigin.CachedMarket, fetched, true),
            new(immortal.Name, 9_000_000m, 0, LootPriceOrigin.LiveMarket, fetched),
        ]);

        Assert.Equal(new BuffPrice(1_200_000m, "na", fetched, true), BuffPriceCatalog.GetPrice(normal, snapshot));
        Assert.Equal(new BuffPrice(9_000_000m, "na", fetched, false), BuffPriceCatalog.GetPrice(immortal, snapshot));
        Assert.Null(BuffPriceCatalog.GetPrice(immortal, new LootPriceSnapshot("na", [
            new(normal.Name, 1_200_000m, 0, LootPriceOrigin.LiveMarket, fetched),
        ])));
        Assert.Null(BuffPriceCatalog.GetPrice(normal, new LootPriceSnapshot("na", [
            new(immortal.Name, 9_000_000m, 0, LootPriceOrigin.LiveMarket, fetched),
        ])));
    }

    [Fact]
    public void NewImmortalConsumptionPreservesPreviouslyBookedNormalHarmonyIdentityAndCost()
    {
        var at = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var normal = BuffPriceCatalog.ResolveRecognitionDefinition("harmony-draught")!;
        var immortal = BuffPriceCatalog.ResolveRecognitionDefinition("immortal-harmony-draught")!;
        var historicalPrice = new BuffPrice(1_200_000m, "eu", at.AddDays(-1), false);
        var old = new BuffConsumption(normal.Id, normal.Name, normal.MarketItemId, at.AddDays(-1), historicalPrice);
        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions);
        ledger.Restore(new([old], [], []));
        var prices = new LootPriceSnapshot("eu", [new(immortal.Name, 9_000_000m, 0, LootPriceOrigin.LiveMarket, at)]);
        BuffPrice? Price(BuffDefinition definition) => BuffPriceCatalog.GetPrice(definition, prices);
        BuffObservation Observe(string id, int seconds) => new(
            BuffPriceCatalog.ResolveRecognitionDefinition(id)!.Id, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1));
        ledger.Apply([Observe(immortal.Id, 60)], at, Price);
        ledger.Apply([Observe(immortal.Id, 59)], at.AddSeconds(1), Price);
        ledger.Apply([Observe(immortal.Id, 1200)], at.AddSeconds(2), Price);
        ledger.Apply([Observe(immortal.Id, 1199)], at.AddSeconds(3), Price);
        var state = ledger.Apply([Observe(immortal.Id, 1198)], at.AddSeconds(4), Price);

        Assert.Equal(old, state.Consumptions[0]);
        Assert.Equal(3, state.Consumptions.Count);
        var current = state.Consumptions.Where(item => item.BuffId == immortal.Id).ToArray();
        Assert.Equal(at, Assert.Single(current, item => item.IsSessionStart).ConsumedAt);
        Assert.Equal(at.AddSeconds(2), Assert.Single(current, item => !item.IsSessionStart).ConsumedAt);
        Assert.All(current, item =>
        {
            Assert.Equal(1400, item.MarketItemId);
            Assert.Equal(9_000_000m, item.Cost);
        });
        Assert.Equal(19_200_000m, state.ConsumedCost);
    }

    [Theory]
    [InlineData("immortal-perfume-of-courage")]
    [InlineData("simple-cron-meal")]
    public void RecognitionResolvesExactKnownIdentitiesOnly(string id)
    {
        Assert.Equal(id, BuffPriceCatalog.ResolveRecognitionDefinition(id)!.Id);
        Assert.Null(BuffPriceCatalog.ResolveRecognitionDefinition("immortal-harmony-unknown"));
    }

    [Theory]
    [InlineData("harmony-draught-demihuman")]
    [InlineData("immortal-harmony-draught-demihuman")]
    public void PartyHarmonyCountsAHigherVisibleTimerImmediatelyWithoutOwnConsumptionAttribution(string id)
    {
        var at = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var buff = BuffPriceCatalog.ResolveRecognitionDefinition(id)!;
        var ledger = new BuffLedger(BuffPriceCatalog.Definitions);
        BuffObservation Observe(int seconds) => new(id, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1));
        BuffPrice? Price(BuffDefinition _) => new(2_000_000m, "eu", at, false);

        ledger.Apply([Observe(60)], at, Price);
        Assert.True(Assert.Single(ledger.Apply([Observe(59)], at.AddSeconds(1), Price).Consumptions).IsSessionStart);
        var renewed = ledger.Apply([Observe(1200)], at.AddSeconds(2), Price);
        Assert.Equal(2, renewed.Consumptions.Count);
        Assert.Equal(at.AddSeconds(2), Assert.Single(renewed.Consumptions,
            item => !item.IsSessionStart).ConsumedAt);
        ledger.Apply([Observe(1199)], at.AddSeconds(3), Price);
        var state = ledger.Apply([Observe(1198)], at.AddSeconds(4), Price);

        Assert.Equal(2, state.Consumptions.Count);
        var consumed = Assert.Single(state.Consumptions, item => !item.IsSessionStart);
        Assert.Equal(id, consumed.BuffId);
        Assert.Equal(buff.MarketItemId, consumed.MarketItemId);
        Assert.Equal(4_000_000m, state.ConsumedCost);
    }

    [Fact]
    public async Task BuffsShareMarketFallbackAndNeverBorrowAnotherRegionsPrices()
    {
        var buff = BuffPriceCatalog.Definitions.Single(item => item.Id == "simple-cron-meal");
        var time = new ManualTime();
        var handler = new Handler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "api.arsha.io")
            {
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                Assert.Contains(buff.MarketItemId!.Value.ToString(), query);
                return new(HttpStatusCode.ServiceUnavailable);
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Contains(buff.MarketItemId!.Value.ToString(), body.RootElement.GetProperty("searchResult").GetString()!.Split(','));
            return request.RequestUri.Host.StartsWith("eu-", StringComparison.Ordinal)
                ? Json("""{"resultCode":0,"resultMsg":"9692-1-390000-1"}""")
                : new(HttpStatusCode.ServiceUnavailable);
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        var eu = await provider.GetSnapshotAsync("eu");
        var price = BuffPriceCatalog.GetPrice(buff, eu);
        Assert.Equal(390_000m, price!.UnitPrice);
        Assert.False(price.IsStale);
        Assert.Equal("eu", price.Region);
        Assert.Null(BuffPriceCatalog.GetPrice(buff, await provider.GetSnapshotAsync("na")));

        time.Now += TimeSpan.FromMinutes(11);
        var cached = BuffPriceCatalog.GetPrice(buff, provider.GetCachedSnapshot("eu"));
        Assert.True(cached!.IsStale);
        Assert.Equal(price.FetchedAt, cached.FetchedAt);
        Assert.Equal(price.UnitPrice, cached.UnitPrice);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
