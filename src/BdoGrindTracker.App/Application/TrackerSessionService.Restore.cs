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
            saved = saved with
            {
                UpdatedAt = newerHistory.UpdatedAt,
                StartedAt = newerHistory.StartedAt,
                Duration = newerHistory.Duration,
                SpotId = newerHistory.SpotId,
                CharacterClassId = newerHistory.CharacterClass is null ? null
                    : CompanionCharacterClassCatalog.Classes.FirstOrDefault(character =>
                        character.DisplayName == newerHistory.CharacterClass)?.Id ?? saved.CharacterClassId,
                Totals = new(newerHistory.Totals, StringComparer.OrdinalIgnoreCase),
                ManualLootItems = saved.ManualLootItems.Concat(newerHistory.ManualLootItems)
                    .Where(newerHistory.Totals.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                GarmothLocallyModified = saved.GarmothLocallyModified || newerHistory.GarmothLocallyModified,
                AgrisActiveDuration = newerHistory.AgrisActiveDuration ?? TimeSpan.Zero,
                AgrisObservedDuration = newerHistory.AgrisObservedDuration ?? TimeSpan.Zero,
                ExperienceGainedPercentagePoints = newerHistory.ExperienceGainedPercentagePoints,
                ExperienceObservedDuration = newerHistory.ExperienceObservedDuration ?? TimeSpan.Zero,
                ExperienceStartLevel = newerHistory.ExperienceStartLevel,
                ExperienceEndLevel = newerHistory.ExperienceEndLevel,
                // Changed history cannot prove the matching remote watermark.
                Uploads = reconciledHistory ? null : saved.Uploads,
            };

        if (saved.Uploads is { } uploads) _garmothIntervals.RestoreState(uploads);
        else _garmothIntervals.SuspendAutomatic();
        var summary = new LootSessionSnapshot(saved.Totals, saved.Totals.Values.Sum(), saved.ConfirmedEventCount);
        _uiMailbox.Restore(summary, saved.ManualLootItems);
        _sessionClock.RestorePaused(saved.Duration);
        _agrisSessionTracker.Restore(saved.Duration, new(saved.AgrisActiveDuration, saved.AgrisObservedDuration));
        _experienceSessionTracker.Restore(saved.Duration, new(saved.ExperienceGainedPercentagePoints,
            saved.ExperienceObservedDuration, saved.ExperienceStartLevel, saved.ExperienceEndLevel));
        _sessionId = saved.SessionId;
        _sessionStartedAt = saved.StartedAt;
        _sessionSpotId = saved.SpotId;
        _sessionClass = CompanionCharacterClassCatalog.FindById(saved.CharacterClassId);
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

    private void PersistCurrentSessionCheckpoint(DateTimeOffset updatedAt, bool throwOnError = false,
        LootSessionSnapshot? proposedSnapshot = null)
    {
        if (!_hasSession || _demoMode) return;
        try
        {
            UpdateAgrisSession();
            UpdateExperienceSession();
            var checkpoint = _uiMailbox.ReadSnapshot(aggregate =>
            {
                var summary = proposedSnapshot ?? aggregate;
                var duration = _sessionClock.Elapsed;
                var agris = _agrisSessionTracker.Snapshot(duration);
                var experience = _experienceSessionTracker.Snapshot(duration);
                var uploads = _garmothIntervals.ExportState();
                if (proposedSnapshot is not null)
                    uploads = uploads with
                    {
                        ObservedTotals = new(summary.Totals, StringComparer.OrdinalIgnoreCase),
                        WindowStartedAt = uploads.WindowStartedAt ?? _sessionStartedAt ?? updatedAt - duration,
                    };
                return new CurrentSessionSnapshot
                {
                    SessionId = _sessionId,
                    StartedAt = _sessionStartedAt,
                    UpdatedAt = updatedAt,
                    Duration = duration,
                    SpotId = _sessionSpotId,
                    CharacterClassId = _sessionClass?.Id,
                    SessionSubmitted = _sessionSubmitted,
                    Totals = new(summary.Totals, StringComparer.OrdinalIgnoreCase),
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
