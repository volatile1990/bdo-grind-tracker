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
    private bool _ocrRestartRequired;

    public Task<TrackerCommandResult> InstallOcrLanguageAsync() => RunOperationAsync(async () =>
    {
        if (_uiRunning || _demoMode || _missingOcrLanguageTag is null) return;
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
        SetOcrInstallationStatus($"Windows installiert die Texterkennung ({offeredTag}) … Das kann einige Minuten dauern.");
        try
        {
            var result = await _ocrLanguageInstaller.InstallAsync(offeredTag);
            switch (result.Status)
            {
                case WindowsOcrInstallStatus.Installed:
                    try
                    {
                        EnsureOcrLanguage(_checkedOcrGameLanguage!);
                        SetOcrInstallationStatus("OCR-Sprachpaket installiert und geprüft. Du kannst das Tracking starten.");
                    }
                    catch (WindowsOcrLanguageUnavailableException)
                    {
                        SetOcrInstallationStatus("Windows hat die Installation abgeschlossen, die Texterkennung ist aber noch nicht verfügbar. " +
                            "Bitte prüfe erneut oder starte Windows neu.", error: true);
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
            _isInstallingOcrLanguage = false;
            PublishState();
        }
    });

    public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => RunOperationAsync(() =>
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
