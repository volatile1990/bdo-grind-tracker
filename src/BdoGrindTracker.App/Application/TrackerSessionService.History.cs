using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private void PersistCurrentSession(DateTimeOffset updatedAt, bool throwOnError = false)
    {
        if (!_hasSession || _demoMode || _sessionSpotId is null ||
            _sessionClock.Elapsed <= TimeSpan.Zero || _sessionSummary.ItemTypeCount == 0)
            return;
        var totals = _sessionSummary.Totals.Where(static pair => pair.Value > 0)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (totals.Count == 0) return;
        var valuation = SilverValuation.Calculate(totals, Prices, Preferences.Tax);
        var entry = new LootHistoryEntry
        {
            SessionId = _sessionId,
            StartedAt = _sessionStartedAt ?? updatedAt - _sessionClock.Elapsed,
            UpdatedAt = updatedAt,
            Duration = _sessionClock.Elapsed,
            SpotId = _sessionSpotId,
            CharacterClass = (_sessionClass ?? SelectedCharacterClass)?.DisplayName,
            Totals = totals,
            SilverBeforeTax = valuation.BeforeTax,
            SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete,
        };
        var index = _historyEntries.FindIndex(candidate => candidate.SessionId == entry.SessionId);
        if (index >= 0)
            _historyEntries[index] = entry with
            {
                GarmothUploadedAt = _historyEntries[index].GarmothUploadedAt,
                GarmothUploadBlocked = _historyEntries[index].GarmothUploadBlocked,
            };
        else _historyEntries.Add(entry);
        SortHistory();
        try { _historyStore.Save(_historyEntries); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Shutdown deliberately catches ordinary persistence failures. The
            // update host still needs a durable failure signal before restarting.
            if (_shutdownStarted) _shutdownFailed = true;
            SetStatus("Verlauf nicht gespeichert: " + exception.Message, true);
            if (throwOnError) throw;
        }
    }

    private void MarkHistoryUploadBlocked(Guid sessionId, bool succeeded)
    {
        if (_hasSession && sessionId == _sessionId)
        {
            // Consume the latest aggregate before preserving the upload guard;
            // capture may have counted more loot while HTTP was in flight.
            RefreshPendingState();
            PersistCurrentSession(DateTimeOffset.UtcNow);
        }
        var index = _historyEntries.FindIndex(entry => entry.SessionId == sessionId);
        if (index < 0) return;
        _historyEntries[index] = _historyEntries[index] with
        {
            GarmothUploadBlocked = true,
            GarmothUploadedAt = succeeded ? DateTimeOffset.UtcNow : null,
        };
        _historyChanged = true;
        try { _historyStore.Save(_historyEntries); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Keep the in-memory block even if saving fails after a remote write.
            SetStatus("Upload abgeschlossen, sein lokaler Status konnte nicht gespeichert werden: " + exception.Message, true);
        }
        PublishState();
    }

    public Task DeleteHistoryAsync(Guid sessionId) => RunOperationAsync(() =>
    {
        if (_hasSession && sessionId == _sessionId)
            throw new InvalidOperationException("Die aktuelle Session kann erst nach einer neuen Session gelöscht werden.");
        var entry = _historyEntries.FirstOrDefault(candidate => candidate.SessionId == sessionId);
        if (entry is null) return Task.CompletedTask;
        _historyEntries.Remove(entry);
        _historyChanged = true;
        try
        {
            _historyStore.Save(_historyEntries);
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

    public Task UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals) => RunOperationAsync(() =>
    {
        ArgumentNullException.ThrowIfNull(totals);
        if (_hasSession && sessionId == _sessionId)
            throw new InvalidOperationException("Die aktuelle Session kann erst nach einer neuen Session bearbeitet werden.");
        var index = _historyEntries.FindIndex(candidate => candidate.SessionId == sessionId);
        if (index < 0) return Task.CompletedTask;
        var cleaned = totals.Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            .ToDictionary(static pair => pair.Key.Trim(), static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (cleaned.Count == 0) throw new ArgumentException("Mindestens eine Lootmenge muss größer als 0 sein.");
        var previous = _historyEntries[index];
        // Historical manual corrections only change this saved entry. They must
        // never become live observations or alter the current upload watermark.
        var valuation = SilverValuation.Calculate(cleaned, Prices, Preferences.Tax);
        _historyEntries[index] = previous with
        {
            Totals = cleaned,
            SilverBeforeTax = valuation.BeforeTax,
            SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete,
        };
        _historyChanged = true;
        try
        {
            _historyStore.Save(_historyEntries);
            SetStatus($"Lootmengen für {LootSpotCatalog.GetRequired(previous.SpotId).DisplayName} gespeichert.");
        }
        catch
        {
            _historyEntries[index] = previous;
            _historyChanged = true;
            throw;
        }
        return Task.CompletedTask;
    });

    private void SortHistory()
    {
        _historyChanged = true;
        _historyEntries.Sort(static (left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
        if (_historyEntries.Count > LootHistoryStore.MaximumEntries)
            _historyEntries.RemoveRange(LootHistoryStore.MaximumEntries, _historyEntries.Count - LootHistoryStore.MaximumEntries);
    }
}
