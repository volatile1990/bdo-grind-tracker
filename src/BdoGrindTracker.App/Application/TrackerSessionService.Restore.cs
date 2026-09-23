using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly CurrentSessionStore _currentSessionStore;
    private string? _currentSessionPersistenceError;
    private bool _restoredSessionNeedsCaptureSetup;

    private void RestoreCurrentSession()
    {
        var saved = _currentSessionStore.Load();
        if (_currentSessionStore.LoadError is { } error)
        {
            _currentSessionPersistenceError = error;
            _status = error;
            _isError = true;
            return;
        }
        _currentSessionPersistenceError = null;
        if (saved is null) return;

        // History is committed before the checkpoint. Recover its newer totals
        // for this same session after an interrupted two-file save; never infer
        // a current session from an unrelated history entry.
        var newerHistory = _historyEntries.FirstOrDefault(entry =>
            entry.SessionId == saved.SessionId && entry.UpdatedAt > saved.UpdatedAt);
        var reconciledHistory = newerHistory is not null &&
            (newerHistory.Duration != saved.Duration || newerHistory.SpotId != saved.SpotId ||
             newerHistory.Totals.Count != saved.Totals.Count ||
             newerHistory.Totals.Any(pair => saved.Totals.GetValueOrDefault(pair.Key) != pair.Value));
        if (newerHistory is not null)
        {
            // The newer record may correct the spot. Compare timestamps only after
            // filtering both candidates for that final spot, so an incompatible newer
            // checkpoint sample cannot hide an older, still applicable history sample.
            var checkpointStats = CombatStatsSpotRules.ForSpot(saved.CombatStats, newerHistory.SpotId);
            var historyStats = CombatStatsSpotRules.ForSpot(newerHistory.CombatStats, newerHistory.SpotId);
            saved = saved with
            {
                UpdatedAt = newerHistory.UpdatedAt,
                Rotations = newerHistory.Rotations,
                Buffs = newerHistory.Buffs ?? saved.Buffs,
                RotationTimeline = newerHistory.RotationTimeline,
                StartedAt = newerHistory.StartedAt,
                Duration = newerHistory.Duration,
                SpotId = newerHistory.SpotId,
                CharacterClassId = newerHistory.CharacterClass is null ? null
                    : CompanionCharacterClassCatalog.Classes.FirstOrDefault(character =>
                        character.DisplayName == newerHistory.CharacterClass)?.Id ?? saved.CharacterClassId,
                Totals = new(newerHistory.Totals, StringComparer.OrdinalIgnoreCase),
                // Legacy history can recover quantities but cannot establish a
                // newer last-drop time for items whose quantities changed.
                DropHistory = newerHistory.DropHistory ?? saved.DropHistory?.Where(drop =>
                    newerHistory.Totals.TryGetValue(drop.ItemName, out var quantity) &&
                    saved.Totals.TryGetValue(drop.ItemName, out var previous) && quantity == previous).ToArray(),
                ManualLootItems = saved.ManualLootItems.Concat(newerHistory.ManualLootItems)
                    .Where(newerHistory.Totals.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                GarmothLocallyModified = saved.GarmothLocallyModified || newerHistory.GarmothLocallyModified,
                AgrisActiveDuration = newerHistory.AgrisActiveDuration ?? TimeSpan.Zero,
                AgrisObservedDuration = newerHistory.AgrisObservedDuration ?? TimeSpan.Zero,
                ExperienceGainedPercentagePoints = newerHistory.ExperienceGainedPercentagePoints,
                ExperienceObservedDuration = newerHistory.ExperienceObservedDuration ?? TimeSpan.Zero,
                ExperienceStartLevel = newerHistory.ExperienceStartLevel,
                ExperienceEndLevel = newerHistory.ExperienceEndLevel,
                // Both records belong to this session. A loot-only correction
                // or legacy history entry must not erase its newer HUD sample.
                CombatStats = historyStats is not null &&
                    historyStats.ObservedAt > (checkpointStats?.ObservedAt ?? DateTimeOffset.MinValue)
                    ? historyStats : checkpointStats,
                // Changed history cannot prove the matching remote watermark.
                Uploads = reconciledHistory ? null : saved.Uploads,
            };
        }

        if (saved.Uploads is { } uploads) _garmothIntervals.RestoreState(uploads);
        else _garmothIntervals.SuspendAutomatic();
        var summary = new LootSessionSnapshot(saved.Totals, saved.Totals.Values.Sum(), saved.ConfirmedEventCount);
        _uiMailbox.Restore(summary, saved.ManualLootItems);
        _dropHistory.Restore(saved.SessionId, summary, saved.Duration, saved.DropHistory);
        _sessionClock.RestorePaused(saved.Duration);
        _agrisSessionTracker.Restore(saved.Duration, new(saved.AgrisActiveDuration, saved.AgrisObservedDuration));
        _experienceSessionTracker.Restore(saved.Duration, new(saved.ExperienceGainedPercentagePoints,
            saved.ExperienceObservedDuration, saved.ExperienceStartLevel, saved.ExperienceEndLevel));
        _sessionId = saved.SessionId;
        _rotationMonitor.RestoreSession(saved.Rotations ?? [], saved.RotationTimeline);
        _sessionStartedAt = saved.StartedAt;
        _sessionSpotId = saved.SpotId;
        _sessionClass = CompanionCharacterClassCatalog.FindById(saved.CharacterClassId);
        _sessionCombatStats = CombatStatsSpotRules.ForSpot(saved.CombatStats, saved.SpotId);
        if (saved.Buffs is { } buffs)
        {
            _buffLedger.Restore(buffs);
            _hasBuffObservation = true;
        }
        else
        {
            // Resuming an existing grind is never a new start, including older
            // checkpoints created before any readable buff observation.
            _buffLedger.Restore(BdoGrindTracker.Core.Buffs.BuffLedgerSnapshot.Empty);
        }
        _sessionSubmitted = saved.SessionSubmitted;
        _sessionSummary = summary;
        _sessionManualLootItems.UnionWith(saved.ManualLootItems);
        _sessionGarmothLocallyModified = saved.GarmothLocallyModified;
        _hasSession = true;
        _uiRunning = false;
        _restoredSessionNeedsCaptureSetup = true;
        _lastCheckpointDuration = saved.Duration;
        Preferences = Preferences with
        {
            GameLanguage = saved.GameLanguage,
            MonitorDeviceName = Monitors.Any(monitor => monitor.DeviceName == saved.MonitorDeviceName)
                ? saved.MonitorDeviceName : Preferences.MonitorDeviceName,
            // Diagnosis recording requires a new explicit choice after restart.
            RecordLoot = false,
            RecordRotation = false,
        };
        if (reconciledHistory || _garmothRestartBlocks.Contains(saved.SessionId)) _garmothIntervals.BlockFurtherUploads();
        _status = saved.SessionSubmitted
            ? "Die zuletzt übertragene Session wurde wiederhergestellt. Für einen weiteren Grind eine neue Session anlegen."
            : "Die letzte Session wurde pausiert wiederhergestellt. Du kannst sie fortsetzen.";
    }

    private void RecoverCurrentSessionIfNeeded()
    {
        if (_currentSessionStore.LoadError is null) return;
        if (_hasSession || _uiRunning || _demoMode)
            throw new IOException("Die gespeicherte Session kann erst ohne laufende Session oder Demo erneut geladen werden.");
        RestoreCurrentSession();
        if (_currentSessionStore.LoadError is { } error) throw new IOException(error);
    }

    // UI delivery can lag behind the drop. Use the activity clock so removing
    // idle time on automatic pause does not also remove the newest marker.
    private IReadOnlyList<SessionDropSample> CaptureDropHistory(LootSessionSnapshot summary, TimeSpan duration,
        bool manualCorrection = false) =>
        _dropHistory.Update(State with
        {
            SessionId = _sessionId,
            HasSession = _hasSession,
            IsDemo = _demoMode,
            Elapsed = duration,
            Loot = summary,
        }, _demoMode ? null : _sessionClock.GetElapsedExcludingTrailingIdle(_inactivityTimer.IdleDuration), manualCorrection);

    private void PersistCurrentSessionCheckpoint(DateTimeOffset updatedAt, bool throwOnError = false,
        LootSessionSnapshot? proposedSnapshot = null)
    {
        if (!_hasSession || _provisionalAutomaticGrind || _demoMode) return;
        try
        {
            UpdateAgrisSession();
            UpdateExperienceSession();
            UpdateCombatStatsSession();
            UpdateBuffSession();
            var checkpoint = _uiMailbox.ReadSnapshot(aggregate =>
            {
                var summary = proposedSnapshot ?? aggregate;
                var duration = _sessionClock.Elapsed;
                // Pair the timing baseline with these exact producer totals. A
                // subsequent UI publish must not regress to an older aggregate.
                _sessionSummary = summary;
                var drops = CaptureDropHistory(summary, duration);
                var agris = _agrisSessionTracker.Snapshot(duration);
                var experience = _experienceSessionTracker.Snapshot(duration);
                var uploads = _garmothIntervals.ExportState(proposedSnapshot?.Totals);
                if (proposedSnapshot is not null)
                    uploads = uploads with
                    {
                        ObservedTotals = new(summary.Totals, StringComparer.OrdinalIgnoreCase),
                        WindowStartedAt = uploads.WindowStartedAt ?? _sessionStartedAt ?? updatedAt - duration,
                    };
                return new CurrentSessionSnapshot
                {
                    SessionId = _sessionId,
                    Rotations = _rotationMonitor.ExportSession(),
                    RotationTimeline = _rotationMonitor.ExportTimeline(),
                    StartedAt = _sessionStartedAt,
                    UpdatedAt = updatedAt,
                    Duration = duration,
                    SpotId = _sessionSpotId,
                    CharacterClassId = _sessionClass?.Id,
                    CombatStats = _sessionCombatStats,
                    Buffs = _hasBuffObservation ? _buffLedger.Snapshot : null,
                    SessionSubmitted = _sessionSubmitted,
                    Totals = new(summary.Totals, StringComparer.OrdinalIgnoreCase),
                    DropHistory = drops,
                    ConfirmedEventCount = summary.ConfirmedEventCount,
                    ManualLootItems = _sessionManualLootItems.ToArray(),
                    GarmothLocallyModified = _sessionGarmothLocallyModified,
                    AgrisActiveDuration = agris.ActiveDuration,
                    AgrisObservedDuration = agris.ObservedDuration,
                    ExperienceGainedPercentagePoints = experience.GainedPercentagePoints,
                    ExperienceObservedDuration = experience.ObservedDuration,
                    ExperienceStartLevel = experience.StartLevel,
                    ExperienceEndLevel = experience.EndLevel,
                    GameLanguage = Preferences.GameLanguage,
                    MonitorDeviceName = Preferences.MonitorDeviceName,
                    RecordLoot = Preferences.RecordLoot,
                    Uploads = uploads,
                };
            });
            _currentSessionStore.Save(checkpoint);
            _currentSessionPersistenceError = null;
            _lastCheckpointDuration = checkpoint.Duration;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            _currentSessionPersistenceError = "Die aktuelle Session ist noch nicht gespeichert. " + error.Message;
            if (_shutdownStarted) _shutdownFailed = true;
            SetStatus(_currentSessionPersistenceError, true);
            if (throwOnError) throw;
        }
    }

    private void ClearCurrentSessionCheckpoint()
    {
        try
        {
            // A durable empty marker distinguishes an explicitly completed
            // session from an interrupted one, including an empty new session.
            _currentSessionStore.Save(null);
            _currentSessionPersistenceError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _currentSessionPersistenceError = "Die neue Session konnte noch nicht angelegt werden. " + error.Message;
            throw;
        }
    }
}
