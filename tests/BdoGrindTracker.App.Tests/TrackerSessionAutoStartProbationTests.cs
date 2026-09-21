using System.Reflection;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task AutoStartFirstDetectedDropStartsTheClockBeforeLiveLootIsPublished()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);

        await StartProvisionalAutomaticGrind(fixture, monitor, replayLoot: false);

        Assert.True(fixture.Service.State.HasSession);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.True(fixture.Clock.IsRunning);
        Assert.Equal(0, fixture.Service.State.Loot.TotalQuantity);
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await fixture.Service.TickAsync();
        Assert.Equal(TimeSpan.FromSeconds(15), fixture.Service.State.Elapsed);
        AssertAutomaticGrindHasNotBeenSaved(fixture);
    }

    [Fact]
    public async Task AutoStartPersistsOnlyAfterFiveSeparateDropArrivals()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await StartProvisionalAutomaticGrind(fixture, monitor);

        for (var i = 0; i < 3; i++)
        {
            await fixture.ProcessAfter(TimeSpan.FromSeconds(10), ("Black Crystal Fragment", 1));
            await fixture.Service.TickAsync();
            AssertAutomaticGrindHasNotBeenSaved(fixture);
        }

        await fixture.ProcessAfter(TimeSpan.FromSeconds(10), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();
        Assert.Equal(5, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(5, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        Assert.Equal(5, new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
            CurrentSessionStore.FileName)).Load()!.Totals["Black Crystal Fragment"]);

        // Confirmed sessions use the ordinary three-minute inactivity rule.
        fixture.Time.Advance(TimeSpan.FromSeconds(60));
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(5, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task AutoStartUsesDetectedArrivalInsteadOfTheLaterReplayCaptureForConfirmation()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await StartProvisionalAutomaticGrind(fixture, monitor, replayLoot: false,
            detectedDropAge: TimeSpan.FromSeconds(2));

        // OCR can publish a second genuine arrival after the trigger but before
        // the timestamp of the most recent buffered screenshot.
        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 1, 1, 1,
            fixture.Time.GetUtcNow().AddSeconds(-1));
        for (var revision = 2; revision <= 4; revision++)
            await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(1), revision,
                revision, revision, fixture.Time.GetUtcNow().AddSeconds(1));
        await fixture.Service.TickAsync();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.NotNull(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
            CurrentSessionStore.FileName)).Load());
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(4, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task AutoStartDiscardsExactlySixtySecondsAfterTheLatestRealDropAndRearms()
    {
        var first = new ControlledAutoStartMonitor();
        var next = new ControlledAutoStartMonitor();
        var constructed = 0;
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true },
            autoStartMonitorFactory: () => ++constructed == 1 ? first : next);
        await StartProvisionalAutomaticGrind(fixture, first);
        var discardedId = fixture.Service.State.SessionId;

        await fixture.ProcessAfter(TimeSpan.FromSeconds(59), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(59));
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.IsRunning);
        AssertAutomaticGrindHasNotBeenSaved(fixture);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await fixture.Service.TickAsync();
        await next.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Service.State.HasSession);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.Preferences.AutoStartGrinding);
        Assert.Empty(fixture.Service.State.Loot.Totals);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.NotEqual(discardedId, fixture.Service.State.SessionId);
        Assert.Equal(2, constructed);
        AssertAutomaticGrindHasNotBeenSaved(fixture);
    }

    [Fact]
    public async Task AutoStartCountsMultipleItemsInOneFrameAsOneArrival()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await StartProvisionalAutomaticGrind(fixture, monitor);

        await fixture.ProcessAfter(TimeSpan.FromSeconds(10),
            ("Black Crystal Fragment", 1), ("Black Crystal Fragment", 2),
            ("Black Crystal Fragment", 3), ("Black Crystal Fragment", 4),
            ("Black Crystal Fragment", 5));
        await fixture.Service.TickAsync();
        Assert.Equal(16, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(6, fixture.Service.State.Loot.ConfirmedEventCount);
        AssertAutomaticGrindHasNotBeenSaved(fixture);

        fixture.Time.Advance(TimeSpan.FromSeconds(60));
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.State.HasSession);
        AssertAutomaticGrindHasNotBeenSaved(fixture);
    }

    [Fact]
    public async Task AutoStartQuantityCorrectionsNeitherConfirmNorExtendTheAttempt()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await StartProvisionalAutomaticGrind(fixture, monitor, replayLoot: false);
        var arrival = fixture.Time.GetUtcNow();
        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 1, 1, 1, arrival, 0);

        // The count and arrival remain unchanged while OCR refines quantities.
        for (var revision = 2; revision <= 5; revision++)
        {
            await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(10), revision,
                revision * 10, 1, arrival, revision - 1);
            await fixture.Service.TickAsync();
        }
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", 100, fixture.Service.State.Loot.TotalQuantity)).Succeeded);
        AssertAutomaticGrindHasNotBeenSaved(fixture);
        fixture.Time.Advance(TimeSpan.FromSeconds(19));
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.IsRunning);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.State.HasSession);
        AssertAutomaticGrindHasNotBeenSaved(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoStartTimeoutDrainsFreshLootAndContinuesTheSameSession(bool fifthDrop)
    {
        var monitor = new ControlledAutoStartMonitor();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<FrameAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replayCount = fifthDrop ? 3 : 1;
        var calls = 0;
        var analyzer = new SyntheticAnalyzer
        {
            Analyze = () =>
            {
                if (Interlocked.Increment(ref calls) <= replayCount)
                    return Task.FromResult(Analysis(("Black Crystal Fragment", 1)));
                entered.TrySetResult();
                return completion.Task;
            },
        };
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        try
        {
            fixture.Time.Advance(TimeSpan.FromSeconds(10));
            await fixture.Service.TickAsync();
            await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var detection = AutoStartFrames(fixture, replayCount);
            // The trigger can precede the retained buffer. In that case its
            // three later arrivals bring confirmation to four before live OCR.
            if (fifthDrop) detection.DetectedDropAt = fixture.Time.GetUtcNow().AddSeconds(-3);
            monitor.Complete(detection);
            await WaitForAutoStartProbeAsync(fixture);
            await fixture.Service.TickAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Service.RefreshPendingState();
            var sessionId = fixture.Service.State.SessionId;
            AssertAutomaticGrindHasNotBeenSaved(fixture);
            analyzer.Analyze = null;

            fixture.Time.Advance(TimeSpan.FromSeconds(60));
            var timeout = fixture.Service.TickAsync();
            Assert.False(timeout.IsCompleted);
            completion.SetResult(Analysis(("Black Crystal Fragment", 1)));
            await timeout.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(fixture.Service.State.IsRunning);
            Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
            Assert.Equal(sessionId, fixture.Service.State.SessionId);
            Assert.Equal(replayCount + 1, fixture.Service.State.Loot.TotalQuantity);
            Assert.True(fixture.Service.Preferences.AutoStartGrinding);
            if (fifthDrop)
            {
                Assert.Equal(replayCount + 1,
                    Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
                Assert.NotNull(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
                    CurrentSessionStore.FileName)).Load());
            }
            else AssertAutomaticGrindHasNotBeenSaved(fixture);
        }
        finally { completion.TrySetResult(Analysis()); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoStartUnconfirmedAttemptIsNotSavedByPauseOrShutdown(bool shutdown)
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await StartProvisionalAutomaticGrind(fixture, monitor);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(20), ("Black Crystal Fragment", 1));

        if (shutdown) await fixture.Service.ShutdownAsync();
        else Assert.True((await fixture.Service.PauseAsync()).Succeeded);

        Assert.False(fixture.Service.State.IsRunning);
        AssertAutomaticGrindHasNotBeenSaved(fixture);
    }

    [Fact]
    public async Task AutoStartUnconfirmedAttemptCannotUploadEvenWithAFullMinuteOfLoot()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(initialSettings: new()
        {
            AutoStartGrinding = true, GarmothAutoUploadEnabled = true,
            CharacterClassId = "warrior-awakening",
        }, autoStartMonitorFactory: () => monitor);
        await StartProvisionalAutomaticGrind(fixture, monitor);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(40), ("Black Crystal Fragment", 1));
        await fixture.ProcessAfter(TimeSpan.FromSeconds(40), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();

        Assert.False((await fixture.Service.UploadAsync()).Succeeded);
        Assert.True(fixture.Service.State.IsRunning);
        AssertAutomaticGrindHasNotBeenSaved(fixture);
    }

    [Fact]
    public async Task AutoStartPreferenceDoesNotMakeAManuallyStartedSessionProvisional()
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true });
        await StartWaitingForDrop(fixture);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 1));
        fixture.Time.Advance(TimeSpan.FromSeconds(60));
        await fixture.Service.TickAsync();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(1, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        Assert.NotNull(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
            CurrentSessionStore.FileName)).Load());
    }

    [Fact]
    public async Task AutoStartResumingAnExistingSessionDoesNotDiscardItAfterSixtySeconds()
    {
        var monitor = new ControlledAutoStartMonitor();
        var saved = CurrentSessionStoreTests.Example();
        await using var fixture = new Fixture(autoUpload: false, restoredSession: saved,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        await StartProvisionalAutomaticGrind(fixture, monitor, replayLoot: false);
        fixture.Time.Advance(TimeSpan.FromSeconds(60));
        await fixture.Service.TickAsync();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(saved.SessionId, fixture.Service.State.SessionId);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(saved.SessionId, Assert.Single(fixture.HistoryStore.Load()).SessionId);
        Assert.NotNull(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
            CurrentSessionStore.FileName)).Load());
    }

    private static async Task StartProvisionalAutomaticGrind(Fixture fixture,
        ControlledAutoStartMonitor monitor, bool replayLoot = true, TimeSpan? detectedDropAge = null)
    {
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (replayLoot) fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 1));
        using var detection = AutoStartFrames(fixture, 1);
        if (detectedDropAge is { } age) detection.DetectedDropAt = fixture.Time.GetUtcNow() - age;
        monitor.Complete(detection);
        await WaitForAutoStartProbeAsync(fixture);
        await fixture.Service.TickAsync();

        // Wait until both the replay and the first ordinary capture have been
        // published before injecting additional frames through ProcessAfter.
        var lastCapture = fixture.Service.GetType().GetField("_lastProcessedCaptureAt",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await WaitUntilAsync(() => fixture.Analyzer.Calls >= 2 &&
            lastCapture.GetValue(fixture.Service) is DateTimeOffset at && at > fixture.Time.GetUtcNow());
        fixture.Service.RefreshPendingState();
        Assert.True(fixture.Service.State.IsRunning);
    }

    private static void AssertAutomaticGrindHasNotBeenSaved(Fixture fixture)
    {
        Assert.Empty(fixture.Service.History);
        Assert.Empty(fixture.HistoryStore.Load());
        Assert.Null(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
            CurrentSessionStore.FileName)).Load());
        Assert.Empty(fixture.Requests);
    }
}
