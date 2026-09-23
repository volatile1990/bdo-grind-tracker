using System.Net;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindRatingSpectrumTests
{
    [Theory]
    [InlineData(12_000, 37.5, "Average → High · 50 %", "Noch 2.000 Trash / h bis High")]
    [InlineData(17_000, 62.5, "High → Top · 50 %", "Noch 3.000 Trash / h bis Top")]
    public void MarkerAndRemainingTrashUseTheCurrentBenchmarkInterval(long trash, double position,
        string progress, string gap)
    {
        var spectrum = Spectrum(trash);

        Assert.Equal(position, spectrum.Position, 8);
        Assert.Equal(progress, spectrum.ProgressLabel);
        Assert.Equal(gap, spectrum.GapLabel);
        Assert.Equal(new[] { "Average", "High", "Top" }, spectrum.Stops.Select(stop => stop.Label));
        Assert.Equal(new[] { 10_000m, 14_000m, 20_000m }, spectrum.Stops.Select(stop => stop.TrashPerHour));
        Assert.Equal(new[] { 25d, 50d, 75d }, spectrum.Stops.Select(stop => stop.Position));
    }

    [Theory]
    [InlineData(10_000, 25, GrindRatingTier.Average)]
    [InlineData(14_000, 50, GrindRatingTier.High)]
    [InlineData(20_000, 75, GrindRatingTier.Top)]
    public void ExactThresholdPlacesTheMarkerOnItsReference(long trash, double position, GrindRatingTier tier)
    {
        var result = Evaluate(trash);
        var spectrum = Assert.IsType<GrindRatingSpectrum>(GrindRatingSpectrum.Create(result, "de"));

        Assert.Equal(tier, result.Tier);
        Assert.Equal(position, spectrum.Position);
        Assert.Equal(position, Assert.Single(spectrum.Stops, stop => stop.TrashPerHour == trash).Position);
        Assert.DoesNotContain("99 %", spectrum.ProgressLabel);
        if (tier == GrindRatingTier.Top)
        {
            Assert.Equal("Top erreicht", spectrum.ProgressLabel);
            Assert.Equal("Referenz: 20.000 Trash / h", spectrum.GapLabel);
        }
    }

    [Theory]
    [InlineData(49_999, 25, "Average", GrindRatingTier.BelowAverage)]
    [InlineData(69_999, 50, "High", GrindRatingTier.Average)]
    [InlineData(99_999, 75, "Top", GrindRatingTier.High)]
    public void RoundedHourlyRateCannotVisuallyPromoteASubthresholdPerformance(long trash, double boundary,
        string nextLabel, GrindRatingTier tier)
    {
        var result = GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, trash, TimeSpan.FromHours(5), Benchmark());
        var spectrum = Assert.IsType<GrindRatingSpectrum>(GrindRatingSpectrum.Create(result, "de"));

        Assert.Equal(tier, result.Tier);
        Assert.True(spectrum.Position < boundary);
        Assert.Contains("99 %", spectrum.ProgressLabel);
        Assert.DoesNotContain("100 %", spectrum.ProgressLabel);
        Assert.DoesNotContain("erreicht", spectrum.ProgressLabel);
        Assert.Equal($"Noch 1 Trash / h bis {nextLabel}", spectrum.GapLabel);
    }

    [Theory]
    [InlineData(0, 0, "0 % von Average", "Noch 10.000 Trash / h bis Average")]
    [InlineData(5_000, 12.5, "50 % von Average", "Noch 5.000 Trash / h bis Average")]
    public void ZeroAndBelowAverageRemainOnTheScale(long trash, double position, string progress, string gap)
    {
        var spectrum = Spectrum(trash);

        Assert.Equal(position, spectrum.Position);
        Assert.Equal(progress, spectrum.ProgressLabel);
        Assert.Equal(gap, spectrum.GapLabel);
    }

    [Theory]
    [InlineData(23_000, 87.5, "15,0 % über Top", "+3.000 Trash / h über Top")]
    [InlineData(26_000, 100, "30,0 % über Top", "+6.000 Trash / h über Top")]
    [InlineData(60_000, 100, "200,0 % über Top", "+40.000 Trash / h über Top")]
    public void AboveTopRetainsItsTrueDistanceWhenTheMarkerReachesTheScaleEnd(long trash, double position,
        string progress, string gap)
    {
        var spectrum = Spectrum(trash);

        Assert.Equal(position, spectrum.Position);
        Assert.Equal(progress, spectrum.ProgressLabel);
        Assert.Equal(gap, spectrum.GapLabel);
    }

    [Theory]
    [InlineData(null, 20_000, 15_000, "Top", "Average → Top · 50 %", "Noch 5.000 Trash / h bis Top")]
    [InlineData(14_000, null, 12_000, "High", "Average → High · 50 %", "Noch 2.000 Trash / h bis High")]
    public void MissingOptionalReferenceUsesOnlyTheKnownInterval(int? high, int? top, long trash,
        string lastLabel, string progress, string gap)
    {
        var spectrum = Spectrum(trash, Benchmark() with { HighTrashPerHour = high, TopTrashPerHour = top });

        Assert.Equal(new[] { "Average", lastLabel }, spectrum.Stops.Select(stop => stop.Label));
        Assert.Equal(50, spectrum.Position, 8);
        Assert.Equal(progress, spectrum.ProgressLabel);
        Assert.Equal(gap, spectrum.GapLabel);
    }

    [Theory]
    [InlineData(5_000, 25, "50 % von Average")]
    [InlineData(10_000, 50, "Average erreicht")]
    [InlineData(12_500, 62.5, "25,0 % über Average")]
    [InlineData(30_000, 100, "200,0 % über Average")]
    public void AverageOnlySourceNeverCreatesHigherReferences(long trash, double position, string progress)
    {
        var spectrum = Spectrum(trash, Benchmark() with { HighTrashPerHour = null, TopTrashPerHour = null });

        Assert.Equal("Average", Assert.Single(spectrum.Stops).Label);
        Assert.Equal(position, spectrum.Position);
        Assert.Equal(progress, spectrum.ProgressLabel);
        Assert.DoesNotContain("High", spectrum.Description);
        Assert.DoesNotContain("Top", spectrum.Description);
    }

    [Theory]
    [InlineData(10_000, 20_000, "Average / High", "Top", "High → Top · 50 %")]
    [InlineData(20_000, 20_000, "Average", "High / Top", "Average → High / Top · 50 %")]
    public void EqualThresholdsShareOneReferenceWithoutADivisionByZero(int high, int top,
        string firstLabel, string lastLabel, string progress)
    {
        var spectrum = Spectrum(15_000, Benchmark() with { HighTrashPerHour = high, TopTrashPerHour = top });

        Assert.Equal(new[] { firstLabel, lastLabel }, spectrum.Stops.Select(stop => stop.Label));
        Assert.Equal(50, spectrum.Position, 8);
        Assert.Equal(progress, spectrum.ProgressLabel);
    }

    [Fact]
    public void AllTiedReferencesKeepTheirNamesAndUseTheHighestTierAtTheBoundary()
    {
        var benchmark = Benchmark() with { HighTrashPerHour = 10_000, TopTrashPerHour = 10_000 };
        var result = Evaluate(10_000, benchmark);
        var spectrum = Assert.IsType<GrindRatingSpectrum>(GrindRatingSpectrum.Create(result, "de"));

        Assert.Equal(GrindRatingTier.Top, result.Tier);
        Assert.Equal("Average / High / Top", Assert.Single(spectrum.Stops).Label);
        Assert.Equal(50, spectrum.Position);
        Assert.Equal("Top erreicht", spectrum.ProgressLabel);
        Assert.Equal("50,0 % über Top", Spectrum(15_000, benchmark).ProgressLabel);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-spot")]
    [InlineData("source")]
    [InlineData("time")]
    [InlineData("negative-loot")]
    public async Task UnavailableRatingHasNoScaleOrPosition(string scenario)
    {
        var benchmark = scenario switch
        {
            "missing" => null,
            "wrong-spot" => Benchmark() with { SpotId = LootSpotCatalog.HermesiaId },
            "source" => Benchmark() with { SourceUrl = "" },
            _ => Benchmark(),
        };
        var result = GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId,
            scenario == "negative-loot" ? -1 : 12_000,
            scenario == "time" ? TimeSpan.Zero : TimeSpan.FromHours(1), benchmark);
        var spectrum = GrindRatingSpectrum.Create(result, "de");

        Assert.Null(spectrum);
        Assert.Equal("", await Render<GrindRatingScale>(new() { [nameof(GrindRatingScale.Value)] = spectrum }));
    }

    [Theory]
    [InlineData("de", "Average → High · 50 %", "Noch 2.000 Trash / h bis High", "12.000 Trash / h", "keinen Spieler-Perzentilrang")]
    [InlineData("en", "Average → High · 50%", "2,000 trash / h to reach High", "12,000 Trash / h", "not a player percentile rank")]
    public void SpectrumLocalizesProgressNumbersAndItsMeaning(string language, string progress,
        string gap, string hourly, string explanation)
    {
        var spectrum = Spectrum(12_000, language: language);

        Assert.Equal(progress, spectrum.ProgressLabel);
        Assert.Equal(gap, spectrum.GapLabel);
        Assert.Equal(hourly, spectrum.TrashHourly);
        Assert.Contains(explanation, spectrum.Description);
        Assert.Contains(gap, spectrum.Description);
    }

    [Fact]
    public void RepeatedProjectionsCompareByTheirReferenceValues()
    {
        var first = Spectrum(12_000);
        var second = Spectrum(12_000);

        Assert.NotSame(first.Stops, second.Stops);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, Spectrum(12_001));
        Assert.NotEqual(first, Spectrum(12_000, language: "en"));
        Assert.NotEqual(first, first with { Stops = first.Stops.Select(stop => stop with { Value = "changed" }).ToArray() });

        var state = State(12_000);
        var firstMetric = new OverlayMetrics().Update(state, new() { UiLanguage = "de" }).Metrics["grind-rating"];
        var secondMetric = new OverlayMetrics().Update(state, new() { UiLanguage = "de" }).Metrics["grind-rating"];
        Assert.Equal(first, firstMetric.Spectrum);
        Assert.Equal(firstMetric, secondMetric);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScaleExposesTheCompletePositionToAssistiveTechnology(bool compact)
    {
        var spectrum = Spectrum(12_000);
        var markup = await Render<GrindRatingScale>(new()
        {
            [nameof(GrindRatingScale.Value)] = spectrum,
            [nameof(GrindRatingScale.Compact)] = compact,
        });

        Assert.Contains("role=\"img\"", markup);
        Assert.Contains($"aria-label=\"{spectrum.Description}\"", markup);
        Assert.Contains("class=\"grind-spectrum-marker\" style=\"left:37.5%\"", markup);
        Assert.Contains("aria-hidden=\"true\"", markup);
        Assert.Contains("Average", markup);
        Assert.Contains("High", markup);
        Assert.Contains("Top", markup);
        Assert.Equal(!compact, markup.Contains("<small>10.000</small>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverlayUsesTheSharedAccessibleScaleAndHidesItWithoutAReference()
    {
        var state = State(12_000);
        var snapshot = new OverlayMetrics().Update(state, new() { UiLanguage = "de" });
        var spectrum = Assert.IsType<GrindRatingSpectrum>(snapshot.Metrics["grind-rating"].Spectrum);
        var markup = await RenderOverlay(snapshot);

        Assert.Contains($"aria-label=\"{spectrum.Description}\"", markup);
        Assert.Contains(spectrum.ProgressLabel, markup);
        Assert.Contains("grind-spectrum-marker", markup);

        var unavailable = new OverlayMetrics().Update(state with { GrindBenchmark = null }, new() { UiLanguage = "de" });
        Assert.Null(unavailable.Metrics["grind-rating"].Spectrum);
        Assert.DoesNotContain("grind-spectrum-marker", await RenderOverlay(unavailable));
    }

    private static GrindBenchmark Benchmark() => new(LootSpotCatalog.MagaiaId, 10_000, 14_000, 20_000,
        new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), "https://garmoth.com/grind-tracker/global", "Test fixture");

    private static GrindRatingResult Evaluate(long trash, GrindBenchmark? benchmark = null) =>
        GrindRatingEvaluator.Evaluate(LootSpotCatalog.MagaiaId, trash, TimeSpan.FromHours(1), benchmark ?? Benchmark());

    private static GrindRatingSpectrum Spectrum(long trash, GrindBenchmark? benchmark = null, string language = "de") =>
        Assert.IsType<GrindRatingSpectrum>(GrindRatingSpectrum.Create(Evaluate(trash, benchmark), language));

    private static TrackerState State(long trash) => new()
    {
        SpotId = LootSpotCatalog.MagaiaId, HasSession = true, IsRunning = true,
        Elapsed = TimeSpan.FromHours(1), GrindBenchmark = Benchmark(),
        Loot = new(new Dictionary<string, long> { ["Elion Follower's Helmet"] = trash }, trash, 100),
    };

    private static Task<string> RenderOverlay(OverlaySnapshot snapshot) => Render<OverlayWidgetPreview>(new()
    {
        [nameof(OverlayWidgetPreview.Widget)] = OverlayCatalog.CreateWidget("grind-rating"),
        [nameof(OverlayWidgetPreview.Snapshot)] = snapshot,
    });

    private static async Task<string> Render<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
    }
}
