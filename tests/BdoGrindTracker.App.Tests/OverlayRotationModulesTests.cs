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

public sealed class OverlayRotationModulesTests
{
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
        Assert.Empty(new OverlayMetrics().Update(state, preferences).SilverDrops);
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
    public async Task TheEditorPreviewShowsTheExampleSessionsRotationModules()
    {
        var snapshot = OverlaySnapshot.Demo;
        var rotations = await RenderAsync(OverlayCatalog.CreateWidget("rotations-hour"), snapshot);
        Assert.Contains("Rotations / h", rotations);
        // Six completed rotations of the example session with walk backs of 14 to 18 seconds.
        Assert.Contains("Ø 10:35 · letzte 3", rotations);
        Assert.Contains("5,7", rotations);
        Assert.Contains("Rotation Counter", await RenderAsync(OverlayCatalog.CreateWidget("rotation-count"), snapshot));
    }

    // A billion-silver favorite and a little trash in the section from 20 to 30 seconds.
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
