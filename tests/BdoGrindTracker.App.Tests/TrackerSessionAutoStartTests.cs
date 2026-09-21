using System.Reflection;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task AutoStartIsOptInAndIdleMonitoringDoesNotCreateASessionOrRunLiveOcr()
    {
        var monitor = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            autoStartMonitorFactory: () => { constructed++; return monitor; });

        await fixture.Service.TickAsync();
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.Preferences.AutoStartGrinding);
        Assert.Equal(0, constructed);
        Assert.Equal(0, fixture.Captures);
        Assert.Equal(0, fixture.Analyzer.Calls);

        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoStartGrinding = true });
        Assert.True(saved.Succeeded);
        Assert.True(fixture.Settings.Load().AutoStartGrinding);
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, constructed);
        Assert.Equal("en", monitor.Language);
        Assert.False(fixture.Service.State.HasSession);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);
        Assert.Equal(0, fixture.Analyzer.Calls);
        Assert.NotNull(fixture.Service.State.AutoStartStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoStartLateConfirmationCannotStartAfterManualPauseOrDisabling(bool disable)
    {
        var monitor = new ControlledAutoStartMonitor(ignoreCancellation: true);
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var late = AutoStartFrames(fixture, 1);
        try
        {
            Task stop = disable
                ? fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoStartGrinding = false })
                : fixture.Service.PauseAsync();
            await monitor.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            monitor.Complete(late);
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Service.TickAsync();
            await fixture.Service.TickAsync();

            Assert.True(monitor.Disposed);
            Assert.Empty(late.Frames);
            Assert.False(fixture.Service.State.HasSession);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.Equal(0, fixture.Captures);
            Assert.Equal(0, fixture.Analyzer.Calls);
            if (disable)
            {
                Assert.False(fixture.Settings.Load().AutoStartGrinding);
                Assert.Null(fixture.Service.State.AutoStartStatus);
            }
            else Assert.True(fixture.Service.State.AutoStartSuspended);
        }
        finally { monitor.Complete(null); }
    }

    [Theory]
    [InlineData("rearm")]
    [InlineData("new-session")]
    [InlineData("manual-start")]
    public async Task AutoStartManualSuspensionRequiresAnExplicitResumeAction(string action)
    {
        var monitor = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true },
            autoStartMonitorFactory: () => { constructed++; return monitor; });

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True(fixture.Settings.Load().AutoStartSuspended);
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.AutoStartSuspended);
        Assert.Equal(0, constructed);

        var resumed = action switch
        {
            "new-session" => await fixture.Service.NewSessionAsync(),
            "manual-start" => await fixture.Service.ToggleTrackingAsync(),
            _ => await fixture.Service.RearmAutoStartAsync(),
        };
        Assert.True(resumed.Succeeded);
        Assert.False(fixture.Service.State.AutoStartSuspended);
        Assert.False(fixture.Settings.Load().AutoStartSuspended);
        await fixture.Service.TickAsync();
        if (action == "manual-start")
        {
            Assert.True(fixture.Service.State.IsRunning);
            Assert.Equal(0, constructed);
        }
        else
        {
            await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, constructed);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.False(fixture.Service.State.HasSession);
        }
    }

    [Fact]
    public async Task AutoStartConfirmationReplaysOnlyTheBoundedBufferOnceAndAutomaticPauseRearms()
    {
        var first = new ControlledAutoStartMonitor();
        var next = new ControlledAutoStartMonitor();
        var constructed = 0;
        var results = 0;
        var analyzer = new SyntheticAnalyzer
        {
            Analyze = () => Task.FromResult(Interlocked.Increment(ref results) <= 3
                ? Analysis(("Black Crystal Fragment", 2)) : Analysis()),
        };
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer,
            initialSettings: new() { AutoStartGrinding = true },
            autoStartMonitorFactory: () => ++constructed == 1 ? first : next);
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        await fixture.Service.TickAsync();
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var detection = AutoStartFrames(fixture, 5);
        Assert.Equal(3, detection.Frames.Count);
        first.Complete(detection);
        await WaitForAutoStartProbeAsync(fixture);
        await fixture.Service.TickAsync();
        await WaitUntilAsync(() => fixture.Analyzer.Calls >= 4);
        fixture.Service.RefreshPendingState();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.HasSession);
        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.True(first.Disposed);
        Assert.Empty(detection.Frames);
        Assert.Equal(6, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(3, fixture.Service.State.Loot.ConfirmedEventCount);
        Assert.Equal(1, fixture.Captures);
        await fixture.Service.TickAsync();
        await fixture.Service.TickAsync();
        Assert.Equal(6, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(4, fixture.Analyzer.Calls);
        Assert.Equal(1, constructed);

        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        await fixture.Service.TickAsync();
        await next.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.AutoStartSuspended);
        Assert.Equal(2, constructed);
        Assert.Equal(6, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(6, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task AutoStartConfirmationLosingForegroundCannotStartTracking()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var detection = AutoStartFrames(fixture, 1);
        monitor.IsGameForeground = false;
        monitor.Complete(detection);
        await WaitForAutoStartProbeAsync(fixture);
        await fixture.Service.TickAsync();

        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.HasSession);
        Assert.Empty(detection.Frames);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task AutoStartRestoresManualSuspensionAndExplicitRearmPersistsAcrossRestarts()
    {
        var monitor = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true, AutoStartSuspended = true },
            restoredSession: CurrentSessionStoreTests.Example(),
            autoStartMonitorFactory: () => { constructed++; return monitor; });
        var restoredId = fixture.Service.State.SessionId;
        var restoredLoot = fixture.Service.State.Loot.TotalQuantity;
        await fixture.Service.TickAsync();
        await fixture.Service.TickAsync();

        Assert.True(fixture.Service.Preferences.AutoStartGrinding);
        Assert.True(fixture.Service.State.AutoStartSuspended);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, constructed);
        Assert.Equal(0, fixture.Captures);

        Assert.True((await fixture.Service.RearmAutoStartAsync()).Succeeded);
        Assert.False(fixture.Settings.Load().AutoStartSuspended);
        Assert.True(fixture.Settings.Load().AutoStartGrinding);
        Assert.Equal(restoredId, fixture.Service.State.SessionId);
        Assert.Equal(restoredLoot, fixture.Service.State.Loot.TotalQuantity);
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, constructed);
        Assert.False(fixture.Service.State.IsRunning);
    }

    private static AutoStartDetection AutoStartFrames(Fixture fixture, int count)
    {
        var detection = new AutoStartDetection();
        for (var i = 0; i < count; i++)
            detection.Add(new CapturedDesktopBitmap(new Bitmap(1920, 1080), false),
                fixture.Time.GetUtcNow().AddSeconds(i - count + 1));
        return detection;
    }

    private static Task WaitForAutoStartProbeAsync(Fixture fixture) => WaitUntilAsync(() =>
        typeof(BdoGrindTracker.App.Services.TrackerSessionService)
            .GetField("_autoStartProbe", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Service) is Task { IsCompleted: true });

    private sealed class ControlledAutoStartMonitor(bool ignoreCancellation = false) : IAutomaticGrindMonitor
    {
        private readonly TaskCompletionSource<AutoStartDetection?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _checks;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsGameForeground { get; set; } = true;
        public bool IsConfirming => true;
        public bool Disposed { get; private set; }
        public string? Language { get; private set; }

        public async Task<AutoStartDetection?> CheckAsync(string language, CancellationToken cancellationToken)
        {
            Language = language;
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult());
            Entered.TrySetResult();
            if (Interlocked.Increment(ref _checks) != 1)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }
            return ignoreCancellation
                ? await _completion.Task
                : await _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(AutoStartDetection? detection) => _completion.TrySetResult(detection);
        public void Dispose() => Disposed = true;
    }
}
