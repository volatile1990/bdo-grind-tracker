using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayRenderStateTests
{
    [Fact]
    public void EquivalentSnapshotsAndUnrelatedMetricsDoNotRedrawStaticWidgets()
    {
        var state = new NativeOverlayRenderState();
        var settings = new OverlaySettings { Widgets = [OverlayCatalog.CreateWidget("spot")] };
        var snapshot = new OverlaySnapshot { Metrics = new Dictionary<string, OverlayMetric> { ["spot"] = new("Spot", "Hermesia") } };
        Assert.False(state.Matches(settings, snapshot, new(360, 260)));
        state.Remember(settings, snapshot, new(360, 260));
        var refreshed = snapshot with { ClockUtcNow = snapshot.ClockUtcNow.AddSeconds(5), Metrics = new Dictionary<string, OverlayMetric>
        {
            ["spot"] = new("Spot", "Hermesia"), ["duration"] = new("Zeit", "00:10:15"),
        } };
        Assert.True(state.Matches(settings with { Widgets = settings.Widgets.ToArray() }, refreshed, new(360, 260)));
        Assert.False(state.Matches(settings, refreshed with { Metrics = new Dictionary<string, OverlayMetric> { ["spot"] = new("Spot", "Aphrodon") } }, new(360, 260)));
        Assert.False(state.Matches(settings, refreshed, new(720, 520)));
        Assert.False(state.Matches(settings with { BackgroundOpacity = .1 }, refreshed, new(360, 260)));
        state.Invalidate();
        Assert.False(state.Matches(settings, snapshot, new(360, 260)));
    }

    [Fact]
    public void ControlsLootChartsAndClockInvalidateTheirDisplayedData()
    {
        static void Changed(string kind, OverlaySnapshot before, OverlaySnapshot after)
        {
            var state = new NativeOverlayRenderState();
            var settings = new OverlaySettings { Widgets = [OverlayCatalog.CreateWidget(kind)] };
            state.Remember(settings, before, new(360, 260));
            Assert.False(state.Matches(settings, after, new(360, 260)));
        }
        var snapshot = new OverlaySnapshot();
        Changed("controls", snapshot, snapshot with { CanToggleTracking = true });
        Changed("drop-grid", snapshot, snapshot with { Drops = [new("Caphras Stone", "Caphras-Stein", "1")] });
        Changed("chart", snapshot, snapshot with { SilverHistory = [new SessionSilverSample(TimeSpan.FromSeconds(10), 100m)] });
        Changed("chart", snapshot, snapshot with { DropMarkers = [new(TimeSpan.FromSeconds(10), new("Rare drop", "Rare drop", "1"))] });
        Changed("clock", snapshot, snapshot with { ClockUtcNow = snapshot.ClockUtcNow.AddSeconds(10) });
    }

    [Fact]
    public void StandaloneRotationRepaintsItsPlayheadEventsAndSelectedReference()
    {
        var state = new NativeOverlayRenderState();
        var settings = new OverlaySettings { Widgets = [OverlayCatalog.CreateWidget("rotation-monitor")] };
        var rotation = new RotationMonitorSnapshot
        {
            SpotId = "hermesia", Synchronized = true, Elapsed = 10,
            Events = [new("start", "Start", 0)], Best = new(600, [new("start", "Start", 0), new("end", "Ende", 600)])
        };
        var snapshot = new OverlaySnapshot { Rotation = rotation };
        state.Remember(settings, snapshot, new(600, 160));
        var equivalent = rotation with { Events = rotation.Events.ToArray(),
            Best = rotation.Best with { Events = rotation.Best.Events.ToArray() } };
        Assert.True(state.Matches(settings, snapshot with { Rotation = equivalent }, new(600, 160)));
        Assert.False(state.Matches(settings, snapshot with { Rotation = rotation with { Elapsed = 11 } }, new(600, 160)));
        Assert.False(state.Matches(settings, snapshot with { Rotation = rotation with { Events = [.. rotation.Events, new("porter", "Porter", 10)] } }, new(600, 160)));
        Assert.False(state.Matches(settings, snapshot with { Rotation = rotation with { Best = rotation.Best with { Duration = 590 } } }, new(600, 160)));
    }

    [Fact]
    public void RotationSectorComparisonRepaintsWhenItsDisplayedBestTimeChanges()
    {
        var state = new NativeOverlayRenderState();
        var rotation = HermesiaRotationDemo.At(350);
        var snapshot = new OverlaySnapshot { Rotation = rotation };
        var changed = snapshot with { Rotation = rotation with {
            SectorBests = rotation.SectorBests.ToDictionary(pair => pair.Key, pair => pair.Value + 10)
        } };
        var widget = OverlayCatalog.CreateWidget("rotation-monitor");
        foreach (var mode in new[] { "best", "sectors" })
        {
            var settings = new OverlaySettings { Widgets = [widget with { RotationComparison = mode }] };
            state.Remember(settings, snapshot, new(600, 160));
            Assert.Equal(mode == "best", state.Matches(settings, changed, new(600, 160)));
        }
    }

    [Fact]
    public void ConflictedHotkeyRetriesWithoutReleasingAnAlreadyRegisteredShortcut()
    {
        var now = 0L;
        var conflict = true;
        var attempts = new List<int>();
        var releases = new List<int>();
        var registration = new NativeOverlayHotkeyRegistration((id, _, _) =>
        {
            attempts.Add(id);
            return id == 1 || !conflict;
        }, releases.Add, () => now);
        Assert.NotNull(registration.Apply(true, OverlayHotkey.DefaultToggleOverlay, OverlayHotkey.DefaultToggleInteraction));
        conflict = false;
        now = 4_999;
        Assert.NotNull(registration.Apply(true, OverlayHotkey.DefaultToggleOverlay, OverlayHotkey.DefaultToggleInteraction));
        Assert.Equal(new[] { 1, 2 }, attempts);
        now = 5_000;
        Assert.Null(registration.Apply(true, OverlayHotkey.DefaultToggleOverlay, OverlayHotkey.DefaultToggleInteraction));
        Assert.Equal(new[] { 1, 2, 2 }, attempts);
        Assert.Empty(releases);
        now += 60_000;
        Assert.Null(registration.Apply(true, OverlayHotkey.DefaultToggleOverlay, OverlayHotkey.DefaultToggleInteraction));
        Assert.Equal(3, attempts.Count);
        registration.Clear();
        Assert.Equal(new[] { 1, 2 }, releases);
    }
}
