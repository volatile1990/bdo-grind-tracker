using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindRatingEvaluatorTests
{
    // Explicit test fixture values; production benchmarks come from the provider.
    private static GrindBenchmark Magaia() => new(LootSpotCatalog.MagaiaId, 13_946m, 16_300m, 18_500m,
        DateTimeOffset.Parse("2026-09-10T12:00:00Z"), "https://garmoth.com/grind-tracker/global",
        "Test fixture conditions");

    [Theory]
    [InlineData(0, GrindRatingTier.BelowAverage)]
    [InlineData(13945, GrindRatingTier.BelowAverage)]
    [InlineData(13946, GrindRatingTier.Average)]
    [InlineData(16299, GrindRatingTier.Average)]
    [InlineData(16300, GrindRatingTier.High)]
    [InlineData(18499, GrindRatingTier.High)]
    [InlineData(18500, GrindRatingTier.Top)]
    [InlineData(25000, GrindRatingTier.Top)]
    public void EachTierIncludesItsExactThreshold(long quantity, GrindRatingTier expectedTier)
    {
        var benchmark = Magaia();

        var result = GrindRatingEvaluator.Evaluate(benchmark.SpotId, quantity, TimeSpan.FromHours(1), benchmark);

        Assert.Equal(expectedTier, result.Tier);
        Assert.Equal((decimal)quantity, result.TrashPerHour);
        Assert.Same(benchmark, result.Benchmark);
        Assert.False(result.IsProvisional);
    }

    [Theory]
    [InlineData(6973, GrindRatingTier.Average)]
    [InlineData(8150, GrindRatingTier.High)]
    [InlineData(9250, GrindRatingTier.Top)]
    public void ClassificationUsesTheWholeSessionsRateInsteadOfItsAbsoluteLootCount(long quantity, GrindRatingTier expectedTier)
    {
        var benchmark = Magaia();

        var result = GrindRatingEvaluator.Evaluate(benchmark.SpotId, quantity, TimeSpan.FromMinutes(30), benchmark);

        Assert.Equal(expectedTier, result.Tier);
        Assert.Equal(quantity * 2m, result.TrashPerHour);
    }

    [Theory]
    [InlineData(69728, GrindRatingTier.BelowAverage, "13.946")]
    [InlineData(81498, GrindRatingTier.Average, "16.300")]
    [InlineData(92498, GrindRatingTier.High, "18.500")]
    public void RoundedDisplayValuesCannotPromoteARateBelowTheThreshold(long quantity, GrindRatingTier expectedTier,
        string roundedDisplay)
    {
        var result = GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, quantity, TimeSpan.FromHours(5), Magaia());

        Assert.Equal(expectedTier, result.Tier);
        Assert.Equal(roundedDisplay, Presentation.Number(result.TrashPerHour!.Value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(123456789)]
    [InlineData(9354762312)]
    [InlineData(1234567891234)]
    public void FractionalSessionDurationsUseExactlyTheSameDecimalRateAsTheLiveSession(long ticks)
    {
        var elapsed = TimeSpan.FromTicks(ticks);

        var result = GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, 20_013, elapsed, Magaia());

        Assert.NotEqual(GrindRatingTier.Unavailable, result.Tier);
        Assert.Equal(Presentation.Hourly(20_013, elapsed), result.TrashPerHour);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2999999999, true)]
    [InlineData(3000000000, false)]
    [InlineData(3000000001, false)]
    public void EarlySessionsKeepTheirActualRatingAndOnlyGainAProvisionalFlag(long ticks, bool provisional)
    {
        var elapsed = TimeSpan.FromTicks(ticks);

        var result = GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, 2_000, elapsed, Magaia());

        Assert.Equal(GrindRatingTier.Top, result.Tier);
        Assert.Equal(Presentation.Hourly(2_000, elapsed), result.TrashPerHour);
        Assert.Equal(provisional, result.IsProvisional);
    }

    [Fact]
    public void ZeroTrashInAKnownSpotIsBelowAverageEvenDuringAProvisionalSession()
    {
        var result = GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, 0, TimeSpan.FromSeconds(1), Magaia());

        Assert.Equal(GrindRatingTier.BelowAverage, result.Tier);
        Assert.Equal(0m, result.TrashPerHour);
        Assert.True(result.IsProvisional);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown-spot")]
    [InlineData("MAGAIA")]
    [InlineData(LootSpotCatalog.HermesiaId)]
    public void MissingUnknownOrChangedSpotsCannotReuseThePreviousBenchmark(string? spotId)
    {
        AssertUnavailable(GrindRatingEvaluator.Evaluate(spotId, 20_000, TimeSpan.FromHours(1), Magaia()));
    }

    [Fact]
    public void AProviderEntryForAnUnsupportedSpotCannotCreateARating()
    {
        var benchmark = Magaia() with { SpotId = "unrecognized" };

        AssertUnavailable(GrindRatingEvaluator.Evaluate(benchmark.SpotId, 20_000, TimeSpan.FromHours(1), benchmark));
    }

    [Fact]
    public void MissingBenchmarkDoesNotInventThresholdsFromTheCurrentSession()
    {
        AssertUnavailable(GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, 20_000, TimeSpan.FromHours(1), null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void NonpositiveElapsedTimeCannotProduceAnHourlyRating(long ticks)
    {
        AssertUnavailable(GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, 20_000, TimeSpan.FromTicks(ticks), Magaia()));
    }

    [Fact]
    public void NegativeLootAndUnrepresentableHourlyValuesReturnUnavailable()
    {
        AssertUnavailable(GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, -1, TimeSpan.FromHours(1), Magaia()));
        AssertUnavailable(GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, long.MaxValue, TimeSpan.FromTicks(1), Magaia()));
    }

    [Fact]
    public void LargeRepresentableValuesRemainUsableWithoutAnArtificialCeiling()
    {
        var result = GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, long.MaxValue, TimeSpan.FromHours(1), Magaia());

        Assert.Equal(GrindRatingTier.Top, result.Tier);
        Assert.Equal((decimal)long.MaxValue, result.TrashPerHour);
        Assert.False(result.IsProvisional);
    }

    [Theory]
    [InlineData(0, 16300, 18500)]
    [InlineData(-1, 16300, 18500)]
    [InlineData(13946, 13946, 18500)]
    [InlineData(13946, 13945, 18500)]
    [InlineData(13946, 16300, 16300)]
    [InlineData(13946, 16300, 16299)]
    public void ThresholdsMustBeStrictlyIncreasingAndPositive(int average, int high, int top)
    {
        var benchmark = Magaia() with { AverageTrashPerHour = average, HighTrashPerHour = high, TopTrashPerHour = top };

        AssertUnavailable(GrindRatingEvaluator.Evaluate(benchmark.SpotId, 20_000, TimeSpan.FromHours(1), benchmark));
    }

    [Fact]
    public void AverageOnlyBenchmarksNeverInventHighOrTopThresholds()
    {
        var benchmark = Magaia() with
        {
            SpotId = LootSpotCatalog.ScalesOfJudgmentId,
            AverageTrashPerHour = 13_535m, HighTrashPerHour = null, TopTrashPerHour = null,
        };

        Assert.Equal(GrindRatingTier.BelowAverage,
            GrindRatingEvaluator.Evaluate(benchmark.SpotId, 13_534, TimeSpan.FromHours(1), benchmark).Tier);
        Assert.Equal(GrindRatingTier.Average,
            GrindRatingEvaluator.Evaluate(benchmark.SpotId, 13_535, TimeSpan.FromHours(1), benchmark).Tier);
        var highRate = GrindRatingEvaluator.Evaluate(benchmark.SpotId, 100_000, TimeSpan.FromHours(1), benchmark);
        Assert.Equal(GrindRatingTier.Average, highRate.Tier);
        Assert.Same(benchmark, highRate.Benchmark);
        Assert.Null(highRate.Benchmark!.HighTrashPerHour);
        Assert.Null(highRate.Benchmark.TopTrashPerHour);
    }

    [Fact]
    public void EventHorizonUsesItsOwnAverageWhenNoHigherQuantilesExist()
    {
        var benchmark = Magaia() with
        {
            SpotId = LootSpotCatalog.EventHorizonId,
            AverageTrashPerHour = 12_267m, HighTrashPerHour = null, TopTrashPerHour = null,
        };

        var result = GrindRatingEvaluator.Evaluate(benchmark.SpotId, 12_267, TimeSpan.FromHours(1), benchmark);

        Assert.Equal(GrindRatingTier.Average, result.Tier);
        Assert.Equal(12_267m, result.TrashPerHour);
    }

    [Fact]
    public void AnAvailableHighThresholdCanBeUsedWithoutATopThreshold()
    {
        var benchmark = Magaia() with { TopTrashPerHour = null };

        Assert.Equal(GrindRatingTier.Average,
            GrindRatingEvaluator.Evaluate(benchmark.SpotId, 16_299, TimeSpan.FromHours(1), benchmark).Tier);
        Assert.Equal(GrindRatingTier.High,
            GrindRatingEvaluator.Evaluate(benchmark.SpotId, 16_300, TimeSpan.FromHours(1), benchmark).Tier);
        Assert.Equal(GrindRatingTier.High,
            GrindRatingEvaluator.Evaluate(benchmark.SpotId, 100_000, TimeSpan.FromHours(1), benchmark).Tier);
    }

    [Fact]
    public void AnAvailableTopThresholdDoesNotRequireAnInventedHighThreshold()
    {
        var benchmark = Magaia() with { HighTrashPerHour = null };

        Assert.Equal(GrindRatingTier.Average,
            GrindRatingEvaluator.Evaluate(benchmark.SpotId, 18_499, TimeSpan.FromHours(1), benchmark).Tier);
        Assert.Equal(GrindRatingTier.Top,
            GrindRatingEvaluator.Evaluate(benchmark.SpotId, 18_500, TimeSpan.FromHours(1), benchmark).Tier);
    }

    [Theory]
    [InlineData(13946, null)]
    [InlineData(0, null)]
    [InlineData(null, 13946)]
    [InlineData(null, 0)]
    public void PresentOptionalThresholdsMustStillExceedThePrecedingKnownThreshold(int? high, int? top)
    {
        var benchmark = Magaia() with { HighTrashPerHour = high, TopTrashPerHour = top };

        AssertUnavailable(GrindRatingEvaluator.Evaluate(benchmark.SpotId, 20_000, TimeSpan.FromHours(1), benchmark));
    }

    [Fact]
    public void SourceTimestampsAndConditionsDoNotCreateASecondFreshnessOrNormalizationPolicy()
    {
        var benchmark = Magaia() with { UpdatedAt = DateTimeOffset.UnixEpoch, Conditions = "" };

        var result = GrindRatingEvaluator.Evaluate(benchmark.SpotId, 16_300, TimeSpan.FromHours(1), benchmark);

        Assert.Equal(GrindRatingTier.High, result.Tier);
        Assert.Same(benchmark, result.Benchmark);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/grind-tracker/global")]
    [InlineData("invalid url")]
    [InlineData("file:///C:/benchmark.json")]
    [InlineData("javascript:alert(1)")]
    public void MissingOrInvalidPublicSourcesCannotProduceAnUnsupportedRating(string? sourceUrl)
    {
        var benchmark = Magaia() with { SourceUrl = sourceUrl! };

        AssertUnavailable(GrindRatingEvaluator.Evaluate(benchmark.SpotId, 20_000, TimeSpan.FromHours(1), benchmark));
    }

    [Theory]
    [InlineData("https://garmoth.com/grind-tracker/global")]
    [InlineData("http://garmoth.com/grind-tracker/global")]
    public void PublicHttpSourcesRemainAvailableForThePresentationTooltip(string sourceUrl)
    {
        var benchmark = Magaia() with { SourceUrl = sourceUrl };

        var result = GrindRatingEvaluator.Evaluate(benchmark.SpotId, 16_300, TimeSpan.FromHours(1), benchmark);

        Assert.Equal(GrindRatingTier.High, result.Tier);
        Assert.Equal(sourceUrl, result.Benchmark!.SourceUrl);
    }

    [Fact]
    public void SourceConditionsDoNotApplyInventedLootScrollOrAgrisNormalization()
    {
        var benchmark = Magaia() with { Conditions = "Loot-Scroll Stufe 2 · Agris aktiv", UpdatedAt = DateTimeOffset.UnixEpoch };

        var result = GrindRatingEvaluator.Evaluate(benchmark.SpotId, 16_300, TimeSpan.FromHours(1), benchmark);

        Assert.Equal(GrindRatingTier.High, result.Tier);
        Assert.Equal(16_300m, result.TrashPerHour);
        Assert.Same(benchmark, result.Benchmark);
        Assert.Equal(benchmark.Conditions, result.Benchmark!.Conditions);
    }

    private static void AssertUnavailable(GrindRatingResult result)
    {
        Assert.Equal(GrindRatingTier.Unavailable, result.Tier);
        Assert.Null(result.TrashPerHour);
        Assert.Null(result.Benchmark);
        Assert.False(result.IsProvisional);
    }
}
