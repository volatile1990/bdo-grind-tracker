using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly CombatStatsMonitor _combatStatsMonitor;
    private CombatStatsState? _sessionCombatStats;

    private CombatStatsState UpdateCombatStatsSession()
    {
        // Re-check saved values even while paused or after the detected spot changes.
        _sessionCombatStats = CombatStatsSpotRules.ForSpot(_sessionCombatStats, _sessionSpotId);
        var now = _captureSession.ObservationTime;
        var visible = false;
        if (_hasSession && !_demoMode && _uiRunning && _lastCaptureDesktopRegion is { } region)
        {
            try { visible = _captureSession.UsesWindowCapture ? _captureSession.IsRunning : _isLootScrollCaptureVisible(region); }
            catch (Exception) { /* Optional HUD evidence must never block tracking. */ }
        }
        if (!visible)
        {
            _combatStatsMonitor.Reset();
            return CombatStatsState.Unknown;
        }

        var state = CombatStatsSpotRules.ForSpot(_combatStatsMonitor.Snapshot(now), _sessionSpotId);
        if (state is null) return CombatStatsState.Unknown;
        // The monitor's reset generation enforces the session boundary. Capture
        // timestamps may use a different clock epoch from a restored checkpoint.
        _sessionCombatStats = state;
        return state;
    }
}
