using System.Net;
using System.Net.Http;
using System.Text;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothGrindBenchmarkProviderTests
{
    // Synthetic payloads based on the public client's field access, not captured live responses.
    // Source provenance and the failed live check are recorded in docs/GRIND_RATING.md.
    private const string Collective = """
        {"start_date":"2026-08-01","end_date":"2026-09-10","data":[
          {"grindspot_id":215,"start_date":"2026-08-13","end_date":"2026-09-11","total_minutes":120,
           "drops":[{"is_trash":false,"hourly_rate":999999},{"is_trash":true,"hourly_rate":4000.2}]}]}
        """;
    private const string Metadata = """
        {"result":{"data":{"215":{"dropRatios":{"l2":3},"highTierTrash":16000.5,"topTierTrash":18000.49}}}}
        """;

    [Fact]
    public async Task SuccessfulAnonymousRefreshUsesSpotMultiplierAndSourceDates()
    {
        var time = new ManualTime();
        var requested = new List<string>();
        using var provider = new GarmothGrindBenchmarkProvider(new Handler((request, _) =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri.Host == "garmoth.com")
            {
                Assert.NotNull(request.Content);
                Assert.Equal(0, request.Content.Headers.ContentLength);
                Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
            }
            else Assert.Null(request.Content);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/json");
            return Task.FromResult(Response(request));
        }), timeProvider: time);

        var snapshot = await provider.RefreshAsync();
        var reference = Assert.IsType<GrindBenchmark>(snapshot.Find(LootSpotCatalog.MagaiaId));

        Assert.Equal(new[]
        {
            "https://api.garmoth.com/api/grind-tracker/collective/all",
            "https://garmoth.com/api/trpc/grindMeta.list",
        }, requested.Order());
        Assert.Equal(12001m, reference.AverageTrashPerHour);
        Assert.Equal(16001m, reference.HighTrashPerHour);
        Assert.Equal(18000m, reference.TopTrashPerHour);
        Assert.Equal(time.GetUtcNow(), reference.UpdatedAt);
        Assert.Equal("https://garmoth.com/grind-tracker/best-grind-spots/215?startDate=2026-08-13&endDate=2026-09-11", reference.SourceUrl);
        Assert.Equal(GarmothGrindBenchmarks.Conditions, reference.Conditions);
        Assert.Contains("aktualisiert", snapshot.Status);
        Assert.Equal(reference, provider.GetCachedSnapshot().Find(reference.SpotId));
    }

    [Fact]
    public async Task IsoSourceDatesRemainTheDatasetDateInTheSourceLink()
    {
        var collective = Collective.Replace("2026-08-13", "2026-08-13T00:00:00.000000Z", StringComparison.Ordinal)
            .Replace("2026-09-11", "2026-09-11T23:59:59Z", StringComparison.Ordinal);
        using var provider = Provider(collective, Metadata);

        var reference = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;

        Assert.Equal(12001m, reference.AverageTrashPerHour);
        Assert.EndsWith("?startDate=2026-08-13&endDate=2026-09-11", reference.SourceUrl);
    }

    [Fact]
    public async Task ModeratorThresholdsBelowTheCurrentAverageClampToAnEqualTopBoundary()
    {
        var metadata = Metadata.Replace("16000.5", "9000", StringComparison.Ordinal)
            .Replace("18000.49", "10000", StringComparison.Ordinal);
        using var provider = Provider(Collective, metadata);

        var reference = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;

        Assert.Equal(12001m, reference.AverageTrashPerHour);
        Assert.Equal(reference.AverageTrashPerHour, reference.HighTrashPerHour);
        Assert.Equal(reference.AverageTrashPerHour, reference.TopTrashPerHour);
        Assert.Equal(GrindRatingTier.Top, GrindRatingEvaluator.Evaluate(reference.SpotId, 12001,
            TimeSpan.FromHours(1), reference).Tier);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"1\"")]
    public async Task NumericDatabaseValuesAndTruthyTrashFlagsAreReadWithoutLosingPrecision(string trashFlag)
    {
        var collective = Collective.Replace("\"grindspot_id\":215", "\"grindspot_id\":\"215\"", StringComparison.Ordinal)
            .Replace("\"total_minutes\":120", "\"total_minutes\":\"120\"", StringComparison.Ordinal)
            .Replace("\"is_trash\":true", "\"is_trash\":" + trashFlag, StringComparison.Ordinal)
            .Replace("\"hourly_rate\":4000.2", "\"hourly_rate\":\"4000.2\"", StringComparison.Ordinal);
        var metadata = Metadata.Replace("\"highTierTrash\":16000.5", "\"highTierTrash\":\"16000.5\"", StringComparison.Ordinal);
        using var provider = Provider(collective, metadata);

        var reference = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;

        Assert.Equal(12001m, reference.AverageTrashPerHour);
        Assert.Equal(16001m, reference.HighTrashPerHour);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"dropRatios\":{\"l2\":0}}")]
    [InlineData("{\"dropRatios\":{\"l2\":\"3\"}}")]
    public async Task MissingOrNonNumericLootRatioUsesTheWebsiteDefault(string spotMetadata)
    {
        using var provider = Provider(Collective, "{\"result\":{\"data\":{\"215\":" + spotMetadata + "}}}");

        var reference = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;

        Assert.Equal(8000m, reference.AverageTrashPerHour);
    }

    [Fact]
    public async Task ASourceRemovingModeratorTiersClearsPreviouslyCachedThresholds()
    {
        var handler = HandlerFor();
        using var provider = new GarmothGrindBenchmarkProvider(handler, timeProvider: new ManualTime());
        var first = await provider.RefreshAsync();
        Assert.NotNull(first.Find(LootSpotCatalog.MagaiaId)!.HighTrashPerHour);
        Assert.NotNull(first.Find(LootSpotCatalog.MagaiaId)!.TopTrashPerHour);
        handler.Respond = (request, _) => Task.FromResult(Response(request, metadata:
            "{\"result\":{\"data\":{\"215\":{\"topTierTrash\":0}}}}"));

        var reference = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;

        Assert.Null(reference.HighTrashPerHour);
        Assert.Null(reference.TopTrashPerHour);
        Assert.Equal(8000m, reference.AverageTrashPerHour);
    }

    [Fact]
    public async Task CachedSuccessSurvivesAnOfflineRestartWithoutAdvancingItsDate()
    {
        using var directory = new CacheDirectory();
        var time = new ManualTime();
        GarmothBenchmarkSnapshot saved;
        using (var provider = new GarmothGrindBenchmarkProvider(HandlerFor(), directory.Path, time))
            saved = await provider.RefreshAsync();
        Assert.True(File.Exists(directory.Path));
        time.Advance(TimeSpan.FromDays(10));
        using var restarted = new GarmothGrindBenchmarkProvider(new Handler((_, _) =>
            throw new HttpRequestException("Offline")), directory.Path, time);

        Assert.Equal(saved.Benchmarks, restarted.GetCachedSnapshot().Benchmarks);
        var failed = await restarted.RefreshAsync();

        Assert.Equal(saved.Benchmarks, failed.Benchmarks);
        Assert.Equal(saved.Find(LootSpotCatalog.MagaiaId)!.UpdatedAt, failed.Find(LootSpotCatalog.MagaiaId)!.UpdatedAt);
        Assert.Contains("weiterverwendet", failed.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "HTTP 403")]
    [InlineData(HttpStatusCode.TooManyRequests, "HTTP 429")]
    public async Task RejectedRequestsPreserveTheEntireSnapshot(HttpStatusCode status, string explanation)
    {
        var handler = HandlerFor();
        using var provider = new GarmothGrindBenchmarkProvider(handler, timeProvider: new ManualTime());
        var before = await provider.RefreshAsync();
        handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.Host == "garmoth.com"
            ? new HttpResponseMessage(status)
            : Response(request, Collective.Replace("4000.2", "5000.2", StringComparison.Ordinal)));

        var after = await provider.RefreshAsync();

        Assert.Equal(before.Benchmarks, after.Benchmarks);
        Assert.Contains(explanation, after.Status);
    }

    [Theory]
    [InlineData("html")]
    [InlineData("malformed-json")]
    [InlineData("invalid-id")]
    [InlineData("invalid-wrapper")]
    [InlineData("array-data")]
    [InlineData("invalid-date")]
    public async Task InvalidPayloadsDoNotReplacePreviouslyUsableData(string scenario)
    {
        var handler = HandlerFor();
        using var provider = new GarmothGrindBenchmarkProvider(handler, timeProvider: new ManualTime());
        var before = await provider.RefreshAsync();
        handler.Respond = (request, _) => Task.FromResult(scenario switch
        {
            "html" => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("<html>Unavailable</html>", Encoding.UTF8, "text/html") },
            "malformed-json" => Json("{invalid"),
            "invalid-id" => Response(request, Collective.Replace("\"grindspot_id\":215", "\"grindspot_id\":{}", StringComparison.Ordinal)),
            "invalid-wrapper" => Response(request, metadata: "{\"result\":[]}"),
            "invalid-date" => Response(request, Collective
                .Replace("2026-09-11", "2026-99-99", StringComparison.Ordinal)
                .Replace("2026-09-10", "2026-99-99", StringComparison.Ordinal)),
            _ => Response(request, metadata: "{\"result\":{\"data\":[]}}"),
        });

        var after = await provider.RefreshAsync();

        Assert.Equal(before.Benchmarks, after.Benchmarks);
        Assert.Contains("weiterverwendet", after.Status);
    }

    [Fact]
    public async Task RequestTimeoutLeavesExistingReferencesAvailable()
    {
        var handler = HandlerFor();
        using var provider = new GarmothGrindBenchmarkProvider(handler, timeProvider: new ManualTime(),
            requestTimeout: TimeSpan.FromSeconds(1));
        var before = await provider.RefreshAsync();
        handler.Respond = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("A timed out request must be canceled.");
        };

        var after = await provider.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(before.Benchmarks, after.Benchmarks);
        Assert.Contains("weiterverwendet", after.Status);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutChangingOrLockingTheSnapshot()
    {
        var handler = HandlerFor();
        using var provider = new GarmothGrindBenchmarkProvider(handler, timeProvider: new ManualTime());
        var before = await provider.RefreshAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("A canceled request must stop.");
        };
        using var cancellation = new CancellationTokenSource();
        var pending = provider.RefreshAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(before.Benchmarks, provider.GetCachedSnapshot().Benchmarks);
        handler.Respond = (request, _) => Task.FromResult(Response(request));
        Assert.Equal(before.Benchmarks, (await provider.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5))).Benchmarks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedResponsesAreRejectedWithOrWithoutAContentLength(bool knownLength)
    {
        var handler = HandlerFor();
        using var provider = new GarmothGrindBenchmarkProvider(handler, timeProvider: new ManualTime());
        var before = await provider.RefreshAsync();
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new OversizedContent(knownLength) });

        var after = await provider.RefreshAsync();

        Assert.Equal(before.Benchmarks, after.Benchmarks);
        Assert.Contains("weiterverwendet", after.Status);
    }

    [Fact]
    public async Task PartialRefreshKeepsEachMissingSpotsOriginalReferenceAndDate()
    {
        const string twoSpots = """
            {"start_date":"2026-08-13","end_date":"2026-09-11","data":[
              {"grindspot_id":214,"total_minutes":120,"drops":[{"is_trash":true,"hourly_rate":7000}]},
              {"grindspot_id":215,"total_minutes":120,"drops":[{"is_trash":true,"hourly_rate":8000}]}]}
            """;
        const string twoMetadata = "{\"result\":{\"data\":{\"214\":{},\"215\":{}}}}";
        var time = new ManualTime();
        var handler = HandlerFor(twoSpots, twoMetadata);
        using var provider = new GarmothGrindBenchmarkProvider(handler, timeProvider: time);
        var before = await provider.RefreshAsync();
        time.Advance(TimeSpan.FromHours(3));
        handler.Respond = (request, _) => Task.FromResult(Response(request));

        var after = await provider.RefreshAsync();

        Assert.Equal(before.Find(LootSpotCatalog.HermesiaId), after.Find(LootSpotCatalog.HermesiaId));
        Assert.Equal(time.GetUtcNow(), after.Find(LootSpotCatalog.MagaiaId)!.UpdatedAt);
        Assert.NotEqual(before.Find(LootSpotCatalog.MagaiaId)!.UpdatedAt, after.Find(LootSpotCatalog.MagaiaId)!.UpdatedAt);
        Assert.Equal(GarmothGrindBenchmarks.Find(LootSpotCatalog.AresionId), after.Find(LootSpotCatalog.AresionId));
        Assert.Contains("1 von 6", after.Status);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"SchemaVersion\":99,\"Benchmarks\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Benchmarks\":null}")]
    [InlineData("{\"SchemaVersion\":1,\"Benchmarks\":[{}]}")]
    public void CorruptedOrUnsupportedCacheFallsBackToTheBundledDatedReference(string content)
    {
        using var directory = new CacheDirectory();
        File.WriteAllText(directory.Path, content);
        using var provider = new GarmothGrindBenchmarkProvider(HandlerFor(), directory.Path, new ManualTime());

        var snapshot = provider.GetCachedSnapshot();

        Assert.Equal(GarmothGrindBenchmarks.All, snapshot.Benchmarks);
        Assert.Contains("Mitgelieferter", snapshot.Status);
    }

    private static GarmothGrindBenchmarkProvider Provider(string collective, string metadata) =>
        new(HandlerFor(collective, metadata), timeProvider: new ManualTime());

    private static Handler HandlerFor(string collective = Collective, string metadata = Metadata) =>
        new((request, _) => Task.FromResult(Response(request, collective, metadata)));

    private static HttpResponseMessage Response(HttpRequestMessage request, string collective = Collective,
        string metadata = Metadata) => request.RequestUri!.AbsoluteUri switch
        {
            "https://api.garmoth.com/api/grind-tracker/collective/all" => Json(collective),
            "https://garmoth.com/api/trpc/grindMeta.list" => Json(metadata),
            _ => throw new InvalidOperationException("Unexpected benchmark endpoint: " + request.RequestUri),
        };

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Respond(request, cancellationToken);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class CacheDirectory : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "grindcrest-benchmark-tests-" + Guid.NewGuid().ToString("N"));
        public CacheDirectory() => Directory.CreateDirectory(_directory);
        public string Path => System.IO.Path.Combine(_directory, GarmothGrindBenchmarkProvider.CacheFileName);
        public void Dispose() => Directory.Delete(_directory, true);
    }

    private sealed class OversizedContent(bool knownLength) : HttpContent
    {
        private const int Length = 8 * 1024 * 1024 + 1;
        protected override bool TryComputeLength(out long length) { length = Length; return knownLength; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(new byte[Length]).AsTask();
    }
}
