using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Tests;

public sealed class MarketLootPriceFallbackTests
{
    [Fact]
    public async Task CompleteArshaResponseDoesNotContactFallback()
    {
        var handler = new Handler(request =>
        {
            Assert.True(IsArsha(request));
            return ArshaAll(135_000);
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(1, handler.ArshaCount);
        Assert.Equal(0, handler.FallbackCount);
        Assert.Equal(135_000, snapshot.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(LootPriceOrigin.LiveMarket, snapshot.Quotes["Black Stone"].Origin);
        Assert.False(snapshot.IsStale);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task PrimaryFailureUsesAnonymousRegionalFallbackContract(HttpStatusCode status)
    {
        var handler = new Handler(async (request, token) =>
        {
            if (IsArsha(request)) return new(status);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("eu-trade.naeu.playblackdesert.com", request.RequestUri.Host);
            Assert.Equal("/Trademarket/GetWorldMarketSearchList", request.RequestUri.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("apiKey"));
            Assert.Equal("BlackDesert", request.Headers.UserAgent.ToString());
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal(LootPriceCatalog.MarketItemIds, await RequestedIds(request, token));
            return Pa("16001-130383-135000-3663755255|721003-1-885000-30");
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(1, handler.ArshaCount);
        Assert.Equal(1, handler.FallbackCount);
        Assert.Equal(135_000, snapshot.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(885_000, snapshot.Quotes["Caphras Stone"].UnitPrice);
        Assert.Equal(150_000, snapshot.Quotes["Ancient Spirit Dust"].UnitPrice);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"id\":16001,\"sid\":0,\"basePrice\":\"135000\"}")]
    public async Task UnusablePrimaryDataFallsBack(string body)
    {
        var handler = new Handler(request => IsArsha(request) ? Json(body) : PaAll(135_000));
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(1, handler.FallbackCount);
        Assert.Equal(135_000, snapshot.Quotes["Black Stone"].UnitPrice);
        Assert.False(snapshot.Quotes["Black Stone"].IsStale);
    }

    [Fact]
    public async Task PrimaryTimeoutLeavesFallbackItsOwnUsableDeadline()
    {
        var handler = new Handler(async (request, token) =>
        {
            if (!IsArsha(request)) return PaAll(135_000);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The primary request must be canceled.");
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime(),
            requestTimeout: TimeSpan.FromMilliseconds(40));

        var snapshot = await provider.GetSnapshotAsync("eu").WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, handler.FallbackCount);
        Assert.Equal(135_000, snapshot.Quotes["Black Stone"].UnitPrice);
    }

    [Fact]
    public async Task PartialPrimaryRequestsOnlyMissingIdsAndFallbackCannotOverwritePrimary()
    {
        var time = new ManualTime();
        var primaryTimestamp = time.GetUtcNow();
        var handler = new Handler(async (request, token) =>
        {
            if (IsArsha(request)) return ArshaAll(100, excludedId: 721003);
            Assert.Equal(new[] { 721003 }, await RequestedIds(request, token));
            time.Advance(TimeSpan.FromSeconds(1));
            // An unrequested extra row must never replace a successful primary quote.
            return Pa("16001-1-999-1|721003-1-1000-1");
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(100, snapshot.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(1000, snapshot.Quotes["Caphras Stone"].UnitPrice);
        Assert.Equal(primaryTimestamp, snapshot.Quotes["Black Stone"].FetchedAt);
        Assert.Equal(time.GetUtcNow(), snapshot.Quotes["Caphras Stone"].FetchedAt);
        Assert.Equal(LootPriceOrigin.LiveMarket, snapshot.Quotes["Black Stone"].Origin);
        Assert.Equal(LootPriceOrigin.LiveMarket, snapshot.Quotes["Caphras Stone"].Origin);
    }

    [Fact]
    public async Task PartialFallbackRetainsMissingCachedPricesAndTheirOriginalTimestamps()
    {
        var time = new ManualTime();
        var handler = new Handler(_ => ArshaAll(100));
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        var original = await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(11));
        handler.Respond = (request, _) => Task.FromResult(IsArsha(request)
            ? Json("{\"id\":16001,\"sid\":0,\"basePrice\":200}")
            : Pa("721003-1-2000-1"));

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(200, snapshot.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(2000, snapshot.Quotes["Caphras Stone"].UnitPrice);
        Assert.False(snapshot.Quotes["Black Stone"].IsStale);
        Assert.False(snapshot.Quotes["Caphras Stone"].IsStale);
        Assert.Equal(100, snapshot.Quotes["Deboreka Earring"].UnitPrice);
        Assert.Equal(original.Quotes["Deboreka Earring"].FetchedAt,
            snapshot.Quotes["Deboreka Earring"].FetchedAt);
        Assert.True(snapshot.Quotes["Deboreka Earring"].IsStale);
        Assert.True(snapshot.IsStale);
    }

    [Fact]
    public async Task BothSourcesFailingKeepOldPricesWithoutRenewingTimestamps()
    {
        var time = new ManualTime();
        var handler = new Handler(_ => ArshaAll(135_000));
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        var original = await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(11));
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(1, handler.FallbackCount);
        Assert.Equal(135_000, snapshot.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(original.Quotes["Black Stone"].FetchedAt, snapshot.Quotes["Black Stone"].FetchedAt);
        Assert.True(snapshot.Quotes["Black Stone"].IsStale);
        Assert.Equal(LootPriceOrigin.CachedMarket, snapshot.Quotes["Black Stone"].Origin);
    }

    [Fact]
    public async Task FallbackPricesUseSharedPersistentCacheWithoutRedundantRequests()
    {
        var path = Path.Combine(Path.GetTempPath(), "bdo-market-fallback-" + Guid.NewGuid().ToString("N") + ".json");
        var time = new ManualTime();
        try
        {
            var handler = new Handler(request => IsArsha(request)
                ? new(HttpStatusCode.ServiceUnavailable) : PaAll(135_000));
            DateTimeOffset? originalTimestamp;
            using (var provider = new MarketLootPriceProvider(handler, path, time))
            {
                originalTimestamp = (await provider.GetSnapshotAsync("eu")).Quotes["Black Stone"].FetchedAt;
                time.Advance(TimeSpan.FromMinutes(1));
                var cached = await provider.GetSnapshotAsync("eu");
                Assert.Equal(originalTimestamp, cached.Quotes["Black Stone"].FetchedAt);
                Assert.Equal(1, handler.ArshaCount);
                Assert.Equal(1, handler.FallbackCount);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(1, document.RootElement.GetProperty("SchemaVersion").GetInt32());
            var readerHandler = new Handler(_ => throw new InvalidOperationException("A fresh full cache must avoid HTTP."));
            using var reader = new MarketLootPriceProvider(readerHandler, path, time);
            var restored = await reader.GetSnapshotAsync("eu");
            Assert.Equal(135_000, restored.Quotes["Black Stone"].UnitPrice);
            Assert.Equal(originalTimestamp, restored.Quotes["Black Stone"].FetchedAt);
            Assert.Equal(LootPriceOrigin.CachedMarket, restored.Quotes["Black Stone"].Origin);
            Assert.False(reader.GetCachedSnapshot("na").Quotes.ContainsKey("Black Stone"));
            Assert.Equal(0, readerHandler.ArshaCount + readerHandler.FallbackCount);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public async Task RegionalFallbackRequestsAndCacheNeverMixEuAndNa()
    {
        var handler = new Handler(request => IsArsha(request)
            ? new(HttpStatusCode.ServiceUnavailable)
            : PaAll(request.RequestUri!.Host == "eu-trade.naeu.playblackdesert.com" ? 100 : 200));
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());

        var eu = await provider.GetSnapshotAsync(" EU ");
        var na = await provider.GetSnapshotAsync("na");

        Assert.Equal("eu", eu.Region);
        Assert.Equal("na", na.Region);
        Assert.Equal(100, eu.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(200, na.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(100, provider.GetCachedSnapshot("eu").Quotes["Black Stone"].UnitPrice);
        Assert.Equal(2, handler.ArshaCount);
        Assert.Equal(2, handler.FallbackCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrimaryRetryAfterDoesNotPreventFallbackRefresh(bool absoluteDate)
    {
        var time = new ManualTime();
        var handler = new Handler(request =>
        {
            if (!IsArsha(request)) return PaAll(100);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = absoluteDate
                ? new(time.GetUtcNow().AddHours(1)) : new(TimeSpan.FromHours(1));
            return response;
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(11));

        var refreshed = await provider.GetSnapshotAsync("eu");

        Assert.Equal(1, handler.ArshaCount);
        Assert.Equal(2, handler.FallbackCount);
        Assert.Equal(time.GetUtcNow(), refreshed.Quotes["Black Stone"].FetchedAt);
        time.Advance(TimeSpan.FromMinutes(49));
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(2, handler.ArshaCount);
    }

    [Fact]
    public async Task FallbackRetryAfterDoesNotPreventPrimaryRecovery()
    {
        var time = new ManualTime();
        var handler = new Handler(request =>
        {
            if (IsArsha(request)) return new(HttpStatusCode.ServiceUnavailable);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromHours(1));
            return response;
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromSeconds(31));
        handler.Respond = (request, _) =>
        {
            Assert.True(IsArsha(request));
            return Task.FromResult(Json("{\"id\":16001,\"sid\":0,\"basePrice\":135000}"));
        };

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(2, handler.ArshaCount);
        Assert.Equal(1, handler.FallbackCount);
        Assert.Equal(135_000, snapshot.Quotes["Black Stone"].UnitPrice);
        Assert.False(snapshot.Quotes.ContainsKey("Caphras Stone"));
    }

    [Fact]
    public async Task ExpiredFallbackRateLimitWarningDisappearsAfterPrimaryRecovery()
    {
        var time = new ManualTime();
        var handler = new Handler(request =>
        {
            if (IsArsha(request)) return Json("{\"id\":16001,\"sid\":0,\"basePrice\":100}");
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(1));
            return response;
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        var limited = await provider.GetSnapshotAsync("eu");
        Assert.Contains("begrenzt", limited.StatusMessage);
        time.Advance(TimeSpan.FromMinutes(11));
        handler.Respond = (request, _) =>
        {
            Assert.True(IsArsha(request));
            return Task.FromResult(ArshaAll(200));
        };

        var recovered = await provider.GetSnapshotAsync("eu");

        Assert.DoesNotContain("begrenzt", recovered.StatusMessage);
        Assert.Equal(2, handler.ArshaCount);
        Assert.Equal(1, handler.FallbackCount);
        Assert.Equal(200, recovered.Quotes["Black Stone"].UnitPrice);
    }

    [Fact]
    public async Task FailedSourcesBothBackOffBeforeTheNextAttempt()
    {
        var time = new ManualTime();
        var handler = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        await provider.GetSnapshotAsync("eu");

        await provider.GetSnapshotAsync("eu");
        Assert.Equal(1, handler.ArshaCount);
        Assert.Equal(1, handler.FallbackCount);
        time.Advance(TimeSpan.FromSeconds(30));
        await provider.GetSnapshotAsync("eu");
        Assert.Equal(2, handler.ArshaCount);
        Assert.Equal(2, handler.FallbackCount);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutStartingFallback()
    {
        var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The caller must cancel this request.");
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetSnapshotAsync("eu", cancellation.Token).WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Equal(1, handler.ArshaCount);
        Assert.Equal(0, handler.FallbackCount);
    }

    [Fact]
    public async Task CancellationDuringFallbackDoesNotSuppressUncommittedPrimarySuccess()
    {
        var fallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (request, token) =>
        {
            if (IsArsha(request)) return Json("{\"id\":16001,\"sid\":0,\"basePrice\":100}");
            fallbackStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The caller must cancel the fallback.");
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());
        using var cancellation = new CancellationTokenSource();
        var canceledRefresh = provider.GetSnapshotAsync("eu", cancellation.Token);
        await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRefresh.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(provider.GetCachedSnapshot("eu").Quotes.ContainsKey("Black Stone"));
        handler.Respond = (request, _) =>
        {
            Assert.True(IsArsha(request));
            return Task.FromResult(ArshaAll(200));
        };

        var retried = await provider.GetSnapshotAsync("eu");

        Assert.Equal(2, handler.ArshaCount);
        Assert.Equal(1, handler.FallbackCount);
        Assert.Equal(200, retried.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(LootPriceOrigin.LiveMarket, retried.Quotes["Black Stone"].Origin);
    }

    [Fact]
    public async Task CancellationDuringFallbackStillHonorsPrimaryIndividualRetryAfter()
    {
        var time = new ManualTime();
        var fallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstId = LootPriceCatalog.MarketItemIds[0];
        var handler = new Handler(async (request, token) =>
        {
            if (IsArsha(request))
            {
                var ids = Uri.UnescapeDataString(request.RequestUri!.Query).Split('=')[1].Split('&')[0];
                if (ids.Contains(',')) return new(HttpStatusCode.InternalServerError);
                if (int.Parse(ids, CultureInfo.InvariantCulture) == firstId)
                    return Json(JsonSerializer.Serialize(new { id = firstId, sid = 0, basePrice = 100 }));
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new(TimeSpan.FromHours(1));
                return limited;
            }
            fallbackStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The caller must cancel the fallback.");
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: time);
        using var cancellation = new CancellationTokenSource();
        var canceledRefresh = provider.GetSnapshotAsync("eu", cancellation.Token);
        await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRefresh.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(handler.ArshaCount >= 3); // Batch failure, one successful row, then a rate limit.
        var primaryRequests = handler.ArshaCount;
        handler.Respond = (request, _) =>
        {
            Assert.False(IsArsha(request));
            return Task.FromResult(PaAll(200));
        };

        var recovered = await provider.GetSnapshotAsync("eu");
        time.Advance(TimeSpan.FromMinutes(11));
        var refreshed = await provider.GetSnapshotAsync("eu");

        Assert.Equal(primaryRequests, handler.ArshaCount);
        Assert.Equal(3, handler.FallbackCount);
        Assert.Equal(200, recovered.Quotes["Black Stone"].UnitPrice);
        Assert.Equal(time.GetUtcNow(), refreshed.Quotes["Black Stone"].FetchedAt);
    }

    [Fact]
    public async Task FallbackBodyReadIsIncludedInItsDeadline()
    {
        var body = new BlockingContent();
        var handler = new Handler(request => IsArsha(request)
            ? new(HttpStatusCode.ServiceUnavailable)
            : new(HttpStatusCode.OK) { Content = body });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime(),
            requestTimeout: TimeSpan.FromMilliseconds(40));

        var snapshot = await provider.GetSnapshotAsync("eu").WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(body.WasRead);
        Assert.Equal(1, handler.FallbackCount);
        Assert.False(snapshot.Quotes.ContainsKey("Black Stone"));
        Assert.True(snapshot.Quotes.ContainsKey("Black Crystal Fragment"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"resultCode\":1,\"resultMsg\":\"16001-1-100-1\"}")]
    [InlineData("{\"resultCode\":\"0\",\"resultMsg\":\"16001-1-100-1\"}")]
    [InlineData("{\"resultCode\":null,\"resultMsg\":\"16001-1-100-1\"}")]
    [InlineData("{\"resultCode\":0,\"resultMsg\":123}")]
    [InlineData("{\"resultCode\":0,\"resultMsg\":null}")]
    [InlineData("{\"resultCode\":0,\"resultMsg\":\"16001-1-0-1\"}")]
    [InlineData("{\"resultCode\":0,\"resultMsg\":\"16001-1-1.5-1\"}")]
    [InlineData("{\"resultCode\":0,\"resultMsg\":\"16001-1-9223372036854775808-1\"}")]
    [InlineData("{\"resultCode\":0,\"resultMsg\":\"999-1-100-1\"}")]
    [InlineData("{\"resultCode\":0,\"resultMsg\":\"16001-1-100-1|16001-1-200-1\"}")]
    public async Task InvalidFallbackDataCannotInventMarketQuotes(string body)
    {
        var handler = new Handler(request => IsArsha(request)
            ? new(HttpStatusCode.ServiceUnavailable) : Json(body));
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.Equal(1, handler.FallbackCount);
        Assert.False(snapshot.Quotes.ContainsKey("Black Stone"));
        Assert.True(snapshot.Quotes.ContainsKey("Black Crystal Fragment"));
    }

    [Fact]
    public async Task OversizedFallbackBodyIsRejectedBeforeItCanSupplyPrice()
    {
        var handler = new Handler(request => IsArsha(request)
            ? new(HttpStatusCode.ServiceUnavailable)
            : Json(JsonSerializer.Serialize(new { resultCode = 0, resultMsg = "16001-1-100-1|" + new string('x', 1_048_577) })));
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.False(snapshot.Quotes.ContainsKey("Black Stone"));
    }

    [Fact]
    public async Task HtmlFallbackResponseCannotSupplyPriceEvenWithJsonLookingBody()
    {
        var handler = new Handler(request =>
        {
            if (IsArsha(request)) return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"resultCode\":0,\"resultMsg\":\"16001-1-100-1\"}", Encoding.UTF8, "text/html"),
            };
        });
        using var provider = new MarketLootPriceProvider(handler, timeProvider: new ManualTime());

        var snapshot = await provider.GetSnapshotAsync("eu");

        Assert.False(snapshot.Quotes.ContainsKey("Black Stone"));
    }

    private static bool IsArsha(HttpRequestMessage request) => request.RequestUri!.Host == "api.arsha.io";

    private static async Task<int[]> RequestedIds(HttpRequestMessage request, CancellationToken token)
    {
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        Assert.Single(document.RootElement.EnumerateObject());
        return document.RootElement.GetProperty("searchResult").GetString()!.Split(',')
            .Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToArray();
    }

    private static HttpResponseMessage ArshaAll(long price = 100, int? excludedId = null) => Json(JsonSerializer.Serialize(
        LootPriceCatalog.MarketItemIds.Where(id => id != excludedId).Select(id => new { id, sid = 0, basePrice = price })));

    private static HttpResponseMessage PaAll(long price) => Pa(string.Join('|',
        LootPriceCatalog.MarketItemIds.Select(id => $"{id}-1-{price}-1")));

    private static HttpResponseMessage Pa(string rows) => Json(JsonSerializer.Serialize(new { resultCode = 0, resultMsg = rows }));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler : HttpMessageHandler
    {
        private int _arshaCount;
        private int _fallbackCount;
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; }
        public int ArshaCount => Volatile.Read(ref _arshaCount);
        public int FallbackCount => Volatile.Read(ref _fallbackCount);

        public Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request))) { }

        public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => Respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsArsha(request)) Interlocked.Increment(ref _arshaCount);
            else Interlocked.Increment(ref _fallbackCount);
            return Respond(request, cancellationToken);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class BlockingContent : HttpContent
    {
        public bool WasRead { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Cancellation-aware overload required.");
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            WasRead = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }
}
