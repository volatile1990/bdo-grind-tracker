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

public sealed class GrindRatingPresentationTests
{
    [Fact]
    public async Task EarlyRatingDisclosesItsTimeAndReferenceWithoutCreatingALootScrollWarning()
    {
        var state = State() with { Elapsed = TimeSpan.FromMinutes(3), Loot = Loot(815) };
        var presentation = new LiveSessionPresentation(state).GrindRating;
        var metric = new OverlayMetrics().Update(state, new()).Metrics["grind-rating"];
        var markup = await Render(state, OverlayCatalog.CreateWidget("grind-rating"));

        Assert.Equal("High Tier", presentation.Label);
        Assert.Equal(16300m, presentation.Result.TrashPerHour);
        Assert.Equal("Vorläufig", metric.Detail);
        Assert.Equal(presentation.Description, metric.Tooltip);
        Assert.Contains("weniger als 5 Minuten", markup);
        Assert.Contains("Stand 10.09.2026", markup);
        Assert.Contains("Quelle: https://garmoth.com/grind-tracker/best-grind-spots/215", markup);
        Assert.Contains("metric-tone-positive", markup);
        Assert.DoesNotContain("is-warning", markup);
        Assert.DoesNotContain("Loot-Scroll ist nicht aktiv.", markup);
        Assert.DoesNotContain("<button", markup);
    }

    [Theory]
    [InlineData("agris-active", true)]
    [InlineData("agris-earlier", true)]
    [InlineData("scroll-one", true)]
    [InlineData("scroll-inactive", true)]
    [InlineData("scroll-two", false)]
    [InlineData("unknown", false)]
    public void KnownDifferentLootBuffsDiscloseComparisonLimitsWithoutChangingTheRate(string scenario, bool differing)
    {
        var state = State();
        state = scenario switch
        {
            "agris-active" => state with { Agris = new(AgrisStatus.Active) },
            "agris-earlier" => state with { AgrisActiveDuration = TimeSpan.FromMinutes(1) },
            "scroll-one" => state with { LootScroll = new(LootScrollStatus.Active, 1) },
            "scroll-inactive" => state with { LootScroll = new(LootScrollStatus.Inactive) },
            "scroll-two" => state with { LootScroll = new(LootScrollStatus.Active, 2) },
            _ => state,
        };
        var presentation = new LiveSessionPresentation(state).GrindRating;

        Assert.Equal("High Tier", presentation.Label);
        Assert.Equal(16300m, presentation.Result.TrashPerHour);
        Assert.Equal(differing, presentation.Description.Contains("Abweichende Loot-Buffs", StringComparison.Ordinal));
        Assert.Equal(differing ? "Abweichende Loot-Buffs" : null, presentation.Detail);
        Assert.False(new OverlayMetrics().Update(state, new()).Metrics["grind-rating"].IsWarning);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    [InlineData("source")]
    [InlineData("time")]
    public async Task UnavailableReferenceNeverInventsATier(string scenario)
    {
        var state = State();
        state = scenario switch
        {
            "missing" => state with { GrindBenchmark = null },
            "mismatch" => state with { GrindBenchmark = state.GrindBenchmark! with { SpotId = LootSpotCatalog.HermesiaId } },
            "source" => state with { GrindBenchmark = state.GrindBenchmark! with { SourceUrl = "" } },
            _ => state with { Elapsed = TimeSpan.Zero },
        };
        var metric = new OverlayMetrics().Update(state, new()).Metrics["grind-rating"];
        var markup = await Render(state, OverlayCatalog.CreateWidget("grind-rating"));

        Assert.Equal("—", new LiveSessionPresentation(state).GrindRating.Label);
        Assert.Equal("—", metric.Value);
        Assert.Equal("Keine Bewertung verfügbar.", metric.Tooltip);
        Assert.Null(metric.Detail);
        Assert.DoesNotContain("Tier", markup);
        Assert.DoesNotContain("Vorläufig", markup);
    }

    [Fact]
    public void AverageOnlySourceNeverInventsHigherThresholds()
    {
        var state = State() with { GrindBenchmark = State().GrindBenchmark! with { HighTrashPerHour = null, TopTrashPerHour = null } };
        var presentation = new LiveSessionPresentation(state).GrindRating;

        Assert.Equal("Average Tier", presentation.Label);
        Assert.Contains("Average ab 13.946 Trash / h", presentation.Description);
        Assert.DoesNotContain("High ab", presentation.Description);
        Assert.DoesNotContain("Top ab", presentation.Description);
    }

    [Fact]
    public async Task ModuleHonorsLabelIconAndFontSettingsWithoutLosingSourceTooltip()
    {
        var widget = OverlayCatalog.CreateWidget("grind-rating") with { ShowLabel = false, ShowIcon = false, FontScale = 1.4 };
        var markup = await Render(State(), widget);

        Assert.Contains("--widget-font-scale:1.4", markup);
        Assert.Contains("High Tier", markup);
        Assert.Contains("Quelle:", markup);
        Assert.DoesNotContain("overlay-widget-label", markup);
        Assert.DoesNotContain("<svg", markup);
    }

    [Fact]
    public void CustomTemplateRoundTripKeepsTheNewModuleAndItsDisplaySettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "grindcrest-rating-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            var widget = OverlayCatalog.CreateWidget("grind-rating", 24, 32) with
                { Width = 240, Height = 88, ShowLabel = false, ShowIcon = false, FontScale = 1.4 };
            var store = new OverlayTemplateStore(directory);
            store.Save([OverlayTemplate.Create("Bewertung", new() { Widgets = [widget] })]);
            var loaded = Assert.Single(new OverlayTemplateStore(directory).Load());
            var restored = Assert.Single(loaded.ApplyTo(new() { Enabled = true, Visibility = "always" }).Widgets);

            Assert.Equal(widget.Kind, restored.Kind);
            Assert.Equal(widget.X, restored.X);
            Assert.Equal(widget.Y, restored.Y);
            Assert.Equal(widget.Width, restored.Width);
            Assert.Equal(widget.Height, restored.Height);
            Assert.Equal(widget.FontScale, restored.FontScale);
            Assert.False(restored.ShowLabel);
            Assert.False(restored.ShowIcon);
            Assert.DoesNotContain(OverlayCatalog.CompactWidgets(), value => value.Kind == "grind-rating");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static LootSessionSnapshot Loot(long quantity) =>
        new(new Dictionary<string, long> { ["Elion Follower's Helmet"] = quantity }, quantity, 100);

    private static TrackerState State() => new()
    {
        SpotId = LootSpotCatalog.MagaiaId, HasSession = true, IsRunning = true, CanPause = true,
        Elapsed = TimeSpan.FromHours(1), Loot = Loot(16300),
        GrindBenchmark = new(LootSpotCatalog.MagaiaId, 13946, 16300, 18500,
            new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), "https://garmoth.com/grind-tracker/best-grind-spots/215", "Loot-Scroll Lv.2 · ohne Agris"),
    };

    private static async Task<string> Render(TrackerState state, OverlayWidget widget)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OverlayWidgetPreview>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OverlayWidgetPreview.Widget)] = widget,
                [nameof(OverlayWidgetPreview.Snapshot)] = new OverlayMetrics().Update(state, new()),
            }));
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
    }
}
