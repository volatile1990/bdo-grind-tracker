using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly ExperienceMonitor _experienceMonitor;
    private readonly ExperienceSessionTracker _experienceSessionTracker = new();
    private long _experienceObservationGeneration;

    private ExperienceState UpdateExperienceSession()
    {
        var now = DateTimeOffset.UtcNow;
        var running = _hasSession && !_demoMode && _uiRunning && _sessionClock.IsRunning;
        var visible = false;
        if (running && _lastCaptureDesktopRegion is { } region)
        {
            try { visible = _captureSession.UsesWindowCapture ? _captureSession.IsRunning : _isLootScrollCaptureVisible(region); }
            catch (Exception) { /* Missing game visibility never blocks loot tracking. */ }
        }
        var snapshot = _experienceMonitor.Snapshot(now, out var generation);
        var elapsed = _sessionClock.Elapsed;
        if (generation != _experienceObservationGeneration)
        {
            _experienceSessionTracker.Pause(elapsed);
            _experienceObservationGeneration = generation;
        }
        var state = visible ? snapshot : ExperienceState.Unknown;
        _experienceSessionTracker.Update(elapsed, state, running && !_sessionClock.IsWaitingForFirstDrop, now);
        return state;
    }
}
