using System.Drawing;
using System.Net;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlaySectionsAndRotationModulesTests
{
    private const decimal Valuable = 1_000_000_000m;

    [Fact]
    public void SectionCurveSumsTheSilverOfEachTenSecondSection()
    {
        var snapshot = Snapshot(TimeSpan.FromSeconds(65),
            (3, 5_000_000), (12, 1_000_000), (19.9, 2_000_000), (20, Valuable), (70, 9_000_000)) with
        {
            DropMarkers = [new(TimeSpan.FromSeconds(20), new("Ring", "Ring", "1"))],
        };

        var chart = OverlayChartSections.Create(Sections(), snapshot);

        Assert.Equal(new[] { 5d, 15, 25, 35, 45, 55, 62.5 }, chart.Points.Select(point => point.Elapsed.TotalSeconds));
        Assert.Equal(new[] { 5_000_000m, 3_000_000, Valuable, 0, 0, 0, 0 }, chart.Points.Select(point => point.Silver));
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromSeconds(65), Valuable), (chart.From, chart.To, chart.Maximum));
        Assert.Equal(chart.Y(Valuable), Assert.Single(chart.Markers).Y, 6);
        Assert.Equal(chart.X(TimeSpan.FromSeconds(25)), chart.Markers[0].X, 6);
        Assert.Single(chart.Markers[0].Drops);
        Assert.Equal("Silber je 10 s · Verlauf", chart.Title);
        Assert.Equal("Zahl Ø Session/h · ganze Session", chart.Detail);
    }

    [Fact]
    public void RareDropsOfOneSectionShareAMarkerCenteredOnItsPeak()
    {
        var snapshot = Snapshot(TimeSpan.FromSeconds(65), (21, Valuable), (28, Valuable), (43, 4_000_000)) with
        {
            DropMarkers = [new(TimeSpan.FromSeconds(28), new("Belt", "Belt", "1")), new(TimeSpan.FromSeconds(21), new("Ring", "Ring", "1")),
                new(TimeSpan.FromSeconds(43), new("Earring", "Earring", "1")), new(TimeSpan.FromSeconds(65), new("Necklace", "Necklace", "1"))],
        };

        var chart = OverlayChartSections.Create(Sections(), snapshot);

        Assert.Equal(3, chart.Markers.Count);
        Assert.Equal(new[] { "Ring", "Belt" }, chart.Markers[0].Drops.Select(drop => drop.Item.Name));
        Assert.Equal((chart.X(TimeSpan.FromSeconds(25)), chart.Y(Valuable * 2)), (chart.Markers[0].X, chart.Markers[0].Y));
        Assert.Equal((chart.X(TimeSpan.FromSeconds(45)), chart.Y(4_000_000)), (chart.Markers[1].X, chart.Markers[1].Y));
        // A drop at the very end belongs to the running section.
        Assert.Equal(chart.X(TimeSpan.FromSeconds(62.5)), chart.Markers[2].X, 6);
    }

    [Fact]
    public async Task BothRenderersPlaceTheIconsOfOneSectionSideBySide()
    {
        var widget = Sections() with { X = 0, Y = 0, Width = 344, Height = 144 };
        var snapshot = Snapshot(TimeSpan.FromSeconds(65), (21, Valuable), (28, Valuable)) with
        {
            DropMarkers = [new(TimeSpan.FromSeconds(21), new("Ring", "Ring", "1")), new(TimeSpan.FromSeconds(28), new("Belt", "Belt", "1"))],
        };

        var html = await RenderAsync(widget, snapshot);
        var group = html[html.IndexOf("overlay-chart-drop-group", StringComparison.Ordinal)..];
        Assert.Equal(2, group.Split("overlay-chart-drop-marker").Length - 1);
        Assert.Contains("left:clamp(25px,", group);

        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new Size(344, 144), new OverlaySettings { Width = 344, Height = 144, Widgets = [widget] },
            snapshot, out _);
        var background = Color.FromArgb(255, 37, 45, 51).ToArgb();
        var columns = Enumerable.Range(0, image.Width)
            .Where(x => Enumerable.Range(0, image.Height).Any(y => image.GetPixel(x, y).ToArgb() == background)).ToArray();
        Assert.InRange(columns[^1] - columns[0] + 1, 45, 52);
        // The vertical stem is the column with the most gold pixels.
        var stem = Enumerable.Range(0, image.Width).MaxBy(x => Enumerable.Range(0, image.Height)
            .Count(y => image.GetPixel(x, y) is { A: > 128, R: > 200, G: > 160, B: < 150 }));
        Assert.InRange((columns[0] + columns[^1]) / 2d - stem, -2, 2);
    }

    [Theory]
    [InlineData(5, 13)]
    [InlineData(30, 3)]
    public void SectionLengthControlsTheResolution(int seconds, int sections)
    {
        var snapshot = Snapshot(TimeSpan.FromSeconds(65), (12, 1_000_000), (20, Valuable));

        var chart = OverlayChartSections.Create(Sections() with { ChartSectionSeconds = seconds }, snapshot);

        Assert.Equal(sections, chart.Points.Count);
        Assert.Equal(Valuable + (seconds == 30 ? 1_000_000 : 0), chart.Maximum);
        Assert.Equal($"Silber je {seconds} s · Verlauf", chart.Title);
    }

    [Fact]
    public void ARecentRangeUsesOnlyItsOwnSectionsForTimeAxisAndScale()
    {
        var snapshot = Snapshot(TimeSpan.FromMinutes(30), (5 * 60, Valuable * 3), (25 * 60, Valuable), (25 * 60 + 15, 4_000_000));

        var chart = OverlayChartSections.Create(Sections() with { ChartRangeMinutes = 10 }, snapshot);

        Assert.Equal((TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30)), (chart.From, chart.To));
        Assert.Equal(60, chart.Points.Count);
        Assert.Equal(Valuable, chart.Maximum);
        Assert.Equal("Zahl Ø Session/h · letzte 10 min", chart.Detail);
        Assert.Equal(Valuable * 3, OverlayChartSections.Create(Sections(), snapshot).Maximum);
    }

    [Fact]
    public void ARestoredSessionStartsAtItsFirstCompleteKnownSection()
    {
        var snapshot = Snapshot(TimeSpan.FromMinutes(45), (41 * 60, 2_000_000)) with
        {
            SilverHistory = [new(TimeSpan.FromSeconds(40 * 60 + 35), 100)],
        };

        var chart = OverlayChartSections.Create(Sections(), snapshot);

        Assert.Equal(TimeSpan.FromSeconds(40 * 60 + 40), chart.From);
        Assert.Equal(2_000_000m, chart.Maximum);
    }

    [Fact]
    public void LongSessionsAreThinnedWithoutLosingASingleValuableSection()
    {
        var drops = Enumerable.Range(0, 1440).Select(section => ((double)section * 5 + 1, section == 997 ? Valuable : 3_000_000m)).ToArray();
        var snapshot = Snapshot(TimeSpan.FromHours(2), drops);

        var chart = OverlayChartSections.Create(Sections() with { ChartSectionSeconds = 5 }, snapshot);

        Assert.InRange(chart.Points.Count, 2, 802);
        Assert.Equal(Valuable, chart.Points.Max(point => point.Silver));
        Assert.Equal(TimeSpan.FromSeconds(997 * 5 + 2.5), chart.Points.Single(point => point.Silver == Valuable).Elapsed);
        Assert.True(chart.Points.Zip(chart.Points.Skip(1)).All(pair => pair.First.Elapsed <= pair.Second.Elapsed));
    }

    [Fact]
    public void WithoutEnoughTimeOrMissingPricesTheModuleExplainsItself()
    {
        Assert.False(OverlayChartSections.Create(Sections(), Snapshot(TimeSpan.FromSeconds(8), (3, 1))).HasData);
        var warning = Snapshot(TimeSpan.FromSeconds(65), (3, 1)) with
        {
            Metrics = new Dictionary<string, OverlayMetric> { ["chart"] = new("Silber / h · Verlauf", "1 *", "Teilbetrag · Preise fehlen") },
        };
        Assert.Equal("Teilbetrag · Preise fehlen", OverlayChartSections.Create(Sections(), warning).Detail);
    }

    [Fact]
    public void LootIncreasesAreValuedWithTheCurrentPricesAndTax()
    {
        var state = Session() with
        {
            Elapsed = TimeSpan.FromMinutes(2),
            DropHistory = [new(TimeSpan.FromSeconds(12), "Trash", 10), new(TimeSpan.FromSeconds(40), "Ring", 1),
                new(TimeSpan.FromSeconds(50), "Unknown", 3)],
        };
        var preferences = new TrackerPreferences { ValuePack = true };
        var cheap = new LootPriceSnapshot("eu", [new("Trash", 0, 150, LootPriceOrigin.FixedCatalog, null),
            new("Ring", 1_000_000_000, 0, LootPriceOrigin.LiveMarket, null)]);
        var expensive = new LootPriceSnapshot("eu", [new("Trash", 0, 300, LootPriceOrigin.FixedCatalog, null),
            new("Ring", 2_000_000_000, 0, LootPriceOrigin.LiveMarket, null)]);

        var before = new OverlayMetrics().Update(state, preferences, cheap);
        var after = new OverlayMetrics().Update(state, preferences, expensive);

        Assert.Equal(TimeSpan.FromMinutes(2), before.SessionElapsed);
        Assert.Equal(new[] { 1_500m, SilverValuation.UnitAfterTax(cheap.Quotes["Ring"], preferences.Tax), 0 },
            before.SilverDrops.Select(drop => drop.Silver));
        Assert.Equal(845_000_000m, before.SilverDrops[1].Silver);
        Assert.Equal(new[] { 3_000m, 1_690_000_000, 0 }, after.SilverDrops.Select(drop => drop.Silver));
        Assert.Equal(new[] { false, true, false }, before.SilverDrops.Select(drop => drop.Valuable));
        var favorite = new OverlayMetrics().Update(state, preferences with { FavoriteItems = ["Trash"] }, cheap);
        Assert.Equal(new[] { true, true, false }, favorite.SilverDrops.Select(drop => drop.Valuable));
        Assert.Empty(new OverlayMetrics().Update(state, preferences).SilverDrops);
    }

    [Fact]
    public void ClippingScalesToTheRegularLootAndMarksHigherSections()
    {
        var snapshot = WithValuableDrop(Snapshot(TimeSpan.FromSeconds(65), (3, 1_000_000), (12, 3_000_000), (43, 2_000_000)));

        var chart = OverlayChartSections.Create(Clipped(), snapshot);

        Assert.Equal(3_000_000m, chart.Maximum);
        var peak = Assert.Single(chart.Points, chart.IsClipped);
        Assert.Equal((TimeSpan.FromSeconds(25), Valuable + 500_000), (peak.Elapsed, peak.Silver));
        Assert.Equal(chart.Y(3_000_000), chart.Y(peak.Silver), 6);
        Assert.Equal(chart.Y(peak.Silver), Assert.Single(chart.Markers).Y, 6);
        Assert.Equal(chart.Y(2_000_000), chart.Y(1_000_000) - (chart.Y(1_000_000) - chart.Y(3_000_000)) / 2, 6);
        Assert.Contains("oben gekappt", chart.Description);

        // Without regular income the highest section sets the scale.
        var onlyValuable = OverlayChartSections.Create(Clipped(), WithValuableDrop(Snapshot(TimeSpan.FromSeconds(65))));
        Assert.Equal(Valuable + 500_000, onlyValuable.Maximum);
        Assert.DoesNotContain(onlyValuable.Points, onlyValuable.IsClipped);
    }

    [Fact]
    public void ExcludingValuableDropsKeepsOnlyTheRegularLootInTheCurve()
    {
        var snapshot = WithValuableDrop(Snapshot(TimeSpan.FromSeconds(65), (3, 1_000_000), (12, 3_000_000)));

        var chart = OverlayChartSections.Create(Sections() with { ChartPeakMode = OverlayChartSections.ExcludePeaks }, snapshot);

        Assert.Equal(new[] { 1_000_000m, 3_000_000, 500_000, 0, 0, 0, 0 }, chart.Points.Select(point => point.Silver));
        Assert.Equal(3_000_000m, chart.Maximum);
        Assert.DoesNotContain(chart.Points, chart.IsClipped);
        var marker = Assert.Single(chart.Markers);
        Assert.Equal((chart.X(TimeSpan.FromSeconds(25)), chart.Y(500_000)), (marker.X, marker.Y));
        Assert.Contains("ohne wertvolle Drops", chart.Description);
    }

    [Fact]
    public void TheLogarithmicScaleIsTheDefaultAndCompressesPeaksWithoutClipping()
    {
        var snapshot = WithValuableDrop(Snapshot(TimeSpan.FromSeconds(65), (12, 10_000_000)));

        var chart = OverlayChartSections.Create(OverlayCatalog.CreateWidget("chart"), snapshot);

        Assert.Equal(Valuable + 500_000, chart.Maximum);
        Assert.DoesNotContain(chart.Points, chart.IsClipped);
        Assert.Equal(6d / 72, chart.Y(chart.Maximum), 6);
        Assert.Equal(70d / 72, chart.Y(0), 6);
        // A hundredth of the highest section still reaches about half the height.
        Assert.InRange(chart.Y(10_000_000), 37.5 / 72, 38.5 / 72);
        Assert.Contains("logarithmische Höhe", chart.Description);
    }

    [Fact]
    public void WidgetSettingsDefaultToTheAverageCurveAndAreNormalized()
    {
        var saved = JsonSerializer.Deserialize<OverlaySettings>("""{"widgets":[{"id":"8f6e0c8b5b7beb8eda6fba31aedf0f97","kind":"chart"}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var legacy = Assert.Single(OverlayLayout.Normalize(saved).Widgets);
        Assert.Equal((OverlayChartSections.AverageMode, 0, 10), (legacy.ChartMode, legacy.ChartRangeMinutes, legacy.ChartSectionSeconds));
        Assert.Equal(OverlayChartSections.LogarithmicPeaks, legacy.ChartPeakMode);
        var created = OverlayCatalog.CreateWidget("chart");
        Assert.Equal((OverlayChartSections.SectionsMode, 0, 10, OverlayChartSections.LogarithmicPeaks),
            (created.ChartMode, created.ChartRangeMinutes, created.ChartSectionSeconds, created.ChartPeakMode));
        Assert.Equal(OverlayChartSections.AverageMode, OverlayCatalog.CreateWidget("silver").ChartMode);

        var widgets = OverlayLayout.Normalize(new OverlaySettings
        {
            Widgets = [Sections() with { ChartRangeMinutes = 40, ChartSectionSeconds = 30, ChartPeakMode = OverlayChartSections.ExcludePeaks },
                Sections() with { ChartMode = "bars", ChartRangeMinutes = 45, ChartSectionSeconds = 1, ChartPeakMode = "zoom" }],
        }).Widgets;
        Assert.Equal((OverlayChartSections.SectionsMode, 40, 30), (widgets[0].ChartMode, widgets[0].ChartRangeMinutes, widgets[0].ChartSectionSeconds));
        Assert.Equal((OverlayChartSections.AverageMode, 0, 10), (widgets[1].ChartMode, widgets[1].ChartRangeMinutes, widgets[1].ChartSectionSeconds));
        Assert.Equal((OverlayChartSections.ExcludePeaks, OverlayChartSections.LogarithmicPeaks), (widgets[0].ChartPeakMode, widgets[1].ChartPeakMode));
    }

    [Fact]
    public void RotationsPerHourUsesTheRecentTempoIncludingTheWalkBack()
    {
        var metrics = new OverlayMetrics().Update(WithRotations(new(900, 20), new(600, 15), new(620, 25), new(640)), new() { UiLanguage = "de" }).Metrics;

        // The latest walk back is still running and counts with the session average of 20 seconds.
        var rate = metrics["rotations-hour"];
        Assert.Equal(("Rotations / h", "5,6", "Ø 10:40 · letzte 3"), (rate.Label, rate.Value, rate.Detail));
        Assert.Contains("letzten bis zu drei", rate.Tooltip);
        Assert.Contains("Rückweg", rate.Tooltip);
        var count = metrics["rotation-count"];
        Assert.Equal(("Rotation Counter", "4", "Zuletzt 10:40"), (count.Label, count.Value, count.Detail));

        // Only complete rotations are rotations: an aborted attempt and the running one are neither counted nor shown.
        var mixed = new OverlayMetrics().Update(WithRotations(new SessionRotationTiming(600, 20),
            new SessionRotationTiming(120, Outcome: "aborted"), new SessionRotationTiming(45, Outcome: "active")),
            new() { UiLanguage = "de" }).Metrics["rotation-count"];
        Assert.Equal(("1", "Zuletzt 10:00"), (mixed.Value, mixed.Detail));

        var single = new OverlayMetrics().Update(WithRotations(new SessionRotationTiming(1180, 20)), new() { UiLanguage = "de" }).Metrics["rotations-hour"];
        Assert.Equal(("3,0", "Ø 20:00 · 1 Rotation"), (single.Value, single.Detail));
        var unknownWalk = new OverlayMetrics().Update(WithRotations(new SessionRotationTiming(1200)), new() { UiLanguage = "de" }).Metrics["rotations-hour"];
        Assert.Equal(("3,0", "Ø 20:00 · ohne Rückweg"), (unknownWalk.Value, unknownWalk.Detail));
    }

    [Fact]
    public void RotationModulesExplainMissingRotationsAndProfiles()
    {
        var none = new OverlayMetrics().Update(WithRotations(), new() { UiLanguage = "de" }).Metrics;
        Assert.Equal(("—", "Nach der ersten vollständigen Rotation"), (none["rotations-hour"].Value, none["rotations-hour"].Detail));
        Assert.Equal(("0", "In dieser Session"), (none["rotation-count"].Value, none["rotation-count"].Detail));

        var unsupported = new OverlayMetrics().Update(Session() with { SpotId = null }, new() { UiLanguage = "de" }).Metrics;
        Assert.Equal(("—", "Kein Rotationsprofil für diesen Spot"), (unsupported["rotations-hour"].Value, unsupported["rotations-hour"].Detail));
        Assert.Equal("—", unsupported["rotation-count"].Value);
        Assert.NotNull(OverlayCatalog.Find("rotations-hour"));
        Assert.NotNull(OverlayCatalog.Find("rotation-count"));
    }

    [Fact]
    public void RotationMonitorPublishesTheSessionsCompletedRotationsWithTheirWalkBack()
    {
        var profile = new CompletingProfile();
        using var monitor = new RotationMonitor(spot => spot == LootSpotCatalog.HermesiaId ? profile : null);
        var start = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        monitor.RestoreSession([
            new(LootSpotCatalog.HermesiaId, start.AddMinutes(10), new(590, [])),
            new(LootSpotCatalog.AphrodonId, start.AddMinutes(9), new(50, [])),
            // Ends three minutes before the next start: a break, not a walk back.
            new(LootSpotCatalog.HermesiaId, start, new(420, [])),
        ]);
        profile.Completed.Add((start.AddMinutes(20), new RotationRun(600, [])));

        var waiting = monitor.Snapshot(start.AddMinutes(30).AddSeconds(10), LootSpotCatalog.HermesiaId);
        Assert.Equal([(420d, (double?)null), (590, 10), (600, null)],
            waiting.SessionRotations.Select(timing => (timing.Duration, timing.WalkBack)));
        // Their start times place them on the session timeline.
        Assert.Equal([start, start.AddMinutes(10), start.AddMinutes(20)], waiting.SessionRotations.Select(timing => timing.StartedAt));

        // The next rotation began 30 seconds after the last one ended.
        profile.Current = new() { Status = "Test", Synchronized = true, Elapsed = 30 };
        var running = monitor.Snapshot(start.AddMinutes(31), LootSpotCatalog.HermesiaId);
        Assert.Equal([(420d, (double?)null), (590, 10), (600, 30)],
            running.SessionRotations.Select(timing => (timing.Duration, timing.WalkBack)));
        Assert.Equal(3, monitor.ExportSession().Count(rotation => rotation.SpotId == LootSpotCatalog.HermesiaId));
    }

    [Fact]
    public async Task EditorPreviewAndNativeOverlayRenderTheSectionCurve()
    {
        var widget = Clipped() with { X = 0, Y = 0, Width = 344, Height = 144 };
        var snapshot = OverlaySnapshot.Demo;

        var html = await RenderAsync(widget, snapshot);
        Assert.Contains("Silber je 10 s · Verlauf", html);
        Assert.Contains("overlay-chart-line", html);
        // The example session's five valuable drops fall into five sections above its regular loot.
        Assert.Equal(5, html.Split("overlay-chart-clip").Length - 1);
        Assert.DoesNotContain("overlay-chart-clip",
            await RenderAsync(widget with { ChartPeakMode = OverlayChartSections.LogarithmicPeaks }, snapshot));
        Assert.Contains("Zahl Ø Session/h · ganze Session", html);
        Assert.DoesNotContain("NaN", html);

        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new Size(344, 144), new OverlaySettings { Width = 344, Height = 144, Widgets = [widget] },
            snapshot, out _);
        var gold = Enumerable.Range(0, image.Width).SelectMany(x => Enumerable.Range(0, image.Height)
            .Select(y => image.GetPixel(x, y))).Count(pixel => pixel.A > 128 && pixel.R > 200 && pixel.G > 160 && pixel.B < 150);
        Assert.True(gold > 100, $"Nur {gold} goldene Pixel.");

        var rotations = await RenderAsync(OverlayCatalog.CreateWidget("rotations-hour"), snapshot);
        Assert.Contains("Rotations / h", rotations);
        // Six completed rotations of the example session with walk backs of 14 to 18 seconds.
        Assert.Contains("Ø 10:35 · letzte 3", rotations);
        Assert.Contains("5,7", rotations);
        Assert.Contains("Rotation Counter", await RenderAsync(OverlayCatalog.CreateWidget("rotation-count"), snapshot));
    }

    [Fact]
    public void TheNativeOverlayRedrawsTheSectionCurveWhenItsSilverChanges()
    {
        var snapshot = OverlaySnapshot.Demo;
        var changed = snapshot with { SilverDrops = [.. snapshot.SilverDrops, new(TimeSpan.FromSeconds(939), 5)] };
        var sections = new OverlaySettings { Widgets = [Sections()] };
        var average = new OverlaySettings { Widgets = [Sections() with { ChartMode = OverlayChartSections.AverageMode }] };
        var state = new NativeOverlayRenderState();

        state.Remember(sections, snapshot, new Size(344, 144));
        Assert.False(state.Matches(sections, changed, new Size(344, 144)));
        state.Remember(average, snapshot, new Size(344, 144));
        Assert.True(state.Matches(average, changed, new Size(344, 144)));
    }

    // A billion-silver favorite and a little trash in the section from 20 to 30 seconds.
    private static OverlaySnapshot WithValuableDrop(OverlaySnapshot snapshot) => snapshot with
    {
        SilverDrops = [.. snapshot.SilverDrops, new(TimeSpan.FromSeconds(22), Valuable, true), new(TimeSpan.FromSeconds(24), 500_000)],
        DropMarkers = [new(TimeSpan.FromSeconds(22), new("Ring", "Ring", "1"))],
    };

    private static OverlayWidget Clipped() => Sections() with { ChartPeakMode = OverlayChartSections.ClipPeaks };

    private static OverlayWidget Sections() => OverlayCatalog.CreateWidget("chart") with { ChartMode = OverlayChartSections.SectionsMode };

    private static OverlaySnapshot Snapshot(TimeSpan elapsed, params (double Seconds, decimal Silver)[] drops) => new()
    {
        UiLanguage = "de",
        SessionElapsed = elapsed,
        SilverHistory = [new(TimeSpan.FromSeconds(10), 100), new(elapsed, 100)],
        SilverDrops = drops.Select(drop => new OverlaySilverDrop(TimeSpan.FromSeconds(drop.Seconds), drop.Silver)).ToArray(),
        Metrics = new Dictionary<string, OverlayMetric> { ["chart"] = new("Silber / h · Verlauf", "1,2 Mrd.", "Session-Durchschnitt") },
    };

    private static TrackerState Session() => new()
    {
        SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, SpotId = LootSpotCatalog.HermesiaId,
    };

    private static TrackerState WithRotations(params SessionRotationTiming[] rotations) => Session() with
    {
        Rotation = new() { SpotId = LootSpotCatalog.HermesiaId, SessionRotations = rotations },
    };

    private sealed class CompletingProfile : IRotationProfileMonitor
    {
        public List<(DateTimeOffset StartedAt, RotationRun Run)> Completed { get; } = [];
        public void Observe(Bitmap frame, DateTimeOffset at) { }
        public void Interrupt(string status) { }
        public RotationMonitorSnapshot Current { get; set; } = new() { Status = "Test" };
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => Current;
        public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
        {
            var result = Completed.ToArray();
            Completed.Clear();
            return result;
        }
        public void Dispose() { }
    }

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
