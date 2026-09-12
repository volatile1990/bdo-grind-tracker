using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private string? _historyPersistenceError;
    private bool _historyDirty;
    private TimeSpan _lastCheckpointDuration;
    private DateTimeOffset _nextCheckpointRetry;
    internal static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(15);
    private readonly HashSet<string> _sessionManualLootItems = new(StringComparer.OrdinalIgnoreCase);
    private bool _sessionGarmothLocallyModified;

    private void SaveHistoryEntries()
    {
        try
        {
            _historyStore.Save(_historyEntries);
            _historyPersistenceError = null;
            _historyDirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _historyDirty = true;
            _historyPersistenceError = "Verlauf noch nicht gespeichert. Die Session bleibt in Grindcrest erhalten. " + exception.Message;
            throw;
        }
    }

    private void SavePendingHistory()
    {
        if (_historyDirty) SaveHistoryEntries();
    }

    public Task<TrackerCommandResult> SaveSessionAsync() => RunOperationAsync(() =>
    {
        if (_historyStore.LoadError is not null)
        {
            var recovered = _historyStore.Load();
            if (_historyStore.LoadError is { } error) throw new IOException(error);
            var knownIds = _historyEntries.Select(entry => entry.SessionId).ToHashSet();
            _historyEntries.AddRange(recovered.Where(entry => !knownIds.Contains(entry.SessionId)));
            SortHistory();
            InitializeGarmothUploadJournal();
        }
        if (_garmothPersistenceError is not null)
        {
            InitializeGarmothUploadJournal();
            if (_garmothPersistenceError is { } error) throw new IOException(error);
        }
        RefreshPendingState();
        PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true);
        SavePendingHistory();
        if (!TrySaveSettings()) throw new IOException(_settingsSaveError);
        SetStatus("Der aktuelle Stand ist im Verlauf gespeichert.");
        return Task.CompletedTask;
    });

    private void SaveCheckpointIfDue()
    {
        if (!_uiRunning || _operationInProgress || _sessionClock.Elapsed - _lastCheckpointDuration < CheckpointInterval ||
            DateTimeOffset.UtcNow < _nextCheckpointRetry) return;
        _nextCheckpointRetry = DateTimeOffset.UtcNow.AddSeconds(15);
        PersistCurrentSession(DateTimeOffset.UtcNow);
    }

    private void PersistCurrentSession(DateTimeOffset updatedAt, bool throwOnError = false,
        Guid? pendingGarmothCorrectionInterval = null, LootSessionSnapshot? proposedSnapshot = null)
    {
        PersistCurrentHistory(updatedAt, throwOnError, pendingGarmothCorrectionInterval);
        if (_historyPersistenceError is null) PersistCurrentSessionCheckpoint(updatedAt, throwOnError, proposedSnapshot);
    }

    private void PersistCurrentHistory(DateTimeOffset updatedAt, bool throwOnError,
        Guid? pendingGarmothCorrectionInterval)
    {
        if (!_hasSession || _demoMode || _sessionSpotId is null ||
            _sessionClock.Elapsed < TimeSpan.Zero || _sessionSummary.Totals.Count == 0)
            return;
        var totals = _sessionSummary.Totals.Where(static pair => pair.Value >= 0)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (totals.Count == 0) return;
        var valuation = SilverValuation.Calculate(totals, Prices, Preferences.Tax);
        UpdateAgrisSession();
        UpdateExperienceSession();
        var duration = _sessionClock.Elapsed;
        var agris = _agrisSessionTracker.Snapshot(duration);
        var experience = _experienceSessionTracker.Snapshot(duration);
        var entry = new LootHistoryEntry
        {
            SessionId = _sessionId,
            StartedAt = _sessionStartedAt ?? updatedAt - _sessionClock.Elapsed,
            UpdatedAt = updatedAt,
            Duration = duration,
            AgrisActiveDuration = agris.ActiveDuration,
            AgrisObservedDuration = agris.ObservedDuration,
            ExperienceGainedPercentagePoints = experience.GainedPercentagePoints,
            ExperienceObservedDuration = experience.ObservedDuration,
            ExperienceStartLevel = experience.StartLevel,
            ExperienceEndLevel = experience.EndLevel,
            SpotId = _sessionSpotId,
            CharacterClass = (_sessionClass ?? SelectedCharacterClass)?.DisplayName,
            Totals = totals,
            SilverBeforeTax = valuation.BeforeTax,
            SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete,
            ManualLootItems = _sessionManualLootItems.ToArray(),
            GarmothLocallyModified = _sessionGarmothLocallyModified,
            GarmothPendingCorrectionIntervals = pendingGarmothCorrectionInterval is { } intervalId ? [intervalId] : [],
        };
        var index = _historyEntries.FindIndex(candidate => candidate.SessionId == entry.SessionId);
        if (index >= 0)
            _historyEntries[index] = entry with
            {
                GarmothUploadedAt = _historyEntries[index].GarmothUploadedAt,
                GarmothUploadBlocked = _historyEntries[index].GarmothUploadBlocked,
                GarmothLocallyModified = _sessionGarmothLocallyModified || _historyEntries[index].GarmothLocallyModified,
                GarmothPendingCorrectionIntervals = _historyEntries[index].GarmothPendingCorrectionIntervals
                    .Concat(entry.GarmothPendingCorrectionIntervals).Distinct().ToArray(),
            };
        else _historyEntries.Add(entry);
        SortHistory();
        try
        {
            SaveHistoryEntries();
            _recording?.SaveCountSummary(entry.SessionId, updatedAt, entry.Duration, totals);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Shutdown deliberately catches ordinary persistence failures. The
            // update host still needs a durable failure signal before restarting.
            if (_shutdownStarted) _shutdownFailed = true;
            SetStatus("Verlauf nicht gespeichert: " + exception.Message, true);
            if (throwOnError) throw;
        }
    }

    private bool MarkHistoryUploadBlocked(Guid sessionId, bool succeeded, Guid? intervalId = null,
        bool completesSession = false)
    {
        if (_hasSession && sessionId == _sessionId)
        {
            // Consume the latest aggregate before preserving the upload guard;
            // capture may have counted more loot while HTTP was in flight.
            RefreshPendingState();
            PersistCurrentSession(DateTimeOffset.UtcNow);
        }
        var index = _historyEntries.FindIndex(entry => entry.SessionId == sessionId);
        if (index < 0) return true;
        var correctedFrozenRequest = intervalId is { } id &&
            _historyEntries[index].GarmothPendingCorrectionIntervals.Contains(id);
        if (_hasSession && sessionId == _sessionId) _sessionGarmothLocallyModified |= correctedFrozenRequest;
        _historyEntries[index] = _historyEntries[index] with
        {
            GarmothUploadBlocked = true,
            GarmothUploadedAt = succeeded ? DateTimeOffset.UtcNow : null,
            GarmothLocallyModified = _historyEntries[index].GarmothLocallyModified || correctedFrozenRequest,
            GarmothPendingCorrectionIntervals = completesSession ? [] :
                _historyEntries[index].GarmothPendingCorrectionIntervals.Where(candidate => candidate != intervalId).ToArray(),
        };
        _historyChanged = true;
        _historyDirty = true;
        var saved = true;
        try { SaveHistoryEntries(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Keep the in-memory block even if saving fails after a remote write.
            SetStatus("Upload abgeschlossen, sein lokaler Status konnte nicht gespeichert werden: " + exception.Message, true);
            saved = false;
        }
        PublishState();
        return saved;
    }

    public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => RunOperationAsync(() =>
    {
        if (_hasSession && sessionId == _sessionId)
            throw new InvalidOperationException("Die aktuelle Session kann erst nach einer neuen Session gelöscht werden.");
        var entry = _historyEntries.FirstOrDefault(candidate => candidate.SessionId == sessionId);
        if (entry is null) return Task.CompletedTask;
        _historyEntries.Remove(entry);
        _historyChanged = true;
        try
        {
            SaveHistoryEntries();
            SetStatus("Grind aus dem Verlauf gelöscht.");
        }
        catch
        {
            _historyEntries.Add(entry);
            SortHistory();
            throw;
        }
        return Task.CompletedTask;
    });

    public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals,
        string? characterClass = null) => RunOperationAsync(() =>
    {
        ArgumentNullException.ThrowIfNull(totals);
        if (_hasSession && sessionId == _sessionId)
            throw new InvalidOperationException("Die aktuelle Session kann erst nach einer neuen Session bearbeitet werden.");
        var index = _historyEntries.FindIndex(candidate => candidate.SessionId == sessionId);
        if (index < 0) return Task.CompletedTask;
        var cleaned = totals.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) &&
                (pair.Value > 0 || pair.Value == 0 && _historyEntries[index].Totals.ContainsKey(pair.Key)))
            .ToDictionary(static pair => pair.Key.Trim(), static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (cleaned.Count == 0) throw new ArgumentException("Mindestens ein Gegenstand muss erhalten bleiben.");
        var previous = _historyEntries[index];
        // Historical manual corrections only change this saved entry. They must
        // never become live observations or alter the current upload watermark.
        var valuation = SilverValuation.Calculate(cleaned, Prices, Preferences.Tax);
        _historyEntries[index] = previous with
        {
            CharacterClass = characterClass is null
                ? previous.CharacterClass
                : string.IsNullOrWhiteSpace(characterClass) ? null : characterClass.Trim(),
            Totals = cleaned,
            ManualLootItems = previous.ManualLootItems.Concat(cleaned.Keys.Where(name =>
                previous.Totals.GetValueOrDefault(name) != cleaned[name])).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            GarmothLocallyModified = previous.GarmothLocallyModified || ((previous.GarmothUploadBlocked || previous.GarmothUploadedAt is not null) &&
                (cleaned.Count != previous.Totals.Count || cleaned.Any(pair => previous.Totals.GetValueOrDefault(pair.Key) != pair.Value) ||
                 (characterClass is not null && characterClass != previous.CharacterClass))),
            SilverBeforeTax = valuation.BeforeTax,
            SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete,
        };
        _historyChanged = true;
        try
        {
            SaveHistoryEntries();
            SetStatus($"Session für {LootSpotCatalog.GetRequired(previous.SpotId).DisplayName} gespeichert.");
        }
        catch
        {
            _historyEntries[index] = previous;
            _historyChanged = true;
            throw;
        }
        return Task.CompletedTask;
    });

    public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity)
    {
        // Frozen HTTP requests can finish while quantities are corrected. Only
        // another local command or shutdown blocks this short transaction.
        if (_shutdownStarted || _disposed || _operationInProgress)
            throw new InvalidOperationException("Bitte warte, bis der laufende Vorgang abgeschlossen ist.");
        _operationInProgress = true;
        PublishState();
        var task = RunOperationCoreAsync(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
            ArgumentOutOfRangeException.ThrowIfNegative(quantity);
            if (sessionId == _sessionId && (_hasSession || _demoMode))
            {
                RefreshPendingState(publish: false);
                var canonicalName = EditableItemName(_sessionSpotId, _sessionSummary.Totals, itemName);
                if (_demoMode)
                {
                    var totals = CorrectedTotals(_sessionSummary.Totals, canonicalName, quantity, originalQuantity);
                    _sessionSummary = new(totals, totals.Values.Sum(), _sessionSummary.ConfirmedEventCount);
                    _sessionManualLootItems.Add(canonicalName);
                }
                else
                {
                    _uiMailbox.AdjustQuantity(canonicalName, quantity, originalQuantity, snapshot =>
                    {
                        var previousSummary = _sessionSummary;
                        var previousHistory = _historyEntries.ToArray();
                        var previousManual = _sessionManualLootItems.ToArray();
                        var previousModified = _sessionGarmothLocallyModified;
                        _sessionManualLootItems.Add(canonicalName);
                        _sessionGarmothLocallyModified |= quantity != originalQuantity && (_sessionSubmitted ||
                            _historyEntries.Any(entry => entry.SessionId == _sessionId && entry.GarmothUploadBlocked));
                        _sessionSummary = snapshot;
                        var pendingCorrection = snapshot.Totals.GetValueOrDefault(canonicalName) != previousSummary.Totals.GetValueOrDefault(canonicalName)
                            ? _garmothIntervals.PreparedIntervalId : null;
                        try { PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true,
                            pendingGarmothCorrectionInterval: pendingCorrection, proposedSnapshot: snapshot); }
                        catch
                        {
                            _sessionSummary = previousSummary;
                            _historyEntries.Clear();
                            _historyEntries.AddRange(previousHistory);
                            _sessionManualLootItems.Clear();
                            _sessionManualLootItems.UnionWith(previousManual);
                            _sessionGarmothLocallyModified = previousModified;
                            _historyChanged = true;
                            // The history write may have completed before the
                            // current-session checkpoint failed. Keep the disk
                            // history aligned with the rejected edit as well.
                            try { SaveHistoryEntries(); }
                            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                            throw;
                        }
                    }, ObserveGarmothTotals);
                }
            }
            else
            {
                var index = _historyEntries.FindIndex(entry => entry.SessionId == sessionId);
                if (index < 0) throw new InvalidOperationException("Diese Session ist nicht mehr verfügbar.");
                var previous = _historyEntries[index];
                var canonicalName = EditableItemName(previous.SpotId, previous.Totals, itemName);
                var totals = CorrectedTotals(previous.Totals, canonicalName, quantity, originalQuantity);
                var valuation = SilverValuation.Calculate(totals, Prices, Preferences.Tax);
                _historyEntries[index] = previous with
                {
                    Totals = totals,
                    ManualLootItems = previous.ManualLootItems.Append(canonicalName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    GarmothLocallyModified = previous.GarmothLocallyModified ||
                        (quantity != originalQuantity && (previous.GarmothUploadBlocked || previous.GarmothUploadedAt is not null)),
                    SilverBeforeTax = valuation.BeforeTax,
                    SilverAfterTax = valuation.AfterTax,
                    SilverIsComplete = valuation.IsComplete,
                };
                _historyChanged = true;
                try { SaveHistoryEntries(); }
                catch { _historyEntries[index] = previous; throw; }
            }
            SetStatus(_demoMode && sessionId == _sessionId
                ? "Menge in der Demo korrigiert." : "Lootmenge gespeichert.");
            return Task.CompletedTask;
        });
        _operationTask = task;
        return task;
    }

    private static string EditableItemName(string? spotId, IReadOnlyDictionary<string, long> totals, string itemName) =>
        totals.Keys.FirstOrDefault(name => string.Equals(name, itemName, StringComparison.OrdinalIgnoreCase))
        ?? (spotId is null ? null : LootSpotCatalog.GetRequired(spotId).AllowedItems.FirstOrDefault(
            name => string.Equals(name, itemName, StringComparison.OrdinalIgnoreCase)))
        ?? throw new ArgumentException("Dieser Gegenstand gehört nicht zur Session.");

    private static Dictionary<string, long> CorrectedTotals(IReadOnlyDictionary<string, long> totals,
        string itemName, long quantity, long originalQuantity)
    {
        var correction = checked(quantity - originalQuantity);
        var corrected = new Dictionary<string, long>(totals, StringComparer.OrdinalIgnoreCase)
        {
            [itemName] = Math.Max(0, checked(totals.GetValueOrDefault(itemName) + correction)),
        };
        // Keep session-wide counters representable before changing persistence.
        _ = corrected.Values.Sum();
        return corrected;
    }

    private void SortHistory()
    {
        _historyChanged = true;
        _historyDirty = true;
        _historyEntries.Sort(static (left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
        if (_historyEntries.Count > LootHistoryStore.MaximumEntries)
            _historyEntries.RemoveRange(LootHistoryStore.MaximumEntries, _historyEntries.Count - LootHistoryStore.MaximumEntries);
    }
}
