using BdoGrindTracker.Core;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private bool SessionSpotIsFrozen => _sessionSubmitted || _garmothUploadInProgress ||
        _garmothIntervals.IsBlocked || _garmothIntervals.HasTransmittedLoot ||
        _garmothRestartBlocks.Contains(_sessionId) ||
        _historyEntries.Any(entry => entry.SessionId == _sessionId &&
            (entry.GarmothUploadBlocked || entry.GarmothUploadedAt is not null));

    private bool CanChangeSpotVariant => _hasSession && !_demoMode && !SessionSpotIsFrozen &&
        _sessionSpotId is { } spotId && HasSelectableSpotVariant(spotId);

    private static bool HasSelectableSpotVariant(string spotId)
    {
        var variants = LootSpotCatalog.VariantsFor(spotId);
        return variants.Count > 1 || variants.Count == 1 && variants[0].Id != spotId;
    }

    private void ApplyDetectedSpot(string? detectedSpotId)
    {
        if (detectedSpotId is null || SessionSpotIsFrozen) return;
        // Shared trash identifies a family. Preserve the user's concrete area
        // across frames, capture completion and a restored analyzer segment.
        if (_sessionSpotId is { } current && LootSpotCatalog.VariantsFor(detectedSpotId)
                .Any(variant => variant.Id == current)) return;
        _sessionSpotId = detectedSpotId;
    }

    public Task<TrackerCommandResult> SelectSpotVariantAsync(Guid sessionId, string spotId) => RunOperationAsync(() =>
    {
        if (sessionId != _sessionId || !_hasSession)
            return SelectHistoricalSpotVariant(sessionId, spotId);
        RefreshPendingState(publish: false);
        if (!CanChangeSpotVariant)
            throw new InvalidOperationException("Der Spot kann für diese Session nicht mehr geändert werden.");
        var variant = LootSpotCatalog.VariantsFor(_sessionSpotId!).FirstOrDefault(candidate => candidate.Id == spotId)
            ?? throw new ArgumentException("Bitte eine Variante des erkannten Spots auswählen.");
        if (_sessionSpotId == variant.Id) return Task.CompletedTask;

        var previousSpotId = _sessionSpotId;
        var previousHistory = _historyEntries.ToArray();
        _sessionSpotId = variant.Id;
        try { PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true); }
        catch
        {
            _sessionSpotId = previousSpotId;
            _historyEntries.Clear();
            _historyEntries.AddRange(previousHistory);
            _historyChanged = true;
            // History may already have been written before the checkpoint
            // failed. Restore it so a rejected selection cannot survive restart.
            try { SaveHistoryEntries(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
        SetStatus("Spot-Auswahl gespeichert: " + variant.DisplayName);
        return Task.CompletedTask;
    });

    private Task SelectHistoricalSpotVariant(Guid sessionId, string spotId)
    {
        EnsureHistoryUploadIdle(sessionId);
        if (_garmothPersistenceError is { } error) throw new IOException(error);
        var index = _historyEntries.FindIndex(entry => entry.SessionId == sessionId);
        if (index < 0) throw new InvalidOperationException("Diese Session ist nicht mehr verfügbar.");
        var previous = _historyEntries[index];
        if (previous.GarmothUploadBlocked || previous.GarmothUploadedAt is not null ||
            _garmothRestartBlocks.Contains(sessionId))
            throw new InvalidOperationException("Der Spot einer bereits übertragenen oder gesperrten Session kann nicht mehr geändert werden.");
        var variant = LootSpotCatalog.VariantsFor(previous.SpotId).FirstOrDefault(candidate => candidate.Id == spotId)
            ?? throw new ArgumentException("Bitte eine Variante des erkannten Spots auswählen.");
        if (previous.SpotId == variant.Id) return Task.CompletedTask;
        _historyEntries[index] = previous with
        {
            SpotId = variant.Id,
            CombatStats = CombatStatsSpotRules.ForSpot(previous.CombatStats, variant.Id),
        };
        _historyChanged = true;
        try { SaveHistoryEntries(); }
        catch
        {
            _historyEntries[index] = previous;
            _historyChanged = true;
            throw;
        }
        SetStatus("Spot-Auswahl gespeichert: " + variant.DisplayName);
        return Task.CompletedTask;
    }
}
