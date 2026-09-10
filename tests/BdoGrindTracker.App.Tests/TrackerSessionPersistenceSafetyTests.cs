using System.Net;
using System.Net.Http;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task SettingsWriteFailureStaysVisibleAcrossSuccessfulActionsAndCanBeRetried()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 10));
        await fixture.Service.PauseAsync();
        var blockedPath = fixture.SettingsPath + ".tmp";
        Directory.CreateDirectory(blockedPath);
        try
        {
            Assert.False((await fixture.Service.SavePreferencesAsync(
                fixture.Service.Preferences with { AutoPauseMinutes = 4 })).Succeeded);
            Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
                "Black Crystal Fragment", 15, 10)).Succeeded);
            Assert.True(fixture.Service.State.IsError);
            Assert.Contains("Einstellungen", fixture.Service.State.PersistenceError);
        }
        finally { Directory.Delete(blockedPath); }
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.PersistenceError);
        Assert.False(fixture.Service.State.IsError);
        Assert.Equal(4, fixture.Settings.Load().AutoPauseMinutes);
    }

    [Fact]
    public async Task FailedPauseAndNewSessionKeepUnsavedLootUntilDurableRetry()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(10), ("Black Crystal Fragment", 100));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        fixture.ResumeClocks();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(10), ("Black Crystal Fragment", 200));
        var blockedPath = Path.Combine(fixture.DirectoryPath, "loot-history-v1.json.tmp");
        Directory.CreateDirectory(blockedPath);
        try
        {
            Assert.False((await fixture.Service.PauseAsync()).Succeeded);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.NotNull(fixture.Service.State.PersistenceError);
            Assert.False((await fixture.Service.NewSessionAsync()).Succeeded);
            Assert.True(fixture.Service.State.HasSession);
            Assert.Equal(300, fixture.Service.State.Loot.TotalQuantity);
            Assert.Equal(100, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        }
        finally { Directory.Delete(blockedPath); }

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.PersistenceError);
        Assert.Equal(300, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.False(fixture.Service.State.HasSession);
    }

    [Fact]
    public async Task FailedShutdownLeavesPausedServiceUsableAndCanBeRetried()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 123));
        var blockedPath = Path.Combine(fixture.DirectoryPath, "loot-history-v1.json.tmp");
        Directory.CreateDirectory(blockedPath);
        try
        {
            await fixture.Service.ShutdownAsync();
            Assert.True(fixture.Service.State.ShutdownFailed);
            Assert.False(fixture.Analyzer.Disposed);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.True(fixture.Service.State.CanEditLoot);
            Assert.Equal(123, fixture.Service.State.Loot.TotalQuantity);
        }
        finally { Directory.Delete(blockedPath); }

        await fixture.Service.ShutdownAsync();
        Assert.False(fixture.Service.State.ShutdownFailed);
        Assert.True(fixture.Analyzer.Disposed);
        Assert.Equal(123, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task CommandOutcomeIsIndependentOfAnExistingOcrFailure()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 10));
        await fixture.Service.PauseAsync();
        fixture.Analyzer.IsAvailable = false;
        var result = await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", 15, 10);
        Assert.True(result.Succeeded);
        Assert.True(fixture.Service.State.IsError);
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(15, saved.Totals["Black Crystal Fragment"]);
        Assert.Contains("Black Crystal Fragment", saved.ManualLootItems);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.False(fixture.Service.State.HasSession);
    }

    [Fact]
    public async Task RunningSessionWritesCheckpointWithoutPausingOrChangingItsClock()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(16), ("Black Crystal Fragment", 10));
        await fixture.Service.TickAsync();
        fixture.AssertTracking(TimeSpan.FromSeconds(16), 10);
        Assert.Equal(10, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();
        Assert.Equal(10, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        await fixture.Service.PauseAsync();
        Assert.Equal(12, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task ManualPauseWorksWhileHourlyHttpIsPending()
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 100));
        await fixture.Service.TickAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        try
        {
            Assert.True(fixture.Service.State.CanPause);
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.Equal(100, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await fixture.Service.ShutdownAsync();
        }
        Assert.True(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
    }

    [Fact]
    public async Task PostUploadCorrectionPersistsLocalDivergence()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 10));
        Assert.True((await fixture.Service.UploadAsync()).Succeeded);
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", 15, 10)).Succeeded);
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.True(saved.GarmothLocallyModified);
        Assert.True(saved.GarmothUploadBlocked);
        Assert.Equal(15, saved.Totals["Black Crystal Fragment"]);
    }
}
