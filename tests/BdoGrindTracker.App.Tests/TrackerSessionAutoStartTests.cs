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
    [InlineData("pause")]
    [InlineData("new-session")]
    [InlineData("disable")]
    public async Task OldConfirmationIsDiscardedButOnlyTheToggleDisablesTheNextProbe(string action)
    {
        var monitor = new ControlledAutoStartMonitor(ignoreCancellation: true);
        var next = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true },
            autoStartMonitorFactory: () => ++constructed == 1 ? monitor : next);
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var late = AutoStartFrames(fixture, 1);
        try
        {
            Task stop = action switch
            {
                "disable" => fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoStartGrinding = false }),
                "new-session" => fixture.Service.NewSessionAsync(),
                _ => fixture.Service.PauseAsync(),
            };
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
            if (action == "disable")
            {
                Assert.False(fixture.Settings.Load().AutoStartGrinding);
                Assert.Null(fixture.Service.State.AutoStartStatus);
                Assert.Equal(1, constructed);
                Assert.False(next.Entered.Task.IsCompleted);
            }
            else
            {
                Assert.True(fixture.Settings.Load().AutoStartGrinding);
                await next.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(2, constructed);
                using var fresh = AutoStartFrames(fixture, 1);
                next.Complete(fresh);
                await WaitForAutoStartProbeAsync(fixture);
                await fixture.Service.TickAsync();
                Assert.True(fixture.Service.State.HasSession);
                Assert.True(fixture.Service.State.IsRunning);
                Assert.Empty(fresh.Frames);
            }
        }
        finally { monitor.Complete(null); }
    }

    [Fact]
    public async Task TurningAutoStartBackOnAllowsAFreshProbeWithoutAnotherResumeAction()
    {
        var monitor = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true },
            autoStartMonitorFactory: () => { constructed++; return monitor; });

        Assert.True((await fixture.Service.SavePreferencesAsync(
            fixture.Service.Preferences with { AutoStartGrinding = false })).Succeeded);
        await fixture.Service.TickAsync();
        Assert.Equal(0, constructed);

        Assert.True((await fixture.Service.SavePreferencesAsync(
            fixture.Service.Preferences with { AutoStartGrinding = true })).Succeeded);
        Assert.True(fixture.Settings.Load().AutoStartGrinding);
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, constructed);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.HasSession);
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

        // The bounded replay represents the one detected start event, even
        // when the live reconciler recovers several item rows from it.
        Assert.Empty(fixture.HistoryStore.Load());
        analyzer.Analyze = null;
        for (var i = 0; i < 4; i++)
            await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();

        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        await fixture.Service.TickAsync();
        await next.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.Preferences.AutoStartGrinding);
        Assert.Equal(2, constructed);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(10, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
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
    public async Task LegacySuspensionCannotDisableAutoStartWhenRestoringASession()
    {
        var monitor = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettingsJson: """{"SettingsVersion":7,"AutoStartGrinding":true,"AutoStartSuspended":true}""",
            restoredSession: CurrentSessionStoreTests.Example(),
            autoStartMonitorFactory: () => { constructed++; return monitor; });
        var restoredId = fixture.Service.State.SessionId;
        var restoredLoot = fixture.Service.State.Loot.TotalQuantity;
        await fixture.Service.TickAsync();
        await fixture.Service.TickAsync();

        Assert.True(fixture.Service.Preferences.AutoStartGrinding);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);

        Assert.True(fixture.Settings.Load().AutoStartGrinding);
        Assert.Equal(restoredId, fixture.Service.State.SessionId);
        Assert.Equal(restoredLoot, fixture.Service.State.Loot.TotalQuantity);
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, constructed);
        Assert.False(fixture.Service.State.IsRunning);
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("check")]
    [InlineData("start")]
    public async Task AutoStartFailuresRetryAfterTenSecondsWithoutManualReactivation(string failure)
    {
        var first = new ControlledAutoStartMonitor();
        var retry = new ControlledAutoStartMonitor();
        var constructed = 0;
        var setupCalls = 0;
        var analyzer = new SyntheticAnalyzer
        {
            ValidateSetup = _ =>
            {
                if (failure == "start" && Interlocked.Increment(ref setupCalls) == 1)
                    throw new InvalidOperationException("Synthetic setup failure");
            },
        };
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () =>
            {
                if (++constructed != 1) return retry;
                if (failure == "factory") throw new IOException("Synthetic monitor factory failure");
                return first;
            });

        await fixture.Service.TickAsync();
        if (failure != "factory")
        {
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (failure == "check") first.Fail(new IOException("Synthetic check failure"));
            else first.Complete(AutoStartFrames(fixture, 1));
            await WaitForAutoStartProbeAsync(fixture);
            await fixture.Service.TickAsync();
        }
        Assert.True(fixture.Service.Preferences.AutoStartGrinding);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.HasSession);
        Assert.Equal(1, constructed);

        await fixture.Service.TickAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(9));
        await fixture.Service.TickAsync();
        Assert.Equal(1, constructed);
        Assert.False(retry.Entered.Task.IsCompleted);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await fixture.Service.TickAsync();
        await retry.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, constructed);
        using var fresh = AutoStartFrames(fixture, 1);
        retry.Complete(fresh);
        await WaitForAutoStartProbeAsync(fixture);
        await fixture.Service.TickAsync();

        Assert.True(fixture.Service.State.HasSession);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Settings.Load().AutoStartGrinding);
        Assert.Empty(fresh.Frames);
    }

    [Fact]
    public async Task PausingALiveSessionAllowsAFreshProbeToResumeTheSameSession()
    {
        var monitor = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true },
            autoStartMonitorFactory: () => { constructed++; return monitor; });
        await StartWaitingForDrop(fixture);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 3));
        var sessionId = fixture.Service.State.SessionId;
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.Preferences.AutoStartGrinding);

        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, constructed);
        Assert.Equal(sessionId, fixture.Service.State.SessionId);
        using var fresh = AutoStartFrames(fixture, 1);
        monitor.Complete(fresh);
        await WaitForAutoStartProbeAsync(fixture);
        await fixture.Service.TickAsync();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(sessionId, fixture.Service.State.SessionId);
        Assert.Equal(3, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(sessionId, Assert.Single(fixture.HistoryStore.Load()).SessionId);
        Assert.Empty(fresh.Frames);
    }

    [Fact]
    public async Task ErrorRetryStillWaitsForOldNativeAnalysisAfterTheCooldownExpires()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new ControlledAutoStartMonitor { PendingAnalysis = pending.Task };
        var retry = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true },
            autoStartMonitorFactory: () => ++constructed == 1 ? first : retry);
        try
        {
            await fixture.Service.TickAsync();
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first.Fail(new IOException("Synthetic native analysis timeout"));
            await WaitForAutoStartProbeAsync(fixture);
            await fixture.Service.TickAsync();
            Assert.True(first.Disposed);

            fixture.Time.Advance(TimeSpan.FromSeconds(30));
            await fixture.Service.TickAsync();
            Assert.Equal(1, constructed);
            Assert.False(retry.Entered.Task.IsCompleted);
            Assert.False(fixture.Service.State.IsRunning);

            pending.SetResult();
            var drain = (Task)fixture.Service.GetType().GetField("_autoStartPendingAnalysis",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Service)!;
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Service.TickAsync();
            await retry.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, constructed);
            Assert.True(fixture.Service.Preferences.AutoStartGrinding);
        }
        finally { pending.TrySetResult(); }
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
        public Task PendingAnalysis { get; set; } = Task.CompletedTask;
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
        public void Fail(Exception exception) => _completion.TrySetException(exception);
        public void Dispose() => Disposed = true;
    }
}
