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
