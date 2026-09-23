using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Diagnostics;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly RollingDebugLog _debugLog;
    private (bool Enabled, int Hours)? _debugLogConfiguration;
    private DateTimeOffset _nextDebugLogCleanup;
    private DateTimeOffset _nextDebugState;
    private (Guid SessionId, bool Running, bool HasSession, bool Error, string Status)? _lastDebugStatus;

    private Guid? DebugLogSessionId => _hasSession ? _sessionId : null;

    private void ConfigureDebugLogging()
    {
        // A failed settings read provides fallback values, not permission to
        // shorten a previously configured retention window.
        if (_settingsStore.LoadError is not null) return;
        var configuration = (Preferences.AutomaticDebugLogging, Preferences.DebugLogRetentionHours);
        if (_debugLogConfiguration == configuration) return;
        _debugLog.Configure(configuration.AutomaticDebugLogging, configuration.DebugLogRetentionHours);
        _debugLogConfiguration = configuration;
        _nextDebugLogCleanup = _autoStartTimeProvider.GetUtcNow().AddMinutes(1);
        _nextDebugState = DateTimeOffset.MinValue;
        _lastDebugStatus = null;
        _debugLog.Write(DebugLogSessionId, "logging-configured", new
        {
            SessionId = _sessionId,
            RetentionHours = configuration.DebugLogRetentionHours,
            AppVersion = typeof(TrackerSessionService).Assembly.GetName().Version?.ToString(),
        });
    }

    private Task CleanupDebugLogsIfDueAsync()
    {
        if (_debugLogConfiguration is null) return Task.CompletedTask;
        var now = _autoStartTimeProvider.GetUtcNow();
        if (now < _nextDebugLogCleanup) return Task.CompletedTask;
        _nextDebugLogCleanup = now.AddMinutes(1);
        // Cleanup also runs when capture is paused or logging has been disabled.
        return Task.Run(_debugLog.Cleanup);
    }

    private void RecordDebugFrame(FrameAnalysisResult analysis, CapturedFrameMetadata metadata, TimeSpan duration)
    {
        _debugLog.Write(DebugLogSessionId, "loot-frame", new
        {
            SessionId = _sessionId,
            Capture = metadata,
            AnalysisDurationMilliseconds = duration.TotalMilliseconds,
            Analysis = new
            {
                analysis.SpotId, analysis.VariantName, analysis.RecognizedLines,
                analysis.TextRecognitionBackend, analysis.TextRecognitionLanguage,
                analysis.MeanMatchConfidence, analysis.PreparedRowCount, analysis.NonBlankRowCount,
                analysis.OcrRowCount, analysis.CatalogMatchCount,
                analysis.PanelRegion, analysis.RareBandRegion, analysis.CaptureCalibration,
                analysis.NewEvents, analysis.Observations, analysis.TrackingResult.Decisions,
                analysis.TrackingResult.NormalCaptureIndex, analysis.TrackingResult.NormalReconciliation,
                analysis.LootProjection, ParsingRevision = analysis.LifetimeParsingContext?.Revision,
                analysis.Recovery, analysis.RareRecovery, analysis.RowReviews,
            },
        });
    }

    private void RecordDebugState()
    {
        var sessionId = State.HasSession ? State.SessionId : (Guid?)null;
        var status = (State.SessionId, State.IsRunning, State.HasSession, State.IsError, State.Status);
        if (_lastDebugStatus != status)
        {
            _lastDebugStatus = status;
            _debugLog.Write(sessionId, "status", new
            {
                State.SessionId, State.HasSession, State.IsRunning, State.IsError, State.Status,
            });
        }

        var now = _autoStartTimeProvider.GetUtcNow();
        if (now < _nextDebugState) return;
        _nextDebugState = now.AddSeconds(5);
        // Explicit fields keep API keys, settings paths and growing history lists
        // out of logs. The full UI state is deliberately never serialized.
        _debugLog.Write(sessionId, "session-state", new
        {
            State.SessionId, State.HasSession, State.IsRunning, State.IsDemo, State.IsWaitingForFirstDrop,
            State.SpotId, State.CharacterClassId, State.Elapsed, State.Loot,
            State.LootScroll, State.Agris, State.Experience, State.CombatStats,
            Buffs = State.Buffs is { } buffs ? new
            {
                buffs.Active, buffs.Usage,
                ConsumptionCount = buffs.Consumptions.Count,
                RecentConsumptions = buffs.Consumptions.TakeLast(20).ToArray(),
            } : null,
            State.BuffStatus,
            Rotation = new
            {
                State.Rotation.SpotId, State.Rotation.Status, State.Rotation.TrackingState,
                State.Rotation.Synchronized, State.Rotation.IsAfk, State.Rotation.Elapsed,
                State.Rotation.CurrentPhaseId, State.Rotation.LastInterruptionReason,
                State.Rotation.SmallScarecrows, State.Rotation.Events, State.Rotation.Completed,
                State.Rotation.Error,
            },
            State.AutoStartStatus, State.TrackingBlockedReason,
        });
    }
}
