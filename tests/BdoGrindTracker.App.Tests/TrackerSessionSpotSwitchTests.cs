using System.Net;
using System.Net.Http;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentSpotAfterPauseCompletesOldSessionAndReplaysLootIntoANewSession(bool automaticPause)
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true, CharacterClassId = "maegu-awakening" },
            autoStartMonitorFactory: () => monitor);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(20), ("Black Crystal Fragment", 12));
        await fixture.ProcessAfter(TimeSpan.FromSeconds(10), ("Black Crystal Fragment", 3));
        var previousId = fixture.Service.State.SessionId;
        if (automaticPause)
        {
            fixture.Time.Advance(TimeSpan.FromMinutes(3));
            await fixture.Service.TickAsync();
        }
        else Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Service.State.Elapsed);
        fixture.Time.Advance(TimeSpan.FromHours(2));

        var resets = 0;
        fixture.Analyzer.OnReset = () => Interlocked.Increment(ref resets);
        var analyses = 0;
        fixture.Analyzer.Analyze = () => Task.FromResult(
            (Interlocked.Increment(ref analyses) == 1 ? Analysis(("Branch of Abundance", 7)) : Analysis())
            with { SpotId = LootSpotCatalog.AphrodonId });
        fixture.Analyzer.CompletionResult = Analysis() with { SpotId = LootSpotCatalog.AphrodonId };
        await CompleteSpotSwitchProbeAsync(fixture, monitor, LootSpotCatalog.AphrodonId);
        await WaitUntilAsync(() => Volatile.Read(ref analyses) >= 2);
        fixture.Service.RefreshPendingState();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(Volatile.Read(ref resets) > 0);
        Assert.NotEqual(previousId, fixture.Service.State.SessionId);
        Assert.Equal(LootSpotCatalog.AphrodonId, fixture.Service.State.SpotId);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Equal("maegu-awakening", fixture.Service.Preferences.CharacterClassId);
        Assert.Equal(7, fixture.Service.State.Loot.Totals["Branch of Abundance"]);
        Assert.False(fixture.Service.State.Loot.Totals.ContainsKey("Black Crystal Fragment"));
        var completed = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(previousId, completed.SessionId);
        Assert.Equal(LootSpotCatalog.HermesiaId, completed.SpotId);
        Assert.Equal(TimeSpan.FromSeconds(30), completed.Duration);
        Assert.Equal(15, completed.Totals["Black Crystal Fragment"]);
        Assert.False(completed.Totals.ContainsKey("Branch of Abundance"));
        // A new spot uses the ordinary automatic-start probation instead of
        // inheriting confirmation or the recovery checkpoint from the old grind.
        Assert.Null(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load());
        fixture.Time.Advance(TimeSpan.FromSeconds(12));
        await fixture.Service.TickAsync();
        Assert.Equal(TimeSpan.FromSeconds(12), fixture.Service.State.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(fixture.HistoryStore.Load()).Duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(LootSpotCatalog.HermesiaId)]
    public async Task SameOrUnknownSpotAfterPauseContinuesExistingSession(string? detectedSpotId)
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(30), ("Black Crystal Fragment", 15));
        var previousId = fixture.Service.State.SessionId;
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        fixture.Time.Advance(TimeSpan.FromHours(2));
        var resets = 0;
        fixture.Analyzer.OnReset = () => Interlocked.Increment(ref resets);
        var analyses = 0;
        fixture.Analyzer.Analyze = () => Task.FromResult(
            Interlocked.Increment(ref analyses) == 1 ? Analysis(("Black Crystal Fragment", 5)) : Analysis());

        await CompleteSpotSwitchProbeAsync(fixture, monitor, detectedSpotId);
        await WaitUntilAsync(() => Volatile.Read(ref analyses) >= 2);
        fixture.Service.RefreshPendingState();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(0, Volatile.Read(ref resets));
        Assert.Equal(previousId, fixture.Service.State.SessionId);
        Assert.Equal(LootSpotCatalog.HermesiaId, fixture.Service.State.SpotId);
        Assert.Equal(TimeSpan.FromSeconds(30), fixture.Service.State.Elapsed);
        Assert.Equal(20, fixture.Service.State.Loot.TotalQuantity);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(previousId, saved.SessionId);
        Assert.Equal(20, saved.Totals["Black Crystal Fragment"]);
    }

    [Theory]
    [InlineData("dark-energy-floodlands", "dark-energy-floodlands-orbita")]
    [InlineData("dehkia-ash-forest-unspecified", "dehkia-ii-ash-forest")]
    [InlineData("winter-tree-fossil-unspecified", "winter-tree-fossil-280")]
    public async Task SameTrashFamilyAfterPausePreservesSelectedSpotVariant(string familyId, string selectedId)
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        fixture.Begin();
        await ProcessVariantFrame(fixture, familyId);
        var previousId = fixture.Service.State.SessionId;
        Assert.True((await fixture.Service.SelectSpotVariantAsync(previousId, selectedId)).Succeeded);
        fixture.Analyzer.CompletionResult = Analysis() with { SpotId = familyId };
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var analyses = 0;
        fixture.Analyzer.Analyze = () =>
        {
            Interlocked.Increment(ref analyses);
            return Task.FromResult(Analysis() with { SpotId = familyId });
        };

        await CompleteSpotSwitchProbeAsync(fixture, monitor, familyId);
        await WaitUntilAsync(() => Volatile.Read(ref analyses) >= 2);
        fixture.Service.RefreshPendingState();

        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(previousId, fixture.Service.State.SessionId);
        Assert.Equal(selectedId, fixture.Service.State.SpotId);
        Assert.Equal(TimeSpan.FromMinutes(1), fixture.Service.State.Elapsed);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(selectedId, Assert.Single(fixture.HistoryStore.Load()).SpotId);
    }

    [Theory]
    [InlineData("loot-history-v1.json")]
    [InlineData("current-session-v1.json")]
    public async Task SpotSwitchPersistenceFailureKeepsPausedSessionAndItsLoot(string blockedFile)
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new() { AutoStartGrinding = true }, autoStartMonitorFactory: () => monitor);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(30), ("Black Crystal Fragment", 15));
        var previousId = fixture.Service.State.SessionId;
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var blockedPath = Path.Combine(fixture.DirectoryPath, blockedFile + ".tmp");
        Directory.CreateDirectory(blockedPath);
        try
        {
            await CompleteSpotSwitchProbeAsync(fixture, monitor, LootSpotCatalog.AphrodonId);

            Assert.False(fixture.Service.State.IsRunning);
            Assert.True(fixture.Service.State.HasSession);
            Assert.Equal(previousId, fixture.Service.State.SessionId);
            Assert.Equal(LootSpotCatalog.HermesiaId, fixture.Service.State.SpotId);
            Assert.Equal(TimeSpan.FromSeconds(30), fixture.Service.State.Elapsed);
            Assert.Equal(15, fixture.Service.State.Loot.TotalQuantity);
            Assert.Equal(0, fixture.Captures);
            Assert.NotNull(fixture.Service.State.PersistenceError);
            var saved = Assert.Single(fixture.HistoryStore.Load());
            Assert.Equal(previousId, saved.SessionId);
            Assert.Equal(15, saved.Totals["Black Crystal Fragment"]);
            Assert.Equal(previousId, new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
                CurrentSessionStore.FileName)).Load()!.SessionId);
        }
        finally { Directory.Delete(blockedPath); }
    }

    [Fact]
    public async Task SpotSwitchStartsNewCaptureWhileCompletedSessionUploadIsStillPending()
    {
        var monitor = new ControlledAutoStartMonitor();
        await using var fixture = new Fixture(
            initialSettings: new() { AutoStartGrinding = true, GarmothAutoUploadEnabled = true },
            autoStartMonitorFactory: () => monitor);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 100));
        var previousId = fixture.Service.State.SessionId;
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var analyses = 0;
        fixture.Analyzer.Analyze = () => Task.FromResult(
            (Interlocked.Increment(ref analyses) == 1 ? Analysis(("Branch of Abundance", 7)) : Analysis())
            with { SpotId = LootSpotCatalog.AphrodonId });
        fixture.Analyzer.CompletionResult = Analysis() with { SpotId = LootSpotCatalog.AphrodonId };
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var detection = AutoStartFrames(fixture, 1);
        detection.SpotId = LootSpotCatalog.AphrodonId;
        monitor.Complete(detection);
        await WaitForAutoStartProbeAsync(fixture);

        var starting = fixture.Service.TickAsync();
        try
        {
            await WaitUntilAsync(() => fixture.Requests.Count == 1 && Volatile.Read(ref analyses) >= 2);
            fixture.Service.RefreshPendingState();
            await starting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(fixture.Service.State.IsBusy);
            Assert.Contains(previousId, fixture.Service.State.PendingGarmothUploads);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.NotEqual(previousId, fixture.Service.State.SessionId);
            Assert.Equal(LootSpotCatalog.AphrodonId, fixture.Service.State.SpotId);
            Assert.Equal(7, fixture.Service.State.Loot.Totals["Branch of Abundance"]);
            Assert.False(fixture.Service.State.Loot.Totals.ContainsKey("Black Crystal Fragment"));
            Assert.Equal(previousId, Assert.Single(fixture.HistoryStore.Load()).SessionId);
            AssertPayload(Assert.Single(fixture.Requests), 60, 100);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await starting;
            await WaitForAutomaticUploadsAsync(fixture);
        }
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
    }

    private static async Task CompleteSpotSwitchProbeAsync(Fixture fixture, ControlledAutoStartMonitor monitor,
        string? spotId)
    {
        await fixture.Service.TickAsync();
        await monitor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var detection = AutoStartFrames(fixture, 1);
        detection.SpotId = spotId;
        monitor.Complete(detection);
        await WaitForAutoStartProbeAsync(fixture);
        await fixture.Service.TickAsync();
        Assert.Empty(detection.Frames);
    }
}
