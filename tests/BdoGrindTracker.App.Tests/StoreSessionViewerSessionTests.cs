using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class StoreSessionViewerSessionTests
{
    [Fact]
    public async Task CheckpointIsProjectedWithoutAdvancingTimeOrTurningAnActiveRotationIntoAnAbort()
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.Example() with
        {
            Buffs = BuffLedgerSnapshot.Empty,
            CombatStats = new(1200, 650, CombatStatsCategory.Edania, fixture.Time.GetUtcNow()),
            DropHistory = [new(TimeSpan.FromMinutes(1), "Black Crystal Fragment", 27)],
            Pauses = [new(TimeSpan.FromSeconds(20), fixture.Time.GetUtcNow().AddMinutes(-2),
                fixture.Time.GetUtcNow().AddMinutes(-1), SessionPause.Manual)],
            Rotations = [new(LootSpotCatalog.HermesiaId, fixture.Time.GetUtcNow().AddMinutes(-1),
                new(32, [new("start", "Start", 0)]) { Id = Guid.NewGuid(), Outcome = "active" })],
            ManualLootItems = ["Black Crystal Fragment"],
        };
        fixture.Save(saved);
        await using var viewer = fixture.Create();

        Assert.True(viewer.State.IsReadOnly);
        Assert.True(viewer.State.HasSession);
        Assert.False(viewer.State.IsRunning);
        Assert.False(viewer.State.AnalyzerAvailable);
        Assert.False(viewer.State.CanEditLoot);
        Assert.False(viewer.State.CanPause);
        Assert.Equal(saved.SessionId, viewer.State.SessionId);
        Assert.Equal(saved.Duration, viewer.State.Elapsed);
        Assert.Equal(saved.UpdatedAt, viewer.State.ObservedAt);
        Assert.Equal(saved.Totals, viewer.State.Loot.Totals);
        Assert.Equal(saved.DropHistory, viewer.State.DropHistory);
        Assert.Equal(saved.Pauses, viewer.State.Pauses);
        Assert.Equal(saved.ManualLootItems, viewer.State.ManualLootItems);
        Assert.Equal(saved.CombatStats, viewer.State.SessionCombatStats);
        Assert.NotNull(viewer.State.Buffs);
        Assert.Equal(saved.AgrisActiveDuration, viewer.State.AgrisActiveDuration);
        Assert.Equal(saved.ExperienceGainedPercentagePoints, viewer.State.ExperienceGainedPercentagePoints);
        Assert.Equal("active", Assert.Single(viewer.State.Rotation.SessionRotations).Outcome);

        fixture.Time.Advance(TimeSpan.FromHours(1));
        await viewer.TickAsync();

        Assert.Equal(saved.Duration, viewer.State.Elapsed);
        Assert.Equal(saved.UpdatedAt, viewer.State.ObservedAt);
        Assert.Single(viewer.State.Pauses);
        Assert.Equal("active", Assert.Single(viewer.State.Rotation.SessionRotations).Outcome);
    }

    [Fact]
    public async Task PollUsesUpdatedCheckpointOfTheSameSessionAndDoesNotPublishUnchangedFiles()
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.Example();
        fixture.Save(saved);
        await using var viewer = fixture.Create();
        var publications = 0;
        viewer.Changed += () => publications++;
        var newer = saved with
        {
            Duration = TimeSpan.FromMinutes(3), UpdatedAt = saved.UpdatedAt.AddMinutes(1),
            Totals = new() { ["Black Crystal Fragment"] = 40 }, ConfirmedEventCount = 9,
            DropHistory = [new(TimeSpan.FromMinutes(3), "Black Crystal Fragment", 40)],
        };
        fixture.Save(newer);

        await viewer.TickAsync();
        Assert.Equal(27, viewer.State.Loot.Totals["Black Crystal Fragment"]);
        fixture.Time.Advance(StoreSessionViewerSession.PollInterval);
        await viewer.TickAsync();
        Assert.Equal(saved.SessionId, viewer.State.SessionId);
        Assert.Equal(newer.Duration, viewer.State.Elapsed);
        Assert.Equal(40, viewer.State.Loot.Totals["Black Crystal Fragment"]);
        Assert.Equal(9, viewer.State.Loot.ConfirmedEventCount);
        Assert.Equal(newer.DropHistory, viewer.State.DropHistory);
        Assert.Equal(1, publications);

        fixture.Time.Advance(StoreSessionViewerSession.PollInterval);
        await viewer.TickAsync();
        Assert.Equal(1, publications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOrDeletedCheckpointClearsCurrentSessionAndNeverInfersOneFromHistory(bool delete)
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.Example();
        fixture.Save(saved);
        fixture.SaveHistory(saved);
        await using var viewer = fixture.Create();
        if (delete) File.Delete(fixture.CheckpointPath);
        else fixture.Save(null);

        fixture.Time.Advance(StoreSessionViewerSession.PollInterval);
        await viewer.TickAsync();

        Assert.False(viewer.State.HasSession);
        Assert.Empty(viewer.State.Loot.Totals);
        Assert.Equal(TimeSpan.Zero, viewer.State.Elapsed);
        Assert.Single(viewer.History);
        Assert.False(viewer.State.IsError);
    }

    [Fact]
    public async Task EmptyCurrentCheckpointIsStillAnExplicitSession()
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.EmptySession();
        fixture.Save(saved);
        await using var viewer = fixture.Create();
        Assert.True(viewer.State.HasSession);
        Assert.Equal(saved.SessionId, viewer.State.SessionId);
        Assert.Empty(viewer.State.Loot.Totals);
    }

    [Fact]
    public async Task LockedCheckpointKeepsLastValidStateAndRetriesWithoutAFileStampChange()
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.Example();
        fixture.Save(saved);
        await using var viewer = fixture.Create();
        fixture.Save(saved with { Totals = new() { ["Black Crystal Fragment"] = 60 } });
        using (var locked = new FileStream(fixture.CheckpointPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fixture.Time.Advance(StoreSessionViewerSession.PollInterval);
            await viewer.TickAsync();
            Assert.Equal(27, viewer.State.Loot.Totals["Black Crystal Fragment"]);
            Assert.True(viewer.State.IsError);
            Assert.NotNull(viewer.State.PersistenceError);
            Assert.Contains("last valid", viewer.State.DataSourceStatus);
        }

        fixture.Time.Advance(StoreSessionViewerSession.PollInterval);
        await viewer.TickAsync();
        Assert.Equal(60, viewer.State.Loot.Totals["Black Crystal Fragment"]);
        Assert.False(viewer.State.IsError);
        Assert.Null(viewer.State.PersistenceError);
    }

    [Fact]
    public async Task InvalidHistoryKeepsLastValidHistoryAndNewValidDataRecovers()
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.Example();
        fixture.Save(saved);
        fixture.SaveHistory(saved);
        await using var viewer = fixture.Create();
        File.WriteAllText(fixture.HistoryPath, "{");
        fixture.Time.Advance(StoreSessionViewerSession.PollInterval);
        await viewer.TickAsync();
        Assert.True(viewer.State.IsError);
        Assert.Equal(saved.SessionId, Assert.Single(viewer.History).SessionId);

        fixture.SaveHistory(saved, CurrentSessionStoreTests.Example());
        fixture.Time.Advance(StoreSessionViewerSession.PollInterval);
        await viewer.TickAsync();
        Assert.False(viewer.State.IsError);
        Assert.Equal(2, viewer.History.Count);
    }

    [Fact]
    public async Task SettingsAndComparisonPreferencesAreReadButMemoryChangesNeverWriteOrEnableTracking()
    {
        using var fixture = new ViewerFixture();
        fixture.Save(CurrentSessionStoreTests.Example());
        var store = new SettingsStore(fixture.DirectoryPath);
        var settings = store.Load();
        settings.DropRatePercent = 375.5m;
        settings.UiLanguage = "de";
        settings.SilverValuePack = true;
        settings.AutoStartGrinding = true;
        settings.GarmothAutoUploadEnabled = true;
        settings.AutomaticDebugLogging = true;
        settings.CloseToTray = true;
        store.Save(settings);
        var before = fixture.Bytes();
        await using var viewer = fixture.Create();
        Assert.Equal(375.5m, viewer.Preferences.DropRatePercent);
        Assert.True(viewer.Preferences.ValuePack);
        Assert.False(viewer.Preferences.AutoStartGrinding);
        Assert.False(viewer.Preferences.AutoUpload);
        Assert.False(viewer.Preferences.AutomaticDebugLogging);
        Assert.False(viewer.Preferences.CloseToTray);

        var result = await viewer.SavePreferencesAsync(viewer.Preferences with
        {
            DropRatePercent = 100, AutoStartGrinding = true, AutoUpload = true,
            RecordLoot = true, RecordRotation = true, AutomaticDebugLogging = true, CloseToTray = true,
        });

        Assert.True(result.Succeeded);
        Assert.Equal(100, viewer.Preferences.DropRatePercent);
        Assert.False(viewer.Preferences.AutoStartGrinding);
        Assert.False(viewer.Preferences.AutoUpload);
        Assert.False(viewer.Preferences.RecordLoot);
        Assert.False(viewer.Preferences.RecordRotation);
        Assert.False(viewer.Preferences.AutomaticDebugLogging);
        Assert.False(viewer.Preferences.CloseToTray);
        fixture.AssertBytes(before);
        Assert.Equal(375.5m, store.Load().DropRatePercent);
    }

    [Fact]
    public async Task AllMutatingCommandsAreRefusedAndShutdownLeavesEverySourceByteUntouched()
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.Example();
        fixture.Save(saved);
        fixture.SaveHistory(saved);
        File.WriteAllBytes(Path.Combine(fixture.DirectoryPath, "garmoth-api-key.dat"), [1, 2, 3, 4]);
        var before = fixture.Bytes();
        var viewer = fixture.Create();
        TrackerCommandResult[] results =
        [
            await viewer.ToggleTrackingAsync(), await viewer.PauseAsync(), await viewer.NewSessionAsync(),
            await viewer.SelectSpotVariantAsync(saved.SessionId, saved.SpotId!), await viewer.SetDemoAsync(true),
            await viewer.InstallOcrLanguageAsync(), await viewer.RecheckOcrLanguageAsync(),
            await viewer.SelectCaptureConfigurationAsync("ignored"), await viewer.UploadAsync(),
            await viewer.UploadHistoryAsync(saved.SessionId), await viewer.UploadConfirmedAsync(viewer.State.CurrentGarmothUpload),
            await viewer.UpdateHistoryLootAsync(saved.SessionId, new Dictionary<string, long>()),
            await viewer.UpdateLootQuantityAsync(saved.SessionId, "Black Crystal Fragment", 100, 27),
            await viewer.DeleteHistoryAsync(saved.SessionId), await viewer.SaveSessionAsync(),
        ];
        Assert.All(results, result => Assert.False(result.Succeeded));
        Assert.False((await viewer.SavePreferencesAsync(viewer.Preferences, apiKey: "ignored")).Succeeded);
        Assert.False((await viewer.SavePreferencesAsync(viewer.Preferences, resumeAutomaticUpload: true)).Succeeded);
        var installed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.RunPreparedUpdateAsync(() =>
        { installed = true; return Task.CompletedTask; }));
        Assert.False(installed);
        await viewer.RefreshPricesAsync();
        await viewer.ShutdownAsync();
        await viewer.DisposeAsync();

        Assert.False(viewer.State.HasApiKey);
        fixture.AssertBytes(before);
    }

    [Fact]
    public async Task TakeoverWithoutAHostCallbackRemainsReadOnly()
    {
        using var fixture = new ViewerFixture();
        fixture.Save(CurrentSessionStoreTests.Example());
        var before = fixture.Bytes();
        await using var viewer = fixture.Create();

        var result = await viewer.TakeOverSessionAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(StoreSessionViewerSession.ReadOnlyReason, result.Error);
        Assert.True(viewer.State.IsReadOnly);
        fixture.AssertBytes(before);
    }

    [Fact]
    public async Task OnlyExplicitTakeoverInvokesHostCallbackAndItsFailureKeepsTheViewerUntouched()
    {
        using var fixture = new ViewerFixture();
        var saved = CurrentSessionStoreTests.Example();
        fixture.Save(saved);
        var before = fixture.Bytes();
        var invocations = 0;
        var refused = new TrackerCommandResult("Die Store-App ist noch geöffnet.");
        await using var viewer = new StoreSessionViewerSession(fixture.DirectoryPath, fixture.Time, () =>
        {
            invocations++;
            return Task.FromResult(refused);
        });
        var stateBefore = viewer.State;

        await viewer.ToggleTrackingAsync();
        await viewer.PauseAsync();
        await viewer.NewSessionAsync();
        await viewer.SetDemoAsync(true);
        await viewer.UploadAsync();
        await viewer.UpdateLootQuantityAsync(saved.SessionId, "Black Crystal Fragment", 100, 27);
        await viewer.ShutdownAsync();
        Assert.Equal(0, invocations);

        var result = await viewer.TakeOverSessionAsync();

        Assert.Same(refused, result);
        Assert.Equal(1, invocations);
        Assert.Same(stateBefore, viewer.State);
        Assert.True(viewer.State.IsReadOnly);
        fixture.AssertBytes(before);
    }

    [Fact]
    public async Task SuccessfulTakeoverResultIsForwardedWithoutWritingOrRestoringTheSource()
    {
        using var fixture = new ViewerFixture();
        fixture.Save(CurrentSessionStoreTests.Example());
        var before = fixture.Bytes();
        await using var viewer = new StoreSessionViewerSession(fixture.DirectoryPath, fixture.Time,
            () => Task.FromResult(TrackerCommandResult.Success));

        var result = await viewer.TakeOverSessionAsync();

        Assert.Same(TrackerCommandResult.Success, result);
        Assert.True(viewer.State.IsReadOnly);
        fixture.AssertBytes(before);
    }

    [Fact]
    public async Task StoredMarketCacheIsAvailableWithoutRefreshingOrWritingIt()
    {
        using var fixture = new ViewerFixture();
        var path = Path.Combine(fixture.DirectoryPath, "market-prices-v1.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Regions = new Dictionary<string, object>
            {
                ["eu"] = new Dictionary<int, object>
                {
                    [16001] = new { UnitPrice = 12345, RetrievedAt = fixture.Time.GetUtcNow().AddMinutes(-1) },
                },
            },
        }));
        var before = fixture.Bytes();
        await using var viewer = fixture.Create();
        Assert.True(viewer.Prices.TryGetQuote("Black Stone", out var quote));
        Assert.Equal(12345, quote.UnitPrice);
        Assert.Equal(LootPriceOrigin.CachedMarket, quote.Origin);
        await viewer.RefreshPricesAsync();
        fixture.AssertBytes(before);
    }

    [Fact]
    public async Task MissingSourceDirectoryRemainsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        await using var viewer = new StoreSessionViewerSession(path);
        await viewer.TickAsync();
        await viewer.ShutdownAsync();
        Assert.False(Directory.Exists(path));
        Assert.False(viewer.State.HasSession);
        Assert.True(viewer.State.IsReadOnly);
    }

    private sealed class ViewerFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        public string CheckpointPath => Path.Combine(DirectoryPath, CurrentSessionStore.FileName);
        public string HistoryPath => Path.Combine(DirectoryPath, "loot-history-v1.json");
        public ManualTimeProvider Time { get; } = new();
        private DateTime _fileTime = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        public ViewerFixture() => Directory.CreateDirectory(DirectoryPath);
        public StoreSessionViewerSession Create() => new(DirectoryPath, Time);
        public void Save(CurrentSessionSnapshot? saved)
        {
            new CurrentSessionStore(CheckpointPath).Save(saved);
            File.SetLastWriteTimeUtc(CheckpointPath, _fileTime = _fileTime.AddSeconds(2));
        }
        public void SaveHistory(params CurrentSessionSnapshot[] saved)
        {
            new LootHistoryStore(HistoryPath).Save(saved.Select(value => new LootHistoryEntry
            {
                SessionId = value.SessionId, StartedAt = value.StartedAt!.Value, UpdatedAt = value.UpdatedAt,
                Duration = value.Duration, SpotId = value.SpotId!, Totals = value.Totals,
                SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
            }));
            File.SetLastWriteTimeUtc(HistoryPath, _fileTime = _fileTime.AddSeconds(2));
        }
        public Dictionary<string, byte[]> Bytes() => Directory.GetFiles(DirectoryPath)
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        public void AssertBytes(Dictionary<string, byte[]> expected)
        {
            var actual = Bytes();
            Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
            foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
        }
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
