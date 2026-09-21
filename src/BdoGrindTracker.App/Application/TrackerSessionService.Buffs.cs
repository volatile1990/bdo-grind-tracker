using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly BuffMonitor _buffMonitor;
    private readonly BuffLedger _buffLedger = new(BuffPriceCatalog.HistoryDefinitions);
    private DateTimeOffset? _lastBuffObservation;
    private long _buffObservationGeneration;
    private bool _hasBuffObservation;
    private string? _buffProfileError;

    private BuffRecognitionProfile? ReadBuffProfile()
    {
        var path = Preferences.BuffRecognitionProfilePath;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var store = new BuffRecognitionProfileStore(path);
        var profile = store.Load();
        Volatile.Write(ref _buffProfileError, store.LastError);
        return profile is null ? null : profile with
        {
            GameVariablePath = profile.GameVariablePath ?? _analyzer.CaptureCalibration?.GameVariablePath
                ?? Preferences.CaptureConfigurationPath,
        };
    }

    private string BuffStatus => string.IsNullOrWhiteSpace(Preferences.BuffRecognitionProfilePath)
        ? "Buff-Erkennung noch nicht kalibriert. Unter Einstellungen → Buff-Erkennung einrichten."
        : Volatile.Read(ref _buffProfileError) is { } error ? error
        : !_uiRunning ? "Buff-Erkennung pausiert."
        : _sessionClock.IsWaitingForFirstDrop ? "Buff-Erkennung wartet auf den ersten Drop."
        : _buffLedger.Snapshot.Active.Count > 0 ? "Buffs bestätigt · Prüfung alle 10 Sekunden."
        : _buffMonitor.LastDiagnostic is { Length: > 0 } diagnostic ? diagnostic
        : "Noch keine unterstützten Buffs bestätigt. Leiste und Restzeiten müssen sichtbar sein.";

    private void UpdateBuffSession()
    {
        var visible = false;
        if (_hasSession && !_demoMode && _uiRunning && _sessionClock.IsRunning &&
            !_sessionClock.IsWaitingForFirstDrop && _lastCaptureDesktopRegion is { } region)
        {
            try { visible = _captureSession.UsesWindowCapture ? _captureSession.IsRunning : _isLootScrollCaptureVisible(region); }
            catch (Exception) { /* Optional recognition never blocks loot tracking. */ }
        }
        var snapshot = _buffMonitor.Snapshot(_captureSession.ObservationTime, out var generation);
        if (generation != _buffObservationGeneration)
        {
            _buffLedger.BreakContinuity();
            _lastBuffObservation = null;
            _buffObservationGeneration = generation;
        }
        if (!visible || !snapshot.IsKnown)
        {
            _buffLedger.BreakContinuity();
            if (!visible) _buffMonitor.Reset();
            return;
        }
        if (snapshot.ObservedAt is not { } at || at == _lastBuffObservation) return;
        _lastBuffObservation = at;
        _buffLedger.Apply(snapshot.Observations, at, definition =>
            BuffPriceCatalog.GetPrice(definition, Prices));
        _hasBuffObservation = true;
    }

    public async Task<string?> BrowseBuffRecognitionProfileAsync()
    {
        string? selected = null;
        var result = await RunOperationAsync(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Buff-Kalibrierungsprofil auswählen",
                Filter = "Buff-Profil (*.json)|*.json",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() == DialogResult.OK) selected = dialog.FileName;
            return Task.CompletedTask;
        });
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
        return selected;
    }

    public async Task<string?> CalibrateBuffRecognitionAsync()
    {
        string? selected = null;
        var result = await RunOperationAsync(() =>
        {
            if (_hasSession)
                throw new InvalidOperationException("Die Buff-Kalibrierung ist vor einer neuen Session möglich.");
            using var dialog = new BuffCalibrationForm(
                Path.Combine(_settingsStore.BaseDirectory, "buff-profiles"), Preferences.BuffRecognitionProfilePath);
            if (dialog.ShowDialog() == DialogResult.OK) selected = dialog.SavedProfilePath;
            return Task.CompletedTask;
        });
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
        return selected;
    }
}
