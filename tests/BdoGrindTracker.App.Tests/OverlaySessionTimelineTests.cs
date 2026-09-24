using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlaySessionTimelineTests
{
    private const decimal Valuable = 1_000_000_000m;
    private const double PlotWidth = 340;

    [Fact]
    public void SilverUsesTheSessionTimelinesIntervalsAndFlattening()
    {
        var snapshot = Snapshot(TimeSpan.FromSeconds(65), (3, 5_000_000), (12, 1_000_000), (19.9, 2_000_000), (20, Valuable));

        var timeline = Create(Widget(), snapshot);

        var expected = SessionTimelineChart.Series("silver", "", "", snapshot.SilverDrops, drop => drop.Elapsed, drop => drop.Silver,
            TimeSpan.Zero, TimeSpan.FromSeconds(65), isSilver: true, flatten: true);
        var silver = Assert.IsType<SessionTimelineSeries>(timeline.Silver);
        Assert.Equal(expected.Bars, silver.Bars);
        Assert.Equal(Valuable, silver.Peak);
        Assert.True(silver.IsFlattened);
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromSeconds(65), "ganze Session"), (timeline.From, timeline.To, timeline.Caption));
        // Flattened, a thousandth of the peak still clearly leaves the baseline.
        Assert.InRange(silver.Bars.Single(bar => bar.Value == 5_000_000).Filled, .1, .2);
    }

    [Fact]
    public void ARecentRangeShowsOnlyItsOwnMinutes()
    {
        var snapshot = Snapshot(TimeSpan.FromMinutes(30), (5 * 60, Valuable * 3), (25 * 60, Valuable)) with
        {
            DropMarkers = [Marker(5 * 60, "Old"), Marker(25 * 60, "Ring")],
        };

        var timeline = Create(Widget() with { ChartRangeMinutes = 10 }, snapshot);

        Assert.Equal((TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30), "letzte 10 min"), (timeline.From, timeline.To, timeline.Caption));
        Assert.Equal(Valuable, timeline.Silver!.Peak);
        Assert.Equal("Ring", Assert.Single(Assert.Single(timeline.Markers).Items).Item.Name);
        Assert.Equal(.5, timeline.Markers[0].X, 6);
        Assert.Equal(new[] { "0:20", "0:25", "0:30" }, timeline.Ticks.Select(tick => tick.Label));
        // A range longer than the session shows the complete session.
        Assert.Equal(TimeSpan.Zero, Create(Widget() with { ChartRangeMinutes = 60 }, snapshot).From);
    }

    [Fact]
    public void SimplifiedRotationsJoinTheirMechanicsAndSetTheAfkPhaseApart()
    {
        var snapshot = WithRotations(Snapshot(TimeSpan.FromSeconds(1000)),
            Rotation(100, 600, "complete", ("drakania", 100), ("dragon", 400), ("afk", 500)),
            Rotation(750, 200, "active"));

        var timeline = Create(Widget(), snapshot);

        Assert.True(timeline.IsSimplified);
        Assert.Equal(2, timeline.Rotations.Count);
        var first = timeline.Rotations[0];
        Assert.Equal((.1, .7, "fastest"), (first.Left, first.Right, first.Status));
        Assert.Equal(new[] { (.1, .6, false), (.6, .7, true) }, first.Phases.Select(part => (Round(part.Left), Round(part.Right), part.IsAfk)));
        Assert.All(first.Phases, part => Assert.Equal("", part.Color));
        // Without recorded mechanics the running rotation is one stretch.
        var running = timeline.Rotations[1];
        Assert.Equal("active", running.Status);
        Assert.Equal((.75, .95, false), (Round(Assert.Single(running.Phases).Left), Round(running.Phases[0].Right), running.Phases[0].IsAfk));
        Assert.Equal(OverlaySessionTimeline.SimpleBandHeight + OverlaySessionTimeline.BandGap, timeline.BandHeight);
    }

    [Fact]
    public void ThePhaseViewShowsEveryMechanicInItsOwnColour()
    {
        var snapshot = WithRotations(Snapshot(TimeSpan.FromSeconds(1000)),
            Rotation(100, 600, "complete", ("drakania", 100), ("dragon", 400), ("afk", 500)));

        var timeline = Create(Widget() with { TimelineRotationView = OverlayTimelineLayers.PhasesView }, snapshot);

        var phases = Assert.Single(timeline.Rotations).Phases;
        Assert.Equal(new[] { "Startup · 5 Porter", "Drakania-Kampf", "Drachenkampf", "AFK-Phase" }, phases.Select(phase => phase.Name));
        Assert.Equal(new[] { false, false, false, true }, phases.Select(phase => phase.IsAfk));
        Assert.All(phases, phase => Assert.StartsWith("#", phase.Color));
        Assert.Equal(OverlaySessionTimeline.PhaseBandHeight + OverlaySessionTimeline.BandGap, timeline.BandHeight);
    }

    [Fact]
    public void EverySpotsAfkPhaseIsRecognized()
    {
        Assert.True(RotationPhases.IsAfk(new("afk", "afk", "AFK-Phase", 0, 1, "#000", "#000")));
        Assert.True(RotationPhases.IsAfk(new("cycle-2-afk", "cycle-2", "Zyklus 2 · AFK-Phase", 0, 1, "#000", "#000")));
        Assert.True(RotationPhases.IsAfk(new("afk:1", "afk", "AFK", 0, 1, "#000", "#000")));
        // Event Horizon's debris break belongs to its wormhole.
        Assert.False(RotationPhases.IsAfk(new("wormhole-1-debris", "wormhole-1", "Wurmloch 1 · Trümmer-AFK", 0, 1, "#000", "#000")));
    }

    [Fact]
    public void LayersThatAreOffDrawNothingAndKeepNoRotationBand()
    {
        var snapshot = WithRotations(Snapshot(TimeSpan.FromSeconds(1000), (20, Valuable)),
            Rotation(100, 600, "complete", ("afk", 500)) with { SpecialEventSeconds = [50] }) with
        {
            DropMarkers = [Marker(20, "Ring")],
            TrashDrops = [new(TimeSpan.FromSeconds(30), "Trash", 20)],
        };

        var all = Create(Widget() with { TimelineLayers = [.. SessionTimelineLayers.All.Select(layer => layer.Id)] }, snapshot);
        Assert.NotNull(all.Silver);
        Assert.Equal(20, all.Trash!.Peak);
        Assert.Equal((1, 1, 1), (all.Rotations.Count, all.Markers.Count, all.SpecialEvents.Count));
        Assert.Equal(.15, all.SpecialEvents[0], 6);

        var none = Create(Widget() with { TimelineLayers = [] }, snapshot);
        Assert.False(none.HasData);
        Assert.Equal(0, none.BandHeight);
        // Trash stays off unless it is chosen: its bars would crowd a module of overlay size.
        Assert.Null(Create(Widget(), snapshot).Trash);
    }

    [Fact]
    public void NearbyDropsShareARowWhereOnlyTheSameItemIsCounted()
    {
        // 600 seconds on 340 pixels: a 20 pixel icon covers about 35 seconds.
        var snapshot = Snapshot(TimeSpan.FromSeconds(600), (100, Valuable), (400, 1_000_000)) with
        {
            DropMarkers = [Marker(100, "Ring"), Marker(110, "Ring"), Marker(120, "Belt"), Marker(150, "Ring"), Marker(400, "Earring")],
        };

        var timeline = Create(Widget(), snapshot);

        Assert.Equal(2, timeline.Markers.Count);
        var row = timeline.Markers[0];
        // Different items stand side by side; the ring at 150 seconds touches the row only because the belt widened it.
        Assert.Equal(new[] { ("Ring", 3), ("Belt", 1) }, row.Items.Select(item => (item.Item.Name, item.Drops.Count)));
        Assert.Equal(100d / 600, row.X, 6);
        Assert.Equal(1, row.Height, 6);
        Assert.Equal(("Earring", 1), (Assert.Single(timeline.Markers[1].Items).Item.Name, timeline.Markers[1].Items[0].Drops.Count));
        Assert.Equal(SessionTimelineChart.Top(TimeSpan.FromSeconds(400), [timeline.Silver!], timeline.From, timeline.To),
            timeline.Markers[1].Height, 6);
    }

    [Fact]
    public void ARowSitsAboveTheTallestLayerOfEveryDropItHolds()
    {
        var snapshot = Snapshot(TimeSpan.FromSeconds(600), (100, 1_000_000), (115, Valuable)) with
        {
            DropMarkers = [Marker(100, "Ring"), Marker(115, "Belt")],
        };

        Assert.Equal(1, Assert.Single(Create(Widget(), snapshot).Markers).Height, 6);
    }

    [Fact]
    public void OnAWideModuleNearbyDropsKeepTheirOwnMarks()
    {
        var snapshot = Snapshot(TimeSpan.FromSeconds(600), (100, Valuable)) with
        {
            DropMarkers = [Marker(100, "Ring"), Marker(120, "Belt")],
        };

        Assert.Equal(2, Create(Widget(), snapshot, plotWidth: 2000).Markers.Count);
    }

    [Fact]
    public void SavedWidgetsMoveToTheTimelineAndAreNormalized()
    {
        // A silver history widget saved before the session timeline replaced it.
        var saved = JsonSerializer.Deserialize<OverlaySettings>(
            """{"widgets":[{"id":"8f6e0c8b5b7beb8eda6fba31aedf0f97","kind":"chart","chartMode":"sections","chartRangeMinutes":20,"chartSectionSeconds":30,"chartPeakMode":"clip"}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var legacy = Assert.Single(OverlayLayout.Normalize(saved).Widgets);
        Assert.Equal((20, OverlayTimelineLayers.SimplifiedView), (legacy.ChartRangeMinutes, legacy.TimelineRotationView));
        Assert.Same(OverlayTimelineLayers.Default, legacy.TimelineLayers);

        var widgets = OverlayLayout.Normalize(new OverlaySettings
        {
            Widgets = [Widget() with { ChartRangeMinutes = 45, TimelineRotationView = "bars", TimelineLayers = ["trash", "unknown", "rotations", "trash"] },
                Widget() with { TimelineRotationView = OverlayTimelineLayers.PhasesView, TimelineLayers = null! }],
        }).Widgets;
        Assert.Equal((0, OverlayTimelineLayers.SimplifiedView), (widgets[0].ChartRangeMinutes, widgets[0].TimelineRotationView));
        Assert.Equal(new[] { "rotations", "trash" }, widgets[0].TimelineLayers);
        Assert.Equal(OverlayTimelineLayers.PhasesView, widgets[1].TimelineRotationView);
        Assert.Same(OverlayTimelineLayers.Default, widgets[1].TimelineLayers);
        // An unchanged selection keeps its instance, so the native overlay sees an equal layout.
        Assert.Same(widgets[0].TimelineLayers, OverlayTimelineLayers.Normalize(widgets[0].TimelineLayers));

        var created = OverlayCatalog.CreateWidget("chart");
        Assert.Equal((360d, 144d), (created.Width, created.Height));
        Assert.Equal("Session-Timeline", OverlayCatalog.Find("chart")!.Label);
    }

    [Fact]
    public void BelowItsReferenceSizeTheTimelineIsScaledAsAWhole()
    {
        var content = OverlayContentLayout.Create(Widget() with { Width = 320, Height = 144 }, new OverlaySnapshot());

        Assert.Equal(320d / 360, content.Scale, 6);
        Assert.Equal((360d, 162d), (content.LayoutWidget.Width, content.LayoutWidget.Height));
        Assert.Equal(1, OverlayContentLayout.Create(Widget() with { Width = 400, Height = 200 }, new OverlaySnapshot()).Scale);
    }

    [Fact]
    public async Task AResizedTimelineScalesItsReferenceLayoutAsAWhole()
    {
        var resized = OverlayLayout.ResizeWidget(Widget() with { FontScale = 1.35 }, 720, 288);

        var html = await RenderAsync(resized, OverlaySnapshot.Demo);

        Assert.Contains("width:360px;height:144px;transform:scale(2)", html);
        Assert.Contains("--widget-font-scale:1.35", html);
        Assert.Contains("<svg", html);
    }

    [Fact]
    public void TheSnapshotCarriesTheSpotsTrashAndTheCaptureTime()
    {
        var trash = Presentation.Profile(LootSpotCatalog.HermesiaId)!.TrashItemName;
        var observed = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var state = new TrackerState
        {
            SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, SpotId = LootSpotCatalog.HermesiaId,
            Elapsed = TimeSpan.FromMinutes(2), ObservedAt = observed,
            DropHistory = [new(TimeSpan.FromSeconds(12), trash, 10), new(TimeSpan.FromSeconds(40), "Ring", 1),
                new(TimeSpan.FromSeconds(50), trash, 4)],
        };
        var metrics = new OverlayMetrics();

        var preferences = new TrackerPreferences { UiLanguage = "de" };
        var snapshot = metrics.Update(state, preferences);

        Assert.Equal(new[] { 10L, 4 }, snapshot.TrashDrops.Select(drop => drop.Quantity));
        Assert.Equal(observed, snapshot.ObservedAt);
        Assert.Same(snapshot.TrashDrops, metrics.Update(state with { Elapsed = TimeSpan.FromMinutes(3) }, preferences).TrashDrops);
        Assert.Empty(new OverlayMetrics().Update(state with { SpotId = null }, new()).TrashDrops);
        Assert.Equal("Session-Timeline", snapshot.Metrics["chart"].Label);
    }

    [Fact]
    public void TheExampleSessionShowsItsRotationsOnTheTimeline()
    {
        var timeline = Create(Widget(), OverlaySnapshot.Demo);

        Assert.Equal(6, timeline.Rotations.Count);
        Assert.All(timeline.Rotations, rotation => Assert.Contains(rotation.Phases, part => part.IsAfk));
        Assert.NotNull(timeline.Silver);
        Assert.NotEmpty(timeline.Markers);
    }

    [Fact]
    public async Task TheEditorPreviewDrawsEveryLayerWithInvariantCoordinates()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var widget = Widget() with { X = 0, Y = 0, TimelineLayers = [.. SessionTimelineLayers.All.Select(layer => layer.Id)] };
            var snapshot = OverlaySnapshot.Demo with { TrashDrops = [new(TimeSpan.FromSeconds(900), "Trash", 25)] };

            var html = await RenderAsync(widget, snapshot);

            Assert.Contains("Session-Timeline", html);
            Assert.Contains("ganze Session", html);
            Assert.Contains("overlay-timeline-silver", html);
            Assert.Contains("overlay-timeline-trash", html);
            Assert.Equal(6, Regex.Count(html, "overlay-timeline-rotation is-"));
            Assert.Equal(6, Regex.Count(html, "overlay-timeline-phase is-afk"));
            Assert.Contains("overlay-timeline-drop", html);
            Assert.DoesNotContain("NaN", html);
            // A decimal comma would break every SVG coordinate and CSS length.
            Assert.DoesNotMatch(@"points=""[^""]*\d,\d+,", html);
            Assert.DoesNotMatch(@"(left|width|top):[^;""]*\d,\d", html);

            var phases = await RenderAsync(widget with { TimelineRotationView = OverlayTimelineLayers.PhasesView }, snapshot);
            Assert.Contains("is-phases", phases);
            Assert.Contains("background:#", phases);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task WithoutDataTheModuleExplainsItselfAndNamesMissingPrices()
    {
        var html = await RenderAsync(Widget(), new OverlaySnapshot { UiLanguage = "de" });
        Assert.Contains("Verlauf entsteht während des Grindens", html);

        var warning = Snapshot(TimeSpan.FromSeconds(65), (3, 1)) with
        {
            Metrics = new Dictionary<string, OverlayMetric> { ["chart"] = new("Session-Timeline", "0:01:05", "Teilbetrag · Preise fehlen") },
        };
        Assert.Contains("Teilbetrag · Preise fehlen", await RenderAsync(Widget(), warning));
    }

    [Fact]
    public void TheNativeOverlayDrawsTheTimelineInTheThemesColours()
    {
        var widget = Widget() with { X = 0, Y = 0, ShowLabel = false };
        var snapshot = OverlaySnapshot.Demo;
        var settings = new OverlaySettings { Width = 360, Height = 144, Widgets = [widget] };

        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new Size(360, 144), settings, snapshot, out _);
        // Grindcrest's --timeline-silver curve.
        Assert.True(Count(image, Color.FromArgb(0xd8, 0xbd, 0x75)) > 40);
        // The phase view's rails in --timeline-complete and, for the fastest rotation, --timeline-fastest.
        using var phases = renderer.Render(new Size(360, 144), settings with
        {
            Widgets = [widget with { TimelineRotationView = OverlayTimelineLayers.PhasesView }],
        }, snapshot, out _);
        Assert.True(Count(phases, Color.FromArgb(0x5f, 0x8f, 0x6d)) > 60);
        Assert.True(Count(phases, Color.FromArgb(0x9f, 0xe6, 0xa8)) > 20);

        using var obsidian = renderer.Render(new Size(360, 144), settings, snapshot with { ThemeId = "obsidian" }, out _);
        Assert.True(Count(obsidian, Color.FromArgb(0x9f, 0xc5, 0xff)) > 40);
        Assert.True(Count(obsidian, Color.FromArgb(0xd8, 0xbd, 0x75)) < 5);
    }

    [Theory]
    [InlineData("Belt", 2, 45)]
    [InlineData("Ring", 1, 20)]
    public async Task BothRenderersSetDifferentItemsSideBySideAndCountTheSameItemOnce(string second, int icons, int width)
    {
        var widget = Widget() with { X = 0, Y = 0, ShowLabel = false, TimelineLayers = ["rare"] };
        var snapshot = Snapshot(TimeSpan.FromSeconds(600)) with
        {
            DropMarkers = [Marker(100, "Ring"), Marker(110, second)],
        };

        var html = await RenderAsync(widget, snapshot);
        Assert.Equal(1, Regex.Count(html, "class=\"overlay-timeline-drops\""));
        Assert.Equal(icons, Regex.Count(html, "class=\"overlay-timeline-drop\""));
        Assert.Equal(icons == 1 ? 1 : 0, Regex.Count(html, "overlay-timeline-count\">2<"));

        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new Size(360, 144), new OverlaySettings
        {
            Width = 360, Height = 144, Widgets = [widget], BackgroundOpacity = 0, ShowBorder = false, Interaction = "passthrough",
        }, snapshot, out _);
        // Grindcrest paints each icon's slot in --timeline-raised.
        var slot = Color.FromArgb(255, 37, 45, 51).ToArgb();
        var columns = Enumerable.Range(0, image.Width)
            .Where(x => Enumerable.Range(0, image.Height).Any(y => image.GetPixel(x, y).ToArgb() == slot)).ToArray();
        Assert.InRange(columns[^1] - columns[0] + 1, width - 3, width + 1);
    }

    [Fact]
    public void TheNativeOverlayRedrawsTheTimelineWhenItsDataChanges()
    {
        var snapshot = OverlaySnapshot.Demo;
        var settings = new OverlaySettings { Widgets = [Widget()] };
        var size = new Size(360, 144);
        var state = new NativeOverlayRenderState();
        state.Remember(settings, snapshot, size);

        Assert.True(state.Matches(settings, snapshot with { Consumables = ConsumablesPresentation.Create(null, "de") }, size));
        Assert.False(state.Matches(settings, snapshot with { SilverDrops = [.. snapshot.SilverDrops, new(TimeSpan.FromSeconds(939), 5)] }, size));
        Assert.False(state.Matches(settings, snapshot with { TrashDrops = [new(TimeSpan.FromSeconds(939), "Trash", 5)] }, size));
        Assert.False(state.Matches(settings, snapshot with { SessionElapsed = snapshot.SessionElapsed + TimeSpan.FromSeconds(1) }, size));
        Assert.False(state.Matches(settings, snapshot with
        {
            Rotation = snapshot.Rotation with { SessionRotations = [.. snapshot.Rotation.SessionRotations.SkipLast(1)] },
        }, size));
    }

    private static double Round(double value) => Math.Round(value, 6);

    private static int Count(Bitmap image, Color color, int tolerance = 24) =>
        Enumerable.Range(0, image.Width).SelectMany(x => Enumerable.Range(0, image.Height).Select(y => image.GetPixel(x, y)))
            .Count(pixel => pixel.A > 200 && Math.Abs(pixel.R - color.R) + Math.Abs(pixel.G - color.G) + Math.Abs(pixel.B - color.B) <= tolerance);

    private static OverlaySessionTimeline Create(OverlayWidget widget, OverlaySnapshot snapshot, double plotWidth = PlotWidth) =>
        OverlaySessionTimeline.Create(widget, snapshot, plotWidth);

    private static OverlayWidget Widget() => OverlayCatalog.CreateWidget("chart");

    private static OverlayDropMarker Marker(double seconds, string name) =>
        new(TimeSpan.FromSeconds(seconds), new(name, name, "1", IsRare: true));

    private static SessionRotationTiming Rotation(double start, double duration, string outcome, params (string Kind, double Seconds)[] events) =>
        new(duration, StartedAfter: TimeSpan.FromSeconds(start), Outcome: outcome,
            Events: events.Length == 0 ? null : [new("start", "Start", 0), .. events.Select(e => new RotationEvent(e.Kind, e.Kind, e.Seconds)),
                new("end", "Ende", duration)]);

    private static OverlaySnapshot WithRotations(OverlaySnapshot snapshot, params SessionRotationTiming[] rotations) => snapshot with
    {
        Rotation = new() { SpotId = LootSpotCatalog.HermesiaId, HasProfile = true, SessionRotations = rotations },
    };

    private static OverlaySnapshot Snapshot(TimeSpan elapsed, params (double Seconds, decimal Silver)[] drops) => new()
    {
        UiLanguage = "de",
        SessionElapsed = elapsed,
        SilverDrops = drops.Select(drop => new OverlaySilverDrop(TimeSpan.FromSeconds(drop.Seconds), drop.Silver)).ToArray(),
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
