using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    public Task UploadAsync() => RunOperationAsync(UploadCurrentSessionCoreAsync);

    private async Task UploadCurrentSessionCoreAsync()
    {
        if (_sessionSubmitted || _garmothIntervals.IsBlocked || !_hasSession || _demoMode) return;
        RefreshPendingState();
        if (_sessionSummary.ItemTypeCount == 0) return;
        if (_garmothApiKey.Length == 0) throw new ArgumentException("Bitte zuerst im Bereich Garmoth einen API-Schlüssel hinterlegen.");
        _garmothUploadInProgress = true;
        PublishState();
        GarmothUploadInterval? interval = null;
        try
        {
            if (_uiRunning) await StopTrackingAsync();
            RefreshPendingState();
            interval = _garmothIntervals.PrepareManual(_sessionClock.Elapsed, _sessionSummary.Totals,
                _sessionStartedAt ?? DateTimeOffset.UtcNow);
            if (interval is null)
                throw new ArgumentException("Kein neuer Loot mit mindestens einer vollen Minute seit dem letzten Upload vorhanden.");
            SetStatus("Noch nicht übertragener Grind wird gesendet …");
            var result = await SendGarmothIntervalAsync(interval);
            _garmothIntervals.Complete(interval, result);
            _sessionSubmitted |= result.BlocksAnotherUpload;
            if (result.BlocksAnotherUpload)
                MarkHistoryUploadBlocked(_sessionId, result.Status == GarmothUploadStatus.Succeeded);
            SetStatus(result.Message + (_sessionSubmitted ? " Zum Weitergrinden eine neue Sitzung starten." : ""),
                result.Status is GarmothUploadStatus.Rejected or GarmothUploadStatus.OutcomeUnknown);
        }
        catch (Exception exception)
        {
            if (interval is not null)
                _garmothIntervals.Complete(interval, new(GarmothUploadStatus.Rejected, exception.Message));
            SetStatus("Garmoth nicht gesendet: " + exception.Message, true);
        }
        finally
        {
            _garmothUploadInProgress = false;
            PublishState();
        }
    }

    internal Task UploadHourlyToGarmothAsync()
    {
        if (!_automaticUploadTask.IsCompleted) return Task.CompletedTask;
        return _automaticUploadTask = UploadHourlyCoreAsync();
    }

    private async Task UploadHourlyCoreAsync()
    {
        if (!Preferences.AutoUpload || !_hasSession || IsBusy || _sessionSubmitted || _shutdownStarted ||
            _garmothIntervals.IsBlocked || _garmothIntervals.AutomaticSuspended)
            return;
        var interval = _garmothIntervals.PrepareAutomatic();
        if (interval is null) return;
        _garmothUploadInProgress = true;
        PublishState();
        try
        {
            if (_garmothApiKey.Length == 0) throw new ArgumentException("API-Schlüssel im Bereich Garmoth hinterlegen.");
            SetStatus("Garmoth automatisch: abgeschlossene Grindstunde wird übertragen …");
            var result = await SendGarmothIntervalAsync(interval);
            _garmothIntervals.Complete(interval, result);
            if (result.BlocksAnotherUpload)
                MarkHistoryUploadBlocked(_sessionId, result.Status == GarmothUploadStatus.Succeeded);
            var guidance = result.Status == GarmothUploadStatus.Succeeded
                ? " Nur dieser Stundenabschnitt wurde übertragen."
                : _garmothIntervals.IsBlocked
                    ? " Weitere Uploads dieser Sitzung sind gesperrt. Tracking läuft weiter; bitte in Garmoth prüfen."
                    : " Auto-Upload angehalten. Korrigiere den Schlüssel oder wähle auf der Garmoth-Seite Automatik fortsetzen.";
            SetStatus(result.Message + guidance, result.Status != GarmothUploadStatus.Succeeded);
        }
        catch (Exception exception)
        {
            // SendGarmothIntervalAsync only throws before issuing HTTP; the
            // client converts transport failures into a conservative outcome.
            _garmothIntervals.Complete(interval, new(GarmothUploadStatus.Rejected, "Lokale Upload-Vorbereitung fehlgeschlagen."));
            SetStatus("Garmoth nicht gesendet: " + exception.Message + " Auto-Upload angehalten.", true);
        }
        finally
        {
            _garmothUploadInProgress = false;
            PublishState();
        }
    }

    private async Task<GarmothUploadResult> SendGarmothIntervalAsync(GarmothUploadInterval interval)
    {
        await RefreshPricesAsync();
        var character = _sessionClass ?? SelectedCharacterClass;
        if (character is null)
        {
            await RefreshClassDetectionAsync();
            character = _sessionClass ?? SelectedCharacterClass;
        }
        if (character is null) throw new ArgumentException("Klasse noch unbekannt. In den Einstellungen auswählen.");
        var valuation = SilverValuation.Calculate(interval.Totals, Prices, Preferences.Tax);
        if (!valuation.HasKnownValue) throw new ArgumentException("Noch kein Silberpreis verfügbar. Nach dem nächsten Preisabruf erneut versuchen.");
        if (valuation.AfterTax < 0 || valuation.AfterTax > long.MaxValue || valuation.OverflowItems.Count > 0)
            throw new ArgumentException("Der berechnete Silberwert ist nicht für Garmoth darstellbar.");
        var draft = new GarmothSessionDraft(interval.Id, _sessionSpotId ?? "", character.Name,
            character.Specialization switch
            {
                CharacterSpecialization.Succession => GarmothSpecialization.Succession,
                CharacterSpecialization.Awakening => GarmothSpecialization.Awakening,
                _ => GarmothSpecialization.Unique,
            }, interval.ActiveDuration, interval.Totals,
            (long)decimal.Truncate(valuation.AfterTax), interval.StartedAt)
        {
            SourceSessionId = _sessionId,
        };
        var payload = GarmothSessionPayload.Create(draft);
        var result = await _garmothClient.UploadAsync(draft, _garmothApiKey);
        var notes = new List<string>();
        if (payload.OmittedItems.Count > 0)
            notes.Add("Ohne Garmoth-Zuordnung ausgelassen: " + string.Join(", ", payload.OmittedItems) + ".");
        if (!valuation.IsComplete) notes.Add("Silber ist eine Teilsumme; fehlende Preise wurden nicht geschätzt.");
        if (valuation.IsStale) notes.Add("Silber verwendet den letzten gespeicherten Preisstand.");
        return notes.Count == 0 ? result : result with { Message = result.Message + " " + string.Join(" ", notes) };
    }

    public Task UploadHistoryAsync(Guid sessionId)
    {
        // Keep current-session uploads on the same delta ledger even after a
        // partial hourly upload has disabled full uploads of its history entry.
        if (_hasSession && sessionId == _sessionId) return UploadAsync();
        return RunOperationAsync(async () =>
        {
            var entry = _historyEntries.FirstOrDefault(e => e.SessionId == sessionId);
            if (entry is null || entry.GarmothUploadBlocked) return;
            if (_garmothApiKey.Length == 0) throw new ArgumentException("Bitte zuerst im Bereich Garmoth einen API-Schlüssel hinterlegen.");
            _garmothUploadInProgress = true;
            PublishState();
            try
            {
                await RefreshPricesAsync();
                var draft = CreateHistoricalGarmothDraft(entry, Prices, Preferences.Tax);
                var payload = GarmothSessionPayload.Create(draft);
                SetStatus("Grind aus dem Verlauf wird übertragen …");
                var result = await _garmothClient.UploadAsync(draft, _garmothApiKey);
                if (result.BlocksAnotherUpload)
                    MarkHistoryUploadBlocked(sessionId, result.Status == GarmothUploadStatus.Succeeded);
                var omitted = payload.OmittedItems.Count == 0 ? "" : " Ohne Garmoth-Zuordnung ausgelassen: " +
                    string.Join(", ", payload.OmittedItems) + ".";
                SetStatus(result.Message + omitted, result.Status is GarmothUploadStatus.Rejected or GarmothUploadStatus.OutcomeUnknown);
            }
            finally
            {
                _garmothUploadInProgress = false;
                PublishState();
            }
        });
    }

    internal static GarmothSessionDraft CreateHistoricalGarmothDraft(
        LootHistoryEntry entry, LootPriceSnapshot prices, SilverTaxOptions tax)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(tax);
        if (string.IsNullOrWhiteSpace(entry.CharacterClass))
            throw new ArgumentException("Für diesen Grind fehlt die Charakterklasse.");
        var normalized = entry.CharacterClass.Replace("Â·", "·", StringComparison.Ordinal);
        var separator = normalized.IndexOf('·');
        var className = (separator >= 0 ? normalized[..separator] : normalized).Trim().TrimEnd('Â').TrimEnd();
        var specialization = entry.CharacterClass.Contains("Awakening", StringComparison.OrdinalIgnoreCase)
            ? GarmothSpecialization.Awakening
            : entry.CharacterClass.Contains("Succession", StringComparison.OrdinalIgnoreCase)
                ? GarmothSpecialization.Succession : GarmothSpecialization.Unique;
        var valuation = SilverValuation.Calculate(entry.Totals, prices, tax);
        if (!valuation.HasKnownValue) throw new ArgumentException("Für den Grind ist noch kein Silberpreis verfügbar.");
        if (valuation.AfterTax < 0 || valuation.AfterTax > long.MaxValue || valuation.OverflowItems.Count > 0)
            throw new ArgumentException("Der berechnete Silberwert ist nicht für Garmoth darstellbar.");
        return new GarmothSessionDraft(entry.SessionId, entry.SpotId, className, specialization,
            entry.Duration, entry.Totals, (long)decimal.Truncate(valuation.AfterTax), entry.StartedAt)
        {
            SourceSessionId = entry.SessionId,
        };
    }
}
