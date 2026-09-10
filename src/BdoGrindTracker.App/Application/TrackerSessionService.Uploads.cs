using System.Text.Json;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private GarmothUploadJournalStore _garmothJournal = null!;
    private string? _garmothPersistenceError;
    private readonly HashSet<Guid> _garmothRestartBlocks = [];

    private void InitializeGarmothUploadJournal()
    {
        _garmothJournal = new(Path.Combine(_settingsStore.BaseDirectory, GarmothUploadJournalStore.FileName));
        try
        {
            var attempts = _garmothJournal.Load().Where(attempt => attempt.BlocksAfterRestart).ToArray();
            _garmothPersistenceError = null;
            foreach (var attempt in attempts) _garmothRestartBlocks.Add(attempt.Draft.SourceSessionId!.Value);
            var succeededBySession = attempts.GroupBy(attempt => attempt.Draft.SourceSessionId!.Value)
                .ToDictionary(group => group.Key, group => group.Where(attempt => attempt.Outcome == GarmothUploadStatus.Succeeded)
                    .Select(attempt => attempt.CompletedAt).Max());
            var unresolvedSessions = attempts.Where(attempt => attempt.Outcome != GarmothUploadStatus.Succeeded)
                .Select(attempt => attempt.Draft.SourceSessionId!.Value).ToHashSet();
            var possiblyCommittedIntervals = attempts.Select(attempt => attempt.Draft.LocalSessionId).ToHashSet();
            for (var index = 0; index < _historyEntries.Count; index++)
            {
                var entry = _historyEntries[index];
                if (!_garmothRestartBlocks.Contains(entry.SessionId)) continue;
                var succeededAt = succeededBySession.GetValueOrDefault(entry.SessionId);
                _historyEntries[index] = entry with
                {
                    GarmothUploadBlocked = true,
                    // An earlier successful hour must not disguise a later
                    // unfinished/unknown attempt as a confirmed transfer.
                    GarmothUploadedAt = unresolvedSessions.Contains(entry.SessionId) ? null : entry.GarmothUploadedAt ?? succeededAt,
                    GarmothLocallyModified = entry.GarmothLocallyModified ||
                        entry.GarmothPendingCorrectionIntervals.Any(possiblyCommittedIntervals.Contains),
                    GarmothPendingCorrectionIntervals = [],
                };
                if (!entry.GarmothUploadBlocked || entry.GarmothUploadedAt != _historyEntries[index].GarmothUploadedAt ||
                    entry.GarmothLocallyModified != _historyEntries[index].GarmothLocallyModified ||
                    entry.GarmothPendingCorrectionIntervals.Length > 0)
                    _historyDirty = true;
                _historyChanged = true;
            }
        }
        catch (Exception exception) when (IsJournalFailure(exception))
        {
            _garmothPersistenceError = "Das Garmoth-Uploadjournal konnte nicht gelesen werden. Uploads sind zum Schutz vor Dubletten gesperrt. " + exception.Message;
        }
    }

    private GarmothUploadPreview CreateCurrentGarmothUploadPreview()
    {
        if (_garmothPersistenceError is { } error) return GarmothUploadPreview.Unavailable(error);
        if ((_historyPersistenceError ?? _historyStore.LoadError) is { } historyError)
            return GarmothUploadPreview.Unavailable(historyError);
        if (_demoMode) return GarmothUploadPreview.Unavailable("Demo-Sessions werden nicht hochgeladen.");
        if (!_hasSession) return GarmothUploadPreview.Unavailable("Starte zuerst eine Live-Session.", "/", "Zur Live-Session");
        if (_sessionSubmitted || _garmothIntervals.IsBlocked || _garmothRestartBlocks.Contains(_sessionId))
            return GarmothUploadPreview.Unavailable("Weitere Uploads dieser Session sind gesperrt. Bitte den Status in Garmoth prüfen.");
        var interval = _garmothIntervals.PreviewManual(_sessionClock.Elapsed, _sessionSummary.Totals,
            _sessionStartedAt ?? DateTimeOffset.UtcNow);
        return interval is null ? GarmothUploadPreview.Unavailable("Ein Upload wird gerade ausgeführt.") : CreateIntervalPreview(interval);
    }

    private GarmothUploadPreview CreateIntervalPreview(GarmothUploadInterval interval) =>
        GarmothUploadPreview.Create(interval.Id, _sessionId, _sessionSpotId,
            (_sessionClass ?? SelectedCharacterClass)?.DisplayName, interval.ActiveDuration, interval.Totals,
            interval.StartedAt, Prices, Preferences.Tax);

    public Task<TrackerCommandResult> UploadAsync() => RunOperationAsync(() => UploadCurrentSessionCoreAsync());

    public Task<TrackerCommandResult> UploadConfirmedAsync(GarmothUploadPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.IsReady || preview.Draft?.SourceSessionId is not { } sessionId)
            return Task.FromResult(new TrackerCommandResult("Die Uploadvorschau ist nicht verfügbar."));
        return _hasSession && sessionId == _sessionId
            ? RunOperationAsync(() => UploadCurrentSessionCoreAsync(preview))
            : RunOperationAsync(() => UploadHistoryCoreAsync(sessionId, preview));
    }

    private async Task UploadCurrentSessionCoreAsync(GarmothUploadPreview? confirmed = null)
    {
        if (_sessionSubmitted || _garmothIntervals.IsBlocked || _garmothRestartBlocks.Contains(_sessionId) || !_hasSession || _demoMode)
            throw new InvalidOperationException("Diese Session kann derzeit nicht übertragen werden.");
        EnsureGarmothUploadAvailable();
        RefreshPendingState();
        _garmothUploadInProgress = true;
        PublishState();
        GarmothUploadInterval? interval = null;
        try
        {
            if (_uiRunning) await StopTrackingAsync();
            RefreshPendingState();
            if (confirmed is null) await RefreshPricesAsync();
            interval = _garmothIntervals.PrepareManual(_sessionClock.Elapsed, _sessionSummary.Totals,
                _sessionStartedAt ?? DateTimeOffset.UtcNow);
            if (interval is null)
                throw new ArgumentException("Kein neuer Loot mit mindestens einer vollen Minute seit dem letzten Upload vorhanden.");
            var preview = ConfirmedOrCurrent(CreateIntervalPreview(interval), confirmed);
            SetStatus("Noch nicht übertragener Grind wird gesendet …");
            var result = await SendJournaledGarmothAsync(preview);
            _garmothIntervals.Complete(interval, result);
            _sessionSubmitted |= result.BlocksAnotherUpload;
            var historySaved = !result.BlocksAnotherUpload || MarkHistoryUploadBlocked(_sessionId,
                result.Status == GarmothUploadStatus.Succeeded, interval.Id, completesSession: true);
            SetUploadResult(result, historySaved, _sessionSubmitted ? " Zum Weitergrinden eine neue Sitzung starten." : "");
        }
        catch (Exception exception)
        {
            if (interval is not null)
                _garmothIntervals.Complete(interval, new(GarmothUploadStatus.Rejected, "Lokale Upload-Vorbereitung fehlgeschlagen."));
            SetStatus("Garmoth nicht gesendet: " + exception.Message, true);
        }
        finally { _garmothUploadInProgress = false; PublishState(); }
    }

    internal Task UploadHourlyToGarmothAsync()
    {
        if (!_automaticUploadTask.IsCompleted) return Task.CompletedTask;
        return _automaticUploadTask = UploadHourlyCoreAsync();
    }

    private async Task UploadHourlyCoreAsync()
    {
        if (!Preferences.AutoUpload || !_hasSession || IsBusy || _sessionSubmitted || _shutdownStarted ||
            _garmothIntervals.IsBlocked || _garmothIntervals.AutomaticSuspended || _garmothPersistenceError is not null ||
            _garmothRestartBlocks.Contains(_sessionId)) return;
        var interval = _garmothIntervals.PrepareAutomatic();
        if (interval is null) return;
        _garmothUploadInProgress = true;
        PublishState();
        try
        {
            EnsureGarmothUploadAvailable();
            await RefreshPricesAsync();
            var preview = ConfirmedOrCurrent(CreateIntervalPreview(interval), null);
            SetStatus("Garmoth automatisch: abgeschlossene Grindstunde wird übertragen …");
            var result = await SendJournaledGarmothAsync(preview);
            _garmothIntervals.Complete(interval, result);
            var historySaved = !result.BlocksAnotherUpload || MarkHistoryUploadBlocked(_sessionId,
                result.Status == GarmothUploadStatus.Succeeded, interval.Id);
            var guidance = result.Status == GarmothUploadStatus.Succeeded
                ? " Nur dieser Stundenabschnitt wurde übertragen."
                : _garmothIntervals.IsBlocked
                    ? " Weitere Uploads dieser Sitzung sind gesperrt. Tracking läuft weiter; bitte in Garmoth prüfen."
                    : " Auto-Upload angehalten. Korrigiere den Schlüssel oder wähle auf der Garmoth-Seite Automatik fortsetzen.";
            SetUploadResult(result, historySaved, guidance);
        }
        catch (Exception exception)
        {
            _garmothIntervals.Complete(interval, new(GarmothUploadStatus.Rejected, "Lokale Upload-Vorbereitung fehlgeschlagen."));
            SetStatus("Garmoth nicht gesendet: " + exception.Message + " Auto-Upload angehalten.", true);
        }
        finally { _garmothUploadInProgress = false; PublishState(); }
    }

    public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId)
    {
        if (_hasSession && sessionId == _sessionId) return UploadAsync();
        return RunOperationAsync(() => UploadHistoryCoreAsync(sessionId));
    }

    private async Task UploadHistoryCoreAsync(Guid sessionId, GarmothUploadPreview? confirmed = null)
    {
        var entry = _historyEntries.FirstOrDefault(e => e.SessionId == sessionId);
        if (entry is null || entry.GarmothUploadBlocked || _garmothRestartBlocks.Contains(sessionId))
            throw new InvalidOperationException("Diese Session ist nicht mehr verfügbar oder bereits gegen weitere Uploads gesperrt.");
        EnsureGarmothUploadAvailable();
        _garmothUploadInProgress = true;
        PublishState();
        try
        {
            if (confirmed is null) await RefreshPricesAsync();
            var preview = ConfirmedOrCurrent(GarmothUploadPreview.ForHistory(entry, Prices, Preferences.Tax), confirmed);
            SetStatus("Grind aus dem Verlauf wird übertragen …");
            var result = await SendJournaledGarmothAsync(preview);
            var historySaved = !result.BlocksAnotherUpload || MarkHistoryUploadBlocked(sessionId,
                result.Status == GarmothUploadStatus.Succeeded, preview.Draft!.LocalSessionId, completesSession: true);
            SetUploadResult(result, historySaved, "");
        }
        finally { _garmothUploadInProgress = false; PublishState(); }
    }

    private void EnsureGarmothUploadAvailable()
    {
        if (_garmothPersistenceError is { } error) throw new IOException(error);
        if ((_historyPersistenceError ?? _historyStore.LoadError) is { } historyError) throw new IOException(historyError);
        if (_garmothApiKey.Length == 0)
            throw new ArgumentException("Bitte zuerst im Bereich Garmoth einen API-Schlüssel hinterlegen.");
    }

    private static GarmothUploadPreview ConfirmedOrCurrent(GarmothUploadPreview current, GarmothUploadPreview? confirmed)
    {
        if (!current.IsReady) throw new ArgumentException(current.Error);
        if (confirmed is null) return current;
        if (!confirmed.IsReady || !confirmed.HasSameSessionData(current))
            throw new InvalidOperationException("Die Session hat sich seit der Vorschau geändert. Bitte die Uploadvorschau erneut öffnen.");
        var draft = confirmed.Draft! with { LocalSessionId = current.Draft!.LocalSessionId };
        return confirmed with { Draft = draft, Payload = GarmothSessionPayload.Create(draft) };
    }

    private async Task<GarmothUploadResult> SendJournaledGarmothAsync(GarmothUploadPreview preview)
    {
        var draft = preview.Draft!;
        Guid attemptId;
        try { attemptId = _garmothJournal.Begin(draft); }
        catch (Exception exception) when (IsJournalFailure(exception))
        {
            throw new IOException("Die Uploadabsicht konnte nicht sicher gespeichert werden. Es wurde nichts gesendet. " + exception.Message, exception);
        }
        GarmothUploadResult result;
        try { result = await _garmothClient.UploadAsync(draft, _garmothApiKey); }
        catch (Exception)
        {
            // Any unexpected failure after dispatch may follow a remote commit.
            // Never clear the intent, retry, or echo an exception containing a key.
            result = new(GarmothUploadStatus.OutcomeUnknown, "Upload-Ergebnis unklar. Bitte direkt in Garmoth prüfen; kein erneuter Upload.");
        }
        try { _garmothJournal.Complete(attemptId, result.Status); }
        catch (Exception exception) when (IsJournalFailure(exception))
        {
            _garmothPersistenceError = "Das Upload-Ergebnis konnte lokal nicht gespeichert werden. Die Uploadabsicht bleibt erhalten; weitere Uploads sind zum Schutz vor Dubletten gesperrt. " + exception.Message;
            _garmothIntervals.BlockFurtherUploads();
            result = result with
            {
                Status = result.Status == GarmothUploadStatus.Rejected ? GarmothUploadStatus.OutcomeUnknown : result.Status,
                Message = result.Message + " " + _garmothPersistenceError,
                LocalPersistenceFailed = true,
            };
        }
        var notes = new List<string>();
        if (preview.Payload!.OmittedItems.Count > 0)
            notes.Add("Ohne Garmoth-Zuordnung ausgelassen: " + string.Join(", ", preview.Payload.OmittedItems) + ".");
        if (!preview.SilverIsComplete) notes.Add("Silber ist eine Teilsumme; fehlende Preise wurden nicht geschätzt.");
        if (preview.SilverIsStale) notes.Add("Silber verwendet den letzten verfügbaren Preisstand.");
        return notes.Count == 0 ? result : result with { Message = result.Message + " " + string.Join(" ", notes) };
    }

    private void SetUploadResult(GarmothUploadResult result, bool historySaved, string guidance) =>
        SetStatus(result.Message + guidance + (historySaved ? "" :
            " Der lokale Verlaufstatus konnte nicht gespeichert werden. Der Dublettenschutz bleibt im Uploadjournal erhalten."),
            result.Status != GarmothUploadStatus.Succeeded || result.LocalPersistenceFailed || !historySaved);

    private static bool IsJournalFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException;

    internal static GarmothSessionDraft CreateHistoricalGarmothDraft(
        LootHistoryEntry entry, LootPriceSnapshot prices, SilverTaxOptions tax)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(tax);
        // This projection helper is also used independently of upload permissions.
        var preview = GarmothUploadPreview.ForHistory(entry with { GarmothUploadBlocked = false, GarmothUploadedAt = null }, prices, tax);
        if (!preview.IsReady) throw new ArgumentException(preview.Error);
        return preview.Draft!;
    }
}
