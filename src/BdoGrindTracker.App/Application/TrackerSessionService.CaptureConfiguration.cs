using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly CaptureConfigurationCatalog _captureConfigurations;
    private readonly Func<string?> _browseCaptureConfiguration;
    private bool _captureConfigurationReloadPending;

    public Task<CaptureConfigurationScan> ScanCaptureConfigurationsAsync()
    {
        var selected = Preferences.CaptureConfigurationPath;
        var bound = _analyzer.CaptureCalibration;
        return Task.Run(() =>
        {
            var scan = _captureConfigurations.Scan(selected);
            return bound is null ? scan : scan with { ActivePath = bound.GameVariablePath };
        });
    }

    public async Task<CaptureConfigurationOption?> BrowseCaptureConfigurationAsync()
    {
        CaptureConfigurationOption? option = null;
        var result = await RunOperationAsync(async () =>
        {
            var path = _browseCaptureConfiguration();
            if (path is not null) option = await Task.Run(() => _captureConfigurations.Inspect(path));
        });
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
        return option;
    }

    public async Task<TrackerCommandResult> SelectCaptureConfigurationAsync(string? gameVariablePath)
    {
        if (_hasSession) return new("Die BDO-Konfiguration kann erst für eine neue Session geändert werden.");
        var changed = !string.Equals(gameVariablePath, Preferences.CaptureConfigurationPath, StringComparison.OrdinalIgnoreCase);
        var result = await SavePreferencesAsync(Preferences with { CaptureConfigurationPath = gameVariablePath });
        if (!result.Succeeded) return new(result.Error);
        // Also reload when the same file was corrected on disk or automatic selection changed.
        return await RunOperationAsync(() =>
        {
            if (_hasSession) throw new InvalidOperationException("Die BDO-Konfiguration kann erst für eine neue Session geändert werden.");
            _captureConfigurations.Read(gameVariablePath);
            if (!changed) RebuildCaptureAnalyzer();
            // OCR readiness is reported separately by TrackerState. A saved, valid
            // file selection remains successful even when a language pack is missing.
            SetStatus("BDO-Konfiguration übernommen. " + (_analyzer.IsAvailable
                ? "Prüfe die Erfassungsbereiche in der Vorschau." : _analyzer.Status));
            return Task.CompletedTask;
        });
    }

    public async Task<CaptureConfigurationPreview> PreviewCaptureConfigurationAsync(string? gameVariablePath)
    {
        CaptureConfigurationPreview preview = new();
        var result = await RunOperationAsync(async () =>
        {
            if (_uiRunning || _captureSession.IsRunning || _captureSession.HasPendingAnalysis)
                throw new InvalidOperationException("Bitte das Tracking pausieren, bevor du die Erfassungsbereiche prüfst.");
            var calibration = _captureConfigurations.Read(gameVariablePath);
            var usesSessionGeometry = _hasSession && _analyzer.CaptureCalibration is { } bound &&
                string.Equals(calibration.GameVariablePath, bound.GameVariablePath, StringComparison.OrdinalIgnoreCase);
            if (usesSessionGeometry) calibration = _analyzer.CaptureCalibration!;
            var configuration = _captureConfigurations.Describe(calibration);
            preview = new(Configuration: configuration);
            var monitor = Monitors.FirstOrDefault(monitor => monitor.DeviceName == Preferences.MonitorDeviceName);
            if (monitor is null && !_captureSession.UsesWindowCapture)
                throw new InvalidOperationException("Bitte zuerst den Spielbildschirm auswählen.");
            if (_captureSession.UsesWindowCapture && _prepareWindowCapture is not null && !await _prepareWindowCapture())
                throw new InvalidOperationException("Die Vorschau wurde abgebrochen.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            preview = await Task.Run(() =>
            {
                using var frame = _captureSession.CapturePreview(monitor?.Bounds ?? Rectangle.Empty, timeout.Token);
                return LootCapturePreviewBuilder.Create(frame, configuration) with { UsesSessionGeometry = usesSessionGeometry };
            });
        });
        return result.Succeeded ? preview : preview with { Error = result.Error };
    }

    private void RebuildCaptureAnalyzer()
    {
        if (_captureSession.HasPendingAnalysis)
            throw new InvalidOperationException("Die vorherige Texterkennung wird noch beendet. Bitte erneut versuchen.");
        var language = Preferences.GameLanguage == "auto" ? _gameLanguageDetection.Language ?? "en" : Preferences.GameLanguage;
        var replacement = _analyzerFactory(language);
        var previous = _analyzer;
        _analyzer = replacement;
        _captureConfigurationReloadPending = false;
        previous.Dispose();
        _checkedOcrGameLanguage = null;
        _missingOcrLanguageTag = null;
        _ocrLanguageError = null;
        Interlocked.Exchange(ref _lastCaptureStopError, null);
        RefreshMissingOcrLanguageOffer();
    }

    private string? BrowseCaptureConfigurationFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = AppText.Translate("BDO-Konfiguration auswählen", Preferences.UiLanguage),
            Filter = AppText.Translate("BDO-Konfiguration (gameVariable.xml)|gameVariable.xml|XML-Dateien (*.xml)|*.xml", Preferences.UiLanguage),
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Black Desert"),
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}
