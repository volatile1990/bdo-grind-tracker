using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothRareDropBenchmarkTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 2, 10, 37, 13, TimeSpan.Zero);
    private const string Metadata = """{"result":{"data":{"215":{},"149":{"dropRatios":{"l2":9}}}}}""";

    [Fact]
    public void PublicKeysResolveCanonicalRareItemsAndRetainUnrounded100PercentRates()
    {
        var reference = Parse("""
            {"item_key":"15296_0","name":"Ein übersetzter Name","is_trash":false,"hourly_rate":"0.123456789"},
            {"item_key":"12144_0","is_trash":0,"hourly_rate":0},
            {"item_key":"16001_0","is_trash":false,"hourly_rate":100},
            {"item_key":"11733_0","is_trash":false,"hourly_rate":5},
            {"item_key":"12144_1","is_trash":false,"hourly_rate":5},
            {"item_key":"unknown","name":"Apeiron Ring","is_trash":false,"hourly_rate":5}
            """);

        Assert.Equal(2, reference.RareDropHourlyRates!.Count);
        Assert.Equal(0.246913578m, reference.RareDropHourlyRates["JIN Wandering Origin Crystal"]);
        Assert.Equal(0m, reference.RareDropHourlyRates["Apeiron Ring"]);
        Assert.True(reference.RareDropRateScalingApplies);
        Assert.Equal(CapturedAt, reference.RareDropUpdatedAt);
        Assert.Equal(reference.SourceUrl, reference.RareDropSourceUrl);
        Assert.Equal(8000m, reference.AverageTrashPerHour);
        Assert.Equal(8000m, reference.RareDropReferenceTrashPerHour);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\"1e500\"")]
    [InlineData("null")]
    [InlineData("{}")]
    public void InvalidRareRatesDoNotSupplyComparisonValues(string rate)
    {
        var reference = Parse($$"""
            {"item_key":"15296_0","is_trash":false,"hourly_rate":{{rate}}}
            """);
        Assert.Null(reference.RareDropHourlyRates);
        Assert.Null(reference.RareDropReferenceTrashPerHour);
        Assert.Equal(8000m, reference.AverageTrashPerHour);
    }

    [Fact]
    public void DuplicateCanonicalRareRatesRejectTheAmbiguousPayload()
    {
        Assert.Throws<InvalidDataException>(() => Parse("""
            {"item_key":"15296_0","is_trash":false,"hourly_rate":1},
            {"item_key":"15296_0","is_trash":false,"hourly_rate":2}
            """));
    }

    [Fact]
    public void CurrentBundledRatesMatchTheAnonymousCapturedResponsesAndKeepIndependentProvenance()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "garmoth");
        var parsed = GarmothGrindBenchmarkProvider.Parse(
            File.ReadAllBytes(Path.Combine(fixtures, "collective-20261002.json")),
            File.ReadAllBytes(Path.Combine(fixtures, "metadata-20261002.json")), CapturedAt);

        Assert.Equal(GarmothCatalog.SupportedSpotCount, parsed.Count);
        Assert.Equal(40, parsed.Count);
        foreach (var observed in parsed)
        {
            var bundled = Assert.IsType<GrindBenchmark>(GarmothGrindBenchmarks.Find(observed.SpotId));
            Assert.Equal(observed.RareDropHourlyRates, bundled.RareDropHourlyRates);
            Assert.Equal(observed.RareDropReferenceTrashPerHour, bundled.RareDropReferenceTrashPerHour);
            Assert.Equal(observed.RareDropHourlyRates is null ? null : observed.AverageTrashPerHour,
                bundled.RareDropReferenceTrashPerHour);
            Assert.Equal(observed.RareDropRateScalingApplies, bundled.RareDropRateScalingApplies);
            Assert.Equal(observed.RareDropSourceUrl, bundled.RareDropSourceUrl);
            Assert.Equal(CapturedAt, bundled.RareDropUpdatedAt);
            foreach (var name in bundled.RareDropHourlyRates?.Keys ?? [])
            {
                Assert.Equal(LootSource.Rare, LootSourceCatalog.GetAllowedSource(name));
                Assert.True(LootSpotCatalog.GetRequired(bundled.SpotId).Allows(name));
            }
        }
        var magaia = GarmothGrindBenchmarks.Find(LootSpotCatalog.MagaiaId)!;
        Assert.Equal(0.01510404m, magaia.RareDropHourlyRates!["JIN Wandering Origin Crystal"]);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), magaia.UpdatedAt);
        Assert.Equal(CapturedAt, magaia.RareDropUpdatedAt);
        Assert.Contains("endDate=2026-10-08", magaia.RareDropSourceUrl);
        Assert.Equal(12803m, magaia.AverageTrashPerHour);
        Assert.Equal(12863m, magaia.RareDropReferenceTrashPerHour);

        foreach (var id in new[] { LootSpotCatalog.AphrodonId, LootSpotCatalog.HermesiaId, LootSpotCatalog.MagaiaId,
                     LootSpotCatalog.AresionId, LootSpotCatalog.ScalesOfJudgmentId, LootSpotCatalog.EventHorizonId })
        {
            var reference = GarmothGrindBenchmarks.Find(id)!;
            Assert.NotEqual(reference.AverageTrashPerHour, reference.RareDropReferenceTrashPerHour);
        }
        var eventHorizon = GarmothGrindBenchmarks.Find(LootSpotCatalog.EventHorizonId)!;
        Assert.Equal(12590m, eventHorizon.AverageTrashPerHour);
        Assert.Equal(12909m, eventHorizon.RareDropReferenceTrashPerHour);
    }

    [Fact]
    public void VerifiedNoScrollSpotUsesUnscaledHourlyRatesForTrashAndRareItems()
    {
        var reference = Parse("""
            {"item_key":"100001004_0","is_trash":false,"hourly_rate":0.50991501}
            """, 149);
        Assert.False(reference.RareDropRateScalingApplies);
        Assert.Equal(0.50991501m, Assert.Single(reference.RareDropHourlyRates!).Value);
        Assert.Equal(4000m, reference.AverageTrashPerHour);
        Assert.Equal(4000m, reference.RareDropReferenceTrashPerHour);
        Assert.Equal(GarmothGrindBenchmarks.NoScrollConditions, reference.Conditions);
    }

    [Fact]
    public void MatchedRareTrashBaselineUsesTheSameSpotSpecificDisplayedLv2Multiplier()
    {
        var reference = Assert.Single(GarmothGrindBenchmarkProvider.Parse(
            Encoding.UTF8.GetBytes(Collective("""{"item_key":"15296_0","is_trash":false,"hourly_rate":1}""")),
            Encoding.UTF8.GetBytes("""{"result":{"data":{"215":{"dropRatios":{"l2":3}}}}}"""), CapturedAt));

        Assert.Equal(12001m, reference.AverageTrashPerHour);
        Assert.Equal(12001m, reference.RareDropReferenceTrashPerHour);
        Assert.Equal(2m, Assert.Single(reference.RareDropHourlyRates!).Value);
    }

    [Fact]
    public async Task RareRatesSurviveCacheRoundTripAndOfflineRestartWithoutChangingTheirDate()
    {
        using var directory = new CacheDirectory();
        var time = new ManualTime();
        var handler = new Handler(Collective("""{"item_key":"15296_0","is_trash":false,"hourly_rate":0.123456789}"""));
        GrindBenchmark saved;
        using (var provider = new GarmothGrindBenchmarkProvider(handler, directory.Path, time))
            saved = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;
        time.Advance(TimeSpan.FromDays(2));
        using var restarted = new GarmothGrindBenchmarkProvider(new Handler(null), directory.Path, time);

        Assert.Equal(saved, restarted.GetCachedSnapshot().Find(LootSpotCatalog.MagaiaId));
        Assert.Equal(saved.GetHashCode(), restarted.GetCachedSnapshot().Find(LootSpotCatalog.MagaiaId)!.GetHashCode());
        Assert.Equal(saved, (await restarted.RefreshAsync()).Find(LootSpotCatalog.MagaiaId));
        Assert.Equal(CapturedAt, saved.RareDropUpdatedAt);
        Assert.Equal(8000m, saved.RareDropReferenceTrashPerHour);
    }

    [Fact]
    public async Task SuccessfulTrashRefreshWithoutRareDataPreservesPriorRareValuesAndTheirOriginalSourceAndDate()
    {
        using var directory = new CacheDirectory();
        var time = new ManualTime();
        var handler = new Handler(Collective("""{"item_key":"15296_0","is_trash":false,"hourly_rate":0.123456789}"""));
        using var provider = new GarmothGrindBenchmarkProvider(handler, directory.Path, time);
        var old = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;
        time.Advance(TimeSpan.FromHours(1));
        handler.CollectivePayload = Collective("").Replace("4000.2", "4500.2", StringComparison.Ordinal);

        var updated = (await provider.RefreshAsync()).Find(LootSpotCatalog.MagaiaId)!;

        Assert.NotEqual(old.AverageTrashPerHour, updated.AverageTrashPerHour);
        Assert.Equal(time.GetUtcNow(), updated.UpdatedAt);
        Assert.Equal(old.RareDropHourlyRates, updated.RareDropHourlyRates);
        Assert.Equal(old.RareDropReferenceTrashPerHour, updated.RareDropReferenceTrashPerHour);
        Assert.NotEqual(updated.AverageTrashPerHour, updated.RareDropReferenceTrashPerHour);
        Assert.Equal(old.RareDropUpdatedAt, updated.RareDropUpdatedAt);
        Assert.Equal(old.RareDropSourceUrl, updated.RareDropSourceUrl);
        using var restarted = new GarmothGrindBenchmarkProvider(new Handler(null), directory.Path, time);
        Assert.Equal(updated, restarted.GetCachedSnapshot().Find(LootSpotCatalog.MagaiaId));
    }

    [Theory]
    [InlineData("unknown-name")]
    [InlineData("wrong-spot-item")]
    [InlineData("negative-rate")]
    [InlineData("future-rare-date")]
    [InlineData("wrong-rare-source")]
    [InlineData("wrong-scaling")]
    [InlineData("zero-trash-baseline")]
    [InlineData("negative-trash-baseline")]
    [InlineData("malformed-trash-baseline")]
    public void TamperedRareCacheCannotReplaceTheDatedBundledReference(string tampering)
    {
        using var directory = new CacheDirectory();
        var reference = Parse("""{"item_key":"15296_0","is_trash":false,"hourly_rate":1}""");
        var cache = JsonSerializer.SerializeToNode(new { SchemaVersion = 1, Benchmarks = new[] { reference } })!;
        var row = cache["Benchmarks"]![0]!;
        switch (tampering)
        {
            case "unknown-name": row["RareDropHourlyRates"] = JsonNode.Parse("""{"Unknown":1}"""); break;
            case "wrong-spot-item": row["RareDropHourlyRates"] = JsonNode.Parse("""{"Apeiron Necklace":1}"""); break;
            case "negative-rate": row["RareDropHourlyRates"]!["JIN Wandering Origin Crystal"] = -1; break;
            case "future-rare-date": row["RareDropUpdatedAt"] = CapturedAt.AddDays(1).ToString("O"); break;
            case "wrong-rare-source": row["RareDropSourceUrl"] = "https://garmoth.com/grind-tracker/best-grind-spots/214"; break;
            case "wrong-scaling": row["RareDropRateScalingApplies"] = false; break;
            case "zero-trash-baseline": row["RareDropReferenceTrashPerHour"] = 0; break;
            case "negative-trash-baseline": row["RareDropReferenceTrashPerHour"] = -1; break;
            case "malformed-trash-baseline": row["RareDropReferenceTrashPerHour"] = JsonNode.Parse("""{"hourly":1}"""); break;
        }
        File.WriteAllText(directory.Path, cache.ToJsonString());
        using var provider = new GarmothGrindBenchmarkProvider(new Handler(null), directory.Path, new ManualTime());
        Assert.Equal(GarmothGrindBenchmarks.Find(LootSpotCatalog.MagaiaId), provider.GetCachedSnapshot().Find(LootSpotCatalog.MagaiaId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyRareCacheWithoutMatchedTrashKeepsTheRatingAndReplacesTheEntireIncompleteRareReference(bool explicitNull)
    {
        using var directory = new CacheDirectory();
        var legacy = Parse("""{"item_key":"15296_0","is_trash":false,"hourly_rate":99}""") with
        {
            AverageTrashPerHour = 12345m,
            RareDropUpdatedAt = CapturedAt.AddDays(-1),
            RareDropSourceUrl = "https://garmoth.com/grind-tracker/best-grind-spots/215?startDate=2026-09-01&endDate=2026-09-10",
        };
        var cache = JsonSerializer.SerializeToNode(new { SchemaVersion = 1, Benchmarks = new[] { legacy } })!;
        var row = cache["Benchmarks"]![0]!.AsObject();
        if (explicitNull) row["RareDropReferenceTrashPerHour"] = null;
        else row.Remove("RareDropReferenceTrashPerHour");
        File.WriteAllText(directory.Path, cache.ToJsonString());
        using var provider = new GarmothGrindBenchmarkProvider(new Handler(null), directory.Path, new ManualTime());

        var loaded = provider.GetCachedSnapshot().Find(LootSpotCatalog.MagaiaId)!;
        var fallback = GarmothGrindBenchmarks.Find(LootSpotCatalog.MagaiaId)!;

        Assert.Equal(legacy.AverageTrashPerHour, loaded.AverageTrashPerHour);
        Assert.Equal(legacy.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(legacy.SourceUrl, loaded.SourceUrl);
        Assert.Equal(fallback.RareDropHourlyRates, loaded.RareDropHourlyRates);
        Assert.Equal(fallback.RareDropReferenceTrashPerHour, loaded.RareDropReferenceTrashPerHour);
        Assert.Equal(fallback.RareDropUpdatedAt, loaded.RareDropUpdatedAt);
        Assert.Equal(fallback.RareDropSourceUrl, loaded.RareDropSourceUrl);
        Assert.NotEqual(legacy.RareDropHourlyRates, loaded.RareDropHourlyRates);
        Assert.NotEqual(loaded.AverageTrashPerHour, loaded.RareDropReferenceTrashPerHour);
    }

    [Fact]
    public void LegacyNoScrollTrashCacheIsRetainedAndGainsOnlySeparatelyDatedBundledRareValues()
    {
        using var directory = new CacheDirectory();
        var reference = Parse("", 149) with { AverageTrashPerHour = 12345m };
        var cache = JsonSerializer.SerializeToNode(new { SchemaVersion = 1, Benchmarks = new[] { reference } })!;
        var row = cache["Benchmarks"]![0]!.AsObject();
        row.Remove("RareDropHourlyRates");
        row.Remove("RareDropReferenceTrashPerHour");
        row.Remove("RareDropRateScalingApplies");
        row.Remove("RareDropUpdatedAt");
        row.Remove("RareDropSourceUrl");
        row["Conditions"] = GarmothGrindBenchmarks.Conditions;
        File.WriteAllText(directory.Path, cache.ToJsonString());
        using var provider = new GarmothGrindBenchmarkProvider(new Handler(null), directory.Path, new ManualTime());
        var loaded = provider.GetCachedSnapshot().Find("winter-tree-fossil-280")!;
        Assert.Equal(12345m, loaded.AverageTrashPerHour);
        Assert.Equal(reference.UpdatedAt, loaded.UpdatedAt);
        Assert.False(loaded.RareDropRateScalingApplies);
        Assert.Equal(GarmothGrindBenchmarks.NoScrollConditions, loaded.Conditions);
        Assert.NotNull(loaded.RareDropHourlyRates);
        Assert.Equal(GarmothGrindBenchmarks.Find("winter-tree-fossil-280")!.RareDropReferenceTrashPerHour,
            loaded.RareDropReferenceTrashPerHour);
        Assert.Equal(CapturedAt, loaded.RareDropUpdatedAt);
    }

    private static GrindBenchmark Parse(string drops, int id = 215) => Assert.Single(GarmothGrindBenchmarkProvider.Parse(
        Encoding.UTF8.GetBytes(Collective(drops, id)), Encoding.UTF8.GetBytes(Metadata), CapturedAt));

    private static string Collective(string drops, int id = 215) => $$"""
        {"start_date":"2026-09-10","end_date":"2026-10-08","data":[
          {"grindspot_id":{{id}},"total_minutes":120,"drops":[
            {"is_trash":true,"hourly_rate":4000.2}{{(string.IsNullOrEmpty(drops) ? "" : "," + drops)}}]}]}
        """;

    private sealed class Handler(string? collective) : HttpMessageHandler
    {
        public string? CollectivePayload { get; set; } = collective;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (CollectivePayload is null) throw new HttpRequestException("Offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.Host == "garmoth.com" ? Metadata : CollectivePayload,
                    Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = CapturedAt;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class CacheDirectory : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "grindcrest-rare-benchmark-tests-" + Guid.NewGuid().ToString("N"));
        public CacheDirectory() => Directory.CreateDirectory(_directory);
        public string Path => System.IO.Path.Combine(_directory, GarmothGrindBenchmarkProvider.CacheFileName);
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
