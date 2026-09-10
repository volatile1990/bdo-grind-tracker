using System.Net;
using System.Net.Http;
using System.Reflection;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class TrackerSessionOcrInstallationTests
{
    [Theory]
    [InlineData("de", "en")]
    [InlineData("auto", "de")]
    public async Task StartupMissingLanguageUsesTheSelectedOrDetectedGermanLanguage(string preference, string detected)
    {
        await using var fixture = new Fixture(new UnavailableFrameAnalyzer("Initial English OCR unavailable", "en-US"),
            preference: preference, languageDetector: () => new(detected, "Synthetic language"),
            analyzerFactory: language => new UnavailableFrameAnalyzer("Selected OCR unavailable",
                language == "de" ? "de-DE" : "en-US"));

        Assert.Equal("de-DE", fixture.Service.State.MissingOcrLanguageTag);
        Assert.Empty(fixture.Installer.Requests);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task InstalledGermanAtStartupClearsTheInitialEnglishOcrFailure()
    {
        await using var fixture = new Fixture(new UnavailableFrameAnalyzer("Initial English OCR unavailable", "en-US"),
            preference: "de", analyzerFactory: _ => new SyntheticAnalyzer());

        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.True(fixture.Service.State.AnalyzerAvailable);
        Assert.False(fixture.Service.State.IsError);
        Assert.DoesNotContain("English OCR unavailable", fixture.Service.State.Status);
        Assert.Empty(fixture.Installer.Requests);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task TypedMissingLanguageBlocksTrackingAndInstallsInPlaceWithoutStartingCapture()
    {
        var analyzer = new SyntheticAnalyzer { Missing = true };
        var installer = new FakeInstaller { Install = _ =>
        {
            analyzer.Missing = false;
            return Task.FromResult(new WindowsOcrInstallResult(WindowsOcrInstallStatus.Installed));
        } };
        await using var fixture = new Fixture(analyzer, installer, preference: "de");

        await fixture.Service.ToggleTrackingAsync();
        Assert.Equal("de-DE", fixture.Service.State.MissingOcrLanguageTag);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.HasSession);

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Equal(new[] { "de-DE" }, installer.Requests);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.False(fixture.Service.State.IsInstallingOcrLanguage);
        Assert.True(fixture.Service.State.AnalyzerAvailable);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.HasSession);
        Assert.Equal(0, fixture.Captures);
        Assert.Equal(0, analyzer.Resets);
        Assert.False(analyzer.Disposed);
        Assert.Equal("de", analyzer.Configured.Last());
    }

    [Fact]
    public async Task InstallationRechecksTheCurrentAutomaticLanguageInsteadOfInstallingAStaleLanguage()
    {
        var detected = "en";
        var analyzer = new SyntheticAnalyzer { Missing = true };
        var installer = new FakeInstaller { Install = _ =>
        {
            analyzer.Missing = false;
            return Task.FromResult(new WindowsOcrInstallResult(WindowsOcrInstallStatus.Installed));
        } };
        await using var fixture = new Fixture(analyzer, installer,
            languageDetector: () => new(detected, "Synthetic language"));
        await fixture.Service.ToggleTrackingAsync();
        Assert.Equal("en-US", fixture.Service.State.MissingOcrLanguageTag);
        detected = "de";

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Empty(installer.Requests);
        Assert.Equal("de-DE", fixture.Service.State.MissingOcrLanguageTag);
        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Equal(new[] { "de-DE" }, installer.Requests);
        Assert.Equal("de", fixture.Service.State.DetectedGameLanguage);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task PackageInstalledOutsideTheAppIsDetectedBeforeAnyElevatedInstall()
    {
        var analyzer = new SyntheticAnalyzer { Missing = true };
        await using var fixture = new Fixture(analyzer);
        await fixture.Service.ToggleTrackingAsync();
        Assert.Equal("en-US", fixture.Service.State.MissingOcrLanguageTag);
        analyzer.Missing = false;

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Empty(fixture.Installer.Requests);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.True(fixture.Service.State.AnalyzerAvailable);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task AutomaticLanguageChangeToAnInstalledLanguageClearsTheOfferWithoutInstalling()
    {
        var detected = "en";
        var analyzer = new SyntheticAnalyzer { IsMissingForLanguage = language => language == "en" };
        await using var fixture = new Fixture(analyzer,
            languageDetector: () => new(detected, "Synthetic language"));
        await fixture.Service.ToggleTrackingAsync();
        Assert.Equal("en-US", fixture.Service.State.MissingOcrLanguageTag);
        detected = "de";

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Empty(fixture.Installer.Requests);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.True(fixture.Service.State.AnalyzerAvailable);
        Assert.False(fixture.Service.State.IsError);
        Assert.Equal("de", fixture.Service.State.DetectedGameLanguage);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task AGenericOcrRuntimeFailureDoesNotOfferOrLaunchPackageInstallation()
    {
        var analyzer = new SyntheticAnalyzer { ConfigureError = new InvalidOperationException("OCR initialization failed") };
        await using var fixture = new Fixture(analyzer);

        await fixture.Service.ToggleTrackingAsync();
        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.Empty(fixture.Installer.Requests);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task GenericUnavailableAnalyzerIsNotReplacedOrTreatedAsAMissingPackage()
    {
        var replacements = 0;
        await using var fixture = new Fixture(new UnavailableFrameAnalyzer("Model file missing"),
            analyzerFactory: _ => { replacements++; return new SyntheticAnalyzer(); });

        await fixture.Service.RecheckOcrLanguageAsync();
        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Equal(0, replacements);
        Assert.Empty(fixture.Installer.Requests);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.False(fixture.Service.State.AnalyzerAvailable);
    }

    [Fact]
    public async Task StartupPlaceholderIsRecreatedForTheRequestedLanguageAfterSuccessfulInstall()
    {
        var installed = false;
        var languages = new List<string>();
        var replacement = new SyntheticAnalyzer();
        var installer = new FakeInstaller { Install = _ =>
        {
            installed = true;
            return Task.FromResult(new WindowsOcrInstallResult(WindowsOcrInstallStatus.Installed));
        } };
        await using var fixture = new Fixture(new UnavailableFrameAnalyzer("OCR missing", "en-US"), installer,
            preference: "de", analyzerFactory: language =>
            {
                languages.Add(language);
                return installed ? replacement : new UnavailableFrameAnalyzer("German OCR missing", "de-DE");
            });

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.NotEmpty(languages);
        Assert.All(languages, language => Assert.Equal("de", language));
        Assert.Equal(new[] { "de-DE" }, installer.Requests);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.True(fixture.Service.State.AnalyzerAvailable);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);
    }

    [Theory]
    [InlineData((int)WindowsOcrInstallStatus.Cancelled)]
    [InlineData((int)WindowsOcrInstallStatus.Failed)]
    public async Task CancelledOrFailedInstallationKeepsTheMissingLanguageAndAllowsRetry(int firstStatusValue)
    {
        var firstStatus = (WindowsOcrInstallStatus)firstStatusValue;
        var analyzer = new SyntheticAnalyzer { Missing = true };
        var attempts = 0;
        var installer = new FakeInstaller { Install = _ =>
        {
            if (++attempts == 1)
                return Task.FromResult(new WindowsOcrInstallResult(firstStatus,
                    firstStatus == WindowsOcrInstallStatus.Failed ? "Synthetic install failure 0x800F0954" : null));
            analyzer.Missing = false;
            return Task.FromResult(new WindowsOcrInstallResult(WindowsOcrInstallStatus.Installed));
        } };
        await using var fixture = new Fixture(analyzer, installer);
        await fixture.Service.ToggleTrackingAsync();

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Equal("en-US", fixture.Service.State.MissingOcrLanguageTag);
        Assert.False(fixture.Service.State.IsInstallingOcrLanguage);
        Assert.False(fixture.Service.State.IsBusy);
        if (firstStatus == WindowsOcrInstallStatus.Failed)
            Assert.Contains("0x800F0954", fixture.Service.State.OcrInstallationStatus);

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Equal(2, installer.Requests.Count);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task RestartRequiredIsExposedAndCanBeClearedByARecheckWithoutAnotherInstall()
    {
        var analyzer = new SyntheticAnalyzer { Missing = true };
        var installer = new FakeInstaller { Install = _ =>
            Task.FromResult(new WindowsOcrInstallResult(WindowsOcrInstallStatus.RestartRequired)) };
        await using var fixture = new Fixture(analyzer, installer);
        await fixture.Service.ToggleTrackingAsync();

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.True(fixture.Service.State.OcrRestartRequired);
        Assert.Equal("en-US", fixture.Service.State.MissingOcrLanguageTag);
        Assert.False(fixture.Service.State.IsInstallingOcrLanguage);
        analyzer.Missing = false;

        await fixture.Service.RecheckOcrLanguageAsync();

        Assert.False(fixture.Service.State.OcrRestartRequired);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
        Assert.Single(installer.Requests);
        Assert.False(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task SuccessfulProcessExitDoesNotHideAStillUnavailableRecognizer()
    {
        await using var fixture = new Fixture(new SyntheticAnalyzer { Missing = true });
        await fixture.Service.ToggleTrackingAsync();

        await fixture.Service.InstallOcrLanguageAsync();

        Assert.Single(fixture.Installer.Requests);
        Assert.Equal("en-US", fixture.Service.State.MissingOcrLanguageTag);
        Assert.False(fixture.Service.State.IsInstallingOcrLanguage);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);
    }

    [Fact]
    public async Task ConcurrentClicksAndRechecksAreBlockedUntilTheInstallerCompletes()
    {
        var analyzer = new SyntheticAnalyzer { Missing = true };
        var completion = new TaskCompletionSource<WindowsOcrInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new FakeInstaller { Install = _ => completion.Task };
        await using var fixture = new Fixture(analyzer, installer);
        await fixture.Service.ToggleTrackingAsync();
        var pending = fixture.Service.InstallOcrLanguageAsync();
        try
        {
            Assert.True(fixture.Service.State.IsInstallingOcrLanguage);
            Assert.True(fixture.Service.State.IsBusy);
            var checks = analyzer.Configured.Count;
            await fixture.Service.InstallOcrLanguageAsync();
            await fixture.Service.RecheckOcrLanguageAsync();
            await fixture.Service.ToggleTrackingAsync();
            Assert.Single(installer.Requests);
            Assert.Equal(checks, analyzer.Configured.Count);
            Assert.Equal(0, fixture.Captures);
        }
        finally
        {
            analyzer.Missing = false;
            completion.TrySetResult(new(WindowsOcrInstallStatus.Installed));
            await pending;
        }
        Assert.False(fixture.Service.State.IsInstallingOcrLanguage);
        Assert.False(fixture.Service.State.IsBusy);
        Assert.Null(fixture.Service.State.MissingOcrLanguageTag);
    }

    [Fact]
    public async Task RunningSessionBlocksInstallationAndReconfiguration()
    {
        var analyzer = new SyntheticAnalyzer();
        await using var fixture = new Fixture(analyzer);
        await fixture.Service.ToggleTrackingAsync();
        Assert.True(fixture.Service.State.IsRunning);
        var configured = analyzer.Configured.Count;
        analyzer.Missing = true;

        await fixture.Service.InstallOcrLanguageAsync();
        await fixture.Service.RecheckOcrLanguageAsync();

        Assert.Empty(fixture.Installer.Requests);
        Assert.Equal(configured, analyzer.Configured.Count);
        Assert.True(fixture.Service.State.IsRunning);
        await fixture.Service.PauseAsync();
    }

    [Fact]
    public async Task RepairOfAPausedSessionPreservesItsIdLootClockAndAnalyzerLedger()
    {
        var analyzer = new SyntheticAnalyzer();
        var installer = new FakeInstaller { Install = _ =>
        {
            analyzer.Missing = false;
            return Task.FromResult(new WindowsOcrInstallResult(WindowsOcrInstallStatus.Installed));
        } };
        var factoryCalls = 0;
        await using var fixture = new Fixture(analyzer, installer,
            analyzerFactory: _ => { factoryCalls++; return new SyntheticAnalyzer(); });
        await fixture.SeedPausedSessionAsync(analyzer);
        var before = fixture.Service.State;
        Assert.Equal(12, before.Loot.TotalQuantity);
        Assert.True(before.HasSession);
        Assert.False(before.IsRunning);
        Assert.Equal(LootSpotCatalog.HermesiaId, before.SpotId);
        Assert.Equal(before.SessionId, Assert.Single(fixture.Service.History).SessionId);
        analyzer.Missing = true;
        await fixture.Service.ToggleTrackingAsync();
        Assert.Equal("en-US", fixture.Service.State.MissingOcrLanguageTag);

        await fixture.Service.InstallOcrLanguageAsync();

        var after = fixture.Service.State;
        Assert.Equal(before.SessionId, after.SessionId);
        Assert.Equal(before.Loot.TotalQuantity, after.Loot.TotalQuantity);
        Assert.Equal(before.Loot.ConfirmedEventCount, after.Loot.ConfirmedEventCount);
        Assert.Equal(before.Elapsed, after.Elapsed);
        Assert.Equal(before.SpotId, after.SpotId);
        Assert.True(after.HasSession);
        Assert.False(after.IsRunning);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, analyzer.Resets);
        Assert.False(analyzer.Disposed);
        Assert.Equal(0, fixture.Captures);
        Assert.Equal(before.SessionId, Assert.Single(fixture.Service.History).SessionId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        public Fixture(ILootFrameAnalyzer analyzer, FakeInstaller? installer = null, string preference = "auto",
            Func<GameLanguageDetection>? languageDetector = null, Func<string, ILootFrameAnalyzer>? analyzerFactory = null)
        {
            Directory.CreateDirectory(_directory);
            var settings = new SettingsStore(_directory);
            settings.Save(new AppSettings { GameLanguage = preference, GarmothAutoUploadEnabled = false });
            Installer = installer ?? new();
            var capture = new PassiveCaptureSession(_ =>
            {
                Captures++;
                return new Bitmap(2, 2);
            }, frameInterval: TimeSpan.FromDays(1));
            Clock = new GrindSessionClock(Time);
            Activity = new GrindInactivityTimer(Time);
            Service = new TrackerSessionService(capture, analyzer, settings,
                [new("synthetic-monitor", "Synthetic monitor", new Rectangle(0, 0, 1920, 1080), true)],
                Clock, Activity, () => CharacterClassDetection.Unknown, new FixedPrices(),
                new GarmothUploadClient(new NoNetworkHandler()),
                new GarmothApiKeyStore(Path.Combine(_directory, "test-key.dpapi")),
                new LootHistoryStore(Path.Combine(_directory, "loot-history-v1.json")),
                languageDetector ?? (() => new("en", "Synthetic English config")),
                ocrLanguageInstaller: Installer, analyzerFactory: analyzerFactory ?? (_ => new SyntheticAnalyzer()));
        }

        public TrackerSessionService Service { get; }
        public FakeInstaller Installer { get; }
        public int Captures { get; private set; }
        private ManualTimeProvider Time { get; } = new();
        private GrindSessionClock Clock { get; }
        private GrindInactivityTimer Activity { get; }

        public async Task SeedPausedSessionAsync(SyntheticAnalyzer analyzer)
        {
            SetField(Service, "_hasSession", true);
            SetField(Service, "_sessionStartedAt", Time.GetUtcNow());
            SetField(Service, "_sessionSpotId", LootSpotCatalog.HermesiaId);
            SetField(Service, "_captureSegmentCompleted", false);
            SetField(Service, "_uiRunning", true);
            Clock.Start();
            Activity.Start();
            Time.Advance(TimeSpan.FromSeconds(10));
            analyzer.NextResult = new FrameAnalysisResult(
                [new LootEventView(Guid.NewGuid(), Time.GetUtcNow(), "Black Crystal Fragment", 12)],
                [], 1, "synthetic-ocr-install", 0, 0, 0, 0, null) { SpotId = LootSpotCatalog.HermesiaId };
            using var frame = new Bitmap(2, 2);
            await Service.ProcessFrameAsync(frame, new CapturedFrameMetadata(1, Time.GetUtcNow()), CancellationToken.None);
            Service.RefreshPendingState();
            Time.Advance(TimeSpan.FromSeconds(5));
            await Service.PauseAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private sealed class FakeInstaller : IWindowsOcrLanguageInstaller
    {
        public List<string> Requests { get; } = [];
        public Func<string, Task<WindowsOcrInstallResult>> Install { get; init; } =
            _ => Task.FromResult(new WindowsOcrInstallResult(WindowsOcrInstallStatus.Installed));
        public Task<WindowsOcrInstallResult> InstallAsync(string languageTag)
        {
            Requests.Add(languageTag);
            return Install(languageTag);
        }
    }

    private sealed class SyntheticAnalyzer : ILootFrameAnalyzer
    {
        private string? _spotId;
        public bool IsAvailable => true;
        public string Status => "Synthetic OCR installation test";
        public bool Missing { get; set; }
        public Func<string, bool>? IsMissingForLanguage { get; init; }
        public Exception? ConfigureError { get; init; }
        public List<string> Configured { get; } = [];
        public int Resets { get; private set; }
        public bool Disposed { get; private set; }
        public FrameAnalysisResult? NextResult { get; set; }
        public void ConfigureGameLanguage(string language)
        {
            Configured.Add(language);
            if (ConfigureError is not null) throw ConfigureError;
            if (IsMissingForLanguage?.Invoke(language) ?? Missing)
                throw new WindowsOcrLanguageUnavailableException(language == "de" ? "de-DE" : "en-US");
        }
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt, CancellationToken cancellationToken)
        {
            var result = NextResult ?? new FrameAnalysisResult([], [], 1, "synthetic-ocr-install", 0, 0, 0, 0, null);
            NextResult = null;
            _spotId = result.SpotId ?? _spotId;
            return Task.FromResult(result);
        }
        public FrameAnalysisResult CompleteSession(DateTimeOffset completedAt) =>
            new([], [], 0, "synthetic-ocr-install-complete", 0, 0, 0, 0, null) { SpotId = _spotId };
        public void Reset()
        {
            Resets++;
            _spotId = null;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class FixedPrices : ILootPriceProvider
    {
        public LootPriceSnapshot GetCachedSnapshot(string region) => LootPriceCatalog.FixedSnapshot(region);
        public Task<LootPriceSnapshot> GetSnapshotAsync(string region, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetCachedSnapshot(region));
        public void Dispose() { }
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
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
