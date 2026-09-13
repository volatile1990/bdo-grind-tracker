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
        Changed("clock", snapshot, snapshot with { ClockUtcNow = snapshot.ClockUtcNow.AddSeconds(10) });
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
