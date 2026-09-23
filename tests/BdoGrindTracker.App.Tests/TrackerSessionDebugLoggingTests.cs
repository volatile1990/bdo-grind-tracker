using System.Text.Json;
using System.Reflection;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task AutomaticDebugLoggingIsDisabledUntilExplicitlyEnabled()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));
        await fixture.Service.PauseAsync();

        Assert.False(fixture.Service.Preferences.AutomaticDebugLogging);
        Assert.Equal(3, fixture.Service.Preferences.DebugLogRetentionHours);
        Assert.False(Directory.Exists(fixture.Service.DebugLogsDirectory));
    }

    [Fact]
    public async Task AutomaticDebugLoggingCanBeEnabledDuringTrackingAndPersistsItsRetention()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;

        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
        {
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = 6,
        });
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));
        await fixture.Service.PauseAsync();

        Assert.True(saved.Succeeded);
        Assert.True(fixture.Settings.Load().AutomaticDebugLogging);
        Assert.Equal(6, fixture.Settings.Load().DebugLogRetentionHours);
        Assert.Equal(sessionId, fixture.Service.State.SessionId);
        Assert.Equal(2, fixture.Service.State.Loot.TotalQuantity);
        var entries = ReadDebugEntries(fixture);
        Assert.Contains(entries, entry => DebugKind(entry) == "logging-configured");
        Assert.Contains(entries, entry => DebugKind(entry) == "session-state");
        Assert.Contains(entries, entry => DebugKind(entry) == "capture-complete");
        var frame = Assert.Single(entries, entry => DebugKind(entry) == "loot-frame");
        Assert.Equal(1, frame.GetProperty("version").GetInt32());
        Assert.Equal(fixture.Time.GetUtcNow(), frame.GetProperty("timestampUtc").GetDateTimeOffset());
        Assert.Equal(sessionId, frame.GetProperty("data").GetProperty("sessionId").GetGuid());
        Assert.DoesNotContain(entries, entry => entry.GetRawText().Contains("synthetic-auto-upload-key", StringComparison.Ordinal));
        Assert.Null(fixture.Service.State.DebugLogError);
    }

    [Fact]
    public async Task SavedAutomaticDebugLoggingStartsWithoutStartingCapture()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = 8,
        });

        Assert.True(fixture.Service.Preferences.AutomaticDebugLogging);
        Assert.Equal(8, fixture.Service.Preferences.DebugLogRetentionHours);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.HasSession);
        Assert.Equal(0, fixture.Captures);
        Assert.Contains(ReadDebugEntries(fixture), entry => DebugKind(entry) == "logging-configured");
        Assert.Empty(Directory.EnumerateDirectories(fixture.Service.DebugLogsDirectory));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(169)]
    [InlineData(int.MaxValue)]
    public async Task InvalidDebugRetentionRejectsTheWholePreferenceWrite(int hours)
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings());
        var previous = fixture.Service.Preferences;
        var previousFile = File.ReadAllText(fixture.SettingsPath);

        var saved = await fixture.Service.SavePreferencesAsync(previous with
        {
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = hours,
            FamilyFame = 1500,
        });

        Assert.False(saved.Succeeded);
        Assert.Contains("Debuglog", saved.Error);
        Assert.Equal(previous, fixture.Service.Preferences);
        Assert.Equal(previousFile, File.ReadAllText(fixture.SettingsPath));
        Assert.Empty(ReadDebugEntries(fixture));
    }

    [Fact]
    public async Task ANewSessionKeepsAutomaticDebugLoggingEnabled()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = 4,
        });
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));
        var previousSessionId = fixture.Service.State.SessionId;

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 3));

        Assert.True(fixture.Service.Preferences.AutomaticDebugLogging);
        Assert.Equal(4, fixture.Service.Preferences.DebugLogRetentionHours);
        Assert.NotEqual(previousSessionId, fixture.Service.State.SessionId);
        var frames = ReadDebugEntries(fixture).Where(entry => DebugKind(entry) == "loot-frame").ToArray();
        Assert.Equal(2, frames.Length);
        Assert.Equal(previousSessionId, frames[0].GetProperty("data").GetProperty("sessionId").GetGuid());
        Assert.Equal(fixture.Service.State.SessionId, frames[1].GetProperty("data").GetProperty("sessionId").GetGuid());
    }

    [Fact]
    public async Task TenTenMinuteSessionsKeepTenSeparateDebugFolders()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
        });
        var sessionIds = new List<Guid>();

        for (var index = 0; index < 10; index++)
        {
            fixture.Begin();
            sessionIds.Add(fixture.Service.State.SessionId);
            await fixture.ProcessAfter(TimeSpan.FromMinutes(10), ("Black Crystal Fragment", index + 1));
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        }

        Assert.Equal(3, fixture.Service.Preferences.DebugLogRetentionHours);
        Assert.False(fixture.Service.State.HasSession);
        var directories = Directory.GetDirectories(fixture.Service.DebugLogsDirectory);
        Assert.Equal(10, directories.Length);
        Assert.Equal(10, sessionIds.Distinct().Count());
        foreach (var sessionId in sessionIds)
        {
            var directory = SessionDebugDirectory(fixture, sessionId);
            Assert.Contains(directory, directories);
            var entries = ReadDebugEntries(directory);
            var frame = Assert.Single(entries, entry => DebugKind(entry) == "loot-frame");
            Assert.Equal(sessionId, frame.GetProperty("data").GetProperty("sessionId").GetGuid());
            Assert.All(entries, entry =>
                Assert.Equal(sessionId, entry.GetProperty("data").GetProperty("sessionId").GetGuid()));
        }
    }

    [Fact]
    public async Task PausingAndResumingKeepsWritingToTheSameSessionDebugFolder()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
        });
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(10), ("Black Crystal Fragment", 2));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        fixture.ResumeClocks();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(10), ("Black Crystal Fragment", 3));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);

        Assert.Equal(sessionId, fixture.Service.State.SessionId);
        Assert.Equal(SessionDebugDirectory(fixture, sessionId),
            Assert.Single(Directory.GetDirectories(fixture.Service.DebugLogsDirectory)));
        var frames = ReadDebugEntries(SessionDebugDirectory(fixture, sessionId))
            .Where(entry => DebugKind(entry) == "loot-frame").ToArray();
        Assert.Equal(2, frames.Length);
        Assert.All(frames, entry =>
            Assert.Equal(sessionId, entry.GetProperty("data").GetProperty("sessionId").GetGuid()));
        Assert.Equal(TimeSpan.FromMinutes(15),
            frames[1].GetProperty("timestampUtc").GetDateTimeOffset()
            - frames[0].GetProperty("timestampUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task RestoringASessionKeepsWritingToItsExistingDebugFolder()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
        });
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(10), ("Black Crystal Fragment", 2));
        await fixture.Service.ShutdownAsync();
        Assert.False(fixture.Service.State.ShutdownFailed);
        await fixture.Service.DisposeAsync();
        var directory = SessionDebugDirectory(fixture, sessionId);
        var entriesBeforeRestart = ReadDebugEntries(directory);
        Assert.NotEmpty(entriesBeforeRestart);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        await using var restored = new TrackerSessionService(
            new PassiveCaptureSession(_ => new Bitmap(2, 2)), new SyntheticAnalyzer(), fixture.Settings,
            fixture.Service.Monitors, classDetector: () => fixture.ClassDetection, priceProvider: fixture.Prices,
            keyStore: fixture.KeyStore, historyStore: fixture.HistoryStore,
            languageDetector: () => new("en", "Test"), timeProvider: fixture.Time);

        Assert.Equal(sessionId, restored.State.SessionId);
        Assert.True(restored.State.HasSession);
        Assert.Equal(directory, Assert.Single(Directory.GetDirectories(restored.DebugLogsDirectory)));
        var entriesAfterRestart = ReadDebugEntries(directory);
        Assert.True(entriesAfterRestart.Length > entriesBeforeRestart.Length);
        Assert.Contains(entriesAfterRestart, entry =>
            entry.GetProperty("timestampUtc").GetDateTimeOffset() == fixture.Time.GetUtcNow());
        Assert.All(entriesAfterRestart, entry =>
            Assert.Equal(sessionId, entry.GetProperty("data").GetProperty("sessionId").GetGuid()));
    }

    [Fact]
    public async Task DisablingAutomaticDebugLoggingStopsWritesButStillExpiresOldLogs()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = 1,
        });
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));
        var sessionDirectory = SessionDebugDirectory(fixture, fixture.Service.State.SessionId);
        Assert.True(Directory.Exists(sessionDirectory));
        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutomaticDebugLogging = false });
        var before = ReadDebugEntries(fixture).Select(entry => entry.GetRawText()).ToArray();

        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 3));
        await fixture.Service.TickAsync();

        Assert.True(saved.Succeeded);
        Assert.False(fixture.Settings.Load().AutomaticDebugLogging);
        Assert.NotEmpty(before);
        Assert.Equal(before, ReadDebugEntries(fixture).Select(entry => entry.GetRawText()).ToArray());
        await fixture.Service.PauseAsync();
        fixture.Time.Advance(TimeSpan.FromHours(1));
        await fixture.Service.TickAsync();
        Assert.Empty(ReadDebugEntries(fixture));
        Assert.Empty(Directory.EnumerateFiles(fixture.Service.DebugLogsDirectory, "debug-*.jsonl", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(sessionDirectory));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Service.DebugLogsDirectory));
    }

    [Fact]
    public async Task ReducingDebugRetentionImmediatelyRemovesExpiredEntries()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
        });
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));
        await fixture.ProcessAfter(TimeSpan.FromHours(2), ("Black Crystal Fragment", 3));
        Assert.Equal(2, ReadDebugEntries(fixture).Count(entry => DebugKind(entry) == "loot-frame"));

        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { DebugLogRetentionHours = 1 });

        Assert.True(saved.Succeeded);
        var entries = ReadDebugEntries(fixture);
        Assert.Single(entries, entry => DebugKind(entry) == "loot-frame");
        Assert.All(entries, entry => Assert.True(
            entry.GetProperty("timestampUtc").GetDateTimeOffset() >= fixture.Time.GetUtcNow().AddHours(-1)));
        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(5, fixture.Service.State.Loot.TotalQuantity);
    }

    [Fact]
    public async Task FailedSettingsSaveDoesNotStartAutomaticDebugLogging()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings());
        fixture.Begin();
        using (var locked = new FileStream(fixture.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutomaticDebugLogging = true });
            await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));

            Assert.False(failed.Succeeded);
            Assert.False(fixture.Service.Preferences.AutomaticDebugLogging);
            Assert.False(fixture.Settings.Load().AutomaticDebugLogging);
            Assert.Empty(ReadDebugEntries(fixture));
            Assert.True(fixture.Service.State.IsRunning);
        }

        Assert.True((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutomaticDebugLogging = true })).Succeeded);
        Assert.Contains(ReadDebugEntries(fixture), entry => DebugKind(entry) == "logging-configured");
    }

    [Fact]
    public async Task AnUnwritableDebugDirectoryReportsTheFailureWithoutInterruptingTracking()
    {
        await using var fixture = new Fixture(autoUpload: false);
        Directory.CreateDirectory(fixture.Service.DiagnosticsDirectory);
        File.WriteAllText(fixture.Service.DebugLogsDirectory, "Blocks creation of the log directory.");
        fixture.Begin();

        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutomaticDebugLogging = true });
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 2));

        Assert.True(saved.Succeeded);
        Assert.NotNull(fixture.Service.State.DebugLogError);
        fixture.AssertTracking(TimeSpan.FromSeconds(1), 2);
        Assert.False(fixture.Service.State.IsError);
    }

    [Fact]
    public async Task UnreadSettingsNeverShortenExistingDebugRetentionOnStartupTickOrShutdown()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = 168,
        });
        await fixture.Service.DisposeAsync();
        var before = ReadDebugEntries(fixture).Select(entry => entry.GetRawText()).ToArray();
        Assert.NotEmpty(before);
        fixture.Time.Advance(TimeSpan.FromHours(5));
        File.WriteAllText(fixture.SettingsPath, "{broken");

        await using var restored = new TrackerSessionService(
            new PassiveCaptureSession(_ => new Bitmap(2, 2)), new SyntheticAnalyzer(), fixture.Settings,
            fixture.Service.Monitors, classDetector: () => fixture.ClassDetection, priceProvider: fixture.Prices,
            keyStore: fixture.KeyStore, historyStore: fixture.HistoryStore,
            languageDetector: () => new("en", "Test"), timeProvider: fixture.Time);
        Assert.NotNull(fixture.Settings.LoadError);
        await restored.TickAsync();
        await restored.DisposeAsync();

        Assert.Equal(before, ReadDebugEntries(fixture).Select(entry => entry.GetRawText()).ToArray());
    }

    [Fact]
    public async Task LongBuffHistoryDoesNotPreventPeriodicDebugStateLogging()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            AutomaticDebugLogging = true,
        });
        var ledger = (BuffLedger)typeof(TrackerSessionService)
            .GetField("_buffLedger", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Service)!;
        var consumptions = Enumerable.Range(0, 4_000).Select(index => new BuffConsumption(
            SessionBuff.Id, SessionBuff.Name, SessionBuff.MarketItemId,
            DateTimeOffset.UnixEpoch.AddMinutes(-4_000 + index), null)).ToArray();
        ledger.Restore(new BuffLedgerSnapshot(consumptions, [], []));
        SetField(fixture.Service, "_hasBuffObservation", true);
        fixture.Time.Advance(TimeSpan.FromSeconds(5));
        fixture.Service.RefreshPendingState();

        var entry = ReadDebugEntries(fixture).Last(entry => DebugKind(entry) == "session-state");
        var buffs = entry.GetProperty("data").GetProperty("buffs");
        Assert.Equal(4_000, buffs.GetProperty("consumptionCount").GetInt32());
        Assert.Equal(20, buffs.GetProperty("recentConsumptions").GetArrayLength());
        Assert.Equal(4_000, fixture.Service.State.Buffs!.Consumptions.Count);
        Assert.Null(fixture.Service.State.DebugLogError);
    }

    private static string SessionDebugDirectory(Fixture fixture, Guid sessionId) =>
        Path.Combine(fixture.Service.DebugLogsDirectory, $"session-{sessionId:N}");

    private static JsonElement[] ReadDebugEntries(Fixture fixture) => ReadDebugEntries(fixture.Service.DebugLogsDirectory);

    private static JsonElement[] ReadDebugEntries(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "debug-*.jsonl", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(File.ReadLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .OrderBy(entry => entry.GetProperty("timestampUtc").GetDateTimeOffset())
            .ToArray();
    }

    private static string? DebugKind(JsonElement entry) => entry.GetProperty("kind").GetString();
}
