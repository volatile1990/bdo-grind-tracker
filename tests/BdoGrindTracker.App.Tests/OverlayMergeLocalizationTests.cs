using System.Net;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Overlay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayMergeLocalizationTests
{
    [Theory]
    [InlineData("Session average")]
    [InlineData("Session-Durchschnitt")]
    public void SectionChartRecognizesTheAverageExplanationInEitherLanguage(string average)
    {
        var snapshot = OverlayMetrics.DemoFor("en") with
        {
            Metrics = new Dictionary<string, OverlayMetric> { ["chart"] = new("Silver / h", "1 B", average) },
        };
        var chart = OverlayChartSections.Create(Sections(), snapshot);

        Assert.Equal("Silver per 10 s · History", chart.Title);
        Assert.Equal("Value: session avg./h · whole session", chart.Detail);
        Assert.Contains("net silver earned per 10 seconds", chart.Description);
        Assert.True(chart.HasData);
    }

    [Fact]
    public async Task EnglishSectionCurveKeepsMissingPriceWarningsAndValuableDropMarkers()
    {
        var snapshot = OverlayMetrics.DemoFor("en") with
        {
            Metrics = new Dictionary<string, OverlayMetric>
            {
                ["chart"] = new("Silver / h", "1 B *", "Teilbetrag · Preise fehlen"),
            },
        };
        var html = await RenderAsync<OverlayWidgetPreview>(new()
        {
            [nameof(OverlayWidgetPreview.Widget)] = Sections() with { ChartPeakMode = OverlayChartSections.ClipPeaks },
            [nameof(OverlayWidgetPreview.Snapshot)] = snapshot,
        });

        Assert.Contains("Silver per 10 s · History", html);
        Assert.Contains(AppText.Translate("Teilbetrag · Preise fehlen", "en"), html);
        Assert.Contains("overlay-chart-drop-marker", html);
        Assert.Contains("Clipped at the top: valuable drop", html);
        Assert.DoesNotContain("Zahl Ø Session", html);
        Assert.DoesNotContain("Silber je", html);
    }

    [Fact]
    public async Task EventHorizonTimelineTranslatesPhasesAndKeepsDistortionMarkers()
    {
        var state = EventHorizonRotationDemo.At(400);
        var html = await RenderAsync<OverlayRotationTimeline>(new()
        {
            [nameof(OverlayRotationTimeline.State)] = state,
            [nameof(OverlayRotationTimeline.Language)] = "en",
        });

        Assert.Contains("Wormhole 1 · Waves", html);
        Assert.Contains("Debris AFK · Halfway", html);
        Assert.Contains("Sample data · Event Horizon recording as reference", html);
        Assert.DoesNotContain("Wurmloch", html);
        Assert.DoesNotContain("Trümmer", html);
        var expectedMarkers = state.Events.Count(e => e.Seconds <= state.Elapsed && RotationTimelinePresentation.IsMarker(e)) +
            state.Best!.Events.Count(RotationTimelinePresentation.IsMarker);
        Assert.True(expectedMarkers > 0);
        Assert.Equal(expectedMarkers, html.Split("rotation-pack-marker").Length - 1);
    }

    private static OverlayWidget Sections() => OverlayCatalog.CreateWidget("chart") with
    {
        ChartMode = OverlayChartSections.SectionsMode,
    };

    private static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters) where T : IComponent
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
