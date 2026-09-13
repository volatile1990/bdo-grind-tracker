using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly IWindowsOcrLanguageInstaller _ocrLanguageInstaller;
    private readonly Func<string, ILootFrameAnalyzer> _analyzerFactory;
    private string? _missingOcrLanguageTag;
    private string? _ocrLanguageError;
    private string? _ocrInstallationStatus;
    private string? _checkedOcrGameLanguage;
    private bool _isInstallingOcrLanguage;
    private int? _ocrInstallationPercent;
    private bool _ocrRestartRequired;
    private Task<WindowsOcrInstallResult>? _ocrInstallerTask;
    private TaskCompletionSource<WindowsOcrInstallResult>? _ocrAvailabilityCompletion;
    private string? _installingOcrGameLanguage;
    private string? _ocrInstallationLogPath;

    public Task<TrackerCommandResult> InstallOcrLanguageAsync() => RunOperationAsync(async () =>
    {
        if (_uiRunning || _demoMode || _missingOcrLanguageTag is null) return;
        if (_ocrInstallerTask is { IsCompleted: false })
        {
            SetOcrInstallationStatus("Der vorherige Windows-Installationsprozess läuft noch. " +
                "Du kannst die Texterkennung erneut prüfen; eine zweite Installation ist noch nicht möglich.");
            return;
        }
        var offeredTag = _missingOcrLanguageTag;
        try
        {
            // Check again before elevating: the user may have installed the feature
            // externally, or Black Desert's automatic language may have changed.
            EnsureOcrLanguage(ResolveGameLanguage());
            SetOcrInstallationStatus("Die Windows-Texterkennung ist bereits verfügbar. Du kannst das Tracking starten.");
            return;
        }
        catch (WindowsOcrLanguageUnavailableException exception)
        {
            if (exception.LanguageTag != offeredTag)
            {
                SetOcrInstallationStatus("Die Spielsprache hat sich geändert. Bitte installiere das jetzt angezeigte OCR-Sprachpaket.");
                return;
            }
        }
        catch (Exception exception)
        {
            SetOcrInstallationStatus(exception.Message, error: true);
            return;
        }

        if (_ocrRestartRequired) return;
        _isInstallingOcrLanguage = true;
        _ocrInstallationPercent = null;
        _installingOcrGameLanguage = _checkedOcrGameLanguage;
        _ocrInstallationLogPath = null;
        var available = new TaskCompletionSource<WindowsOcrInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocrAvailabilityCompletion = available;
        SetOcrInstallationStatus($"Windows installiert die Texterkennung ({offeredTag}) … Das kann einige Minuten dauern.");
        var reportProgress = true;
        var nextAvailabilityCheck = TimeSpan.FromSeconds(30);
        var progress = new Progress<WindowsOcrInstallProgress>(update =>
        {
            // Progress posts to the UI context; ignore queued reports after completion.
            if (reportProgress && !_disposed && !_shutdownStarted && !available.Task.IsCompleted)
            {
                _ocrInstallationLogPath = update.LogPath;
                _ocrInstallationPercent = update.Percent;
                if (update.Elapsed >= nextAvailabilityCheck)
                {
                    nextAvailabilityCheck = update.Elapsed + TimeSpan.FromSeconds(30);
                    if (TryRecoverOcrInstallation()) return;
                }
                SetOcrInstallationStatus(update.Message);
            }
        });
        try
        {
            // Windows can make OCR usable before its servicing process exits.
            // Keep that process observed and guarded, but release the UI after a real OCR check.
            var installation = ObserveOcrInstallationAsync(_ocrLanguageInstaller.InstallAsync(offeredTag, progress));
            _ocrInstallerTask = installation;
            PublishState();
            var completed = await Task.WhenAny(installation, available.Task);
            // A successful manual check can arrive while a process-exit continuation is queued.
            var result = await (available.Task.IsCompletedSuccessfully ? available.Task : completed);
            reportProgress = false;
            if (_shutdownStarted || _disposed) return;
            switch (result.Status)
            {
                case WindowsOcrInstallStatus.Installed:
                    try
                    {
                        EnsureOcrLanguage(_installingOcrGameLanguage!);
                        SetOcrInstallationStatus(installation.IsCompleted
                            ? "OCR-Sprachpaket installiert und geprüft. Du kannst das Tracking starten."
                            : "Die Windows-Texterkennung ist verfügbar und geprüft. Du kannst das Tracking starten.");
                    }
                    catch (WindowsOcrLanguageUnavailableException)
                    {
                        SetOcrInstallationStatus("Windows hat die Installation abgeschlossen, die Texterkennung ist aber noch nicht verfügbar. " +
                            "Bitte prüfe erneut. Falls das Paket weiterhin fehlt, prüfe das Windows-Installationsprotokoll: " +
                            (result.LogPath ?? WindowsOcrLanguageInstaller.DefaultLogPath), error: true);
                    }
                    break;
                case WindowsOcrInstallStatus.RestartRequired:
                    _ocrRestartRequired = true;
                    SetOcrInstallationStatus("Das OCR-Sprachpaket wurde installiert. Windows benötigt einen Neustart, bevor es verwendet werden kann.");
                    break;
                case WindowsOcrInstallStatus.Cancelled:
                    SetOcrInstallationStatus("Die Installation wurde abgebrochen. Du kannst sie jederzeit erneut starten.");
                    break;
                default:
                    SetOcrInstallationStatus(result.Error ?? "Das OCR-Sprachpaket konnte nicht installiert werden. Bitte versuche es erneut.", error: true);
                    break;
            }
        }
        catch (Exception exception)
        {
            SetOcrInstallationStatus("OCR-Installation: " + exception.Message, error: true);
        }
        finally
        {
            reportProgress = false;
            _ocrAvailabilityCompletion = null;
            _installingOcrGameLanguage = null;
            _isInstallingOcrLanguage = false;
            _ocrInstallationPercent = null;
            PublishState();
        }
    });

    public Task<TrackerCommandResult> RecheckOcrLanguageAsync()
    {
        // This is the one read/check operation allowed while the install command owns the UI lock.
        if (_isInstallingOcrLanguage && _ocrAvailabilityCompletion is not null && !_shutdownStarted && !_disposed)
        {
            if (!TryRecoverOcrInstallation())
                SetOcrInstallationStatus("Die Texterkennung ist noch nicht nutzbar. Windows arbeitet weiter an der Installation; " +
                    "du kannst erneut prüfen, sobald das Paket verfügbar ist.");
            return Task.FromResult(TrackerCommandResult.Success);
        }
        return RunOperationAsync(() =>
        {
            if (_uiRunning || _demoMode) return Task.CompletedTask;
            try
            {
                EnsureOcrLanguage(ResolveGameLanguage());
                SetOcrInstallationStatus("Die Windows-Texterkennung ist verfügbar. Du kannst das Tracking starten.");
            }
            catch (Exception exception)
            {
                SetOcrInstallationStatus(_ocrRestartRequired
                    ? "Windows benötigt einen Neustart, bevor das installierte OCR-Sprachpaket verwendet werden kann."
                    : exception.Message, error: true);
            }
            return Task.CompletedTask;
        });
    }

    private bool TryRecoverOcrInstallation()
    {
        if (!_isInstallingOcrLanguage || _uiRunning || _shutdownStarted || _disposed ||
            _ocrAvailabilityCompletion is null || _installingOcrGameLanguage is null) return false;
        if (_ocrAvailabilityCompletion.Task.IsCompleted) return true;
        try
        {
            // Capability state or a 100% console message alone never enables tracking.
            EnsureOcrLanguage(_installingOcrGameLanguage);
            return _ocrAvailabilityCompletion.TrySetResult(new(WindowsOcrInstallStatus.Installed, LogPath: _ocrInstallationLogPath));
        }
        catch (Exception) { return false; }
    }

    private static async Task<WindowsOcrInstallResult> ObserveOcrInstallationAsync(Task<WindowsOcrInstallResult> installation)
    {
        // After OCR recovery/shutdown no continuation may mutate the tracker or its analyzer.
        try { return await installation.ConfigureAwait(false); }
        catch (Exception exception)
        {
            return new(WindowsOcrInstallStatus.Failed, "OCR-Installation: " + exception.Message);
        }
    }

    private string ResolveGameLanguage()
    {
        if (Preferences.GameLanguage == "auto") _gameLanguageDetection = _detectGameLanguage();
        var language = Preferences.GameLanguage == "auto" ? _gameLanguageDetection.Language : Preferences.GameLanguage;
        if (language is null)
        {
            _missingOcrLanguageTag = null;
            _ocrLanguageError = _analyzer.IsAvailable ? null : _gameLanguageDetection.Message;
            _ocrInstallationStatus = null;
            _ocrRestartRequired = false;
            throw new InvalidOperationException(_gameLanguageDetection.Message);
        }
        return language;
    }

    private void EnsureOcrLanguage(string language)
    {
        if (_captureSession.HasPendingAnalysis)
            throw new InvalidOperationException("Die abgebrochene Texterkennung wird noch beendet. Bitte Grindcrest neu starten, falls sie nicht reagiert.");
        if (_captureConfigurationReloadPending) RebuildCaptureAnalyzer();
        if (_checkedOcrGameLanguage != language)
        {
            _ocrInstallationStatus = null;
            _ocrRestartRequired = false;
        }
        _checkedOcrGameLanguage = language;
        _missingOcrLanguageTag = null;
        _ocrLanguageError = null;
        try
        {
            // Only startup failures lack an analyzer to reconfigure. Keep a real
            // analyzer and its session ledger intact across language retries.
            if (!_analyzer.IsAvailable && _analyzer.MissingOcrLanguageTag is not null)
            {
                var replacement = _analyzerFactory(language);
                var previous = _analyzer;
                _analyzer = replacement;
                previous.Dispose();
            }
            if (!_analyzer.IsAvailable)
            {
                if (_analyzer.MissingOcrLanguageTag is { } tag)
                    throw new WindowsOcrLanguageUnavailableException(tag);
                throw new InvalidOperationException(_analyzer.Status);
            }
            _analyzer.ConfigureGameLanguage(language);
            _ocrRestartRequired = false;
        }
        catch (WindowsOcrLanguageUnavailableException exception)
        {
            _missingOcrLanguageTag = exception.LanguageTag;
            _ocrLanguageError = exception.Message;
            throw;
        }
    }

    private void RefreshMissingOcrLanguageOffer()
    {
        var language = Preferences.GameLanguage == "auto" ? _gameLanguageDetection.Language : Preferences.GameLanguage;
        if (language is null && _analyzer.MissingOcrLanguageTag is not null)
        {
            _missingOcrLanguageTag = null;
            _ocrLanguageError = _gameLanguageDetection.Message;
            _ocrInstallationStatus = null;
            _ocrRestartRequired = false;
            return;
        }
        if (_checkedOcrGameLanguage == language) return;
        if (_missingOcrLanguageTag is not null || _analyzer.MissingOcrLanguageTag is not null)
        {
            _missingOcrLanguageTag = null;
            _ocrLanguageError = null;
            if (language is not null)
            {
                try
                {
                    EnsureOcrLanguage(language);
                    _status = "Bereit für deine nächste Session.";
                    _isError = false;
                }
                catch (Exception exception)
                {
                    // EnsureOcrLanguage retains only confirmed missing packages.
                    // Preference persistence remains independent of OCR readiness.
                    _status = exception.Message;
                    _isError = true;
                }
            }
        }
        else if (_checkedOcrGameLanguage != language)
        {
            _ocrLanguageError = null;
        }
        _ocrInstallationStatus = null;
        _ocrRestartRequired = false;
    }

    private void SetOcrInstallationStatus(string message, bool error = false)
    {
        _ocrInstallationStatus = message;
        SetStatus(message, error);
    }
}
