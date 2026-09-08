using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Tests;

public sealed class ArshaLootPriceProviderTests
{
    [Fact]
    public async Task BatchServerErrorFallsBackToIndividualPricesAndKeepsMissingCacheStale()
    {
        var time = new ManualTime();
        var handler = new Handler(_ => Json("""[{"id":16001,"sid":0,"basePrice":100},{"id":721003,"sid":0,"basePrice":1000}]"""));
        using var provider = new ArshaLootPriceProvider(handler, timeProvider: time);
        var before = await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(11));
        handler.Respond = request => request.RequestUri!.Query == "?id=16001&lang=en"
            ? Json(Price(200)) : new(HttpStatusCode.InternalServerError);
        var after = await provider.GetSnapshotAsync("eu");
        Assert.Equal(200, after.Quotes["Black Stone"].UnitPrice);
        Assert.False(after.Quotes["Black Stone"].IsStale);
        Assert.Equal(before.Quotes["Caphras Stone"].FetchedAt, after.Quotes["Caphras Stone"].FetchedAt);
        Assert.True(after.Quotes["Caphras Stone"].IsStale);
        Assert.Contains("teilweise", after.StatusMessage);
        var count = handler.Count;
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(count, handler.Count);
    }

    [Fact]
    public void ConstructionAndCachedSnapshotNeverSendRequests()
    {
        var handler = new Handler(_ => Json("[]"));
        using var provider = new ArshaLootPriceProvider(handler);
        var snapshot = provider.GetCachedSnapshot(" EU ");
        Assert.Equal(0, handler.Count);
        Assert.Equal("eu", snapshot.Region);
        Assert.Equal(160_539, snapshot.Quotes["Black Crystal Fragment"].UntaxedUnitPrice);
        Assert.False(snapshot.Quotes.ContainsKey("Black Stone"));
        Assert.False(snapshot.Quotes.ContainsKey("Pure Black Stone"));
        Assert.Null(snapshot.RetrievedAt);
    }

    [Fact]
    public async Task AnonymousBatchUsesOnlyCatalogIdsAndBaseEnhancementPrices()
    {
        Uri? uri = null;
        var handler = new Handler(request =>
        {
            uri = request.RequestUri;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("apiKey"));
            return Json("""[[{"id":16001,"sid":0,"basePrice":134000},{"id":16001,"sid":1,"basePrice":999999}],[{"id":721003,"sid":0,"basePrice":885000}],[{"id":999,"sid":0,"basePrice":100}]]""");
        });
        using var provider = new ArshaLootPriceProvider(handler, timeProvider: new ManualTime());
        var result = await provider.GetSnapshotAsync("eu");
        Assert.Equal("api.arsha.io", uri!.Host);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("/v2/eu/GetWorldMarketSubList", uri.AbsolutePath);
        Assert.Equal($"?id={string.Join(',', LootPriceCatalog.MarketItemIds)}&lang=en", Uri.UnescapeDataString(uri.Query));
        Assert.Equal(134000, result.Quotes["Black Stone"].TaxableUnitPrice);
        Assert.Equal(LootPriceOrigin.LiveMarket, result.Quotes["Black Stone"].Origin);
        Assert.False(result.IsStale);
        Assert.Equal(result.RetrievedAt, result.Quotes["Black Stone"].FetchedAt);
        Assert.Equal(150200, result.Quotes["Ancient Spirit Dust"].TaxableUnitPrice);
        Assert.Equal(0, result.Quotes["Ancient Spirit Dust"].UntaxedUnitPrice);
    }

    [Fact]
    public async Task DustUsesIntegerUnitAppraisalAndDoesNotInventNegativeValue()
    {
        var time = new ManualTime();
        var handler = new Handler(_ => Json("""[{"id":16001,"sid":0,"basePrice":102},{"id":721003,"sid":0,"basePrice":1001}]"""));
        using var provider = new ArshaLootPriceProvider(handler, timeProvider: time);
        var first = await provider.GetSnapshotAsync("eu");
        Assert.Equal(179, first.Quotes["Ancient Spirit Dust"].UnitPrice);
        time.Advance(TimeSpan.FromMinutes(10));
        handler.Respond = _ => Json("""[{"id":16001,"sid":0,"basePrice":1001},{"id":721003,"sid":0,"basePrice":102}]""");
        var next = await provider.GetSnapshotAsync("eu");
        Assert.False(next.Quotes.ContainsKey("Ancient Spirit Dust"));
    }

    [Theory]
    [InlineData("{\"id\":\"16001\",\"sid\":0,\"basePrice\":10}")]
    [InlineData("{\"id\":null,\"sid\":0,\"basePrice\":10}")]
    [InlineData("{\"id\":true,\"sid\":0,\"basePrice\":10}")]
    [InlineData("{\"id\":16001,\"sid\":\"0\",\"basePrice\":10}")]
    [InlineData("{\"id\":16001,\"sid\":null,\"basePrice\":10}")]
    [InlineData("{\"id\":16001,\"sid\":false,\"basePrice\":10}")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":\"10\"}")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":null}")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":true}")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":0}")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":-1}")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":1.5}")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":9223372036854775808}")]
    [InlineData("{\"id\":16001,\"basePrice\":1}")]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("not-json")]
    [InlineData("[{\"id\":16001,\"sid\":0,\"basePrice\":1},{\"id\":16001,\"sid\":0,\"basePrice\":2}]")]
    public async Task InvalidRemoteDataNeverCrashesOrSuppliesZeroPrice(string body)
    {
        using var provider = new ArshaLootPriceProvider(new Handler(_ => Json(body)));
        var result = await provider.GetSnapshotAsync("eu");
        Assert.False(result.Quotes.ContainsKey("Black Stone"));
        Assert.Contains("Festwerte", result.StatusMessage);
    }

    [Fact]
    public async Task FreshCachePreventsRepeatedRequestAndRegionsNeverMix()
    {
        var handler = new Handler(request => Json(Price(request.RequestUri!.AbsolutePath.Contains("/eu/") ? 100 : 200)));
        using var provider = new ArshaLootPriceProvider(handler, timeProvider: new ManualTime());
        await provider.GetSnapshotAsync("eu");
        var eu = await provider.GetSnapshotAsync("eu");
        var na = await provider.GetSnapshotAsync("na");
        Assert.Equal(2, handler.Count);
        Assert.Equal(100, eu.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(200, na.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(LootPriceOrigin.CachedMarket, eu.Quotes["Black Stone"].Origin);
    }

    [Fact]
    public async Task PartialResponseRetainsOldTimestampAndStaleMarker()
    {
        var time = new ManualTime();
        var handler = new Handler(_ => Json("""[{"id":16001,"sid":0,"basePrice":100},{"id":721003,"sid":0,"basePrice":1000}]"""));
        using var provider = new ArshaLootPriceProvider(handler, timeProvider: time);
        var first = await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(11));
        handler.Respond = _ => Json(Price(200));
        var next = await provider.GetSnapshotAsync("eu");
        Assert.True(next.Quotes["Caphras Stone"].IsStale);
        Assert.Equal(first.Quotes["Caphras Stone"].FetchedAt, next.Quotes["Caphras Stone"].FetchedAt);
        Assert.False(next.Quotes["Black Stone"].IsStale);
        Assert.True(next.Quotes["Ancient Spirit Dust"].IsStale);
        Assert.True(next.IsStale);
        Assert.Contains("teilweise", next.StatusMessage);
    }

    [Fact]
    public async Task FailureKeepsStalePricesAndBacksOffWithoutRetrying()
    {
        var time = new ManualTime();
        var handler = new Handler(_ => Json(Price(100)));
        using var provider = new ArshaLootPriceProvider(handler, timeProvider: time);
        var original = await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(10));
        handler.Respond = _ => new(HttpStatusCode.Forbidden);
        var failed = await provider.GetSnapshotAsync("eu");
        Assert.Equal(2, handler.Count);
        Assert.True(failed.IsStale);
        Assert.Equal(original.RetrievedAt, failed.RetrievedAt);
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(2, handler.Count);
        time.Advance(TimeSpan.FromSeconds(30));
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(3, handler.Count);
        time.Advance(TimeSpan.FromSeconds(30));
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task RetryAfterIsRespectedButBoundedToOneHour()
    {
        var time = new ManualTime();
        var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromDays(1));
            return response;
        });
        using var provider = new ArshaLootPriceProvider(handler, timeProvider: time);
        await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(59));
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(1, handler.Count);
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpErrorIsFixedSafeMessageAndDoesNotReadResponseBody(HttpStatusCode status)
    {
        var body = new BlockingContent();
        using var provider = new ArshaLootPriceProvider(new Handler(_ => new(status) { Content = body }));
        var result = await provider.GetSnapshotAsync("eu");
        Assert.False(body.WasRead);
        Assert.Contains("derzeit nicht verfügbar", result.StatusMessage);
    }

    [Fact]
    public async Task BodyReadIsIncludedInDeadline()
    {
        var body = new BlockingContent();
        using var provider = new ArshaLootPriceProvider(new Handler(_ => new(HttpStatusCode.OK) { Content = body }),
            requestTimeout: TimeSpan.FromMilliseconds(30));
        var result = await provider.GetSnapshotAsync("eu").WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(body.WasRead);
        Assert.Contains("nicht aktualisiert", result.StatusMessage);
    }

    [Fact]
    public async Task OversizedBodyIsRejected()
    {
        using var provider = new ArshaLootPriceProvider(new Handler(_ => Json(new string('x', 1_048_577))));
        var result = await provider.GetSnapshotAsync("eu");
        Assert.False(result.Quotes.ContainsKey("Black Stone"));
        Assert.Contains("keine gültigen", result.StatusMessage);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        using var provider = new ArshaLootPriceProvider(new Handler(_ => new(HttpStatusCode.OK) { Content = new BlockingContent() }));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetSnapshotAsync("eu", cancel.Token));
    }

    [Fact]
    public async Task CacheRoundTripRetainsRegionPricesAndOriginalTimestamps()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bdo-price-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "prices.json");
        var time = new ManualTime();
        try
        {
            using (var writer = new ArshaLootPriceProvider(new Handler(_ => Json(Price(100))), path, time))
                await writer.GetSnapshotAsync("eu");
            time.Advance(TimeSpan.FromMinutes(11));
            var handler = new Handler(_ => throw new HttpRequestException("untrusted server text"));
            using var reader = new ArshaLootPriceProvider(handler, path, time);
            var cached = reader.GetCachedSnapshot("eu");
            Assert.Equal(100, cached.Quotes["Black Stone"].UnitPrice);
            Assert.True(cached.IsStale);
            Assert.Equal(time.GetUtcNow() - TimeSpan.FromMinutes(11), cached.RetrievedAt);
            Assert.False(reader.GetCachedSnapshot("na").Quotes.ContainsKey("Black Stone"));
            Assert.Equal(0, handler.Count);
            Assert.DoesNotContain("untrusted", (await reader.GetSnapshotAsync("eu")).StatusMessage);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"SchemaVersion\":1,\"Regions\":{\"eu\":{\"16001\":{\"UnitPrice\":-1,\"RetrievedAt\":\"2026-09-05T00:00:00Z\"}}}}")]
    [InlineData("{\"SchemaVersion\":1,\"Regions\":{\"eu\":{\"16001\":{\"UnitPrice\":100,\"RetrievedAt\":\"2099-01-01T00:00:00Z\"}}}}")]
    [InlineData("{\"SchemaVersion\":1,\"Regions\":{\"eu\":{\"16001\":null}}}")]
    [InlineData("{\"SchemaVersion\":2,\"Regions\":{}}")]
    public void CorruptOrFutureCacheDoesNotSupplyPrice(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "bdo-price-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, text);
            using var provider = new ArshaLootPriceProvider(new Handler(_ => Json("[]")), path, new ManualTime());
            Assert.False(provider.GetCachedSnapshot("eu").Quotes.ContainsKey("Black Stone"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("kr")]
    [InlineData("../../secret")]
    [InlineData("")]
    public async Task InvalidRegionCannotChangeRequestDestination(string region)
    {
        var handler = new Handler(_ => Json("[]"));
        using var provider = new ArshaLootPriceProvider(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetSnapshotAsync(region));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public void SnapshotCopiesQuoteCollection()
    {
        var quotes = new List<LootPriceQuote> { new("Test", 1, 0, LootPriceOrigin.LiveMarket, null) };
        var snapshot = new LootPriceSnapshot("eu", quotes);
        quotes.Clear();
        Assert.True(snapshot.TryGetQuote("Test", out var quote));
        Assert.Equal(1, quote.UnitPrice);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, LootPriceQuote>)snapshot.Quotes).Clear());
    }

    private static string Price(long value) => JsonSerializer.Serialize(new { id = 16001, sid = 0, basePrice = value });
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Count++; return Task.FromResult(Respond(request)); }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class BlockingContent : HttpContent
    {
        public bool WasRead { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Cancellation-aware overload required.");
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        { WasRead = true; await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
    }
}
