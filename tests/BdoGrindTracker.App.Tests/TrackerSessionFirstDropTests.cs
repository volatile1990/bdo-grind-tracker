using System.Reflection;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task TrackingWaitsForANewDropAndExcludesAllInitialWaitingTime()
    {
        await using var fixture = new Fixture(autoUpload: false);
        await StartWaitingForDrop(fixture);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(40));
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.True(fixture.Service.State.CanPause);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);

        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", 2, 0)).Succeeded);
        Assert.True(fixture.Service.State.IsWaitingForFirstDrop);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(20), ("Black Crystal Fragment", 3));
        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Equal(5, fixture.Service.State.Loot.TotalQuantity);

        await fixture.ProcessAfter(TimeSpan.FromSeconds(12));
        await fixture.ProcessAfter(TimeSpan.FromSeconds(8), ("Black Crystal Fragment", 1));
        Assert.Equal(TimeSpan.FromSeconds(20), fixture.Service.State.Elapsed);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(fixture.HistoryStore.Load()).Duration);
    }

    [Fact]
    public async Task ResumePreservesActiveTimeAndWaitsForAnotherNewDrop()
    {
        await using var fixture = new Fixture(autoUpload: false);
        await StartWaitingForDrop(fixture);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(30), ("Black Crystal Fragment", 3));
        fixture.Time.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        fixture.Time.Advance(TimeSpan.FromHours(1));

        await StartWaitingForDrop(fixture);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2));
        Assert.True(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromSeconds(20), fixture.Service.State.Elapsed);
        await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 1));
        await fixture.ProcessAfter(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromSeconds(25), fixture.Service.State.Elapsed);
    }

    [Fact]
    public async Task FirstProjectionArrivalStartsTimeBeforeDelayedQuantityPublication()
    {
        await using var fixture = new Fixture(autoUpload: false);
        await StartWaitingForDrop(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        var arrival = fixture.Time.GetUtcNow();
        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 1, 4, 1, arrival);
        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Equal(0, fixture.Service.State.Loot.TotalQuantity);

        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 1, 4, 1, arrival);
        Assert.Equal(TimeSpan.FromSeconds(2), fixture.Service.State.Elapsed);
        Assert.Equal(4, fixture.Service.State.Loot.TotalQuantity);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        await StartWaitingForDrop(fixture);

        // Re-reading the same projection on resume must not start another segment.
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(45), 1, 4, 1, arrival);
        Assert.True(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromSeconds(2), fixture.Service.State.Elapsed);
        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 2, 8, 2, fixture.Time.GetUtcNow());
        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 2, 8, 2, fixture.Time.GetUtcNow().AddSeconds(-2));
        Assert.Equal(TimeSpan.FromSeconds(4), fixture.Service.State.Elapsed);
    }

    [Fact]
    public async Task AutomaticPauseStillStopsCaptureWhenNoFirstDropArrives()
    {
        await using var fixture = new Fixture(autoUpload: false);
        await StartWaitingForDrop(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.False(fixture.Clock.IsRunning);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Empty(fixture.HistoryStore.Load());
        var saved = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load();
        Assert.NotNull(saved);
        Assert.Equal(TimeSpan.Zero, saved.Duration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstDropFinishingDuringPauseOrShutdownCannotRestartTheClock(bool shutdown)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<FrameAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new SyntheticAnalyzer { Analyze = () => { entered.TrySetResult(); return complete.Task; } };
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer);
        try
        {
            Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Time.Advance(TimeSpan.FromSeconds(30));
            Task stopped = shutdown ? fixture.Service.ShutdownAsync() : fixture.Service.PauseAsync();
            Assert.False(fixture.Clock.IsRunning);
            Assert.False(fixture.Clock.IsWaitingForFirstDrop);
            fixture.Time.Advance(TimeSpan.FromSeconds(20));
            complete.SetResult(Analysis(("Black Crystal Fragment", 3)));
            await stopped.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(fixture.Clock.IsRunning);
            Assert.Equal(TimeSpan.Zero, fixture.Clock.Elapsed);
            var saved = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load();
            Assert.NotNull(saved);
            Assert.Equal(3, saved.Totals["Black Crystal Fragment"]);
            Assert.Equal(TimeSpan.Zero, saved.Duration);
        }
        finally { complete.TrySetResult(Analysis()); }
    }

    [Fact]
    public async Task RestoredSessionWaitsForFreshLootWithoutChangingItsSavedDuration()
    {
        var saved = CurrentSessionStoreTests.Example();
        await using var fixture = new Fixture(autoUpload: false, restoredSession: saved);
        await StartWaitingForDrop(fixture);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2));
        Assert.True(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(saved.Duration, fixture.Service.State.Elapsed);
        await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 3));
        await fixture.ProcessAfter(TimeSpan.FromSeconds(10));
        Assert.Equal(saved.Duration + TimeSpan.FromSeconds(10), fixture.Service.State.Elapsed);
    }

    [Fact]
    public async Task InitialWaitingTimeCannotTriggerAnEarlyHourlyUpload()
    {
        await using var fixture = new Fixture();
        Assert.True((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
            { CharacterClassId = "warrior-awakening" })).Succeeded);
        await StartWaitingForDrop(fixture);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 3));
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();
        Assert.Equal(TimeSpan.FromMinutes(59), fixture.Service.State.Elapsed);
        Assert.Empty(fixture.Requests);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        Assert.Equal(TimeSpan.FromHours(1), fixture.Service.State.Elapsed);
    }

    private static async Task StartWaitingForDrop(Fixture fixture)
    {
        var previousCalls = fixture.Analyzer.Calls;
        var capturedAtField = fixture.Service.GetType().GetField("_lastProcessedCaptureAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousCapture = capturedAtField.GetValue(fixture.Service);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await WaitUntilAsync(() => fixture.Analyzer.Calls > previousCalls &&
            capturedAtField.GetValue(fixture.Service) is { } capturedAt && !capturedAt.Equals(previousCapture));
        fixture.Service.RefreshPendingState();
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.IsWaitingForFirstDrop);
    }
}
