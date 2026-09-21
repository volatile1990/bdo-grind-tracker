using System.Drawing;
using System.Reflection;
using System.Net;
using System.Net.Http;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionRestoreTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, -10)]
    [InlineData(true, 10)]
    public async Task RestoreKeepsNewestConfirmedStatsFromTheSameSessionWithoutClaimingLiveObservation(
        bool historyHasStats, int historySampleOffsetSeconds)
    {
        using var directory = new TestDirectory();
        var original = CurrentSessionStoreTests.Example();
        var checkpointStats = new CombatStatsState(2401, 841, CombatStatsCategory.Edania,
            original.UpdatedAt.AddSeconds(-20));
        original = original with { CombatStats = checkpointStats };
        var historyStats = historyHasStats
            ? new CombatStatsState(1560, 740, CombatStatsCategory.General,
                checkpointStats.ObservedAt!.Value.AddSeconds(historySampleOffsetSeconds)) : null;
        var history = new LootHistoryEntry
        {
            SessionId = original.SessionId, StartedAt = original.StartedAt!.Value,
            UpdatedAt = original.UpdatedAt.AddMinutes(1), Duration = original.Duration,
            SpotId = original.SpotId!, Totals = new(original.Totals), CombatStats = historyStats,
            SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
        };
        // A newer, unrelated session must never supply a restored session's AP/DP.
        var unrelated = history with
        {
            SessionId = Guid.NewGuid(), UpdatedAt = history.UpdatedAt.AddHours(1),
            CombatStats = new(999, 999, CombatStatsCategory.General, history.UpdatedAt),
        };
        new CurrentSessionStore(directory.CurrentPath).Save(original);
        new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json")).Save([history, unrelated]);

        await using var fixture = new Fixture(directory.Path);
        var expected = historyHasStats && historySampleOffsetSeconds > 0 ? historyStats : checkpointStats;
        Assert.Equal(expected, fixture.Service.State.SessionCombatStats);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        Assert.Equal(expected, new CurrentSessionStore(directory.CurrentPath).Load()!.CombatStats);
        Assert.Equal(expected, fixture.History.Load().Single(entry => entry.SessionId == original.SessionId).CombatStats);
        Assert.Equal(unrelated.CombatStats, fixture.History.Load().Single(entry => entry.SessionId == unrelated.SessionId).CombatStats);
    }

    [Fact]
    public async Task LegacyCheckpointNeverBorrowsCombatStatsFromAnotherHistoricalSession()
    {
        using var directory = new TestDirectory();
        var original = CurrentSessionStoreTests.Example();
        new CurrentSessionStore(directory.CurrentPath).Save(original);
        var unrelated = new LootHistoryEntry
        {
            SessionId = Guid.NewGuid(), StartedAt = original.StartedAt!.Value,
            UpdatedAt = original.UpdatedAt.AddHours(1), Duration = original.Duration,
            SpotId = original.SpotId!, Totals = new(original.Totals),
            CombatStats = new(2401, 841, CombatStatsCategory.Edania, original.UpdatedAt),
            SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
        };
        new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json")).Save([unrelated]);

        await using var fixture = new Fixture(directory.Path);
        Assert.Null(fixture.Service.State.SessionCombatStats);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        Assert.Null(new CurrentSessionStore(directory.CurrentPath).Load()!.CombatStats);
        Assert.Null(fixture.History.Load().Single(entry => entry.SessionId == original.SessionId).CombatStats);
        Assert.Equal(unrelated.CombatStats, fixture.History.Load().Single(entry => entry.SessionId == unrelated.SessionId).CombatStats);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Edania, CombatStatsCategory.General, true)]
    [InlineData(CombatStatsCategory.Edania, CombatStatsCategory.Demihuman, true)]
    [InlineData(CombatStatsCategory.General, CombatStatsCategory.Demihuman, false)]
    public async Task CorrectedSpotSelectsNewestCompatibleSampleBeforeComparingTimestamps(
        CombatStatsCategory checkpointCategory, CombatStatsCategory historyCategory, bool expectHistory)
    {
        using var directory = new TestDirectory();
        var original = CurrentSessionStoreTests.Example();
        var checkpointStats = new CombatStatsState(2401, 841, checkpointCategory, original.UpdatedAt.AddSeconds(-20));
        original = original with { CombatStats = checkpointStats };
        var historyStats = new CombatStatsState(1560, 740, historyCategory, original.UpdatedAt.AddSeconds(-30));
        var history = new LootHistoryEntry
        {
            SessionId = original.SessionId, StartedAt = original.StartedAt!.Value,
            UpdatedAt = original.UpdatedAt.AddMinutes(1), Duration = original.Duration,
            SpotId = "tungrad-ruins", Totals = new(original.Totals), CombatStats = historyStats,
            SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
        };
        new CurrentSessionStore(directory.CurrentPath).Save(original);
        new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json")).Save([history]);

        await using var fixture = new Fixture(directory.Path);
        var expected = expectHistory ? historyStats : checkpointStats;
        Assert.Equal("tungrad-ruins", fixture.Service.State.SpotId);
        Assert.Equal(expected, fixture.Service.State.SessionCombatStats);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Equal(original.Totals, fixture.Service.State.Loot.Totals);
        Assert.Equal(0, fixture.Captures);

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        Assert.Equal(expected, new CurrentSessionStore(directory.CurrentPath).Load()!.CombatStats);
        Assert.Equal(expected, Assert.Single(fixture.History.Load()).CombatStats);
        Assert.Equal(original.Totals, Assert.Single(fixture.History.Load()).Totals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadSettingsStayUnchangedAndDoNotPreventClosingWhenPreferenceChangesWereRejected(bool locked)
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var content = locked ? "{\"AutoPauseMinutes\":12}" : "broken settings";
        File.WriteAllText(path, content);
        Fixture fixture;
        using (var fileLock = locked ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
            fixture = new Fixture(directory.Path);
        await using (fixture)
        {
            var previous = fixture.Service.Preferences;
            Assert.NotNull(fixture.Service.State.PersistenceError);
            Assert.False((await fixture.Service.SavePreferencesAsync(previous with
            {
                AutoPauseMinutes = 9,
                FavoriteItems = ["Black Stone"],
                LootColumnOrders = new Dictionary<string, string[]> { [LootSpotCatalog.HermesiaId] = ["Black Stone"] },
            })).Succeeded);
            Assert.Same(previous, fixture.Service.Preferences);

            await fixture.Service.ShutdownAsync();

            Assert.False(fixture.Service.State.ShutdownFailed);
            Assert.True(fixture.Analyzer.Disposed);
            Assert.Equal(content, File.ReadAllText(path));
            Assert.False(File.Exists(path + ".bak"));
        }
    }

    [Fact]
    public async Task LockedSettingsStayProtectedAndExplicitRetryReloadsPreferencesWithoutRestart()
    {
        using var directory = new TestDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(settingsPath, "{\"AutoPauseMinutes\":12,\"MarketRegion\":\"na\",\"FavoriteItems\":[\"Black Stone\"]}");
        Fixture fixture;
        using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
            fixture = new Fixture(directory.Path);
        await using (fixture)
        {
            Assert.Contains("Einstellungen", fixture.Service.State.PersistenceError);
            Assert.False((await fixture.Service.SavePreferencesAsync(
                fixture.Service.Preferences with { AutoPauseMinutes = 4 }, "synthetic-key")).Succeeded);
            Assert.Contains("12", File.ReadAllText(settingsPath));
            Assert.False(File.Exists(Path.Combine(directory.Path, "test-key.dpapi")));
            Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
            Assert.Equal(12, fixture.Service.Preferences.AutoPauseMinutes);
            Assert.Equal("na", fixture.Service.Preferences.MarketRegion);
            Assert.Contains("Black Stone", fixture.Service.Preferences.FavoriteItems);
            Assert.Null(fixture.Service.State.PersistenceError);
            Assert.False(fixture.Service.State.HasSession);
            Assert.Equal(0, fixture.Captures);
        }
    }

    [Fact]
    public async Task RepairedCheckpointCanBeReloadedWithoutRestartAndDoesNotLoseNewerHistory()
    {
        using var directory = new TestDirectory();
        var original = CurrentSessionStoreTests.Example();
        var history = new LootHistoryEntry
        {
            SessionId = original.SessionId, StartedAt = original.StartedAt!.Value,
            UpdatedAt = original.UpdatedAt.AddMinutes(1), Duration = TimeSpan.FromMinutes(3),
            SpotId = original.SpotId!, CharacterClass = CompanionCharacterClassCatalog.FindById("maegu-awakening")!.DisplayName,
            Totals = new() { [Item] = 35 }, SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
        };
        new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json")).Save([history]);
        File.WriteAllText(directory.CurrentPath, "broken checkpoint");
        await using var fixture = new Fixture(directory.Path);
        Assert.False((await fixture.Service.SaveSessionAsync()).Succeeded);
        Assert.False((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        Assert.False((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Equal("broken checkpoint", File.ReadAllText(directory.CurrentPath));
        new CurrentSessionStore(directory.CurrentPath).Save(original);

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        Assert.Equal(original.SessionId, fixture.Service.State.SessionId);
        Assert.Equal(35, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(TimeSpan.FromMinutes(3), fixture.Service.State.Elapsed);
        Assert.Equal("maegu-awakening", fixture.Service.State.CharacterClassId);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Null(fixture.Service.State.PersistenceError);
        Assert.Equal(35, Assert.Single(fixture.History.Load()).Totals[Item]);
        Assert.Equal(35, new CurrentSessionStore(directory.CurrentPath).Load()!.Totals[Item]);
        Assert.Equal("maegu-awakening", new CurrentSessionStore(directory.CurrentPath).Load()!.CharacterClassId);
        Assert.False((await fixture.Service.UploadAsync()).Succeeded);
        Assert.Equal(0, fixture.Requests);
    }

    [Fact]
    public async Task TransientCurrentReadLockCanRecoverExactSessionAfterUnlock()
    {
        using var directory = new TestDirectory();
        var original = CurrentSessionStoreTests.Example();
        new CurrentSessionStore(directory.CurrentPath).Save(original);
        Fixture fixture;
        using (var locked = new FileStream(directory.CurrentPath, FileMode.Open, FileAccess.Read, FileShare.None))
            fixture = new Fixture(directory.Path);
        await using (fixture)
        {
            Assert.False(fixture.Service.State.HasSession);
            Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
            Assert.Equal(original.SessionId, fixture.Service.State.SessionId);
            Assert.Equal(original.Totals, fixture.Service.State.Loot.Totals);
            Assert.Null(fixture.Service.State.PersistenceError);
        }
    }

    [Fact]
    public async Task ClosingRunningSessionRestoresSameSessionPausedWithoutCapturingOrCountingOfflineTime()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        first.Begin();
        await first.ProcessProjection(TimeSpan.FromMinutes(2), 1, 12);
        var id = first.Service.State.SessionId;
        await first.Service.ShutdownAsync();

        await using var second = new Fixture(directory.Path);
        Assert.True(second.Service.State.HasSession);
        Assert.False(second.Service.State.IsRunning);
        Assert.Equal(id, second.Service.State.SessionId);
        Assert.Equal(12, second.Service.State.Loot.Totals[Item]);
        Assert.Equal("warrior-awakening", second.Service.State.CharacterClassId);
        Assert.Equal(LootSpotCatalog.HermesiaId, second.Service.State.SpotId);
        Assert.Equal(TimeSpan.FromMinutes(2), second.Service.State.Elapsed);
        second.Time.Advance(TimeSpan.FromHours(8));
        await second.Service.TickAsync();
        Assert.Equal(TimeSpan.FromMinutes(2), second.Service.State.Elapsed);
        Assert.False(second.Clock.IsRunning);
        Assert.Equal(0, second.Captures);
        Assert.Equal(id, Assert.Single(second.History.Load()).SessionId);
    }

    [Fact]
    public async Task NewAnalyzerProjectionAddsToRestoredTotalsAndKeepsManualCorrection()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        first.Begin();
        await first.ProcessProjection(TimeSpan.FromMinutes(2), 11, 12);
        await first.Service.PauseAsync();
        await first.Service.UpdateLootQuantityAsync(first.Service.State.SessionId, Item, 10, 12);
        await first.Service.ShutdownAsync();

        await using var second = new Fixture(directory.Path);
        Assert.Equal(10, second.Service.State.Loot.Totals[Item]);
        Assert.Contains(Item, second.Service.State.ManualLootItems);
        second.ResumeSynthetic();
        await second.ProcessProjection(TimeSpan.FromSeconds(10), 1, 4);
        Assert.Equal(14, second.Service.State.Loot.Totals[Item]);
        Assert.Equal(TimeSpan.FromSeconds(130), second.Service.State.Elapsed);
        await second.Service.PauseAsync();
        var saved = Assert.Single(second.History.Load());
        Assert.Equal(14, saved.Totals[Item]);
        Assert.Equal(first.Service.State.SessionId, saved.SessionId);
    }

    [Fact]
    public async Task StartingRestoredSessionCreatesFreshCaptureSetupWithoutStartingItAtLaunch()
    {
        using var directory = new TestDirectory();
        var snapshot = CurrentSessionStoreTests.Example();
        new CurrentSessionStore(directory.CurrentPath).Save(snapshot);
        await using var fixture = new Fixture(directory.Path);
        Assert.Equal(0, fixture.Captures);
        var result = await fixture.Service.ToggleTrackingAsync();
        Assert.Null(result.Error);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(1, fixture.Analyzer.Resets);
        await fixture.Service.PauseAsync();
        Assert.Equal(snapshot.SessionId, fixture.Service.State.SessionId);
        Assert.Equal(27, fixture.Service.State.Loot.Totals[Item]);
    }

    [Fact]
    public async Task NewSessionClearsCurrentMarkerAndDoesNotRestoreLatestHistoricalSession()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        first.Begin();
        await first.ProcessProjection(TimeSpan.FromMinutes(1), 1, 5);
        await first.Service.PauseAsync();
        var oldId = first.Service.State.SessionId;
        await first.Service.NewSessionAsync();
        await first.Service.ShutdownAsync();

        await using var second = new Fixture(directory.Path);
        Assert.False(second.Service.State.HasSession);
        Assert.Empty(second.Service.State.Loot.Totals);
        Assert.NotEqual(oldId, second.Service.State.SessionId);
        Assert.Equal(oldId, Assert.Single(second.History.Load()).SessionId);
        Assert.Null(new CurrentSessionStore(directory.CurrentPath).Load());
    }

    [Fact]
    public async Task EmptySessionSurvivesCloseWithoutManufacturingHistory()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        first.Begin(hasSpot: false);
        var id = first.Service.State.SessionId;
        await first.Service.ShutdownAsync();
        await using var second = new Fixture(directory.Path);
        Assert.True(second.Service.State.HasSession);
        Assert.Equal(id, second.Service.State.SessionId);
        Assert.Null(second.Service.State.SpotId);
        Assert.Empty(second.Service.State.Loot.Totals);
        Assert.Empty(second.History.Load());
    }

    [Fact]
    public async Task RestoredExperienceAndAgrisArePreservedWithoutCrossingClosedAppGap()
    {
        using var directory = new TestDirectory();
        var snapshot = CurrentSessionStoreTests.Example();
        new CurrentSessionStore(directory.CurrentPath).Save(snapshot);
        await using var fixture = new Fixture(directory.Path);
        fixture.Time.Advance(TimeSpan.FromDays(1));
        await fixture.Service.TickAsync();
        Assert.Equal(snapshot.ExperienceGainedPercentagePoints, fixture.Service.State.ExperienceGainedPercentagePoints);
        Assert.Equal(snapshot.ExperienceObservedDuration, fixture.Service.State.ExperienceObservedDuration);
        Assert.Equal(snapshot.AgrisActiveDuration, fixture.Service.State.AgrisActiveDuration);
        Assert.Equal(snapshot.AgrisObservedDuration, fixture.Service.State.AgrisObservedDuration);
        await fixture.Service.ShutdownAsync();
        var saved = new CurrentSessionStore(directory.CurrentPath).Load()!;
        Assert.Equal(snapshot.ExperienceGainedPercentagePoints, saved.ExperienceGainedPercentagePoints);
        Assert.Equal(snapshot.Duration, saved.Duration);
    }

    [Fact]
    public async Task SubmittedSessionRemainsReadOnlyAfterReopening()
    {
        using var directory = new TestDirectory();
        var snapshot = CurrentSessionStoreTests.Example() with { SessionSubmitted = true };
        new CurrentSessionStore(directory.CurrentPath).Save(snapshot);
        await using var fixture = new Fixture(directory.Path);
        await fixture.Service.ToggleTrackingAsync();
        Assert.True(fixture.Service.State.IsSubmitted);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);
        Assert.Equal(snapshot.CharacterClassId, fixture.Service.State.CharacterClassId);
    }

    [Fact]
    public async Task RestoredPausedSessionDoesNotAutomaticallyUploadPendingHours()
    {
        using var directory = new TestDirectory();
        var uploads = new GarmothUploadIntervals();
        uploads.Observe(TimeSpan.FromHours(1), new Dictionary<string, long> { [Item] = 27 }, DateTimeOffset.UtcNow);
        new CurrentSessionStore(directory.CurrentPath).Save(CurrentSessionStoreTests.Example() with
        {
            Duration = TimeSpan.FromHours(1),
            Uploads = uploads.ExportState(),
        });
        await using var fixture = new Fixture(directory.Path, autoUpload: true);
        await fixture.Service.TickAsync();
        await fixture.Service.UploadHourlyToGarmothAsync();
        Assert.Equal(0, fixture.Requests);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.AutomaticSuspended);
    }

    [Fact]
    public async Task RestartDoesNotReactivateDiagnosticRecording()
    {
        using var directory = new TestDirectory();
        new CurrentSessionStore(directory.CurrentPath).Save(CurrentSessionStoreTests.Example() with { RecordLoot = true });
        await using var fixture = new Fixture(directory.Path);
        Assert.False(fixture.Service.Preferences.RecordLoot);
        Assert.False(fixture.Service.State.IsRecording);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "diagnostics")));
    }

    [Fact]
    public async Task UploadClockSamplingLeadDoesNotAddActiveTimeOrBlockShutdownAfterRestore()
    {
        using var directory = new TestDirectory();
        var ledger = new GarmothUploadIntervals();
        ledger.Observe(TimeSpan.FromHours(1), new Dictionary<string, long> { [Item] = 27 }, DateTimeOffset.UtcNow);
        var snapshot = CurrentSessionStoreTests.Example() with
        {
            Duration = TimeSpan.FromHours(1) - TimeSpan.FromTicks(1),
            Uploads = ledger.ExportState(),
        };
        new CurrentSessionStore(directory.CurrentPath).Save(snapshot);
        await using var fixture = new Fixture(directory.Path);
        fixture.Time.Advance(TimeSpan.FromHours(8));
        await fixture.Service.TickAsync();
        await fixture.Service.ShutdownAsync();
        Assert.False(fixture.Service.State.ShutdownFailed);
        Assert.Equal(snapshot.Duration, fixture.Service.State.Elapsed);
        var saved = new CurrentSessionStore(directory.CurrentPath).Load()!;
        Assert.Equal(snapshot.Duration, saved.Duration);
        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(saved.Uploads!.Hours).EndDuration);
    }

    [Fact]
    public async Task DemoIsNeverSavedAsCurrentSession()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        await first.Service.SetDemoAsync(true);
        await first.Service.ShutdownAsync();
        await using var second = new Fixture(directory.Path);
        Assert.False(second.Service.State.HasSession);
        Assert.False(second.Service.State.IsDemo);
        Assert.False(File.Exists(directory.CurrentPath));
    }

    [Fact]
    public async Task ShutdownFailsSafelyWhenCurrentCheckpointCannotBeWrittenAndCanRetry()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        first.Begin();
        await first.ProcessProjection(TimeSpan.FromMinutes(1), 1, 5);
        Directory.CreateDirectory(directory.CurrentPath + ".tmp");
        try
        {
            await first.Service.ShutdownAsync();
            Assert.True(first.Service.State.ShutdownFailed);
            Assert.False(first.Analyzer.Disposed);
            Assert.NotNull(first.Service.State.PersistenceError);
            Assert.Equal(5, first.Service.State.Loot.TotalQuantity);
        }
        finally { Directory.Delete(directory.CurrentPath + ".tmp"); }
        await first.Service.ShutdownAsync();
        Assert.False(first.Service.State.ShutdownFailed);
        await using var second = new Fixture(directory.Path);
        Assert.Equal(5, second.Service.State.Loot.TotalQuantity);
    }

    [Fact]
    public async Task FailedNewSessionWriteDoesNotResetTheCurrentSession()
    {
        using var directory = new TestDirectory();
        await using var fixture = new Fixture(directory.Path);
        fixture.Begin(hasSpot: false);
        await fixture.Service.PauseAsync();
        var id = fixture.Service.State.SessionId;
        Directory.CreateDirectory(directory.CurrentPath + ".tmp");
        try
        {
            var result = await fixture.Service.NewSessionAsync();
            Assert.NotNull(result.Error);
            Assert.True(fixture.Service.State.HasSession);
            Assert.Equal(id, fixture.Service.State.SessionId);
        }
        finally { Directory.Delete(directory.CurrentPath + ".tmp"); }
    }

    [Fact]
    public async Task FailedCorrectionCheckpointRollsBackBothMemoryAndHistory()
    {
        using var directory = new TestDirectory();
        await using var fixture = new Fixture(directory.Path);
        fixture.Begin();
        await fixture.ProcessProjection(TimeSpan.FromMinutes(1), 1, 5);
        await fixture.Service.PauseAsync();
        Directory.CreateDirectory(directory.CurrentPath + ".tmp");
        try
        {
            var result = await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId, Item, 9, 5);
            Assert.NotNull(result.Error);
            Assert.Equal(5, fixture.Service.State.Loot.Totals[Item]);
            Assert.Equal(5, Assert.Single(fixture.History.Load()).Totals[Item]);
            Assert.Equal(5, new CurrentSessionStore(directory.CurrentPath).Load()!.Totals[Item]);
        }
        finally { Directory.Delete(directory.CurrentPath + ".tmp"); }
    }

    [Fact]
    public async Task CorruptCurrentMarkerDoesNotFallBackToAnUnrelatedHistoryEntry()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.CurrentPath, "invalid current checkpoint");
        await using var fixture = new Fixture(directory.Path);
        Assert.False(fixture.Service.State.HasSession);
        Assert.NotNull(fixture.Service.State.PersistenceError);
        Assert.True(fixture.Service.State.IsError);
        await fixture.Service.ShutdownAsync();
        Assert.Equal("invalid current checkpoint", File.ReadAllText(directory.CurrentPath));
    }

    private const string Item = "Black Crystal Fragment";

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(string directory, bool autoUpload = false)
        {
            var settings = new SettingsStore(directory);
            var keyStore = new GarmothApiKeyStore(Path.Combine(directory, "test-key.dpapi"));
            if (autoUpload)
            {
                var preferences = settings.Load();
                preferences.GarmothAutoUploadEnabled = true;
                settings.Save(preferences);
                keyStore.Save("synthetic-test-key");
            }
            History = new(Path.Combine(directory, "loot-history-v1.json"));
            Clock = new(Time);
            Activity = new(Time);
            var capture = new PassiveCaptureSession(_ => { Captures++; return new Bitmap(2, 2); },
                frameInterval: TimeSpan.FromDays(1));
            Service = new(capture, Analyzer, settings,
                [new("synthetic-monitor", "Synthetic", new(0, 0, 1920, 1080), true)],
                Clock, Activity,
                () => new(CompanionCharacterClassCatalog.FindById("warrior-awakening"), CharacterClassDetectionStatus.Detected, 3),
                new FixedPrices(), new GarmothUploadClient(new CountingHandler(() => Requests++)),
                keyStore, History,
                () => new("en", "Synthetic language"), isLootScrollCaptureVisible: _ => false);
        }
        public TrackerSessionService Service { get; }
        public LootHistoryStore History { get; }
        public ManualTimeProvider Time { get; } = new();
        public GrindSessionClock Clock { get; }
        public GrindInactivityTimer Activity { get; }
        public Analyzer Analyzer { get; } = new();
        public int Captures { get; private set; }
        public int Requests { get; private set; }

        public void Begin(bool hasSpot = true)
        {
            SetField(Service, "_hasSession", true);
            SetField(Service, "_sessionStartedAt", Time.GetUtcNow());
            if (hasSpot) SetField(Service, "_sessionSpotId", LootSpotCatalog.HermesiaId);
            SetField(Service, "_sessionClass", CompanionCharacterClassCatalog.FindById("warrior-awakening"));
            ResumeSynthetic();
        }
        public void ResumeSynthetic()
        {
            SetField(Service, "_uiRunning", true);
            Clock.Start();
            Activity.Start();
            Service.RefreshPendingState();
        }
        public Task ProcessProjection(TimeSpan elapsed, long revision, long total)
        {
            Time.Advance(elapsed);
            var analysis = new FrameAnalysisResult([], [], 0, "restore-test", 0, 0, 0, 0, null)
            {
                SpotId = LootSpotCatalog.HermesiaId,
                LootProjection = new(revision, new Dictionary<string, long> { [Item] = total }, 1, Time.GetUtcNow()),
            };
            var mailbox = (FrameUiMailbox)Service.GetType().GetField("_uiMailbox",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Service)!;
            mailbox.Publish(analysis, onPublished: Service.ObserveGarmothTotals,
                capturedAt: Time.GetUtcNow(), flushProjection: true);
            Service.RefreshPendingState();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }

    private static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private sealed class Analyzer : ILootFrameAnalyzer
    {
        public bool IsAvailable => true;
        public string Status => "Synthetic";
        public FrameAnalysisResult? Next { get; set; }
        public int Resets { get; private set; }
        public bool Disposed { get; private set; }
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt, CancellationToken token)
        {
            var result = Next ?? new FrameAnalysisResult([], [], 0, "empty", 0, 0, 0, 0, null);
            Next = null;
            return Task.FromResult(result);
        }
        public void Reset() => Resets++;
        public void Dispose() => Disposed = true;
    }

    private sealed class FixedPrices : ILootPriceProvider
    {
        public LootPriceSnapshot GetCachedSnapshot(string region) => LootPriceCatalog.FixedSnapshot(region);
        public Task<LootPriceSnapshot> GetSnapshotAsync(string region, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetCachedSnapshot(region));
        public void Dispose() { }
    }

    private sealed class CountingHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            onRequest();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(_ticks);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        public string CurrentPath => System.IO.Path.Combine(Path, CurrentSessionStore.FileName);
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
