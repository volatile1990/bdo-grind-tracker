using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData("inactive-user-20260910.png", false)]
    [InlineData("inactive-expanded-user-20260910.png", false)]
    [InlineData("inactive-zero-user-20260910.png", false)]
    [InlineData("active-2-user-20260910.png", false)]
    [InlineData("inactive-user-20260910.png", true)]
    public async Task UserHudCountdownReachesLiveSessionAndOverlayWithoutInterruptingLoot(string image, bool countsDown)
    {
        var remaining = TimeSpan.FromHours(6);
        var monitor = new LootScrollMonitor(new LootScrollFrameDetector(new SessionTimer(() => remaining)));
        await using var fixture = new Fixture(autoUpload: false, lootScrollMonitor: monitor, lootScrollVisible: _ => true);
        BeginScrollSession(fixture);
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "loot-scroll", image));
        var now = DateTimeOffset.UtcNow;
        const int sampleCount = 2;
        for (var sample = 0; sample < sampleCount; sample++)
        {
            if (countsDown && sample > 0) remaining -= TimeSpan.FromSeconds(30);
            fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
            await fixture.Service.ProcessFrameAsync(frame,
                new CapturedFrameMetadata(sample, now.AddSeconds(30 * (sample - sampleCount + 1))) { CanObserveHud = true }, CancellationToken.None);
            await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Service.RefreshPendingState();
        }
        var state = fixture.Service.State;
        Assert.Equal(countsDown ? LootScrollStatus.Active : LootScrollStatus.Inactive, state.LootScroll.Status);
        Assert.Equal(countsDown ? 1 : (int?)null, state.LootScroll.Level);
        Assert.Equal(!countsDown, state.LootScroll.ShouldWarn);
        Assert.True(state.IsRunning);
        Assert.True(state.CanPause);
        Assert.False(state.IsError);
        Assert.Equal(sampleCount * 10, state.Loot.TotalQuantity);
        var overlay = new BdoGrindTracker.App.Overlay.OverlayMetrics().Update(state, fixture.Service.Preferences);
        Assert.Same(state.LootScroll, overlay.LootScroll);
        Assert.Equal(!countsDown, overlay.Metrics["loot-scroll"].IsWarning);
    }

    [Fact]
    public async Task UnchangedTimerAfterThirtySecondsWarnsWithoutBlockingAndPauseClearsIt()
    {
        var detector = new SessionScrollDetector(StoppedScroll);
        var monitor = new LootScrollMonitor(detector);
        await using var fixture = new Fixture(autoUpload: false, lootScrollMonitor: monitor, lootScrollVisible: _ => true);
        BeginScrollSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessScrollFrame(fixture, now.AddSeconds(-30));
        await monitor.CurrentAnalysis;
        fixture.Service.RefreshPendingState();
        Assert.Equal(LootScrollStatus.Unknown, fixture.Service.State.LootScroll.Status);
        Assert.Equal("Nicht erkannt", new LiveSessionPresentation(fixture.Service.State).LootScroll);
        Assert.False(new LiveSessionPresentation(fixture.Service.State).LootScrollWarning);

        await ProcessScrollFrame(fixture, now);
        await monitor.CurrentAnalysis;
        fixture.Service.RefreshPendingState();
        Assert.True(fixture.Service.State.LootScroll.ShouldWarn);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.CanPause);
        Assert.Null(fixture.Service.State.TrackingBlockedReason);
        Assert.False(fixture.Service.State.IsError);
        Assert.Equal(20, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal("Inaktiv", new LiveSessionPresentation(fixture.Service.State).LootScroll);
        var overlay = new OverlayMetrics().Update(fixture.Service.State, fixture.Service.Preferences);
        Assert.Equal("Inaktiv", overlay.Metrics["loot-scroll"].Value);
        Assert.True(overlay.Metrics["loot-scroll"].IsWarning);
        Assert.True(overlay.CanToggleTracking);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);
        Assert.Equal(20, fixture.Service.State.Loot.TotalQuantity);
        await fixture.Service.NewSessionAsync();
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task ThirtySecondTimerRateReachesLiveAndOverlayAndSurvivesOneMissingReading(
        int consumptionRate, bool ocrThrows)
    {
        var remaining = TimeSpan.FromHours(6);
        var missing = false;
        var detector = new SessionScrollDetector(() =>
        {
            if (missing)
            {
                if (ocrThrows) throw new InvalidOperationException("Synthetic timer OCR failure");
                return LootScrollReading.Unknown;
            }
            // Deliberately contradict the glyph. Timer rate must establish the level.
            return new(LootScrollStatus.Inactive, consumptionRate == 2 ? 1 : 2)
            {
                RemainingTime = remaining, TimerResolution = TimeSpan.FromSeconds(1),
            };
        });
        var monitor = new LootScrollMonitor(detector);
        await using var fixture = new Fixture(autoUpload: false, lootScrollMonitor: monitor, lootScrollVisible: _ => true);
        BeginScrollSession(fixture);
        using var overlay = new OverlayService(fixture.Service);
        var first = DateTimeOffset.UtcNow.AddSeconds(-60);

        await ProcessScrollFrame(fixture, first);
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.RefreshPendingState();
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);
        Assert.Equal("Nicht erkannt", overlay.Snapshot.Metrics["loot-scroll"].Value);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);

        remaining -= TimeSpan.FromSeconds(30 * consumptionRate);
        fixture.Time.Advance(TimeSpan.FromSeconds(30));
        await ProcessScrollFrame(fixture, first.AddSeconds(30));
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.RefreshPendingState();
        var confirmed = fixture.Service.State.LootScroll;
        Assert.Equal(LootScrollStatus.Active, confirmed.Status);
        Assert.Equal(consumptionRate, confirmed.Level);
        Assert.Equal(first.AddSeconds(30), confirmed.ObservedAt);
        Assert.Equal($"Aktiv · Lvl. {consumptionRate}", new LiveSessionPresentation(fixture.Service.State).LootScroll);
        Assert.Equal($"Aktiv · Lvl. {consumptionRate}", overlay.Snapshot.Metrics["loot-scroll"].Value);

        missing = true;
        fixture.Time.Advance(TimeSpan.FromSeconds(30));
        await ProcessScrollFrame(fixture, first.AddSeconds(60));
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.RefreshPendingState();
        Assert.Equal(confirmed, fixture.Service.State.LootScroll);
        Assert.Equal(confirmed, overlay.Snapshot.LootScroll);
        Assert.Equal($"Aktiv · Lvl. {consumptionRate}", overlay.Snapshot.Metrics["loot-scroll"].Value);
        Assert.False(overlay.Snapshot.Metrics["loot-scroll"].IsWarning);
        Assert.True(overlay.Snapshot.CanToggleTracking);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.CanPause);
        Assert.False(fixture.Service.State.IsError);
        Assert.Null(fixture.Service.State.TrackingBlockedReason);
        Assert.Equal(30, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(3, detector.Calls);
        Assert.Equal(confirmed, monitor.Snapshot(first.AddSeconds(149)));
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(first.AddSeconds(150)));
    }

    [Fact]
    public async Task PauseAndNewSessionClearConfirmedLevelAndItsCountdownBaseline()
    {
        var remaining = TimeSpan.FromHours(6);
        var monitor = new LootScrollMonitor(new SessionScrollDetector(() => new(LootScrollStatus.Active, 2)
        {
            RemainingTime = remaining, TimerResolution = TimeSpan.FromSeconds(1),
        }));
        await using var fixture = new Fixture(autoUpload: false, lootScrollMonitor: monitor, lootScrollVisible: _ => true);
        BeginScrollSession(fixture);
        using var overlay = new OverlayService(fixture.Service);
        var firstSessionId = fixture.Service.State.SessionId;
        var first = DateTimeOffset.UtcNow.AddSeconds(-60);
        await ProcessScrollFrame(fixture, first);
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        remaining -= TimeSpan.FromSeconds(60);
        fixture.Time.Advance(TimeSpan.FromSeconds(30));
        await ProcessScrollFrame(fixture, first.AddSeconds(30));
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.RefreshPendingState();
        Assert.Equal(2, fixture.Service.State.LootScroll.Level);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);
        Assert.Equal("Nicht erkannt", overlay.Snapshot.Metrics["loot-scroll"].Value);
        Assert.False(overlay.Snapshot.Metrics["loot-scroll"].IsWarning);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(first.AddSeconds(31)));
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.NotEqual(firstSessionId, fixture.Service.State.SessionId);
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);

        BeginScrollSession(fixture);
        remaining -= TimeSpan.FromSeconds(60);
        await ProcessScrollFrame(fixture, first.AddSeconds(60));
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.RefreshPendingState();
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);
        Assert.Equal("Nicht erkannt", overlay.Snapshot.Metrics["loot-scroll"].Value);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task SwitchingToTheAppKeepsTheRecentWarningReadableAndIgnoresQueuedNonGameFrames()
    {
        var detector = new SessionScrollDetector(StoppedScroll);
        var monitor = new LootScrollMonitor(detector);
        await using var fixture = new Fixture(autoUpload: false, lootScrollMonitor: monitor, lootScrollVisible: _ => true);
        BeginScrollSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessScrollFrame(fixture, now.AddSeconds(-30));
        await monitor.CurrentAnalysis;
        await ProcessScrollFrame(fixture, now);
        await monitor.CurrentAnalysis;
        fixture.Service.RefreshPendingState();
        var warning = fixture.Service.State.LootScroll;
        Assert.True(warning.ShouldWarn);

        // Acquisition eligibility travels with the frame even if game foreground has returned.
        await ProcessScrollFrame(fixture, DateTimeOffset.UtcNow, canObserveHud: false);
        Assert.Equal(warning, fixture.Service.State.LootScroll);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(30, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(2, detector.Calls);
        Assert.Equal(warning, monitor.Snapshot(now.AddSeconds(119)));
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(now.AddSeconds(120)));
    }

    [Fact]
    public async Task FailingVisibilityCheckCannotStopLootCapture()
    {
        var detector = new SessionScrollDetector(() => new(LootScrollStatus.Inactive));
        var monitor = new LootScrollMonitor(detector);
        await using var fixture = new Fixture(autoUpload: false, lootScrollMonitor: monitor,
            lootScrollVisible: _ => throw new InvalidOperationException("Synthetic window lookup failure"));
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        await fixture.Service.ToggleTrackingAsync();
        await WaitUntilAsync(() =>
        {
            fixture.Service.RefreshPendingState();
            return fixture.Service.State.Loot.TotalQuantity == 10;
        });
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.IsError);
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public async Task SlowAndFailingScrollDetectorNeverHoldsUpLootFrames()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var detector = new SessionScrollDetector(() =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException("Synthetic image recognition failure");
        });
        var monitor = new LootScrollMonitor(detector);
        await using var fixture = new Fixture(autoUpload: false, lootScrollMonitor: monitor, lootScrollVisible: _ => true);
        BeginScrollSession(fixture);
        try
        {
            await ProcessScrollFrame(fixture, DateTimeOffset.UtcNow).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            await ProcessScrollFrame(fixture, DateTimeOffset.UtcNow).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(monitor.CurrentAnalysis.IsCompleted);
            Assert.Equal(20, fixture.Service.State.Loot.TotalQuantity);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.False(fixture.Service.State.IsError);
        }
        finally
        {
            release.Set();
            await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        }
        fixture.Service.RefreshPendingState();
        Assert.Equal(LootScrollState.Unknown, fixture.Service.State.LootScroll);
        Assert.Null(fixture.Service.State.TrackingBlockedReason);
    }

    private static void BeginScrollSession(Fixture fixture)
    {
        fixture.Begin();
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
    }

    private static LootScrollReading StoppedScroll() => new(LootScrollStatus.Inactive)
    {
        RemainingTime = TimeSpan.FromHours(6),
        TimerResolution = TimeSpan.FromSeconds(1),
    };

    private static async Task ProcessScrollFrame(Fixture fixture, DateTimeOffset capturedAt, bool canObserveHud = true)
    {
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(1, capturedAt) { CanObserveHud = canObserveHud }, CancellationToken.None);
        fixture.Service.RefreshPendingState();
    }

    private sealed class SessionScrollDetector(Func<LootScrollReading> read) : ILootScrollFrameDetector
    {
        public int Calls { get; private set; }
        public LootScrollReading Analyze(Bitmap frame, CancellationToken cancellationToken)
        {
            Calls++;
            return read();
        }
        public void Dispose() { }
    }

    private sealed class SessionTimer(Func<TimeSpan> remaining) : ILootScrollTimerReader
    {
        public LootScrollTimerReading? Read(Bitmap frame, Rectangle gauge, CancellationToken cancellationToken) =>
            new(remaining(), TimeSpan.FromSeconds(1));
        public void Dispose() { }
    }
}
