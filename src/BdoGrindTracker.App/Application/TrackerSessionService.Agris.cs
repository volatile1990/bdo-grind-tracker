using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly AgrisMonitor _agrisMonitor;
    private readonly AgrisSessionTracker _agrisSessionTracker = new();
    private long _agrisObservationGeneration;

    private AgrisState UpdateAgrisSession()
    {
        var now = DateTimeOffset.UtcNow;
        var running = _hasSession && !_demoMode && _uiRunning && _sessionClock.IsRunning;
        var visible = false;
        if (running && _lastCaptureDesktopRegion is { } region)
        {
            try { visible = _captureSession.UsesWindowCapture ? _captureSession.IsRunning : _isLootScrollCaptureVisible(region); }
            catch (Exception) { /* Missing HUD visibility never blocks loot tracking. */ }
        }
        var snapshot = _agrisMonitor.Snapshot(now, out var generation);
        var elapsed = _sessionClock.Elapsed;
        if (generation != _agrisObservationGeneration)
        {
            _agrisSessionTracker.Pause(elapsed);
            _agrisObservationGeneration = generation;
        }
        var state = visible ? snapshot : AgrisState.Unknown;
        _agrisSessionTracker.Update(elapsed, state, running, now);
        return state;
    }
}
