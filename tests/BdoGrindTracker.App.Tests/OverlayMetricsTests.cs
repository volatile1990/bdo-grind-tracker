using System.Net;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayMetricsTests
{
    [Fact]
    public void MetricsUseCanonicalTotalsAndLocalizeOnlyDisplayedNames()
    {
        var state = Session() with
        {
            Elapsed = TimeSpan.FromMinutes(30), DetectedGameLanguage = "de",
            Loot = new(new Dictionary<string, long>
            {
                ["Black Stone"] = 3000, ["Black Crystal Fragment"] = 1735,
                ["BON Wandering Origin Crystal"] = 1, ["Caphras Stone"] = 0,
            }, 4736, 429),
            Silver = new(60_000_000, 50_000_000, 3, [], [], false),
        };

        var actual = new OverlayMetrics().Update(state, new() { GameLanguage = "auto" });

        Assert.Equal("00:30:00", actual.Metrics["duration"].Value);
        Assert.Equal("1.735", actual.Metrics["trash"].Value);
        Assert.Equal("3.470", actual.Metrics["trash-hour"].Value);
        Assert.Equal("50,0 Mio.", actual.Metrics["silver"].Value);
        Assert.Equal("100,0 Mio.", actual.Metrics["silver-hour"].Value);
        Assert.Equal("429", actual.Metrics["total-drops"].Value);
        Assert.Equal("Black Crystal Fragment", actual.Drops[0].CanonicalName);
        Assert.Equal("Schwarzkristallfragment", actual.Drops[0].Name);
        Assert.Equal("assets/icons/black-crystal-fragment.png", actual.Drops[0].IconPath);
        Assert.Equal(3, actual.Drops.Count);
        Assert.Single(actual.RareDrops);
        Assert.Equal("BON Wandering Origin Crystal", actual.RareDrops[0].CanonicalName);
        Assert.Equal(4, state.Loot.Totals.Count);
        Assert.Equal(1735, state.Loot.Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public void RareSelectionDoesNotIncludeEveryNonTrashMarketMaterial()
    {
        var actual = new OverlayMetrics().Update(Session() with
        {
            Loot = new(new Dictionary<string, long>
            {
                ["Black Stone"] = 4, ["Caphras Stone"] = 2, ["Ancient Spirit Dust"] = 5,
                ["BON Origin Shard"] = 1, ["Broken Vestige of Ebonmere"] = 1, ["Pure Black Stone"] = 1,
            }, 14, 8),
        }, new());

        Assert.Equal(6, actual.Drops.Count);
        Assert.Equal(new[] { "Broken Vestige of Ebonmere", "Pure Black Stone" }, actual.RareDrops.Select(item => item.CanonicalName));
    }

    [Fact]
    public void UnknownPricesNeverBecomeZeroAndPartialPricesKeepTheirMarker()
    {
        var metrics = new OverlayMetrics();
        var state = Session() with { Silver = new(0, 0, 0, ["Black Crystal Fragment"], [], false) };
        var unknown = metrics.Update(state, new());
        Assert.Equal("—", unknown.Metrics["silver"].Value);
        Assert.Equal("—", unknown.Metrics["silver-hour"].Value);
        Assert.Empty(unknown.SilverHistory);

        var partial = metrics.Update(state with { Silver = new(12_000_000, 12_000_000, 1, ["Black Stone"], [], false) }, new());
        Assert.Equal("12,0 Mio. *", partial.Metrics["silver"].Value);
        Assert.EndsWith(" *", partial.Metrics["silver-hour"].Value);
        Assert.Equal("Teilbetrag · Preise fehlen", partial.Metrics["chart"].Detail);

        var stale = metrics.Update(state with { Silver = new(12_000_000, 12_000_000, 1, [], [], true) }, new());
        Assert.Equal("Gespeicherte Marktpreise", stale.Metrics["silver"].Detail);
    }

    [Fact]
    public void EmptyOrZeroDurationSessionShowsNoInventedHourlyRate()
    {
        var snapshot = new OverlayMetrics().Update(new(), new());
        Assert.Equal("0", snapshot.Metrics["silver"].Value);
        Assert.Equal("—", snapshot.Metrics["silver-hour"].Value);
        Assert.Equal("—", snapshot.Metrics["trash-hour"].Value);
        Assert.Empty(snapshot.SilverHistory);
        Assert.Empty(snapshot.Drops);
    }

    [Fact]
    public void HistorySamplesActiveTimeAndReplacesEditsInTheCurrentBucket()
    {
        var metrics = new OverlayMetrics();
        var state = Session() with { Elapsed = TimeSpan.FromSeconds(9), Silver = new(100, 100, 1, [], [], false) };
        Assert.Empty(metrics.Update(state, new()).SilverHistory);

        var first = metrics.Update(state with { Elapsed = TimeSpan.FromSeconds(10) }, new());
        Assert.Equal(36_000m, Assert.Single(first.SilverHistory), precision: 12);
        var correction = state with { Elapsed = TimeSpan.FromSeconds(10), Silver = new(200, 200, 1, [], [], false) };
        Assert.Equal(72_000m, Assert.Single(metrics.Update(correction, new()).SilverHistory), precision: 12);
        Assert.Equal(36_000m, Assert.Single(first.SilverHistory), precision: 12);

        var paused = metrics.Update(correction with { IsRunning = false }, new());
        Assert.Single(paused.SilverHistory);
        var later = metrics.Update(correction with { Elapsed = TimeSpan.FromSeconds(20) }, new());
        Assert.Collection(later.SilverHistory,
            value => Assert.Equal(72_000m, value, precision: 12),
            value => Assert.Equal(36_000m, value, precision: 12));
    }

    [Fact]
    public void HistoryResetsForNewSessionsDemoAndElapsedTimeRewindAndRemainsBounded()
    {
        var metrics = new OverlayMetrics();
        var state = Session();
        OverlaySnapshot snapshot = new();
        for (var step = 1; step <= 200; step++)
            snapshot = metrics.Update(state with { Elapsed = TimeSpan.FromSeconds(step * 10) }, new());
        Assert.Equal(120, snapshot.SilverHistory.Count);

        var newSession = state with { SessionId = Guid.NewGuid(), Elapsed = TimeSpan.FromSeconds(40) };
        Assert.Single(metrics.Update(newSession, new()).SilverHistory);
        Assert.Single(metrics.Update(newSession with { IsDemo = true, Elapsed = TimeSpan.FromSeconds(50) }, new()).SilverHistory);
        Assert.Single(metrics.Update(newSession with { IsDemo = true, Elapsed = TimeSpan.FromSeconds(20) }, new()).SilverHistory);
        Assert.Empty(metrics.Update(newSession with { HasSession = false, IsRunning = false }, new()).SilverHistory);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("restart")]
    [InlineData("calibration")]
    [InlineData("busy")]
    [InlineData("submitted")]
    [InlineData("unavailable")]
    public void ControlsCannotBypassTrackerStartRequirements(string condition)
    {
        var state = Session() with { IsRunning = false };
        state = condition switch
        {
            "missing" => state with { MissingOcrLanguageTag = "en-US" },
            "restart" => state with { OcrRestartRequired = true },
            "calibration" => state with { TrackingBlockedReason = "Droplog fehlt" },
            "busy" => state with { IsBusy = true },
            "submitted" => state with { IsSubmitted = true },
            _ => state with { AnalyzerAvailable = false },
        };
        Assert.False(new OverlayMetrics().Update(state, new()).CanToggleTracking);
    }

    [Fact]
    public void RunningSessionCanAlwaysPauseWhenCaptureHasNoStartBlock()
    {
        var metrics = new OverlayMetrics();
        var active = metrics.Update(Session(), new());
        Assert.True(active.CanToggleTracking);
        Assert.Equal("Pausieren", active.TrackingButtonLabel);
        Assert.Equal("Fortsetzen", metrics.Update(Session() with { IsRunning = false }, new()).TrackingButtonLabel);
        Assert.Equal("Tracking starten", metrics.Update(new() { AnalyzerAvailable = true }, new()).TrackingButtonLabel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RunningControlsUseTheSamePauseCapabilityAsTheLiveDashboard(bool canPause)
    {
        var state = Session() with { IsBusy = true, CanPause = canPause };
        Assert.Equal(canPause, new OverlayMetrics().Update(state, new()).CanToggleTracking);
    }

    [Fact]
    public void ExtremeRateDoesNotOverflowOrInventAValue()
    {
        var state = Session() with { Elapsed = TimeSpan.FromTicks(1), Silver = new(decimal.MaxValue, decimal.MaxValue, 1, [], [], false) };
        var snapshot = new OverlayMetrics().Update(state, new());
        Assert.Equal("—", snapshot.Metrics["silver-hour"].Value);
        Assert.Empty(snapshot.SilverHistory);
    }

    [Fact]
    public async Task PreviewDoesNotInvokeTrackingAndRendersListsAsLocalizedContent()
    {
        var controls = await RenderAsync(OverlayCatalog.CreateWidget("controls"), OverlaySnapshot.Demo);
        Assert.Contains("disabled", controls);
        Assert.Contains("Pausieren", controls);
        var items = await RenderAsync(OverlayCatalog.CreateWidget("drops") with { ItemView = "list", ItemLimit = 2 }, OverlaySnapshot.Demo);
        Assert.Contains("Schwarzkristallfragment", items);
        Assert.Contains("assets/icons/black-crystal-fragment.png", items);
        Assert.Contains("+ 3 weitere", items);
        var hidden = await RenderAsync(OverlayCatalog.CreateWidget("duration") with { ShowLabel = false }, OverlaySnapshot.Demo);
        Assert.DoesNotContain("overlay-widget-label", hidden);
        Assert.DoesNotContain("overlay-widget-detail", hidden);
        Assert.Contains("<svg", hidden);
        var noIcon = await RenderAsync(OverlayCatalog.CreateWidget("duration") with { ShowLabel = false, ShowIcon = false }, OverlaySnapshot.Demo);
        Assert.DoesNotContain("<svg", noIcon);
    }

    [Fact]
    public async Task PreviewChartAndEmptyLootHaveExplicitAccessibleContent()
    {
        var chart = await RenderAsync(OverlayCatalog.CreateWidget("chart"), OverlaySnapshot.Demo);
        Assert.Contains("role=\"img\"", chart);
        Assert.Contains("overlay-chart-line", chart);
        Assert.DoesNotContain("NaN", chart);
        var empty = await RenderAsync(OverlayCatalog.CreateWidget("rare-drops"), new());
        Assert.Contains("Noch keine seltenen Drops", empty);
    }

    private static TrackerState Session() => new()
    {
        SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, CanPause = true, AnalyzerAvailable = true,
        SpotId = LootSpotCatalog.HermesiaId, Elapsed = TimeSpan.FromMinutes(10),
        Loot = new(new Dictionary<string, long> { ["Black Crystal Fragment"] = 100 }, 100, 25),
        Silver = new(1_000_000, 1_000_000, 1, [], [], false),
    };

    private static async Task<string> RenderAsync(OverlayWidget widget, OverlaySnapshot snapshot)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OverlayWidgetPreview>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OverlayWidgetPreview.Widget)] = widget,
                [nameof(OverlayWidgetPreview.Snapshot)] = snapshot,
            }));
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
    }
}
