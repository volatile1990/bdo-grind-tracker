using System.Net.Http;
using System.Security.Cryptography;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    public async Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null,
        bool resumeAutomaticUpload = false)
    {
        if (_shutdownStarted || _disposed)
            return new("Die Einstellungen konnten nicht gespeichert werden, weil Grindcrest beendet wird.");
        if (IsBusy)
            return new("Die Einstellungen konnten noch nicht gespeichert werden. Bitte warte, bis der laufende Vorgang abgeschlossen ist.");
        var result = await RunOperationAsync(() =>
        {
            ArgumentNullException.ThrowIfNull(preferences);
            if (_hasSession && (preferences.MonitorDeviceName != Preferences.MonitorDeviceName ||
                preferences.GameLanguage != Preferences.GameLanguage ||
                preferences.RecordLoot != Preferences.RecordLoot))
                throw new ArgumentException("Monitor, Spielsprache und Aufzeichnung können erst für eine neue Session geändert werden.");
            if (preferences.GameLanguage is not ("auto" or "en" or "de"))
                throw new ArgumentException("Unterstützte Spielsprachen sind Deutsch und Englisch.");
            var classChanged = preferences.CharacterClassId != Preferences.CharacterClassId;
            if (classChanged && (_uiRunning || _sessionSubmitted))
                throw new InvalidOperationException(_sessionSubmitted
                    ? "Die Klasse einer bereits übertragenen Session kann nicht mehr geändert werden."
                    : "Bitte die Session pausieren, bevor du die Klasse änderst.");
            if (preferences.AutoPauseMinutes is < AppSettings.MinimumAutoPauseMinutes or > AppSettings.MaximumAutoPauseMinutes)
                throw new ArgumentException("Auto-Pause muss zwischen 1 und 60 Minuten liegen.");
            if (preferences.CharacterClassId is { } classId && CompanionCharacterClassCatalog.FindById(classId) is null)
                throw new ArgumentException("Die ausgewählte Charakterklasse ist nicht bekannt.");
            if (preferences.MonitorDeviceName is { } monitor && !Monitors.Any(m => m.DeviceName == monitor))
                throw new ArgumentException("Der ausgewählte Monitor ist nicht mehr verfügbar.");
            var region = LootPriceCatalog.NormalizeRegion(preferences.MarketRegion);
            var tax = preferences.Tax;
            var nextKey = apiKey?.Trim() ?? _garmothApiKey;
            if (nextKey.Length > 4096 || nextKey.Any(static c => c < 0x21 || c > 0x7e))
                throw new ArgumentException("Der Garmoth-Key darf keine Leerzeichen enthalten und muss ein gültiger API-Key sein.");
            if (apiKey is not null)
            {
                try { _garmothKeyStore.Save(nextKey); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                    CryptographicException or ArgumentException)
                {
                    // Never put a key or exception carrying key material into state.
                    SetStatus("Der Garmoth-Key konnte nicht sicher gespeichert werden.", true);
                    return Task.CompletedTask;
                }
            }
            _garmothApiKey = nextKey;
            var regionChanged = region != Preferences.MarketRegion;
            Preferences = preferences with { MarketRegion = region, AutoUpload = preferences.AutoUpload && nextKey.Length > 0 };
            if (!_hasSession && Preferences.GameLanguage == "auto") _gameLanguageDetection = _detectGameLanguage();
            if (!_hasSession) RefreshMissingOcrLanguageOffer();
            if (classChanged || (!_hasSession && !_demoMode)) _sessionClass = SelectedCharacterClass;
            _settings.UpdateSilverPreferences(region, tax);
            if (regionChanged)
            {
                Prices = _priceProvider.GetCachedSnapshot(region);
                _priceStatus = FormatPriceStatus(Prices);
                _nextPriceRefreshAt = DateTimeOffset.MinValue;
            }
            if (!TrySaveSettings()) return Task.CompletedTask;
            if (resumeAutomaticUpload) _garmothIntervals.ResumeAutomatic();
            SetStatus(_garmothIntervals.IsBlocked
                ? "Einstellungen gespeichert. Das unklare Upload-Ergebnis muss in Garmoth geprüft werden; diese Sitzung bleibt für Uploads gesperrt."
                : Preferences.AutoUpload && _garmothIntervals.AutomaticSuspended
                    ? "Einstellungen gespeichert. Der automatische Upload bleibt angehalten. Korrigiere den Schlüssel oder setze die Automatik auf der Garmoth-Seite fort."
                    : Preferences.AutoUpload
                        ? "Einstellungen gespeichert. Jede volle Grindstunde wird einmal automatisch übertragen."
                        : "Einstellungen gespeichert.");
            // Valuation refresh must not keep the command gate occupied while
            // waiting for HTTP and delay the normal inactivity pause.
            if (regionChanged) _ = RefreshPricesAsync();
            return Task.CompletedTask;
        });
        // A blocked capture has its own persistent error. It does not turn a
        // successfully persisted setting into a failed save.
        return new(result.Error);
    }

    private bool TrySaveSettings()
    {
        _settings.UpdateCapturePreferences(Preferences.MonitorDeviceName);
        _settings.AutoPauseMinutes = Preferences.AutoPauseMinutes;
        _settings.GameLanguage = Preferences.GameLanguage;
        _settings.FavoriteItems = Preferences.FavoriteItems.ToArray();
        _settings.LootColumnOrders = Preferences.LootColumnOrders.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        _settings.CharacterClassId = Preferences.CharacterClassId;
        _settings.GarmothAutoUploadEnabled = Preferences.AutoUpload;
        _settings.UpdateSilverPreferences(Preferences.MarketRegion, Preferences.Tax);
        try
        {
            _settingsStore.Save(_settings);
            _settingsSaveError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings persistence is optional for local capture. Keep the error
            // visible across producer updates, while explicit Save still fails.
            _settingsSaveError = "Einstellungen nicht gespeichert: " + exception.Message;
            SetStatus(_settingsSaveError, true);
            return false;
        }
    }

    private Task RefreshClassDetectionAsync()
    {
        if (_shutdownStarted) return Task.CompletedTask;
        if (_classDetectionTask is { IsCompleted: false } pending) return pending;
        return _classDetectionTask = RefreshClassDetectionCoreAsync();
    }

    private async Task RefreshClassDetectionCoreAsync()
    {
        try
        {
            var detected = await Task.Run(_detectCharacterClass);
            if (_shutdownStarted) return;
            _classDetection = detected;
            if (!_hasSession || _sessionClass is null) _sessionClass = SelectedCharacterClass;
            PublishState();
        }
        catch
        {
            // The detector can observe account-specific paths. Its failure text
            // is intentionally not exposed; manual class selection remains usable.
            _classDetection = CharacterClassDetection.Unavailable;
            PublishState();
        }
    }

    public Task RefreshPricesAsync()
    {
        if (_shutdownStarted || _disposed) return Task.CompletedTask;
        _priceRefreshEnabled = true;
        if (_priceRefreshTask is { IsCompleted: false } pending)
            return _priceRefreshRegion == Preferences.MarketRegion ? pending : RefreshChangedRegionAsync(pending);
        return _priceRefreshTask = RefreshPricesCoreAsync();
    }

    private async Task RefreshChangedRegionAsync(Task previousRegion)
    {
        await previousRegion;
        await RefreshPricesAsync();
    }

    private async Task RefreshPricesCoreAsync()
    {
        var region = Preferences.MarketRegion;
        _priceRefreshRegion = region;
        try
        {
            var snapshot = await _priceProvider.GetSnapshotAsync(region, _priceLifetime.Token);
            if (_shutdownStarted || region != Preferences.MarketRegion) return;
            Prices = snapshot;
            _priceStatus = FormatPriceStatus(snapshot);
        }
        catch (OperationCanceledException) when (_priceLifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            InvalidDataException or OperationCanceledException)
        {
            if (!_shutdownStarted && region == Preferences.MarketRegion)
            {
                Prices = _priceProvider.GetCachedSnapshot(region);
                _priceStatus = region.ToUpperInvariant() + " · Preise offline, letzte bekannte Werte";
            }
        }
        finally
        {
            _nextPriceRefreshAt = region == Preferences.MarketRegion
                ? DateTimeOffset.UtcNow.AddMinutes(1) : DateTimeOffset.MinValue;
            PublishState();
        }
    }

    private static string FormatPriceStatus(LootPriceSnapshot snapshot)
    {
        var status = !string.IsNullOrWhiteSpace(snapshot.StatusMessage)
            ? snapshot.StatusMessage
            : snapshot.IsStale ? "Letzter gespeicherter Preisstand"
            : snapshot.RetrievedAt is { } retrievedAt ? $"Preisstand {retrievedAt.ToLocalTime():HH:mm}"
            : "NPC- und Festwerte";
        return snapshot.Region.ToUpperInvariant() + " · " + status;
    }
}
