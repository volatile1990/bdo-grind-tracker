using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly Func<IAutomaticGrindMonitor> _createAutoStartMonitor;
    private IAutomaticGrindMonitor? _autoStartMonitor;
    private CancellationTokenSource? _autoStartCancellation;
    private Task _autoStartCancellationWork = Task.CompletedTask;
    private Task<AutoStartDetection?>? _autoStartProbe;
    private Task _autoStartPendingAnalysis = Task.CompletedTask;
    private Task _autoStartStopTask = Task.CompletedTask;
    private bool _autoStartSuspended;
    private string? _autoStartError;

    private IAutomaticGrindMonitor CreateAutomaticGrindMonitor()
    {
        // Read/validate files before allocating a native notification thread.
        var calibration = _captureConfigurations.Read(Preferences.CaptureConfigurationPath);
        return new AutomaticGrindMonitor(new GrindStandbyCapture(), new GrindStartVisualDetector(),
            calibration, _analyzerFactory);
    }

    private bool CanWatchForGrind => Preferences.AutoStartGrinding && !_autoStartSuspended &&
        !_uiRunning && _autoStartStopTask.IsCompleted && _autoStartPendingAnalysis.IsCompleted && !_captureSession.IsRunning && !_captureSession.HasPendingAnalysis && !IsBusy &&
        !_demoMode && !_sessionSubmitted && !_shutdownStarted && !_disposed &&
        _analyzer.IsAvailable && _ocrLanguageError is null && _currentSessionStore.LoadError is null &&
        _settingsSaveError is null && _currentSessionPersistenceError is null;

    private string? AutoStartStatus => !Preferences.AutoStartGrinding ? null :
        _autoStartSuspended ? _autoStartError ?? "Automatik pausiert · bitte wieder aktivieren." :
        _uiRunning ? "Automatische Grind-Erkennung aktiviert." :
        !CanWatchForGrind ? "Automatik wartet · Tracking ist derzeit nicht verfügbar." :
        _autoStartMonitor?.IsConfirming == true ? "Automatik prüft neue Monsterdrops …" :
        _autoStartMonitor?.IsGameForeground == true ? "Automatik bereit · warte auf neue Monsterdrops." :
        "Automatik bereit · warte auf Black Desert im Vordergrund.";

    public Task<TrackerCommandResult> RearmAutoStartAsync() => RunOperationAsync(() =>
    {
        _autoStartSuspended = false;
        _autoStartError = null;
        if (Preferences.AutoStartGrinding) TrySaveSettings();
        return Task.CompletedTask;
    });

    private async Task TickAutoStartAsync()
    {
        if (!CanWatchForGrind)
        {
            // Never join a worker here: manual commands remain responsive while
            // cancellation unwinds an in-progress native recognition request.
            RequestAutoStartCancellation();
            return;
        }
        if (_autoStartProbe is { IsCompleted: false }) return;
        if (_autoStartProbe is { } completed)
        {
            _autoStartProbe = null;
            try
            {
                using var detection = await completed;
                _autoStartPendingAnalysis = _autoStartMonitor?.PendingAnalysis ?? Task.CompletedTask;
                if (detection is not null && CanWatchForGrind && _autoStartMonitor?.IsGameForeground == true)
                {
                    // Take ownership before entering the normal command gate. Its
                    // cleanup can now stop the standby source without losing replay.
                    await RunOperationAsync(() => StartTrackingAsync(detection));
                    if (!_uiRunning)
                    {
                        _autoStartSuspended = true;
                        _autoStartError = "Autostart konnte nicht starten. Ursache prüfen und Automatik wieder aktivieren.";
                    }
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                _autoStartPendingAnalysis = _autoStartMonitor?.PendingAnalysis ?? Task.CompletedTask;
            }
            catch (Exception exception)
            {
                // Ordinary focus/resize transitions may invalidate a sample.
                if (_autoStartMonitor?.IsGameForeground != true)
                {
                    await StopAutoStartProbeAsync();
                    return;
                }
                _autoStartSuspended = true;
                _autoStartError = "Automatik angehalten: " + exception.Message;
                await StopAutoStartProbeAsync();
                return;
            }
        }
        if (!CanWatchForGrind) return;
        try
        {
            _autoStartMonitor ??= _createAutoStartMonitor();
            var language = ResolveGameLanguage();
            var monitor = _autoStartMonitor;
            _autoStartCancellation?.Dispose();
            _autoStartCancellation = new CancellationTokenSource();
            _autoStartCancellationWork = Task.CompletedTask;
            var token = _autoStartCancellation.Token;
            _autoStartProbe = Task.Run(() => monitor.CheckAsync(language, token), CancellationToken.None);
        }
        catch (Exception exception)
        {
            _autoStartSuspended = true;
            _autoStartError = "Automatik angehalten: " + exception.Message;
        }
    }

    private Task StopAutoStartProbeAsync()
    {
        // Shutdown can enter while a user command is awaiting cancellation.
        // Both callers join one cleanup instead of disposing its native inputs twice.
        if (!_autoStartStopTask.IsCompleted) return _autoStartStopTask;
        return _autoStartStopTask = StopAutoStartProbeCoreAsync();
    }

    private async Task StopAutoStartProbeCoreAsync()
    {
        RequestAutoStartCancellation();
        var pending = _autoStartProbe;
        var cancellation = _autoStartCancellation;
        var cancellationWork = _autoStartCancellationWork;
        var monitor = _autoStartMonitor;
        _autoStartProbe = null;
        _autoStartCancellation = null;
        _autoStartMonitor = null;
        try
        {
            if (pending is not null)
            {
                try { (await pending)?.Dispose(); }
                catch (Exception) { /* A canceled attempt cannot start or change a session. */ }
            }
        }
        finally
        {
            var cancellationCleanup = DisposeCancellationAsync(cancellation, cancellationWork);
            _autoStartPendingAnalysis = Task.WhenAll(_autoStartPendingAnalysis,
                monitor?.PendingAnalysis ?? Task.CompletedTask, cancellationCleanup);
            monitor?.Dispose();
        }
    }

    private void RequestAutoStartCancellation()
    {
        if (_autoStartCancellation is { IsCancellationRequested: false } cancellation)
            _autoStartCancellationWork = ObserveCancellationAsync(cancellation.CancelAsync());
    }

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); } catch (Exception) { }
    }

    private static async Task DisposeCancellationAsync(CancellationTokenSource? cancellation, Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        finally { cancellation?.Dispose(); }
    }
}
