using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task AgrisObservationsReachLiveStatePersistWithTheSessionAndResetForTheNextGrind()
    {
        var detector = new SessionAgrisDetector(() => new(AgrisStatus.Active));
        var monitor = new AgrisMonitor(detector);
        await using var fixture = new Fixture(autoUpload: false, agrisMonitor: monitor, lootScrollVisible: _ => true);
        BeginAgrisSession(fixture);
        fixture.Time.Advance(TimeSpan.FromSeconds(12));
        var now = DateTimeOffset.UtcNow;
        foreach (var secondsAgo in new[] { 10, 5, 0 })
            await ProcessAgrisFrame(fixture, monitor, now.AddSeconds(-secondsAgo));
        var active = fixture.Service.State.AgrisActiveDuration;
        Assert.Equal(AgrisStatus.Active, fixture.Service.State.Agris.Status);
        Assert.InRange(active.TotalSeconds, 8, 10);
        Assert.Equal(active, fixture.Service.State.AgrisObservedDuration);
        Assert.Equal(30, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.IsError);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(AgrisStatus.Unknown, fixture.Service.State.Agris.Status);
        var history = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(active, history.AgrisActiveDuration);
        Assert.Equal(active, history.AgrisObservedDuration);
        Assert.Equal(TimeSpan.FromSeconds(12), history.Duration);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.AgrisActiveDuration);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.AgrisObservedDuration);
        Assert.Equal(active, Assert.Single(fixture.HistoryStore.Load()).AgrisActiveDuration);
    }

    [Fact]
    public async Task MissingForegroundAndQueuedNonGameFramesCannotAccumulateAgrisTime()
    {
        var visible = true;
        var detector = new SessionAgrisDetector(() => new(AgrisStatus.Active));
        var monitor = new AgrisMonitor(detector);
        await using var fixture = new Fixture(autoUpload: false, agrisMonitor: monitor, lootScrollVisible: _ => visible);
        BeginAgrisSession(fixture);
        fixture.Time.Advance(TimeSpan.FromSeconds(12));
        var now = DateTimeOffset.UtcNow;
        await ProcessAgrisFrame(fixture, monitor, now.AddSeconds(-10));
        await ProcessAgrisFrame(fixture, monitor, now.AddSeconds(-5));
        var active = fixture.Service.State.AgrisActiveDuration;
        Assert.True(active > TimeSpan.Zero);
        visible = false;
        fixture.Service.RefreshPendingState();
        Assert.Equal(AgrisState.Unknown, fixture.Service.State.Agris);
        visible = true;
        await ProcessAgrisFrame(fixture, monitor, now, canObserveHud: false);
        Assert.Equal(AgrisState.Unknown, fixture.Service.State.Agris);
        Assert.Equal(active, fixture.Service.State.AgrisActiveDuration);
        Assert.Equal(2, detector.Calls);
        Assert.Equal(30, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task OptionalAgrisFailureNeverChangesLootOrBlocksTracking()
    {
        var monitor = new AgrisMonitor(new SessionAgrisDetector(() => throw new InvalidOperationException("Synthetic Agris failure")));
        await using var fixture = new Fixture(autoUpload: false, agrisMonitor: monitor, lootScrollVisible: _ => true);
        BeginAgrisSession(fixture);
        fixture.Time.Advance(TimeSpan.FromSeconds(12));
        await ProcessAgrisFrame(fixture, monitor, DateTimeOffset.UtcNow);
        Assert.Equal(AgrisState.Unknown, fixture.Service.State.Agris);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.AgrisObservedDuration);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.CanPause);
        Assert.False(fixture.Service.State.IsError);
        Assert.Null(fixture.Service.State.TrackingBlockedReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgrisHudInterruptionsSkippedByUiRefreshCannotBridgeActiveIntervals(bool detectorReturnsUnknown)
    {
        var status = AgrisStatus.Active;
        var detector = new SessionAgrisDetector(() => new(status));
        // Use a short sampling interval to replay a complete foreground gap
        // inside the normal 15-second freshness window without wall-clock waits.
        var monitor = new AgrisMonitor(detector, TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, agrisMonitor: monitor, lootScrollVisible: _ => true);
        BeginAgrisSession(fixture);
        fixture.Time.Advance(TimeSpan.FromSeconds(20));
        var now = DateTimeOffset.UtcNow;
        await ProcessAgrisFrame(fixture, monitor, now.AddSeconds(-12));
        await ProcessAgrisFrame(fixture, monitor, now.AddSeconds(-8));
        var beforeGap = fixture.Service.State.AgrisActiveDuration;
        Assert.InRange(beforeGap.TotalSeconds, 3, 4);

        // Simulate an occupied UI while capture observes either a hidden HUD
        // or an unknown glyph, followed by another valid active observation.
        status = AgrisStatus.Unknown;
        await ProcessAgrisFrame(fixture, monitor, now.AddSeconds(-5),
            canObserveHud: detectorReturnsUnknown, refresh: false);
        status = AgrisStatus.Active;
        await ProcessAgrisFrame(fixture, monitor, now.AddSeconds(-2), refresh: false);
        Assert.Equal(AgrisStatus.Active, fixture.Service.State.Agris.Status);
        fixture.Service.RefreshPendingState();

        Assert.Equal(AgrisStatus.Active, fixture.Service.State.Agris.Status);
        Assert.Equal(beforeGap, fixture.Service.State.AgrisActiveDuration);
        Assert.Equal(beforeGap, fixture.Service.State.AgrisObservedDuration);
        await ProcessAgrisFrame(fixture, monitor, now);
        Assert.InRange((fixture.Service.State.AgrisActiveDuration - beforeGap).TotalSeconds, 1, 2);
        Assert.Equal(fixture.Service.State.AgrisActiveDuration, fixture.Service.State.AgrisObservedDuration);
        Assert.Equal(50, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
    }

    private static void BeginAgrisSession(Fixture fixture)
    {
        fixture.Begin();
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
        // Construction and Begin publish Unknown at real wall-clock time. This
        // fixture replays frames captured just before that time, so start its
        // Agris timeline after setup instead of treating replay as stale input.
        var field = fixture.Service.GetType().GetField("_agrisSessionTracker",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.IsType<AgrisSessionTracker>(field.GetValue(fixture.Service)).Reset();
    }

    private static async Task ProcessAgrisFrame(Fixture fixture, AgrisMonitor monitor, DateTimeOffset capturedAt,
        bool canObserveHud = true, bool refresh = true)
    {
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame, new CapturedFrameMetadata(1, capturedAt) { CanObserveHud = canObserveHud }, CancellationToken.None);
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
        if (refresh) fixture.Service.RefreshPendingState();
    }

    private sealed class SessionAgrisDetector(Func<AgrisReading> read) : IAgrisFrameDetector
    {
        public int Calls;
        public AgrisReading Analyze(Bitmap frame, CancellationToken cancellationToken) { Calls++; return read(); }
        public void Dispose() { }
    }
}
