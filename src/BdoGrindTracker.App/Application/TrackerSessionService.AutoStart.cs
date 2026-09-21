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
    private readonly TimeProvider _autoStartTimeProvider;
    private long? _autoStartRetryStarted;
    private string? _autoStartError;
    internal static readonly TimeSpan AutoStartRetryInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan AutomaticGrindConfirmationTimeout = TimeSpan.FromMinutes(1);
    internal const int AutomaticGrindRequiredDrops = 5;
    private bool _provisionalAutomaticGrind;
    private int _automaticGrindDropCount;
    private DateTimeOffset? _automaticGrindLastArrival;
    private bool _automaticGrindNeedsCheckpoint;

    private void BeginAutomaticGrindConfirmation(AutoStartDetection detection)
    {
        _provisionalAutomaticGrind = true;
        _automaticGrindDropCount = 1;
        _automaticGrindLastArrival = detection.DetectedDropAt ??
            (detection.Frames.Count > 0 ? detection.Frames[^1].Metadata.CapturedAtUtc : null);
        _automaticGrindNeedsCheckpoint = false;
    }

    // Called under the publication gate, once for a new arrival. Multiple items
    // in a frame, replayed start frames and quantity corrections are not new drops.
    private void ObserveAutomaticGrindDrop(DateTimeOffset? arrival)
    {
        if (!_provisionalAutomaticGrind || arrival is null ||
            _automaticGrindLastArrival is { } previous && arrival <= previous) return;
        _automaticGrindLastArrival = arrival;
        Interlocked.Increment(ref _automaticGrindDropCount);
    }

    private void ConfirmAutomaticGrindIfReady()
    {
        if (!_provisionalAutomaticGrind || Volatile.Read(ref _automaticGrindDropCount) < AutomaticGrindRequiredDrops) return;
        _provisionalAutomaticGrind = false;
        _automaticGrindNeedsCheckpoint = true;
    }

    private void ResetAutomaticGrindConfirmation()
    {
        _provisionalAutomaticGrind = false;
        _automaticGrindDropCount = 0;
        _automaticGrindLastArrival = null;
        _automaticGrindNeedsCheckpoint = false;
    }

    private IAutomaticGrindMonitor CreateAutomaticGrindMonitor()
    {
        // Read/validate files before allocating a native notification thread.
        var calibration = _captureConfigurations.Read(Preferences.CaptureConfigurationPath);
        return new AutomaticGrindMonitor(new GrindStandbyCapture(), new GrindStartVisualDetector(),
            calibration, _analyzerFactory);
    }

    private bool CanWatchForGrind => Preferences.AutoStartGrinding &&
        !_uiRunning && _autoStartStopTask.IsCompleted && _autoStartPendingAnalysis.IsCompleted && !_captureSession.IsRunning && !_captureSession.HasPendingAnalysis && !IsBusy &&
        !_demoMode && !_sessionSubmitted && !_shutdownStarted && !_disposed &&
        _analyzer.IsAvailable && _ocrLanguageError is null && _currentSessionStore.LoadError is null &&
        _settingsSaveError is null && _currentSessionPersistenceError is null;

    private string? AutoStartStatus => !Preferences.AutoStartGrinding ? null :
        _autoStartError is { } error ? error :
        _provisionalAutomaticGrind ? "Grind gestartet · Bestätigung nach 5 getrennten Drops. Nach 1 Minute ohne neuen Drop wird die unbestätigte Session verworfen." :
        _uiRunning ? "Automatische Grind-Erkennung aktiviert." :
        !CanWatchForGrind ? "Automatik wartet · Tracking ist derzeit nicht verfügbar." :
        _autoStartMonitor?.IsConfirming == true ? "Automatik prüft neue Monsterdrops …" :
        _autoStartMonitor?.IsGameForeground == true ? "Automatik bereit · warte auf neue Monsterdrops." :
        "Automatik bereit · warte auf Black Desert im Vordergrund.";

    private void ResetAutoStartRetry()
    {
        _autoStartRetryStarted = null;
        _autoStartError = null;
    }

    private void ScheduleAutoStartRetry(string error)
    {
        _autoStartRetryStarted = _autoStartTimeProvider.GetTimestamp();
        _autoStartError = error;
    }

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
                ResetAutoStartRetry();
                _autoStartPendingAnalysis = _autoStartMonitor?.PendingAnalysis ?? Task.CompletedTask;
                if (detection is not null && CanWatchForGrind && _autoStartMonitor?.IsGameForeground == true)
                {
                    // Take ownership before entering the normal command gate. Its
                    // cleanup can now stop the standby source without losing replay.
                    var result = await RunOperationAsync(() => StartTrackingAsync(detection));
                    if (!_uiRunning)
                    {
                        ScheduleAutoStartRetry(result.Error ?? "Autostart konnte nicht starten.");
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
                ScheduleAutoStartRetry(exception.Message);
                await StopAutoStartProbeAsync();
                return;
            }
        }
        if (!CanWatchForGrind) return;
        // Remain enabled after errors, but avoid repeatedly allocating capture
        // sources or retrying a failed start on every UI tick.
        if (_autoStartRetryStarted is { } retryStarted &&
            _autoStartTimeProvider.GetElapsedTime(retryStarted) < AutoStartRetryInterval) return;
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
            ScheduleAutoStartRetry(exception.Message);
            await StopAutoStartProbeAsync();
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
