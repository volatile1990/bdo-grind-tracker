using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

/// <summary>
/// Exercises the service used by the real Blazor frontend, with synthetic frames,
/// a monotonic test clock, mock HTTP and private temporary persistence. No game
/// capture, WebView, message pump or user configuration is involved.
/// </summary>
public sealed class TrackerSessionServiceTests
{
    [Fact]
    public async Task DisabledAutomaticUploadDoesNotSendACompleteHour()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();

        Assert.Empty(fixture.Requests);
        Assert.False(fixture.Settings.Load().GarmothAutoUploadEnabled);
        fixture.AssertTracking(TimeSpan.FromHours(1), 2);
    }

    [Fact]
    public async Task SuccessiveTicksUploadOnlyTheirHourWithoutPausingOrResetting()
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        var id = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();
        Assert.Empty(fixture.Requests);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Stone", 3));
        await fixture.Service.TickAsync();
        AssertPayload(Assert.Single(fixture.Requests), 60, 2, 3);
        await fixture.Service.TickAsync();
        Assert.Single(fixture.Requests);

        fixture.Prices.BlackStonePrice = 4_000;
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 5), ("Black Stone", 2));
        await fixture.Service.TickAsync();

        var uploads = fixture.Requests.ToArray();
        Assert.Equal(2, uploads.Length);
        AssertPayload(uploads[1], 60, 5, 2, blackStoneNetUnit: 2_600);
        Assert.NotEqual(uploads[0].GetProperty("note").GetString(), uploads[1].GetProperty("note").GetString());
        Assert.Equal(id, fixture.Service.State.SessionId);
        fixture.AssertTracking(TimeSpan.FromHours(2), 12);
    }

    [Fact]
    public async Task PendingHourCannotBeSentTwiceAndNewDropsStayInTheNextHour()
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
        var pending = fixture.Service.UploadHourlyToGarmothAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        try
        {
            Assert.False(pending.IsCompleted);
            await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 5));
            await fixture.Service.TickAsync();
            Assert.Single(fixture.Requests);
            fixture.AssertTracking(TimeSpan.FromMinutes(61), 7);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await pending;
        }

        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 7));
        await fixture.Service.TickAsync();
        AssertPayload(fixture.Requests.ToArray()[1], 60, 12);
        fixture.AssertTracking(TimeSpan.FromHours(2), 14);
    }

    [Fact]
    public async Task ManualUploadAfterDisablingAutomaticUploadContainsOnlyTheRemainder()
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2), ("Black Stone", 3));
        await fixture.Service.TickAsync();
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoUpload = false });
        await fixture.ProcessAfter(TimeSpan.FromMinutes(30), ("Black Crystal Fragment", 5), ("Black Stone", 1));

        await fixture.Service.UploadAsync();

        var uploads = fixture.Requests.ToArray();
        Assert.Equal(2, uploads.Length);
        AssertPayload(uploads[0], 60, 2, 3);
        AssertPayload(uploads[1], 30, 5, 1);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.IsSubmitted);
        Assert.Equal(TimeSpan.FromMinutes(90), fixture.Service.State.Elapsed);
        Assert.Equal(11, fixture.Service.State.Loot.TotalQuantity);
        await fixture.Service.UploadAsync();
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task PossiblySavedHourBlocksTheFullHistoricalSessionEvenAfterReset(HttpStatusCode status)
    {
        await using var fixture = new Fixture();
        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(status));
        fixture.Begin();
        var id = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2), ("Black Stone", 3));
        await fixture.Service.TickAsync();
        Assert.True(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(30), ("Black Crystal Fragment", 5), ("Black Stone", 1));
        await fixture.Service.PauseAsync();
        await fixture.Service.NewSessionAsync();

        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(id, saved.SessionId);
        Assert.Equal(TimeSpan.FromMinutes(90), saved.Duration);
        Assert.Equal(7, saved.Totals["Black Crystal Fragment"]);
        Assert.Equal(4, saved.Totals["Black Stone"]);
        Assert.True(saved.GarmothUploadBlocked);
        Assert.Equal(status == HttpStatusCode.OK, saved.GarmothUploadedAt.HasValue);
        await fixture.Service.UploadHistoryAsync(id);
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task DefiniteRejectionLeavesTheFullHistoricalSessionAvailable()
    {
        await using var fixture = new Fixture();
        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        fixture.Begin();
        var id = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(30), ("Black Crystal Fragment", 5));
        await fixture.Service.PauseAsync();
        await fixture.Service.NewSessionAsync();
        Assert.False(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        await fixture.Service.UploadHistoryAsync(id);

        Assert.Equal(2, fixture.Requests.Count);
        AssertPayload(fixture.Requests.ToArray()[1], 90, 7);
        Assert.True(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        await fixture.Service.UploadHistoryAsync(id);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task GeneralPreferencesKeepAutomaticUploadsSuspendedUntilGarmothIsExplicitlySaved()
    {
        await using var fixture = new Fixture();
        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.AutomaticSuspended);

        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoPauseMinutes = 4 });
        Assert.False(fixture.Service.State.IsError);
        Assert.Contains("bleibt angehalten", fixture.Service.State.Status);
        Assert.True(fixture.Service.Preferences.AutoUpload);
        Assert.True(fixture.Service.State.HasApiKey);
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.AutomaticSuspended);
        Assert.Single(fixture.Requests);

        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences, resumeAutomaticUpload: true);
        Assert.False(fixture.Service.State.AutomaticSuspended);
        await fixture.Service.TickAsync();
        Assert.Equal(2, fixture.Requests.Count);
        AssertPayload(fixture.Requests.ToArray()[1], 60, 2);
        await fixture.Service.TickAsync();
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task PausedCurrentHistoryUploadUsesRemainderAndClosesTheLiveUploadPath()
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        var id = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 3));
        await fixture.Service.TickAsync();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(30), ("Black Crystal Fragment", 2));
        await fixture.Service.PauseAsync();

        await fixture.Service.UploadHistoryAsync(id);

        AssertPayload(fixture.Requests.ToArray()[1], 30, 2);
        Assert.True(fixture.Service.State.IsSubmitted);
        await fixture.Service.UploadAsync();
        await fixture.Service.TickAsync();
        await fixture.Service.UploadHistoryAsync(id);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task UnknownUploadOutcomeBlocksFurtherUploadsButKeepsTracking()
    {
        await using var fixture = new Fixture();
        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.UploadBlocked);
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences, resumeAutomaticUpload: true);
        Assert.True(fixture.Service.State.UploadBlocked);
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 3));
        await fixture.Service.TickAsync();
        await fixture.Service.UploadAsync();

        Assert.Single(fixture.Requests);
        fixture.AssertTracking(TimeSpan.FromHours(2), 5);
    }

    [Fact]
    public async Task PreviewKeepsItsKeyAcrossSessionsAndDemoAndDisablesAutomaticUploadWhenRemoved()
    {
        await using var preview = new PreviewTrackerSession(empty: true);
        await preview.SavePreferencesAsync(preview.Preferences with { AutoUpload = true }, "preview-key");
        await preview.NewSessionAsync();
        Assert.True(preview.State.HasApiKey);
        Assert.True(preview.Preferences.AutoUpload);
        await preview.SetDemoAsync(true);
        Assert.True(preview.State.HasApiKey);
        await preview.SavePreferencesAsync(preview.Preferences, apiKey: "");
        Assert.False(preview.State.HasApiKey);
        Assert.False(preview.Preferences.AutoUpload);
    }

    [Fact]
    public async Task ProducerCorrectionsStayBeforeTheBoundaryAndLaterDropsStayInTheNextHour()
    {
        await using var fixture = new Fixture();
        var epsilon = TimeSpan.FromMilliseconds(5);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 10), ("Black Stone", 3));
        await fixture.ProcessAfter(TimeSpan.FromSeconds(59), ("Black Crystal Fragment", -2), ("Black Stone", -1));
        Assert.Equal(TimeSpan.FromSeconds(59), fixture.Activity.IdleDuration);
        await fixture.Service.UploadHourlyToGarmothAsync();
        Assert.Empty(fixture.Requests);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1) + epsilon, ("Black Crystal Fragment", 5), ("Black Stone", 1));
        await fixture.Service.UploadHourlyToGarmothAsync();
        AssertPayload(Assert.Single(fixture.Requests), 60, 8, 2);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59) - epsilon, ("Black Crystal Fragment", 7), ("Black Stone", 2));
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1) + epsilon, ("Black Crystal Fragment", 4));
        await fixture.Service.UploadHourlyToGarmothAsync();

        AssertPayload(fixture.Requests.ToArray()[1], 60, 12, 3);
        Assert.Equal(5, fixture.Analyzer.Calls);
        fixture.AssertTracking(TimeSpan.FromHours(2) + epsilon, 29);
    }

    [Fact]
    public async Task IdleFramesDoNotCompleteAnUploadHourAndAutoPauseRemovesTheEntireIdleTail()
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 2));
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2));
        await fixture.Service.TickAsync();
        Assert.Empty(fixture.Requests);
        Assert.True(fixture.Service.State.IsRunning);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(6));
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(59), fixture.Service.State.Elapsed);
        Assert.Equal(2, fixture.Service.State.Loot.TotalQuantity);
        fixture.ResumeClocks();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 3));
        await fixture.Service.TickAsync();
        AssertPayload(Assert.Single(fixture.Requests), 60, 5);
    }

    [Fact]
    public async Task AutoPauseCanFinishWhileAnHourlyHttpRequestIsPending()
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
        var pending = fixture.Service.UploadHourlyToGarmothAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        try
        {
            fixture.Time.Advance(TimeSpan.FromMinutes(4));
            await fixture.Service.TickAsync();
            Assert.False(pending.IsCompleted);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.Equal(TimeSpan.FromHours(1), fixture.Service.State.Elapsed);
            Assert.Equal(2, fixture.Service.State.Loot.TotalQuantity);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await pending;
        }
        Assert.False(fixture.Service.State.IsSubmitted);
        AssertPayload(Assert.Single(fixture.Requests), 60, 2);
    }

    [Fact]
    public async Task ManualPauseRetainsTheTimeAfterTheLastDropAndResetKeepsHistory()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var id = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 3));
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await fixture.Service.PauseAsync();
        Assert.Equal(TimeSpan.FromMinutes(3), fixture.Service.State.Elapsed);
        Assert.Equal(id, Assert.Single(fixture.Service.History).SessionId);
        await fixture.Service.NewSessionAsync();

        Assert.NotEqual(id, fixture.Service.State.SessionId);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Empty(fixture.Service.State.Loot.Totals);
        Assert.Null(fixture.Service.State.SpotId);
        Assert.False(fixture.Service.State.HasSession);
        Assert.False(fixture.Service.Preferences.RecordLoot);
        Assert.Single(fixture.HistoryStore.Load());
    }

    [Fact]
    public async Task PreferencesAndKeyUseOnlyTheInjectedStoreAndKeyRemovalDisablesAutomaticUpload()
    {
        await using var fixture = new Fixture(autoUpload: false, saveKey: false);
        Assert.False(File.Exists(fixture.SettingsPath));
        Assert.False(fixture.Service.State.HasApiKey);
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
        {
            MonitorDeviceName = "synthetic-secondary",
            AutoPauseMinutes = 7,
            AutoUpload = true,
            MarketRegion = "na",
            ValuePack = true,
            FamilyFame = 4_000
        }, "  synthetic-auto-upload-key  ");

        var saved = fixture.Settings.Load();
        Assert.Equal("synthetic-secondary", saved.MonitorDeviceName);
        Assert.Equal(7, saved.AutoPauseMinutes);
        Assert.Equal("na", saved.MarketRegion);
        Assert.True(saved.GarmothAutoUploadEnabled);
        Assert.True(saved.SilverValuePack);
        Assert.Equal(4_000, saved.SilverFamilyFame);
        Assert.True(fixture.Service.State.HasApiKey);
        Assert.Equal("synthetic-auto-upload-key", fixture.KeyStore.Load());
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences, apiKey: "");
        Assert.False(fixture.Service.State.HasApiKey);
        Assert.False(fixture.Service.Preferences.AutoUpload);
        Assert.False(fixture.Settings.Load().GarmothAutoUploadEnabled);
    }

    [Fact]
    public async Task DemoNeverStartsCaptureRecordsLootOrUploadsAndDoesNotPolluteHistory()
    {
        await using var fixture = new Fixture();
        await fixture.Service.SetDemoAsync(true);
        Assert.True(fixture.Service.State.IsDemo);
        Assert.True(fixture.Service.State.Loot.TotalQuantity > 0);
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { RecordLoot = true });
        await fixture.Service.UploadAsync();
        await fixture.Service.TickAsync();
        await fixture.Service.ShutdownAsync();

        Assert.Equal(0, fixture.Captures);
        Assert.Empty(fixture.Requests);
        Assert.Empty(fixture.HistoryStore.Load());
        Assert.False(fixture.Service.State.IsRecording);
        Assert.False(Directory.Exists(Path.Combine(fixture.DirectoryPath, "diagnostics")));
    }

    [Fact]
    public async Task EditingHistoricalLootRevaluesTheSessionAndRetainsItsUploadGuard()
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        var id = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 10));
        await fixture.Service.TickAsync();
        await fixture.Service.PauseAsync();
        await fixture.Service.NewSessionAsync();

        await fixture.Service.UpdateHistoryLootAsync(id, new Dictionary<string, long>
        {
            ["Black Crystal Fragment"] = 8,
            ["Black Stone"] = 2
        });

        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(8, saved.Totals["Black Crystal Fragment"]);
        Assert.Equal(2, saved.Totals["Black Stone"]);
        Assert.Equal(8 * 160_539m + 2 * 1_300m, saved.SilverAfterTax);
        Assert.True(saved.GarmothUploadBlocked);
        await fixture.Service.UploadHistoryAsync(id);
        Assert.Single(fixture.Requests);
        await fixture.Service.DeleteHistoryAsync(id);
        Assert.Empty(fixture.Service.History);
        Assert.Empty(fixture.HistoryStore.Load());
    }

    [Fact]
    public async Task StartUsesTheSelectedMonitorAndDetectedClassAndAllowsCorrectionAfterPausing()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.ClassDetection = new(CompanionCharacterClassCatalog.FindById("warrior-awakening"),
            CharacterClassDetectionStatus.Detected, 4);
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { MonitorDeviceName = "synthetic-secondary" });
        await fixture.Service.ToggleTrackingAsync();
        await WaitUntilAsync(() => fixture.Captures > 0);

        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(new Rectangle(1920, 0, 1920, 1080), fixture.LastCaptureRegion);
        Assert.Equal("warrior-awakening", fixture.Service.State.CharacterClassId);
        await fixture.Service.PauseAsync();
        Assert.False(fixture.Service.State.IsRunning);
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { CharacterClassId = "maegu-awakening" });
        Assert.Equal("maegu-awakening", fixture.Service.State.CharacterClassId);
    }

    [Fact]
    public async Task AnAmbiguousClassStaysUnknownUntilTheUserChoosesIt()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.ClassDetection = new(null, CharacterClassDetectionStatus.Ambiguous, 3);
        await fixture.Service.ToggleTrackingAsync();
        Assert.Null(fixture.Service.State.CharacterClassId);
        Assert.Contains("mehrdeutig", fixture.Service.State.CharacterLabel);
        await fixture.Service.PauseAsync();
    }

    [Fact]
    public async Task RunningSessionLocksCapturePreferencesButAllowsChangingTheIdleTimeout()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var preferences = fixture.Service.Preferences;
        await fixture.Service.SavePreferencesAsync(preferences with { MonitorDeviceName = "synthetic-secondary", RecordLoot = true });
        Assert.Equal(preferences, fixture.Service.Preferences);
        Assert.True(fixture.Service.State.IsError);
        await fixture.Service.SavePreferencesAsync(preferences with { AutoPauseMinutes = 2 });
        Assert.Equal(2, fixture.Settings.Load().AutoPauseMinutes);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
    }

    [Fact]
    public async Task ShutdownDrainsTheLastCapturedFrameAndFinalBatchBeforeSavingAndDisposing()
    {
        await using var fixture = new Fixture(autoUpload: false);
        var pendingAnalysis = new TaskCompletionSource<FrameAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Analyzer.Analyze = () => pendingAnalysis.Task;
        fixture.Analyzer.CompletionResult = Analysis(("Black Crystal Fragment", 3));
        await fixture.Service.ToggleTrackingAsync();
        await WaitUntilAsync(() => fixture.Analyzer.Calls == 1);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        var shutdown = fixture.Service.ShutdownAsync();
        try
        {
            Assert.False(shutdown.IsCompleted);
            Assert.False(fixture.Analyzer.Disposed);
        }
        finally
        {
            pendingAnalysis.TrySetResult(Analysis(("Black Crystal Fragment", 7)));
            await shutdown;
        }

        Assert.True(fixture.Analyzer.Disposed);
        Assert.Equal(1, fixture.Analyzer.CompletionCalls);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(TimeSpan.FromMinutes(2), saved.Duration);
        Assert.Equal(10, saved.Totals["Black Crystal Fragment"]);
        Assert.Empty(fixture.Requests);
        await fixture.Service.ShutdownAsync();
        Assert.Equal(1, fixture.Analyzer.CompletionCalls);
    }

    [Fact]
    public async Task ShutdownWaitsForAnAutomaticUploadAndPersistsItsDuplicateGuard()
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 3));
        await fixture.Service.TickAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        var shutdown = fixture.Service.ShutdownAsync();
        try
        {
            Assert.False(shutdown.IsCompleted);
            Assert.False(fixture.Analyzer.Disposed);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await shutdown;
        }
        Assert.True(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        Assert.True(fixture.Analyzer.Disposed);
    }

    [Fact]
    public async Task MissingKeyDoesNotPauseOrSubmitTheRunningSession()
    {
        await using var fixture = new Fixture(autoUpload: false, saveKey: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 3));
        await fixture.Service.UploadAsync();
        Assert.Empty(fixture.Requests);
        Assert.Contains("API-Schlüssel", fixture.Service.State.Status);
        Assert.True(fixture.Service.State.IsError);
        fixture.AssertTracking(TimeSpan.FromMinutes(2), 3);
    }

    [Fact]
    public async Task InvalidKeyIsNotPersistedOrEchoedIntoState()
    {
        await using var fixture = new Fixture(autoUpload: false, saveKey: false);
        const string invalidKey = "synthetic secret with spaces";
        var previous = fixture.Service.Preferences;
        await fixture.Service.SavePreferencesAsync(previous with { AutoUpload = true }, invalidKey);
        Assert.Equal(previous, fixture.Service.Preferences);
        Assert.False(fixture.Service.State.HasApiKey);
        Assert.True(fixture.Service.State.IsError);
        Assert.DoesNotContain(invalidKey, fixture.Service.State.Status);
        Assert.Equal("", fixture.KeyStore.Load());
        Assert.False(File.Exists(fixture.SettingsPath));
    }

    [Fact]
    public Task ChangingMarketRegionWaitsForItsOwnPricesBeforeAllowingUpload() => RunOnHostContextAsync(async () =>
    {
        await using var fixture = new Fixture(autoUpload: false);
        var eu = new TaskCompletionSource<LootPriceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var na = new TaskCompletionSource<LootPriceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedRegions = new ConcurrentQueue<string>();
        fixture.Prices.Fetch = region =>
        {
            requestedRegions.Enqueue(region);
            return region == "eu" ? eu.Task : na.Task;
        };
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Stone", 1));
        var initialRefresh = fixture.Service.RefreshPricesAsync();
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { MarketRegion = "na" });
        var upload = fixture.Service.UploadAsync();
        try
        {
            await fixture.Service.UploadAsync();
            Assert.Empty(fixture.Requests);
            Assert.True(fixture.Service.State.IsBusy);
            Assert.False(upload.IsCompleted);
            eu.TrySetResult(new LootPriceSnapshot("eu", [new("Black Stone", 9999, 0, LootPriceOrigin.LiveMarket, null)]));
            await initialRefresh;
            await WaitUntilAsync(() => requestedRegions.Count == 2);
            Assert.Equal(new[] { "eu", "na" }, requestedRegions);
            Assert.Empty(fixture.Requests);
            Assert.False(upload.IsCompleted);
        }
        finally
        {
            eu.TrySetResult(LootPriceCatalog.FixedSnapshot("eu"));
            na.TrySetResult(new LootPriceSnapshot("na", [new("Black Stone", 4000, 0, LootPriceOrigin.LiveMarket, null)]));
            await upload;
        }
        Assert.Equal(2600, Assert.Single(fixture.Requests).GetProperty("total").GetInt64());
    });

    [Fact]
    public async Task PublishedLootSnapshotDoesNotChangeWhenAnotherFrameArrives()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 3));
        var previous = fixture.Service.State;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 5));
        Assert.Equal(3, previous.Loot.Totals["Black Crystal Fragment"]);
        Assert.Equal(8, fixture.Service.State.Loot.Totals["Black Crystal Fragment"]);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, long>>(previous.Loot.Totals);
        Assert.Throws<NotSupportedException>(() => dictionary["Black Crystal Fragment"] = 999);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnwritableSettingsDoNotStopStartingOrResumingAndRemainVisibleAfterFrames(bool pathIsDirectory)
    {
        await using var fixture = new Fixture(autoUpload: false);
        if (pathIsDirectory) Directory.CreateDirectory(fixture.SettingsPath);
        else fixture.Settings.Save(new AppSettings());
        // A directory raises UnauthorizedAccessException; a private sharing lock
        // raises IOException. Neither changes ACLs or accesses user settings.
        using var lockedSettings = pathIsDirectory ? null : new FileStream(
            fixture.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        await fixture.Service.ToggleTrackingAsync();
        await WaitUntilAsync(() => fixture.Captures == 1);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.IsError);
        Assert.Contains("Einstellungen nicht gespeichert", fixture.Service.State.Status);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 2));
        await fixture.Service.TickAsync();
        Assert.True(fixture.Service.State.IsError);
        Assert.Contains("Einstellungen nicht gespeichert", fixture.Service.State.Status);

        await fixture.Service.PauseAsync();
        await fixture.Service.ToggleTrackingAsync();
        await WaitUntilAsync(() => fixture.Captures == 2);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.IsError);
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoPauseMinutes = 4 });
        Assert.True(fixture.Service.State.IsError);
        Assert.Contains("Einstellungen nicht gespeichert", fixture.Service.State.Status);
        Assert.True(fixture.Service.State.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulProviderTaskWithOfflinePricesDoesNotClaimAMarketRefresh(bool hasCachedMarketPrice)
    {
        await using var fixture = new Fixture(autoUpload: false);
        var message = hasCachedMarketPrice
            ? "Markt nicht erreichbar. Letzte bekannte Marktpreise aus dem Cache."
            : "Markt nicht erreichbar. Nur NPC- und Festwerte verfügbar.";
        var quotes = LootPriceCatalog.FixedSnapshot("eu").Quotes.Values.AsEnumerable();
        if (hasCachedMarketPrice)
            quotes = quotes.Append(new("Black Stone", 2_000, 0, LootPriceOrigin.CachedMarket,
                DateTimeOffset.UnixEpoch, IsStale: true));
        var offlineSnapshot = new LootPriceSnapshot("eu", quotes, statusMessage: message);
        fixture.Prices.Fetch = _ => Task.FromResult(offlineSnapshot);

        await fixture.Service.RefreshPricesAsync();

        Assert.Same(offlineSnapshot, fixture.Service.Prices);
        Assert.Contains(message, fixture.Service.State.PriceStatus);
        Assert.DoesNotContain("aktualisiert", fixture.Service.State.PriceStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningOrSubmittedClassCannotBeChangedAndRejectsTheWholePreferenceWrite(bool submitted)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        if (submitted)
        {
            await fixture.Service.PauseAsync();
            SetField(fixture.Service, "_sessionSubmitted", true);
            fixture.Service.RefreshPendingState();
        }
        var previous = fixture.Service.Preferences;

        await fixture.Service.SavePreferencesAsync(previous with { CharacterClassId = "maegu-awakening" },
            "different-synthetic-key");

        Assert.True(fixture.Service.State.IsError);
        Assert.Equal(previous, fixture.Service.Preferences);
        Assert.Equal("warrior-awakening", fixture.Service.State.CharacterClassId);
        Assert.Equal("synthetic-auto-upload-key", fixture.KeyStore.Load());
    }

    private static FrameAnalysisResult Analysis(params (string Name, int Quantity)[] items) => new(
        items.Select(item => new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, item.Name, item.Quantity)).ToArray(),
        [], 1, "synthetic-service-test", 0, 0, 0, 0, null) { SpotId = LootSpotCatalog.HermesiaId };

    private static void AssertPayload(JsonElement payload, long minutes, long trash, long blackStones = 0,
        long blackStoneNetUnit = 1_300)
    {
        var silver = trash * 160_539 + blackStones * blackStoneNetUnit;
        Assert.Equal(minutes, payload.GetProperty("minutes").GetInt64());
        Assert.Equal(silver, payload.GetProperty("total").GetInt64());
        Assert.Equal(silver * 60 / minutes, payload.GetProperty("hourly").GetInt64());
        Assert.Equal(214, payload.GetProperty("grindspot_id").GetInt32());
        var drops = payload.GetProperty("drops");
        Assert.Equal(trash, drops.GetProperty("980128_0").GetInt64());
        if (blackStones > 0) Assert.Equal(blackStones, drops.GetProperty("16001_0").GetInt64());
        Assert.Equal(blackStones > 0 ? 2 : 1, drops.EnumerateObject().Count());
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }

    private static Task RunOnHostContextAsync(Func<Task> action)
    {
        // Service commands and their continuations share the desktop host's
        // serialized context. xUnit's concurrent continuations do not model that
        // contract when two callers wait for the same previous-region request.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var callbacks = new BlockingCollection<(SendOrPostCallback Callback, object? State)>();
            var context = new QueuedSynchronizationContext(callbacks);
            SynchronizationContext.SetSynchronizationContext(context);
            context.Post(async _ =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { callbacks.CompleteAdding(); }
            }, null);
            foreach (var callback in callbacks.GetConsumingEnumerable())
                callback.Callback(callback.State);
        }) { IsBackground = true };
        thread.Start();
        return completion.Task;
    }

    private sealed class QueuedSynchronizationContext(
        BlockingCollection<(SendOrPostCallback Callback, object? State)> callbacks) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Add((callback, state));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private long _sequence;
        public Fixture(bool autoUpload = true, bool saveKey = true)
        {
            Directory.CreateDirectory(DirectoryPath);
            SetField(Settings, "_settingsPath", SettingsPath);
            KeyStore = new GarmothApiKeyStore(Path.Combine(DirectoryPath, "test-key.dpapi"));
            HistoryStore = new LootHistoryStore(Path.Combine(DirectoryPath, "loot-history-v1.json"));
            if (autoUpload) Settings.Save(new AppSettings { GarmothAutoUploadEnabled = true });
            if (saveKey) KeyStore.Save("synthetic-auto-upload-key");
            Clock = new GrindSessionClock(Time);
            Activity = new GrindInactivityTimer(Time);
            var capture = new PassiveCaptureSession(region =>
            {
                LastCaptureRegion = region;
                Captures++;
                return new Bitmap(2, 2);
            }, frameInterval: TimeSpan.FromDays(1));
            var client = new GarmothUploadClient(new MockUploadHandler(async request =>
            {
                Assert.Equal("synthetic-auto-upload-key", request.Headers.GetValues("apiKey").Single());
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Requests.Enqueue(json.RootElement.Clone());
                return await Respond();
            }));
            Service = new TrackerSessionService(capture, Analyzer, Settings,
            [
                new("synthetic-primary", "Bildschirm 1", new Rectangle(0, 0, 1920, 1080), true),
                new("synthetic-secondary", "Bildschirm 2", new Rectangle(1920, 0, 1920, 1080), false)
            ], Clock, Activity, () => ClassDetection, Prices, client, KeyStore, HistoryStore);
        }

        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
        public SettingsStore Settings { get; } = new();
        public GarmothApiKeyStore KeyStore { get; }
        public LootHistoryStore HistoryStore { get; }
        public TrackerSessionService Service { get; }
        public ManualTimeProvider Time { get; } = new();
        public GrindSessionClock Clock { get; }
        public GrindInactivityTimer Activity { get; }
        public SyntheticAnalyzer Analyzer { get; } = new();
        public SyntheticPrices Prices { get; } = new();
        public CharacterClassDetection ClassDetection { get; set; } = CharacterClassDetection.Unknown;
        public int Captures { get; private set; }
        public Rectangle? LastCaptureRegion { get; private set; }
        public ConcurrentQueue<JsonElement> Requests { get; } = new();
        public Func<Task<HttpResponseMessage>> Respond { get; set; } =
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        public void Begin()
        {
            // Start only the producer's session state; capture remains inactive.
            SetField(Service, "_hasSession", true);
            SetField(Service, "_sessionStartedAt", Time.GetUtcNow());
            SetField(Service, "_sessionSpotId", LootSpotCatalog.HermesiaId);
            SetField(Service, "_sessionClass", CompanionCharacterClassCatalog.FindById("warrior-awakening")!);
            SetField(Service, "_classDetection", new CharacterClassDetection(
                CompanionCharacterClassCatalog.FindById("warrior-awakening"), CharacterClassDetectionStatus.Detected, 4));
            SetField(Service, "_captureSegmentCompleted", false);
            ResumeClocks();
        }

        public void ResumeClocks()
        {
            SetField(Service, "_uiRunning", true);
            Clock.Start();
            Activity.Start();
            Service.RefreshPendingState();
        }

        public async Task ProcessAfter(TimeSpan elapsed, params (string Name, int Quantity)[] items)
        {
            Time.Advance(elapsed);
            Analyzer.NextResult = new FrameAnalysisResult(items.Select(item => new LootEventView(
                Guid.NewGuid(), Time.GetUtcNow(), item.Name, item.Quantity)).ToArray(),
                [], 1, "synthetic-service-test", 0, 0, 0, 0, null) { SpotId = LootSpotCatalog.HermesiaId };
            using var frame = new Bitmap(2, 2);
            await Service.ProcessFrameAsync(frame, new CapturedFrameMetadata(++_sequence, Time.GetUtcNow()), CancellationToken.None);
            Service.RefreshPendingState();
        }

        public void AssertTracking(TimeSpan elapsed, long total)
        {
            Assert.True(Clock.IsRunning);
            Assert.True(Activity.IsRunning);
            Assert.True(Service.State.IsRunning);
            Assert.False(Service.State.IsSubmitted);
            Assert.Equal(elapsed, Service.State.Elapsed);
            Assert.Equal(total, Service.State.Loot.TotalQuantity);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private sealed class MockUploadHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }

    private sealed class SyntheticPrices : ILootPriceProvider
    {
        public long BlackStonePrice { get; set; } = 2_000;
        public Func<string, Task<LootPriceSnapshot>>? Fetch { get; set; }
        public LootPriceSnapshot GetCachedSnapshot(string region) => new(region,
            LootPriceCatalog.FixedSnapshot(region).Quotes.Values.Concat(
            [new LootPriceQuote("Black Stone", BlackStonePrice, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UnixEpoch)]));
        public Task<LootPriceSnapshot> GetSnapshotAsync(string region, CancellationToken cancellationToken = default) =>
            Fetch?.Invoke(region) ?? Task.FromResult(GetCachedSnapshot(region));
        public void Dispose() { }
    }

    private sealed class SyntheticAnalyzer : ILootFrameAnalyzer
    {
        public bool IsAvailable => true;
        public string Status => "Synthetic service test";
        public FrameAnalysisResult? NextResult { get; set; }
        public Func<Task<FrameAnalysisResult>>? Analyze { get; set; }
        public FrameAnalysisResult CompletionResult { get; set; } = Analysis();
        public int CompletionCalls { get; private set; }
        public bool Disposed { get; private set; }
        public int Calls { get; private set; }
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt, CancellationToken cancellationToken)
        {
            Calls++;
            if (Analyze is not null) return Analyze();
            var result = NextResult ?? Analysis();
            NextResult = null;
            return Task.FromResult(result);
        }
        public FrameAnalysisResult CompleteSession(DateTimeOffset completedAt)
        {
            CompletionCalls++;
            return CompletionResult;
        }
        public void Reset() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_timestamp);
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}

