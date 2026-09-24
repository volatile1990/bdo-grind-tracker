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
    [Fact]
    public async Task EnglishTimelineTranslatesItsTextsAndKeepsMissingPriceWarnings()
    {
        var demo = OverlayMetrics.DemoFor("en");
        var snapshot = demo with
        {
            Metrics = new Dictionary<string, OverlayMetric>
            {
                ["chart"] = demo.Metrics["chart"] with { Detail = "Teilbetrag · Preise fehlen" },
            },
        };
        var html = await RenderAsync<OverlayWidgetPreview>(new()
        {
            [nameof(OverlayWidgetPreview.Widget)] = OverlayCatalog.CreateWidget("chart"),
            [nameof(OverlayWidgetPreview.Snapshot)] = snapshot,
        });

        Assert.Contains("Session timeline", html);
        Assert.Contains("whole session", html);
        Assert.Contains("title=\"Mechanics\"", html);
        Assert.Contains(AppText.Translate("Teilbetrag · Preise fehlen", "en"), html);
        Assert.Contains("overlay-timeline-drop", html);
        Assert.DoesNotContain("ganze Session", html);
        Assert.DoesNotContain("Mechaniken", html);
        Assert.DoesNotContain("Teilbetrag", html);
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

    [Fact]
    public async Task APickedUpRotationMeetsTheReferenceAtItsFirstCertainPhase()
    {
        var reference = EventHorizonRotationDemo.Reference with { Sections = [new("boss", 418.133, 447.717)] };
        // Tracking began mid-rotation; only the boss spawn fits exactly one phase.
        var state = new RotationMonitorSnapshot
        {
            SpotId = BdoGrindTracker.Core.LootSpotCatalog.EventHorizonId, HasProfile = true, Synchronized = true,
            TrackingState = "partial", Elapsed = 40, Best = reference, AlignedAt = 30, AlignedSection = "boss",
            Events = [new("start", "Rotationsstart", 0), new("anomaly", "Wurmloch gestartet", 12), new("boss", "Boss-Spawn", 30)],
        };
        var html = await RenderAsync<OverlayRotationTimeline>(new()
        {
            [nameof(OverlayRotationTimeline.State)] = state,
            [nameof(OverlayRotationTimeline.Language)] = "en",
        });

        Assert.Contains("rotation-tracking-error", html);
        Assert.Contains("Tracking error", html);
        // The boss spawn sits where the reference's does; the guessed wormhole before it is not drawn as a phase.
        var x = (418.133 / RotationTimelinePresentation.Extent(state, "best") * 540).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"x=\"{x}\" y=\"71\"", html);
        Assert.Equal(1, html.Split("Wormhole 1 · Waves").Length - 1);
        var now = ((418.133 + 10) / RotationTimelinePresentation.Extent(state, "best") * 540).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"class=\"rotation-playhead\" x1=\"{now}\"", html);
    }

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
