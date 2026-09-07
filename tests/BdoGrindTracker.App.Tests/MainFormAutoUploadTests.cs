using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class MainFormAutoUploadTests
{
    [Fact]
    public void DisabledAutomaticUploadDoesNothingEvenWithASavedKeyAndACompleteHour()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture(autoUploadEnabled: false);
            fixture.Begin();
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));

            fixture.UploadHour();

            Assert.Empty(fixture.Requests);
            Assert.False(fixture.Settings.Store.Load().GarmothAutoUploadEnabled);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(1), 2);
        });
    }

    [Fact]
    public void AutomaticUploadPreferencePersistsAndCanBeDisabledDuringTracking()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture(autoUploadEnabled: false);
            fixture.Begin();
            Invoke(fixture.Form, "SaveGarmothPreferences", "  synthetic-auto-upload-key  ", true);
            Assert.True(fixture.Settings.Store.Load().GarmothAutoUploadEnabled);
            Assert.Equal("synthetic-auto-upload-key", fixture.Settings.KeyStore.Load());
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
            fixture.UploadHour();
            Assert.Single(fixture.Requests);

            Invoke(fixture.Form, "SaveGarmothPreferences", "synthetic-auto-upload-key", false);
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 3));
            fixture.UploadHour();

            Assert.Single(fixture.Requests);
            Assert.False(fixture.Settings.Store.Load().GarmothAutoUploadEnabled);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(2), 5);
        });
    }

    [Fact]
    public void SuccessiveUploadsContainOnlyTheirHourAndNetSilverWithoutPausingOrResetting()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var sessionId = GetField<Guid>(fixture.Form, "_sessionId");
            fixture.ObserveAfter(TimeSpan.FromHours(1),
                ("Black Crystal Fragment", 2), ("Black Stone", 3));
            fixture.UploadHour();

            AssertPayload(Assert.Single(fixture.Requests), 60, trash: 2, blackStones: 3);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(1), 5);
            fixture.UploadHour();
            Assert.Single(fixture.Requests);

            fixture.Prices.BlackStonePrice = 4_000;
            fixture.ObserveAfter(TimeSpan.FromHours(1),
                ("Black Crystal Fragment", 5), ("Black Stone", 2));
            fixture.UploadHour();

            var uploads = fixture.Requests.ToArray();
            Assert.Equal(2, uploads.Length);
            AssertPayload(uploads[1], 60, trash: 5, blackStones: 2, blackStoneNetUnit: 2_600);
            Assert.NotEqual(uploads[0].GetProperty("note").GetString(),
                uploads[1].GetProperty("note").GetString());
            Assert.Equal(sessionId, GetField<Guid>(fixture.Form, "_sessionId"));
            fixture.AssertTrackingContinues(TimeSpan.FromHours(2), 12);
        });
    }

    [Fact]
    public void OverlappingTicksCannotResendAPendingHourAndNewLootWaitsForTheNextHour()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            var response = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Respond = () => response.Task;
            fixture.Begin();
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
            var pendingUpload = InvokeAsync(fixture.Form, "UploadHourlyToGarmothAsync");
            PumpUntil(() => fixture.Requests.Count == 1);
            Assert.False(pendingUpload.IsCompleted);

            fixture.ObserveAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 5));
            fixture.UploadHour();
            Assert.Single(fixture.Requests);
            Assert.True(fixture.Clock.IsRunning);
            Assert.True(GetField<bool>(fixture.Form, "_uiRunning"));

            response.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
            Complete(pendingUpload);
            fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            fixture.UploadHour();
            AssertPayload(Assert.Single(fixture.Requests), 60, trash: 2);

            fixture.ObserveAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 7));
            fixture.UploadHour();

            var uploads = fixture.Requests.ToArray();
            Assert.Equal(2, uploads.Length);
            AssertPayload(uploads[1], 60, trash: 12);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(2), 14);
        });
    }

    [Fact]
    public void ManualUploadExcludesPreviouslyUploadedHoursAfterAutomaticUploadIsDisabled()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            fixture.ObserveAfter(TimeSpan.FromHours(1),
                ("Black Crystal Fragment", 2), ("Black Stone", 3));
            fixture.UploadHour();
            Invoke(fixture.Form, "SaveGarmothPreferences", "synthetic-auto-upload-key", false);
            fixture.ObserveAfter(TimeSpan.FromMinutes(30),
                ("Black Crystal Fragment", 5), ("Black Stone", 1));

            Complete(InvokeAsync(fixture.Form, "UploadToGarmothAsync"));

            var uploads = fixture.Requests.ToArray();
            Assert.Equal(2, uploads.Length);
            AssertPayload(uploads[0], 60, trash: 2, blackStones: 3);
            AssertPayload(uploads[1], 30, trash: 5, blackStones: 1);
            Assert.False(fixture.Clock.IsRunning);
            Assert.False(GetField<bool>(fixture.Form, "_uiRunning"));
            Assert.True(GetField<bool>(fixture.Form, "_sessionSubmitted"));
            Assert.Equal(TimeSpan.FromMinutes(90), fixture.Clock.Elapsed);
            Assert.Equal(11, GetField<LootSessionSnapshot>(fixture.Form, "_sessionSummary").TotalQuantity);
            Complete(InvokeAsync(fixture.Form, "UploadToGarmothAsync"));
            Assert.Equal(2, fixture.Requests.Count);
            Assert.False(fixture.Form.Visible);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void AutomaticUploadThatMayHaveBeenSavedBlocksFullHistoryUploadsAfterMoreLootAndReset(
        HttpStatusCode status)
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Respond = () => Task.FromResult(new HttpResponseMessage(status));
            fixture.Begin();
            var sessionId = GetField<Guid>(fixture.Form, "_sessionId");
            fixture.ObserveAfter(TimeSpan.FromHours(1),
                ("Black Crystal Fragment", 2), ("Black Stone", 3));
            fixture.UploadHour();

            AssertPayload(Assert.Single(fixture.Requests), 60, trash: 2, blackStones: 3);
            Assert.True(Assert.Single(fixture.Settings.HistoryStore.Load()).GarmothUploadBlocked);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(1), 5);

            fixture.ObserveAfter(TimeSpan.FromMinutes(30),
                ("Black Crystal Fragment", 5), ("Black Stone", 1));
            fixture.Stop();
            fixture.ResetSession();

            var saved = Assert.Single(fixture.Settings.HistoryStore.Load());
            Assert.Equal(sessionId, saved.SessionId);
            Assert.Equal(TimeSpan.FromMinutes(90), saved.Duration);
            Assert.Equal(7, saved.Totals["Black Crystal Fragment"]);
            Assert.Equal(4, saved.Totals["Black Stone"]);
            Assert.True(saved.GarmothUploadBlocked);
            Assert.Equal(status == HttpStatusCode.OK, saved.GarmothUploadedAt.HasValue);

            fixture.UploadHistory(sessionId);

            Assert.Single(fixture.Requests);
        });
    }

    [Fact]
    public void DefinitelyRejectedAutomaticUploadLeavesTheFullHistoricalSessionAvailable()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            fixture.Begin();
            var sessionId = GetField<Guid>(fixture.Form, "_sessionId");
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
            fixture.UploadHour();
            fixture.ObserveAfter(TimeSpan.FromMinutes(30), ("Black Crystal Fragment", 5));
            fixture.Stop();
            fixture.ResetSession();

            var saved = Assert.Single(fixture.Settings.HistoryStore.Load());
            Assert.False(saved.GarmothUploadBlocked);
            Assert.Null(saved.GarmothUploadedAt);
            fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            fixture.UploadHistory(sessionId);

            var uploads = fixture.Requests.ToArray();
            Assert.Equal(2, uploads.Length);
            AssertPayload(uploads[0], 60, trash: 2);
            AssertPayload(uploads[1], 90, trash: 7);
            Assert.True(Assert.Single(fixture.Settings.HistoryStore.Load()).GarmothUploadBlocked);
        });
    }

    [Fact]
    public void UploadingTheCurrentPausedRemainderFromHistoryAlsoClosesItsLiveUploadPath()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var sessionId = GetField<Guid>(fixture.Form, "_sessionId");
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 3));
            fixture.UploadHour();
            fixture.ObserveAfter(TimeSpan.FromMinutes(30), ("Black Crystal Fragment", 2));
            fixture.Stop();
            Assert.True(Assert.Single(fixture.Settings.HistoryStore.Load()).GarmothUploadBlocked);

            fixture.UploadHistory(sessionId);

            var uploads = fixture.Requests.ToArray();
            Assert.Equal(2, uploads.Length);
            AssertPayload(uploads[0], 60, trash: 3);
            AssertPayload(uploads[1], 30, trash: 2);
            Assert.True(GetField<bool>(fixture.Form, "_sessionSubmitted"));
            Assert.False(fixture.Clock.IsRunning);
            Assert.True(Assert.Single(fixture.Settings.HistoryStore.Load()).GarmothUploadBlocked);

            Complete(InvokeAsync(fixture.Form, "UploadToGarmothAsync"));
            fixture.UploadHour();
            fixture.UploadHistory(sessionId);

            Assert.Equal(2, fixture.Requests.Count);
        });
    }

    [Fact]
    public void UncertainAutomaticUploadBlocksFurtherUploadsButTrackingKeepsItsLootAndClock()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            fixture.Begin();
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
            fixture.UploadHour();

            Assert.Single(fixture.Requests);
            Assert.True(GetField<GarmothUploadIntervals>(fixture.Form, "_garmothIntervals").IsBlocked);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(1), 2);

            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 3));
            fixture.UploadHour();
            Complete(InvokeAsync(fixture.Form, "UploadToGarmothAsync"));

            Assert.Single(fixture.Requests);
            Assert.False(GetField<BdoButton>(fixture.Form, "_garmothButton").Enabled);
            Assert.True(GetField<BdoButton>(fixture.Form, "_trackingButton").Enabled);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(2), 5);
        });
    }

    [Fact]
    public void RefreshTimerUploadsEachCompletedHourOnceAndOnlyItsNewLoot()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 2));
            fixture.TickRefreshTimer();
            Assert.Empty(fixture.Requests);

            fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 3));
            fixture.TickRefreshTimer();
            PumpUntil(() => fixture.Requests.Count == 1 &&
                !GetField<bool>(fixture.Form, "_garmothUploadInProgress"));
            AssertPayload(Assert.Single(fixture.Requests), 60, trash: 5);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(1), 5);

            fixture.TickRefreshTimer();
            PumpUntil(() => !GetField<bool>(fixture.Form, "_garmothUploadInProgress"));
            Assert.Single(fixture.Requests);

            fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 7));
            fixture.TickRefreshTimer();
            Assert.Single(fixture.Requests);

            fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Stone", 2));
            fixture.TickRefreshTimer();
            PumpUntil(() => fixture.Requests.Count == 2 &&
                !GetField<bool>(fixture.Form, "_garmothUploadInProgress"));
            AssertPayload(fixture.Requests.ToArray()[1], 60, trash: 7, blackStones: 2);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(2), 14);
        });
    }

    [Fact]
    public void ProducerFramesRetainCorrectionsAndKeepDropsAfterTheBoundaryForTheNextHour()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            var epsilon = TimeSpan.FromMilliseconds(5);
            fixture.Begin();
            fixture.ProcessAfter(TimeSpan.FromMinutes(59),
                ("Black Crystal Fragment", 10), ("Black Stone", 3));
            fixture.ProcessAfter(TimeSpan.FromSeconds(59),
                ("Black Crystal Fragment", -2), ("Black Stone", -1));
            Assert.Equal(TimeSpan.FromSeconds(59), fixture.Activity.IdleDuration);
            fixture.UploadHour();
            Assert.Empty(fixture.Requests);

            fixture.ProcessAfter(TimeSpan.FromSeconds(1) + epsilon,
                ("Black Crystal Fragment", 5), ("Black Stone", 1));
            fixture.UploadHour();
            AssertPayload(Assert.Single(fixture.Requests), 60, trash: 8, blackStones: 2);

            fixture.ProcessAfter(TimeSpan.FromMinutes(59) - epsilon,
                ("Black Crystal Fragment", 7), ("Black Stone", 2));
            fixture.ProcessAfter(TimeSpan.FromMinutes(1) + epsilon,
                ("Black Crystal Fragment", 4));
            fixture.UploadHour();

            var uploads = fixture.Requests.ToArray();
            Assert.Equal(2, uploads.Length);
            AssertPayload(uploads[1], 60, trash: 12, blackStones: 3);
            Assert.Equal(5, fixture.Analyzer.Calls);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(2) + epsilon, 29);
        });
    }

    [Fact]
    public void ProducerFramesWithoutNewDropsDoNotCountIdleTimeTowardTheUploadHour()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            fixture.ProcessAfter(TimeSpan.FromMinutes(59), ("Black Crystal Fragment", 2));
            fixture.ProcessAfter(TimeSpan.FromMinutes(2));
            fixture.UploadHour();
            Assert.Empty(fixture.Requests);
            Assert.Equal(TimeSpan.FromMinutes(2), fixture.Activity.IdleDuration);

            fixture.ProcessAfter(TimeSpan.FromMinutes(2));
            Complete(InvokeAsync(fixture.Form, "PauseIfInactiveAsync"));
            Assert.False(fixture.Clock.IsRunning);
            Assert.Equal(TimeSpan.FromMinutes(59), fixture.Clock.Elapsed);
            fixture.UploadHour();
            Assert.Empty(fixture.Requests);

            fixture.ResumeClocks();
            fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 3));
            fixture.UploadHour();

            AssertPayload(Assert.Single(fixture.Requests), 60, trash: 5);
            Assert.Equal(4, fixture.Analyzer.Calls);
            fixture.AssertTrackingContinues(TimeSpan.FromHours(1), 5);
        });
    }

    [Fact]
    public void AutomaticPauseStillRemovesIdleTimeWhileAnHourlyHttpRequestIsPending()
    {
        RunInSta(() =>
        {
            using var fixture = new Fixture();
            var response = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Respond = () => response.Task;
            fixture.Begin();
            fixture.ObserveAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 2));
            fixture.Time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(50));
            var pendingUpload = InvokeAsync(fixture.Form, "UploadHourlyToGarmothAsync");
            PumpUntil(() => fixture.Requests.Count == 1);

            fixture.Time.Advance(TimeSpan.FromSeconds(10));
            Complete(InvokeAsync(fixture.Form, "PauseIfInactiveAsync"));

            Assert.False(pendingUpload.IsCompleted);
            Assert.False(fixture.Clock.IsRunning);
            Assert.False(fixture.Activity.IsRunning);
            Assert.False(GetField<bool>(fixture.Form, "_uiRunning"));
            Assert.Equal(TimeSpan.FromHours(1), fixture.Clock.Elapsed);
            Assert.True(GetField<bool>(fixture.Form, "_garmothUploadInProgress"));
            response.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
            Complete(pendingUpload);

            AssertPayload(Assert.Single(fixture.Requests), 60, trash: 2);
            Assert.False(GetField<bool>(fixture.Form, "_garmothUploadInProgress"));
            Assert.False(GetField<bool>(fixture.Form, "_sessionSubmitted"));
            Assert.False(fixture.Clock.IsRunning);
            Assert.Equal(TimeSpan.FromHours(1), fixture.Clock.Elapsed);
            Assert.Equal(2, GetField<LootSessionSnapshot>(fixture.Form, "_sessionSummary").TotalQuantity);
            Assert.False(fixture.Form.Visible);
        });
    }

    private static void AssertPayload(JsonElement payload, long minutes, long trash, long blackStones = 0,
        long blackStoneNetUnit = 1_300)
    {
        var expectedSilver = trash * 160_539 + blackStones * blackStoneNetUnit;
        Assert.Equal(minutes, payload.GetProperty("minutes").GetInt64());
        Assert.Equal(expectedSilver, payload.GetProperty("total").GetInt64());
        Assert.Equal(expectedSilver * 60 / minutes, payload.GetProperty("hourly").GetInt64());
        Assert.Equal(214, payload.GetProperty("grindspot_id").GetInt32());
        var drops = payload.GetProperty("drops");
        Assert.Equal(trash, drops.GetProperty("980128_0").GetInt64());
        if (blackStones > 0)
            Assert.Equal(blackStones, drops.GetProperty("16001_0").GetInt64());
        Assert.Equal(blackStones > 0 ? 2 : 1, drops.EnumerateObject().Count());
    }

    private sealed class Fixture : IDisposable
    {
        private long _frameSequence;

        public Fixture(bool autoUploadEnabled = true)
        {
            Settings = new IsolatedSettingsStore();
            var preferences = Settings.Store.Load();
            preferences.GarmothAutoUploadEnabled = autoUploadEnabled;
            Settings.Store.Save(preferences);
            Settings.KeyStore.Save("synthetic-auto-upload-key");
            Clock = new GrindSessionClock(Time);
            Activity = new GrindInactivityTimer(Time);
            var client = new GarmothUploadClient(new MockUploadHandler(async request =>
            {
                Assert.Equal("synthetic-auto-upload-key", request.Headers.GetValues("apiKey").Single());
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Requests.Enqueue(json.RootElement.Clone());
                return await Respond();
            }));
            Form = new MainForm(new PassiveScreenCapture(), Analyzer, Settings.Store,
                Clock, Activity, () => CharacterClassDetection.Unknown, Prices, client,
                Settings.KeyStore, Settings.HistoryStore);
            // All ticks are driven explicitly; neither a shown window nor real capture is required.
            GetField<System.Windows.Forms.Timer>(Form, "_uiRefreshTimer").Stop();
        }

        public MainForm Form { get; }
        public IsolatedSettingsStore Settings { get; }
        public ManualTimeProvider Time { get; } = new();
        public GrindSessionClock Clock { get; }
        public GrindInactivityTimer Activity { get; }
        public SyntheticFrameAnalyzer Analyzer { get; } = new();
        public SyntheticPrices Prices { get; } = new();
        public ConcurrentQueue<JsonElement> Requests { get; } = new();
        public Func<Task<HttpResponseMessage>> Respond { get; set; } =
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        public void Begin()
        {
            SetField(Form, "_hasSession", true);
            SetField(Form, "_sessionStartedAt", Time.GetUtcNow());
            SetField(Form, "_sessionSpotId", LootSpotCatalog.HermesiaId);
            SetField(Form, "_sessionClass", CompanionCharacterClassCatalog.FindById("warrior-awakening")!);
            ResumeClocks();
        }

        public void ResumeClocks()
        {
            SetField(Form, "_uiRunning", true);
            Clock.Start();
            Activity.Start();
            Invoke(Form, "UpdateControlState");
        }

        public void ObserveAfter(TimeSpan activeTime, params (string Name, int Quantity)[] items)
        {
            Time.Advance(activeTime);
            Activity.RecordDrop();
            var analysis = new FrameAnalysisResult(items.Select(item =>
                new LootEventView(Guid.NewGuid(), Time.GetUtcNow(), item.Name, item.Quantity)).ToArray(),
                [], 1, "synthetic-auto-upload", 0, 0, 0, 0, null) { SpotId = LootSpotCatalog.HermesiaId };
            GetField<FrameUiMailbox>(Form, "_uiMailbox").Publish(analysis);
            Invoke(Form, "RefreshPendingUi");
            // Mirrors the producer's observation without starting a screen-capture session.
            GetField<GarmothUploadIntervals>(Form, "_garmothIntervals").Observe(Clock.Elapsed,
                GetField<LootSessionSnapshot>(Form, "_sessionSummary").Totals, Time.GetUtcNow());
        }

        public void ProcessAfter(TimeSpan elapsedTime, params (string Name, int Quantity)[] items)
        {
            Time.Advance(elapsedTime);
            Analyzer.NextResult = new FrameAnalysisResult(items.Select(item =>
                new LootEventView(Guid.NewGuid(), Time.GetUtcNow(), item.Name, item.Quantity)).ToArray(),
                [], 1, "synthetic-producer-upload", 0, 0, 0, 0, null) { SpotId = LootSpotCatalog.HermesiaId };
            // Exercise the actual producer callback with a test-owned bitmap only.
            // This path never updates the inactivity clock or upload ledger directly.
            using var frame = new Bitmap(2, 2);
            Complete(Assert.IsAssignableFrom<Task>(Invoke(Form, "ProcessFrameAsync", frame,
                new CapturedFrameMetadata(++_frameSequence, Time.GetUtcNow()), CancellationToken.None)));
            Invoke(Form, "RefreshPendingUi");
        }

        public void UploadHour() => Complete(InvokeAsync(Form, "UploadHourlyToGarmothAsync"));

        public void Stop() => Complete(Assert.IsAssignableFrom<Task>(Invoke(Form, "StopTrackingAsync", false)));

        public void ResetSession() => Invoke(Form, "ResetButton_Click", null, EventArgs.Empty);

        public void UploadHistory(Guid sessionId)
        {
            Invoke(Form, "UploadHistorySession", sessionId);
            PumpUntil(() => !GetField<bool>(Form, "_garmothUploadInProgress"));
        }

        public void TickRefreshTimer()
        {
            var onTick = typeof(System.Windows.Forms.Timer).GetMethod("OnTick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(onTick);
            // Raise the real timer event so a missing Tick subscription or upload call fails the test.
            onTick.Invoke(GetField<System.Windows.Forms.Timer>(Form, "_uiRefreshTimer"), [EventArgs.Empty]);
        }

        public void AssertTrackingContinues(TimeSpan elapsed, long totalQuantity)
        {
            Assert.True(Clock.IsRunning);
            Assert.True(Activity.IsRunning);
            Assert.True(GetField<bool>(Form, "_uiRunning"));
            Assert.False(GetField<bool>(Form, "_sessionSubmitted"));
            Assert.Equal(elapsed, Clock.Elapsed);
            Assert.Equal(totalQuantity, GetField<LootSessionSnapshot>(Form, "_sessionSummary").TotalQuantity);
            Assert.False(Form.Visible);
        }

        public void Dispose()
        {
            Form.Dispose();
            Settings.Dispose();
        }
    }

    private sealed class MockUploadHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }

    private sealed class SyntheticPrices : ILootPriceProvider
    {
        public long BlackStonePrice { get; set; } = 2_000;
        public LootPriceSnapshot GetCachedSnapshot(string region) => new(region,
            LootPriceCatalog.FixedSnapshot(region).Quotes.Values.Concat(
                [new LootPriceQuote("Black Stone", BlackStonePrice, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UtcNow)]));
        public Task<LootPriceSnapshot> GetSnapshotAsync(string region, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetCachedSnapshot(region));
        public void Dispose() { }
    }

    private sealed class SyntheticFrameAnalyzer : ILootFrameAnalyzer
    {
        public bool IsAvailable => true;
        public string Status => "Synthetic auto-upload test";
        public FrameAnalysisResult? NextResult { get; set; }
        public int Calls { get; private set; }
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt,
            CancellationToken cancellationToken)
        {
            var result = NextResult ?? throw new InvalidOperationException("Tests must supply synthetic analysis and never start capture.");
            NextResult = null;
            Calls++;
            return Task.FromResult(result);
        }
        public void Reset() { }
        public void Dispose() { }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_timestamp);
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private sealed class IsolatedSettingsStore : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        private readonly string _settingsPath;
        private readonly string _historyPath;

        public IsolatedSettingsStore()
        {
            Directory.CreateDirectory(_directory);
            _settingsPath = Path.Combine(_directory, "settings.json");
            _historyPath = Path.Combine(_directory, "loot-history-v1.json");
            Store = new SettingsStore();
            SetField(Store, "_settingsPath", _settingsPath);
            KeyStore = new GarmothApiKeyStore(Path.Combine(_directory, "test-key.dpapi"));
            HistoryStore = new LootHistoryStore(_historyPath);
        }

        public SettingsStore Store { get; }
        public GarmothApiKeyStore KeyStore { get; }
        public LootHistoryStore HistoryStore { get; }

        public void Dispose()
        {
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            if (File.Exists(_historyPath)) File.Delete(_historyPath);
            KeyStore.Save("");
            Directory.Delete(_directory);
        }
    }

    private static T GetField<T>(object target, string name) =>
        Assert.IsType<T>(GetFieldInfo(target, name).GetValue(target));

    private static void SetField(object target, string name, object value) =>
        GetFieldInfo(target, name).SetValue(target, value);

    private static FieldInfo GetFieldInfo(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field;
    }

    private static object? Invoke(object target, string name, params object?[]? args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(target, args);
    }

    private static Task InvokeAsync(MainForm form, string method) => Assert.IsAssignableFrom<Task>(Invoke(form, method));

    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!completed() && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert.True(completed(), "Async automatic-upload operation did not finish.");
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Automatic-upload test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
