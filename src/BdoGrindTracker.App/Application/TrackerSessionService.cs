using System.Collections.ObjectModel;
using System.Security.Cryptography;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Services;

/// <summary>
/// Owns a desktop tracking session independently of its rendered controls.
/// Commands and TickAsync run on the host synchronization context; only the
/// capture producer enters ProcessFrameAsync and the thread-safe mailbox.
/// </summary>
internal sealed partial class TrackerSessionService : ITrackerSession
{
    private readonly PassiveCaptureSession _captureSession;
    private ILootFrameAnalyzer _analyzer;
    private readonly LootScrollMonitor _lootScrollMonitor;
    private readonly Func<Rectangle, bool> _isLootScrollCaptureVisible;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly GrindSessionClock _sessionClock;
    private readonly GrindInactivityTimer _inactivityTimer;
    private readonly Func<CharacterClassDetection> _detectCharacterClass;
    private readonly Func<GameLanguageDetection> _detectGameLanguage;
    private GameLanguageDetection _gameLanguageDetection;
    private readonly ILootPriceProvider _priceProvider;
    private readonly GarmothUploadClient _garmothClient;
    private readonly GarmothApiKeyStore _garmothKeyStore;
    private readonly LootHistoryStore _historyStore;
    private readonly List<LootHistoryEntry> _historyEntries;
    private readonly FrameUiMailbox _uiMailbox = new();
    private readonly SessionSilverHistory _silverHistory = new();
    private readonly GarmothUploadIntervals _garmothIntervals = new();
    private readonly CancellationTokenSource _priceLifetime = new();
    private readonly AsyncLocal<CommandOutcome?> _commandOutcome = new();
    private sealed class CommandOutcome
    {
        public string? Error { get; set; }
        public bool Completed { get; set; }
    }
    private DiagnosticRecordingSession? _recording;
    private LootSessionSnapshot _sessionSummary = LootSessionSnapshot.Empty;
    private CharacterClassDetection _classDetection = CharacterClassDetection.Unknown;
    private CharacterClass? _sessionClass;
    private Guid _sessionId = Guid.NewGuid();
    private DateTimeOffset? _sessionStartedAt;
    private string? _sessionSpotId;
    private Rectangle? _lastCaptureDesktopRegion;
    private Exception? _lastCaptureStopError;
    private bool _captureSegmentCompleted = true;
    private bool _hasSession;
    private bool _uiRunning;
    private bool _operationInProgress;
    private bool _garmothUploadInProgress;
    private bool _sessionSubmitted;
    private bool _demoMode;
    private bool _tickInProgress;
    private bool _shutdownStarted;
    private bool _disposed;
    private bool _priceRefreshEnabled;
    private bool _historyChanged = true;
    private string _garmothApiKey;
    private string? _settingsSaveError;
    private string _status;
    private bool _isError;
    private string _priceStatus = "NPC- und Festwerte";
    private Task _operationTask = Task.CompletedTask;
    private Task _tickTask = Task.CompletedTask;
    private Task _automaticUploadTask = Task.CompletedTask;
    private Task? _shutdownTask;
    private Task? _classDetectionTask;
    private Task? _priceRefreshTask;
    private string? _priceRefreshRegion;
    private DateTimeOffset _nextPriceRefreshAt = DateTimeOffset.MinValue;

    public TrackerSessionService(
        PassiveCaptureSession capture,
        ILootFrameAnalyzer analyzer,
        SettingsStore settingsStore,
        IReadOnlyList<TrackerMonitor> monitors,
        GrindSessionClock? sessionClock = null,
        GrindInactivityTimer? inactivityTimer = null,
        Func<CharacterClassDetection>? classDetector = null,
        ILootPriceProvider? priceProvider = null,
        GarmothUploadClient? garmothClient = null,
        GarmothApiKeyStore? keyStore = null,
        LootHistoryStore? historyStore = null,
        Func<GameLanguageDetection>? languageDetector = null,
        IWindowsOcrLanguageInstaller? ocrLanguageInstaller = null,
        Func<string, ILootFrameAnalyzer>? analyzerFactory = null,
        LootScrollMonitor? lootScrollMonitor = null,
        Func<Rectangle, bool>? isLootScrollCaptureVisible = null)
    {
        _captureSession = capture ?? throw new ArgumentNullException(nameof(capture));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _lootScrollMonitor = lootScrollMonitor ?? new LootScrollMonitor(new LootScrollFrameDetector());
        _isLootScrollCaptureVisible = isLootScrollCaptureVisible ?? CreateLootScrollVisibilityCheck();
        _ocrLanguageInstaller = ocrLanguageInstaller ?? new WindowsOcrLanguageInstaller();
        _analyzerFactory = analyzerFactory ?? (language => FrameAnalyzerFactory.Create(language));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        ArgumentNullException.ThrowIfNull(monitors);
        Monitors = Array.AsReadOnly(monitors.ToArray());
        _settings = settingsStore.Load();
        _sessionClock = sessionClock ?? new GrindSessionClock();
        _inactivityTimer = inactivityTimer ?? new GrindInactivityTimer();
        _detectCharacterClass = classDetector ?? new CompanionCharacterClassDetector().DetectDefault;
        _detectGameLanguage = languageDetector ?? BlackDesertLanguageDetector.Detect;
        _gameLanguageDetection = _detectGameLanguage();
        _priceProvider = priceProvider ?? new ArshaLootPriceProvider();
        _garmothClient = garmothClient ?? new GarmothUploadClient();
        _garmothKeyStore = keyStore ?? new GarmothApiKeyStore();
        try { _garmothApiKey = _garmothKeyStore.Load(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // A key copied from another Windows account must not prevent local
            // tracking, and must never be reused or exposed to the frontend.
            _garmothApiKey = string.Empty;
        }
        _historyStore = historyStore ?? new LootHistoryStore(
            Path.Combine(settingsStore.BaseDirectory, "loot-history-v1.json"));
        _historyEntries = _historyStore.Load().ToList();
        InitializeGarmothUploadJournal();
        Prices = _priceProvider.GetCachedSnapshot(_settings.MarketRegion);
        _priceStatus = FormatPriceStatus(Prices);
        Preferences = new TrackerPreferences
        {
            MonitorDeviceName = Monitors.FirstOrDefault(m => m.DeviceName == _settings.MonitorDeviceName)?.DeviceName
                ?? Monitors.FirstOrDefault(m => m.IsPrimary)?.DeviceName ?? Monitors.FirstOrDefault()?.DeviceName,
            AutoPauseMinutes = _settings.AutoPauseMinutes,
            GameLanguage = _settings.GameLanguage,
            FavoriteItems = _settings.FavoriteItems ?? [],
            LootColumnOrders = _settings.LootColumnOrders ?? new(),
            CharacterClassId = CompanionCharacterClassCatalog.FindById(_settings.CharacterClassId ?? "")?.Id,
            AutoUpload = _settings.GarmothAutoUploadEnabled,
            MarketRegion = _settings.MarketRegion,
            ValuePack = _settings.SilverValuePack,
            MerchantRing = _settings.SilverMerchantRing,
            FamilyFame = _settings.SilverFamilyFame,
        };
        _status = analyzer.IsAvailable ? "Bereit für deine nächste Session." : analyzer.Status;
        _isError = !analyzer.IsAvailable;
        RefreshMissingOcrLanguageOffer();
        _captureSession.Stopped += CaptureSessionStopped;
        PublishState();
    }

    public event Action? Changed;
    public TrackerState State { get; private set; } = new();
    public TrackerPreferences Preferences { get; private set; }
    public IReadOnlyList<TrackerMonitor> Monitors { get; }
    public IReadOnlyList<LootHistoryEntry> History { get; private set; } = [];
    public LootPriceSnapshot Prices { get; private set; } = LootPriceCatalog.FixedSnapshot("eu");
    private bool IsBusy => _operationInProgress || _garmothUploadInProgress;
    private CharacterClass? SelectedCharacterClass => Preferences.CharacterClassId is { } id
        ? CompanionCharacterClassCatalog.FindById(id) : _classDetection.Class;

    public Task<TrackerCommandResult> ToggleTrackingAsync() => _uiRunning ? PauseAsync() : RunOperationAsync(async () =>
    {
        if (_demoMode) ClearDemo();
        if (_uiRunning) await StopTrackingAsync();
        else await StartTrackingAsync();
    });

    public Task<TrackerCommandResult> PauseAsync() => RunOperationAsync(async () =>
    {
        if (_uiRunning) await StopTrackingAsync();
    }, allowDuringUpload: true);

    public Task<TrackerCommandResult> NewSessionAsync() => RunOperationAsync(() =>
    {
        if (_uiRunning)
        {
            SetStatus("Bitte die laufende Session zuerst pausieren.", true);
            return Task.CompletedTask;
        }
        RefreshPendingState();
        PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true);
        SavePendingHistory();
        _analyzer.Reset();
        _lootScrollMonitor.Reset();
        _recording?.Dispose();
        _recording = null;
        _hasSession = false;
        _sessionId = Guid.NewGuid();
        _sessionStartedAt = null;
        _sessionSpotId = null;
        _demoMode = false;
        _sessionSubmitted = false;
        _sessionClass = null;
        Preferences = Preferences with { CharacterClassId = null, RecordLoot = false };
        _garmothIntervals.Reset();
        _sessionClock.Reset();
        _inactivityTimer.Reset();
        _uiMailbox.Reset();
        _sessionSummary = LootSessionSnapshot.Empty;
        _sessionManualLootItems.Clear();
        _sessionGarmothLocallyModified = false;
        _lastCheckpointDuration = TimeSpan.Zero;
        _nextCheckpointRetry = DateTimeOffset.MinValue;
        _lastCaptureDesktopRegion = null;
        _captureSegmentCompleted = true;
        Interlocked.Exchange(ref _lastCaptureStopError, null);
        SetStatus("Neue Session angelegt. Die Diagnose-Aufzeichnung ist ausgeschaltet.");
        return Task.CompletedTask;
    });

    public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => RunOperationAsync(() =>
    {
        if (_hasSession || _uiRunning) return Task.CompletedTask;
        if (!enabled)
        {
            ClearDemo();
            SetStatus("Bereit für deine nächste Session.");
            return Task.CompletedTask;
        }
        _demoMode = true;
        _sessionManualLootItems.Clear();
        _sessionGarmothLocallyModified = false;
        _sessionSpotId = LootSpotCatalog.AphrodonId;
        _sessionClass = CompanionCharacterClassCatalog.FindById("warrior-awakening");
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["Branch of Abundance"] = 18_420, ["Ancient Spirit Dust"] = 76,
            ["Black Stone"] = 51, ["WON Origin Shard"] = 4,
            ["WON Wandering Origin Crystal"] = 1, ["Broken Vestige of Goldroot"] = 1,
            ["Nev's Fragment"] = 9,
        };
        _sessionSummary = new(totals, totals.Values.Sum(), 147);
        SetStatus("Demostunde: nicht gespeicherte Beispieldaten. Tracking starten beendet die Demo.");
        return Task.CompletedTask;
    });

    private void ClearDemo()
    {
        _demoMode = false;
        _sessionSpotId = null;
        _sessionClass = null;
        _sessionSummary = LootSessionSnapshot.Empty;
        _sessionManualLootItems.Clear();
        _sessionGarmothLocallyModified = false;
    }

    private async Task StartTrackingAsync()
    {
        if (_sessionSubmitted) return;
        var monitor = Monitors.FirstOrDefault(m => m.DeviceName == Preferences.MonitorDeviceName);
        if (monitor is null) throw new InvalidOperationException("Kein Spielmonitor verfügbar.");
        var gameLanguage = ResolveGameLanguage();
        EnsureOcrLanguage(gameLanguage);
        if (Interlocked.CompareExchange(ref _lastCaptureStopError, null, null) is LootPanelUnavailableException panelError)
            throw panelError;
        _analyzer.ValidateCaptureSetup(monitor.Bounds.Size);
        var continuesExistingSession = _hasSession;
        var geometryChanged = _lastCaptureDesktopRegion is { } previous && previous != monitor.Bounds;
        try
        {
            TrySaveSettings();
            await RefreshClassDetectionAsync();
            if (_shutdownStarted) return;
            _sessionClass ??= SelectedCharacterClass;
            if (!continuesExistingSession || geometryChanged)
            {
                _analyzer.Reset();
                _sessionSpotId = null;
                _recording?.Dispose();
                _recording = Preferences.RecordLoot
                    ? DiagnosticRecordingSession.Start(Path.Combine(_settingsStore.BaseDirectory, "diagnostics"),
                        minimumTrashQuantities: TrashLootMinimumCatalog.MinimumQuantities)
                    : null;
            }
            Interlocked.Exchange(ref _lastCaptureStopError, null);
            _captureSegmentCompleted = false;
            _ocrInstallationStatus = null;
            if (!_hasSession) _sessionStartedAt = DateTimeOffset.UtcNow;
            _hasSession = true;
            _lootScrollMonitor.Reset();
            _uiRunning = true;
            _inactivityTimer.Start();
            _sessionClock.Start();
            _lastCaptureDesktopRegion = monitor.Bounds;
            _captureSession.StartCompanion(monitor.Bounds, ProcessFrameAsync,
                () => _isLootScrollCaptureVisible(monitor.Bounds));
            _priceRefreshEnabled = true;
            SetStatus(_settingsSaveError is { } settingsError
                ? "Tracking aktiv. " + settingsError
                : "Tracking aktiv. Drops werden automatisch erkannt und gezählt.",
                _settingsSaveError is not null);
        }
        catch
        {
            _uiRunning = false;
            _sessionClock.Pause();
            _inactivityTimer.Pause();
            await _captureSession.StopAsync();
            CompleteCaptureSegment(DateTimeOffset.UtcNow);
            throw;
        }
    }

    private async Task StopTrackingAsync(bool automatic = false)
    {
        if (!automatic)
        {
            _sessionClock.Pause();
            _inactivityTimer.Pause();
        }
        SetStatus("Wird pausiert. Die letzten Drops werden noch übernommen …");
        await _captureSession.StopAsync();
        if (automatic)
            _sessionClock.Pause(_inactivityTimer.PauseAndGetIdleDuration());
        CompleteCaptureSegment(DateTimeOffset.UtcNow);
        _uiRunning = false;
        _lootScrollMonitor.Reset();
        PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true);
        var failure = Interlocked.CompareExchange(ref _lastCaptureStopError, null, null);
        SetStatus(failure is not null ? "Tracking gestoppt: " + failure.Message
            : automatic ? $"Automatisch pausiert: seit {Preferences.AutoPauseMinutes} Minuten kein neuer Drop. Die Zeit ohne Drops wurde abgezogen."
            : "Pausiert. Die Session bleibt erhalten.", failure is not null);
    }

    internal async Task ProcessFrameAsync(Bitmap frame, CapturedFrameMetadata metadata,
        CancellationToken cancellationToken)
    {
        var analysis = await _analyzer.AnalyzeAsync(frame, metadata.CapturedAtUtc,
            metadata.UseHdrOcr, metadata.IsToneMapped, cancellationToken).ConfigureAwait(false);
        if (_analyzer.RequiresLootPanel && (analysis.PanelRegion is not { Width: > 0, Height: > 0 } panel ||
            !new Rectangle(Point.Empty, frame.Size).Contains(panel)))
            throw new LootPanelUnavailableException(LootPanelCaptureGuard.MissingPanelMessage);
        _recording?.RecordFrame(metadata.CapturedAtUtc, analysis.Observations,
            analysis.TrackingResult, frame, analysis.PanelRegion, analysis.RareBandRegion, analysis.Recovery,
            isHdr: metadata.IsHdr,
            isToneMapped: metadata.IsToneMapped, rowReviews: analysis.RowReviews,
            recognitionVariant: analysis.VariantName);
        _uiMailbox.Publish(analysis, onPublished: ObserveGarmothTotals);
        if (_uiRunning && metadata.CanObserveHud)
            _lootScrollMonitor.Observe(frame, metadata.CapturedAtUtc);
    }

    private static Func<Rectangle, bool> CreateLootScrollVisibilityCheck()
    {
        var gameWindow = new NativeOverlayGameWindow();
        return region =>
        {
            var (screen, foreground) = gameWindow.Locate();
            // A wiki screenshot, video or this app must not count as game HUD evidence.
            return foreground && screen?.Bounds == region;
        };
    }

    internal void ObserveGarmothTotals(IReadOnlyDictionary<string, long> totals, bool hasNewDrop)
    {
        if (hasNewDrop) _inactivityTimer.RecordDrop();
        var duration = _sessionClock.GetElapsedExcludingTrailingIdle(_inactivityTimer.IdleDuration);
        _garmothIntervals.Observe(duration, totals, DateTimeOffset.UtcNow);
    }

    internal void RefreshPendingState(bool publish = true)
    {
        if (_disposed) return;
        using var update = _uiMailbox.TakeLatest();
        if (update is not null)
        {
            _sessionSpotId = update.Analysis.SpotId;
            if (update.Totals is { } totals) _sessionSummary = totals;
            if (_uiRunning && !_operationInProgress && !_garmothUploadInProgress &&
                !_garmothIntervals.IsBlocked && !_garmothIntervals.AutomaticSuspended &&
                _settingsSaveError is null)
            {
                _status = update.Analysis.PanelRegion is null
                    ? "Lootbereich nicht verfügbar. Companion-Kalibrierung prüfen."
                    : "Tracking aktiv. Drops werden automatisch erkannt und gezählt.";
                _isError = false;
            }
        }
        if (_recording?.LastError is { } recordingError)
        {
            _status = "Aufzeichnung beendet: " + recordingError;
            _isError = true;
        }
        if (publish) PublishState();
    }

    private void CompleteCaptureSegment(DateTimeOffset completedAt)
    {
        if (_captureSegmentCompleted) return;
        var completed = _analyzer.CompleteSession(completedAt);
        _captureSegmentCompleted = true;
        _recording?.RecordCompletion(completedAt, completed.TrackingResult);
        _uiMailbox.Publish(completed, onPublished: ObserveGarmothTotals);
        RefreshPendingState();
    }

    private void CaptureSessionStopped(object? sender, CaptureSessionStoppedEventArgs args)
    {
        // The event is raised on capture's background thread. The next host tick
        // handles it after the serial producer has finished, without UI access.
        if (args.Error is { } error)
        {
            Interlocked.Exchange(ref _lastCaptureStopError, error);
            CaptureFailureDiagnostics.TryWrite(_settingsStore.BaseDirectory, error, DateTimeOffset.UtcNow);
        }
    }

    public Task TickAsync()
    {
        if (_shutdownStarted || _disposed || _tickInProgress) return Task.CompletedTask;
        _tickInProgress = true;
        return _tickTask = TickCoreAsync();
    }

    private async Task TickCoreAsync()
    {
        try
        {
            RefreshPendingState(publish: false);
            if (_classDetectionTask is null) _ = RefreshClassDetectionAsync();
            var failed = Interlocked.CompareExchange(ref _lastCaptureStopError, null, null) is not null;
            // An HTTP upload does not defer inactivity or capture-failure handling.
            if (_uiRunning && !_operationInProgress && (failed ||
                _inactivityTimer.ShouldPause(TimeSpan.FromMinutes(Preferences.AutoPauseMinutes))))
            {
                _operationInProgress = true;
                try { await StopTrackingAsync(automatic: !failed); }
                finally { _operationInProgress = false; }
            }
            if (_shutdownStarted) return;
            // Never await hourly HTTP in the timer: the next tick must still be
            // able to pause, consume producer snapshots, and update the duration.
            if (_automaticUploadTask.IsCompleted)
                _automaticUploadTask = UploadHourlyToGarmothAsync();
            SaveCheckpointIfDue();
            if (_priceRefreshEnabled && (_priceRefreshTask is null || _priceRefreshTask.IsCompleted) &&
                DateTimeOffset.UtcNow >= _nextPriceRefreshAt)
                _ = RefreshPricesAsync();
        }
        catch (Exception exception) { SetStatus("Tracking: " + exception.Message, true); }
        finally
        {
            _tickInProgress = false;
            PublishState();
        }
    }

    private Task<TrackerCommandResult> RunOperationAsync(Func<Task> action, bool allowDuringUpload = false)
    {
        if (_shutdownStarted || _disposed || _operationInProgress || (!allowDuringUpload && _garmothUploadInProgress))
            return Task.FromResult(new TrackerCommandResult("Bitte warte, bis der laufende Vorgang abgeschlossen ist."));
        _operationInProgress = true;
        PublishState();
        var task = RunOperationCoreAsync(action);
        _operationTask = task;
        return task;
    }

    private async Task<TrackerCommandResult> RunOperationCoreAsync(Func<Task> action)
    {
        var previous = _commandOutcome.Value;
        var outcome = new CommandOutcome();
        _commandOutcome.Value = outcome;
        try { await action(); }
        catch (Exception exception) { outcome.Error = exception.Message; SetStatus(exception.Message, true); }
        finally
        {
            outcome.Completed = true;
            _commandOutcome.Value = previous;
            _operationInProgress = false;
            PublishState();
        }
        return new(outcome.Error);
    }

    private void SetStatus(string message, bool error = false)
    {
        if (error && _commandOutcome.Value is { Completed: false } outcome) outcome.Error = message;
        _status = message;
        _isError = error;
        PublishState();
    }

    private void PublishState()
    {
        if (_disposed) return;
        var character = _hasSession || _demoMode ? _sessionClass : SelectedCharacterClass;
        var trackingBlockedReason = _demoMode ? null : _ocrLanguageError ?? (!_analyzer.IsAvailable ? _analyzer.Status :
            (Interlocked.CompareExchange(ref _lastCaptureStopError, null, null) as LootPanelUnavailableException)?.Message);
        var totals = new ReadOnlyDictionary<string, long>(
            new Dictionary<string, long>(_sessionSummary.Totals, StringComparer.OrdinalIgnoreCase));
        State = new TrackerState
        {
            SessionId = _sessionId, HasSession = _hasSession, IsRunning = _uiRunning,
            IsBusy = IsBusy, IsDemo = _demoMode, IsSubmitted = _sessionSubmitted,
            CanEditLoot = !_operationInProgress && !_shutdownStarted && !_disposed,
            CanPause = _uiRunning && !_operationInProgress && !_shutdownStarted && !_disposed,
            PersistenceError = _historyPersistenceError ?? _historyStore.LoadError ?? _garmothPersistenceError ?? _settingsSaveError,
            CurrentGarmothUpload = CreateCurrentGarmothUploadPreview(),
            AnalyzerAvailable = _analyzer.IsAvailable, SpotId = _sessionSpotId,
            TrackingBlockedReason = trackingBlockedReason,
            MissingOcrLanguageTag = _missingOcrLanguageTag,
            IsInstallingOcrLanguage = _isInstallingOcrLanguage,
            OcrInstallationStatus = _ocrInstallationStatus,
            OcrRestartRequired = _ocrRestartRequired,
            DetectedGameLanguage = _gameLanguageDetection.Language,
            GameLanguageStatus = _gameLanguageDetection.Message,
            CharacterClassId = character?.Id,
            CharacterLabel = character?.DisplayName ?? (_classDetection.Status == CharacterClassDetectionStatus.Ambiguous
                ? "Klasse mehrdeutig – bitte auswählen" : "Klasse unbekannt – automatische Erkennung"),
            Elapsed = _demoMode ? TimeSpan.FromHours(1) : _sessionClock.Elapsed,
            LootScroll = !_demoMode && _uiRunning
                ? _lootScrollMonitor.Snapshot(DateTimeOffset.UtcNow) : LootScrollState.Unknown,
            Loot = new(totals, _sessionSummary.TotalQuantity, _sessionSummary.ConfirmedEventCount),
            ManualLootItems = Array.AsReadOnly(_sessionManualLootItems.ToArray()),
            Silver = SilverValuation.Calculate(totals, Prices, Preferences.Tax),
            PriceStatus = _priceStatus, Status = _isInstallingOcrLanguage ? _ocrInstallationStatus! : trackingBlockedReason ?? _status,
            IsError = _isError || trackingBlockedReason is not null || _historyPersistenceError is not null || _historyStore.LoadError is not null || _garmothPersistenceError is not null || _settingsSaveError is not null,
            RecordingPath = _recording?.RecordingPath, IsRecording = _recording?.IsRecording ?? false,
            HasApiKey = _garmothApiKey.Length > 0, UploadBlocked = _sessionSubmitted || _garmothIntervals.IsBlocked ||
                _garmothPersistenceError is not null || _garmothRestartBlocks.Contains(_sessionId),
            AutomaticSuspended = _garmothIntervals.AutomaticSuspended,
            ShutdownFailed = _shutdownFailed,
        };
        State = State with { SilverHistory = _silverHistory.Update(State) };
        if (_historyChanged)
        {
            History = Array.AsReadOnly(_historyEntries.Select(entry => entry with
            {
                Totals = new Dictionary<string, long>(entry.Totals, StringComparer.OrdinalIgnoreCase),
                ManualLootItems = entry.ManualLootItems.ToArray(),
                GarmothPendingCorrectionIntervals = entry.GarmothPendingCorrectionIntervals.ToArray(),
            }).ToArray());
            _historyChanged = false;
        }
        Changed?.Invoke();
    }

    private bool _shutdownFailed;

    public Task PrepareUpdateRestartAsync() => RunPreparedUpdateAsync(() => Task.CompletedTask);

    public async Task RunPreparedUpdateAsync(Func<Task> install)
    {
        if (_disposed || _shutdownStarted || _uiRunning || IsBusy)
            throw new InvalidOperationException("Bitte die Session pausieren und laufende Vorgänge abwarten.");
        _operationInProgress = true;
        try
        {
            // Keep every resource and the in-memory aggregate alive until both
            // files have been saved. On failure the host can remain open/retry.
            RefreshPendingState();
            PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true);
            if (!TrySaveSettings())
                throw new IOException("Die Einstellungen konnten nicht gespeichert werden.");
            PublishState();
            await install();
        }
        finally
        {
            _operationInProgress = false;
            PublishState();
        }
    }

    public async Task ShutdownAsync()
    {
        var pending = _shutdownTask ??= ShutdownCoreAsync();
        await pending;
        // A failed save must leave a live service that can retry shutdown later.
        if (!_disposed && ReferenceEquals(_shutdownTask, pending)) _shutdownTask = null;
    }

    private async Task ShutdownCoreAsync()
    {
        if (_disposed) return;
        _shutdownStarted = true;
        _shutdownFailed = false;
        _sessionClock.Pause();
        _inactivityTimer.Pause();
        try
        {
            await _captureSession.StopAsync();
            CompleteCaptureSegment(DateTimeOffset.UtcNow);
            _uiRunning = false;
            // A possibly committed HTTP request must settle before disposing its
            // client or forgetting its upload/history guard.
            await Task.WhenAll(_operationTask, _automaticUploadTask, _tickTask);
            if (_priceRefreshTask is { } pricing) await pricing;
            if (_classDetectionTask is { } classes) await classes;
            RefreshPendingState();
            PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true);
            SavePendingHistory();
            if (!TrySaveSettings()) throw new IOException("Die Einstellungen konnten nicht gespeichert werden.");
        }
        catch (Exception exception)
        {
            _shutdownFailed = true;
            _shutdownStarted = false;
            _uiRunning = false;
            SetStatus("Grindcrest bleibt geöffnet: " + exception.Message, true);
            return;
        }
        _priceLifetime.Cancel();
        try
        {
            _uiRunning = false;
            _captureSession.Stopped -= CaptureSessionStopped;
            await _captureSession.DisposeAsync();
            _recording?.Dispose();
            _recording = null;
            _garmothApiKey = string.Empty;
            PublishState();
            _disposed = true;
            _analyzer.Dispose();
            _lootScrollMonitor.Dispose();
            _priceProvider.Dispose();
            _garmothClient.Dispose();
            _uiMailbox.Dispose();
            _priceLifetime.Dispose();
        }
        finally { _disposed = true; }
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync();
}
