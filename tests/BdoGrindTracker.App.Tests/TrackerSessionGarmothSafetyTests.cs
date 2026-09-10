using System.Net;
using System.Net.Http;
using System.Text.Json;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task GarmothPersistsFrozenIntentBeforeAnyHttpRequest()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 4));
        var path = Path.Combine(fixture.DirectoryPath, GarmothUploadJournalStore.FileName);
        fixture.Respond = () =>
        {
            var intent = Assert.Single(new GarmothUploadJournalStore(path).Load());
            Assert.Null(intent.Outcome);
            Assert.Equal(fixture.Service.State.SessionId, intent.Draft.SourceSessionId);
            Assert.Equal(4, intent.Draft.Totals["Black Crystal Fragment"]);
            Assert.Equal(TimeSpan.FromMinutes(2), intent.Draft.ActiveDuration);
            Assert.DoesNotContain("synthetic-auto-upload-key", File.ReadAllText(path));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        };

        var result = await fixture.Service.UploadAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(GarmothUploadStatus.Succeeded, Assert.Single(new GarmothUploadJournalStore(path).Load()).Outcome);
    }

    [Fact]
    public async Task GarmothIntentSaveFailurePreventsHttpAndCanBeRetriedAfterRepair()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 4));
        var path = Path.Combine(fixture.DirectoryPath, GarmothUploadJournalStore.FileName);
        Directory.CreateDirectory(path);
        try
        {
            var result = await fixture.Service.UploadAsync();
            Assert.False(result.Succeeded);
            Assert.Contains("nichts gesendet", result.Error);
            Assert.Empty(fixture.Requests);
            Assert.False(fixture.Service.State.IsSubmitted);
        }
        finally { Directory.Delete(path); }

        Assert.True((await fixture.Service.UploadAsync()).Succeeded);
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GarmothJournalBlocksRestartEvenWhenHistoryGuardCouldNotBeSaved(HttpStatusCode response)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 4));
        await fixture.Service.PauseAsync();
        var id = fixture.Service.State.SessionId;
        FileStream? historyLock = null;
        fixture.Respond = () =>
        {
            historyLock = new FileStream(Path.Combine(fixture.DirectoryPath, "loot-history-v1.json"),
                FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult(new HttpResponseMessage(response));
        };
        try
        {
            var result = await fixture.Service.UploadAsync();
            Assert.False(result.Succeeded);
            Assert.Contains("lokale Verlaufstatus", result.Error);
            Assert.False(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        }
        finally { historyLock?.Dispose(); }

        await using var restarted = RestartForGarmothTest(fixture);
        var recovered = Assert.Single(restarted.History);
        Assert.True(recovered.GarmothUploadBlocked);
        Assert.Equal(response == HttpStatusCode.OK, recovered.GarmothUploadedAt.HasValue);
        Assert.False((await restarted.UploadHistoryAsync(id)).Succeeded);
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task GarmothResultSaveFailureKeepsPendingIntentAndBlocksRestart()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 4));
        await fixture.Service.PauseAsync();
        var id = fixture.Service.State.SessionId;
        var path = Path.Combine(fixture.DirectoryPath, GarmothUploadJournalStore.FileName);
        FileStream? journalLock = null;
        FileStream? historyLock = null;
        fixture.Respond = () =>
        {
            journalLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            historyLock = new FileStream(Path.Combine(fixture.DirectoryPath, "loot-history-v1.json"),
                FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        };
        try
        {
            var result = await fixture.Service.UploadAsync();
            Assert.False(result.Succeeded);
            Assert.Contains("Upload-Ergebnis konnte lokal nicht gespeichert", result.Error);
            Assert.True(fixture.Service.State.UploadBlocked);
            Assert.Null(Assert.Single(new GarmothUploadJournalStore(path).Load()).Outcome);
            Assert.False(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        }
        finally { journalLock?.Dispose(); historyLock?.Dispose(); }

        await using var restarted = RestartForGarmothTest(fixture);
        Assert.True(Assert.Single(restarted.History).GarmothUploadBlocked);
        Assert.False((await restarted.UploadHistoryAsync(id)).Succeeded);
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task GarmothUnfinishedIntentBlocksRestartWithoutAnyHistoryGuard()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 4));
        await fixture.Service.PauseAsync();
        var preview = fixture.Service.State.CurrentGarmothUpload;
        Assert.True(preview.IsReady, preview.Error);
        var journal = new GarmothUploadJournalStore(Path.Combine(fixture.DirectoryPath, GarmothUploadJournalStore.FileName));
        journal.Begin(preview.Draft!); // Simulate process exit without a recorded response.
        Assert.False(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);

        await using var restarted = RestartForGarmothTest(fixture);
        var entry = Assert.Single(restarted.History);
        Assert.True(entry.GarmothUploadBlocked);
        Assert.Null(entry.GarmothUploadedAt);
        Assert.False((await restarted.UploadHistoryAsync(entry.SessionId)).Succeeded);
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GarmothEarlierSuccessfulHourCannotHideALaterUnresolvedAttemptOnRestart(bool hasResponse)
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 10));
        await fixture.Service.UploadHourlyToGarmothAsync();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 2));
        await fixture.Service.PauseAsync();
        var journal = new GarmothUploadJournalStore(Path.Combine(fixture.DirectoryPath, GarmothUploadJournalStore.FileName));
        var attemptId = journal.Begin(fixture.Service.State.CurrentGarmothUpload.Draft!);
        if (hasResponse) journal.Complete(attemptId, GarmothUploadStatus.OutcomeUnknown);
        Assert.NotNull(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadedAt);

        await using var restarted = RestartForGarmothTest(fixture);
        var restored = Assert.Single(restarted.History);
        Assert.True(restored.GarmothUploadBlocked);
        Assert.Null(restored.GarmothUploadedAt);
        Assert.False((await restarted.UploadHistoryAsync(restored.SessionId)).Succeeded);
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData("{\"Version\":9,\"Entries\":[]}")]
    [InlineData("{\"Version\":1,\"Entries\":null}")]
    [InlineData("{\"Version\":1,\"Entries\":[null]}")]
    [InlineData("not-json")]
    public async Task GarmothCorruptJournalDoesNotBecomeEmptyOrAllowAnotherUpload(string invalid)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 4));
        await fixture.Service.PauseAsync();
        var path = Path.Combine(fixture.DirectoryPath, GarmothUploadJournalStore.FileName);
        File.WriteAllText(path, invalid);
        await using var restarted = RestartForGarmothTest(fixture);

        Assert.False((await restarted.UploadHistoryAsync(fixture.Service.State.SessionId)).Succeeded);
        Assert.NotNull(restarted.State.PersistenceError);
        Assert.Empty(fixture.Requests);
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public async Task GarmothRemainderReadinessUsesUnsentMinuteAndUnsentQuantities()
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 100));
        await fixture.Service.UploadHourlyToGarmothAsync();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(30), ("Black Crystal Fragment", 1));
        Assert.False(fixture.Service.State.CurrentGarmothUpload.IsReady);
        Assert.Contains("volle aktive Minute", fixture.Service.State.CurrentGarmothUpload.Error);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(30), ("Black Crystal Fragment", 2));
        await fixture.Service.PauseAsync();
        var preview = fixture.Service.State.CurrentGarmothUpload;

        Assert.True(preview.IsReady, preview.Error);
        Assert.Equal(TimeSpan.FromMinutes(1), preview.Draft!.ActiveDuration);
        Assert.Equal(3, preview.Draft.Totals["Black Crystal Fragment"]);
        Assert.Equal(1, preview.Payload!.Minutes);
        Assert.True((await fixture.Service.UploadConfirmedAsync(preview)).Succeeded);
        AssertPayload(fixture.Requests.Last(), 1, 3);
    }

    [Fact]
    public async Task GarmothConfirmedPreviewUsesFrozenSilverAndRejectsChangedCounters()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Stone", 10));
        await fixture.Service.PauseAsync();
        var stale = fixture.Service.State.CurrentGarmothUpload;
        Assert.True(stale.IsReady, stale.Error);
        await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId, "Black Stone", 12, 10);
        Assert.False((await fixture.Service.UploadConfirmedAsync(stale)).Succeeded);
        Assert.Empty(fixture.Requests);
        var confirmed = fixture.Service.State.CurrentGarmothUpload;
        fixture.Prices.BlackStonePrice = 9000;
        await fixture.Service.RefreshPricesAsync();

        Assert.True((await fixture.Service.UploadConfirmedAsync(confirmed)).Succeeded);
        Assert.Equal(confirmed.Payload!.Total, Assert.Single(fixture.Requests).GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task GarmothHistoricalReadinessChecksClassDurationAndCurrentValuation()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Stone", 10));
        await fixture.Service.PauseAsync();
        var entry = Assert.Single(fixture.Service.History);
        var missingClass = GarmothUploadPreview.ForHistory(entry with { CharacterClass = null }, fixture.Service.Prices, SilverTaxOptions.Default);
        var tooShort = GarmothUploadPreview.ForHistory(entry with { Duration = TimeSpan.FromSeconds(59) }, fixture.Service.Prices, SilverTaxOptions.Default);
        var invalidClass = GarmothUploadPreview.ForHistory(entry with { CharacterClass = "Nonexistent" }, fixture.Service.Prices, SilverTaxOptions.Default);

        Assert.False(missingClass.IsReady);
        Assert.Equal($"/history/spots/{entry.SpotId}/{entry.SessionId}?edit=1", missingClass.CorrectionHref);
        Assert.False(tooShort.IsReady);
        Assert.False(invalidClass.IsReady);
        var updated = GarmothUploadPreview.ForHistory(entry with { SilverAfterTax = 1 }, fixture.Service.Prices, SilverTaxOptions.Default);
        Assert.True(updated.IsReady);
        Assert.Equal(13000, updated.Payload!.Total);
    }

    [Fact]
    public async Task GarmothCorrectionDuringAnOpenFirstAttemptSurvivesRestartAsALocalDivergence()
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 10));
        var pending = fixture.Service.UploadHourlyToGarmothAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        try
        {
            Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
                "Black Crystal Fragment", 3, 10)).Succeeded);
            var diskEntry = Assert.Single(fixture.HistoryStore.Load());
            Assert.False(diskEntry.GarmothUploadBlocked);
            Assert.False(diskEntry.GarmothLocallyModified);
            Assert.Single(diskEntry.GarmothPendingCorrectionIntervals);

            await using var restarted = RestartForGarmothTest(fixture);
            var recovered = Assert.Single(restarted.History);
            Assert.True(recovered.GarmothUploadBlocked);
            Assert.True(recovered.GarmothLocallyModified);
            Assert.Equal(3, recovered.Totals["Black Crystal Fragment"]);
            Assert.False((await restarted.UploadHistoryAsync(recovered.SessionId)).Succeeded);
            Assert.Single(fixture.Requests);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            await pending;
        }
        Assert.True(Assert.Single(fixture.Service.History).GarmothLocallyModified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GarmothRejectedAttemptOnlyWarnsWhenItsUncorrectedFrozenRequestIsRetried(bool freshManual)
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 10));
        var pending = fixture.Service.UploadHourlyToGarmothAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        try
        {
            Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
                "Black Crystal Fragment", 3, 10)).Succeeded);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            await pending;
        }
        Assert.False(Assert.Single(fixture.Service.History).GarmothLocallyModified);
        Assert.Single(Assert.Single(fixture.HistoryStore.Load()).GarmothPendingCorrectionIntervals);
        fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        if (freshManual)
        {
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            var correctedPreview = fixture.Service.State.CurrentGarmothUpload;
            Assert.True(correctedPreview.IsReady, correctedPreview.Error);
            Assert.Equal(3, correctedPreview.Draft!.Totals["Black Crystal Fragment"]);
            Assert.True((await fixture.Service.UploadConfirmedAsync(correctedPreview)).Succeeded);
        }
        else
        {
            await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences, resumeAutomaticUpload: true);
            await fixture.Service.UploadHourlyToGarmothAsync();
        }
        Assert.Equal(2, fixture.Requests.Count);
        AssertPayload(fixture.Requests.Last(), 60, freshManual ? 3 : 10);
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.True(saved.GarmothUploadBlocked);
        Assert.Equal(!freshManual, saved.GarmothLocallyModified);
        Assert.Empty(saved.GarmothPendingCorrectionIntervals);
    }

    [Fact]
    public async Task GarmothNewTrackedDropsDuringAnUploadAreNotManualDivergences()
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 10));
        var pending = fixture.Service.UploadHourlyToGarmothAsync();
        await WaitUntilAsync(() => fixture.Requests.Count == 1);
        try { await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 5)); }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await pending;
        }
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(15, saved.Totals["Black Crystal Fragment"]);
        Assert.False(saved.GarmothLocallyModified);
        Assert.Empty(saved.GarmothPendingCorrectionIntervals);
    }

    private static TrackerSessionService RestartForGarmothTest(Fixture fixture) => new(
        new PassiveCaptureSession(_ => new Bitmap(2, 2), frameInterval: TimeSpan.FromDays(1)),
        new SyntheticAnalyzer(), fixture.Settings, [],
        priceProvider: new SyntheticPrices(),
        garmothClient: new GarmothUploadClient(new MockUploadHandler(_ =>
        {
            // The production client deliberately catches transport exceptions;
            // also count dispatch so an unexpected request cannot pass unnoticed.
            fixture.Requests.Enqueue(default);
            throw new InvalidOperationException("Restart must never issue HTTP for this blocked session.");
        })),
        keyStore: fixture.KeyStore, historyStore: fixture.HistoryStore,
        languageDetector: () => new("en", "Synthetic journal restart"));
}
