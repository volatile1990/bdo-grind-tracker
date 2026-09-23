using System.Net.Http;
using System.Security.Cryptography;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Theming;
using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private bool _settingsChangesPending;

    public async Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null,
        bool resumeAutomaticUpload = false)
    {
        if (_shutdownStarted || _disposed)
            return new("Die Einstellungen konnten nicht gespeichert werden, weil Grindcrest beendet wird.");
        if (IsBusy)
            return new("Die Einstellungen konnten noch nicht gespeichert werden. Bitte warte, bis der laufende Vorgang abgeschlossen ist.");
        var result = await RunOperationAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(preferences);
            preferences = preferences with { BuffRecognitionProfilePath = null };
            if (!AppText.IsKnownLanguage(preferences.UiLanguage))
                throw new ArgumentException("Bitte wähle Deutsch oder Englisch als App-Sprache.");
            if (!AppThemes.IsKnown(preferences.ThemeId))
                throw new ArgumentException("Bitte wähle ein bekanntes Theme aus der Liste.");
            if (preferences.OverlayThemeId is not null && !AppThemes.IsKnown(preferences.OverlayThemeId))
                throw new ArgumentException("Bitte wähle ein bekanntes Overlay-Theme oder „Wie Hauptfenster“.");
            // A form populated from fallback values must never overwrite an unread file.
            if (_settingsStore.LoadError is { } loadError) throw new IOException(loadError);
            var captureConfigurationChanged = !string.Equals(preferences.CaptureConfigurationPath,
                Preferences.CaptureConfigurationPath, StringComparison.OrdinalIgnoreCase);
            if (_hasSession && (preferences.MonitorDeviceName != Preferences.MonitorDeviceName ||
                captureConfigurationChanged ||
                preferences.GameLanguage != Preferences.GameLanguage ||
                preferences.RecordLoot != Preferences.RecordLoot ||
                preferences.RecordRotation != Preferences.RecordRotation))
                throw new ArgumentException("Monitor, BDO-Konfiguration, Spielsprache und Aufzeichnung können erst für eine neue Session geändert werden.");
            if (captureConfigurationChanged)
            {
                if (_captureSession.HasPendingAnalysis)
                    throw new InvalidOperationException("Die vorherige Texterkennung wird noch beendet. Bitte erneut versuchen.");
                _captureConfigurations.Read(preferences.CaptureConfigurationPath);
            }
            if (preferences.GameLanguage is not ("auto" or "en" or "de"))
                throw new ArgumentException("Unterstützte Spielsprachen sind Deutsch und Englisch.");
            var classChanged = preferences.CharacterClassId != Preferences.CharacterClassId;
            if (classChanged && (_uiRunning || _sessionSubmitted))
                throw new InvalidOperationException(_sessionSubmitted
                    ? "Die Klasse einer bereits übertragenen Session kann nicht mehr geändert werden."
                    : "Bitte die Session pausieren, bevor du die Klasse änderst.");
            if (preferences.AutoPauseMinutes is < AppSettings.MinimumAutoPauseMinutes or > AppSettings.MaximumAutoPauseMinutes)
                throw new ArgumentException("Auto-Pause muss zwischen 1 und 60 Minuten liegen.");
            if (preferences.DebugLogRetentionHours is < AppSettings.MinimumDebugLogRetentionHours or > AppSettings.MaximumDebugLogRetentionHours)
                throw new ArgumentException("Die Aufbewahrungsdauer für Debuglogs muss zwischen 1 und 168 Stunden liegen.");
            if (preferences.CharacterClassId is { } classId && CompanionCharacterClassCatalog.FindById(classId) is null)
                throw new ArgumentException("Die ausgewählte Charakterklasse ist nicht bekannt.");
            if (preferences.MonitorDeviceName is { } monitor && !Monitors.Any(m => m.DeviceName == monitor))
                throw new ArgumentException("Der ausgewählte Monitor ist nicht mehr verfügbar.");
            var region = LootPriceCatalog.NormalizeRegion(preferences.MarketRegion);
            var tax = preferences.Tax;
            var nextKey = apiKey?.Trim() ?? _garmothApiKey;
            if (nextKey.Length > 4096 || nextKey.Any(static c => c < 0x21 || c > 0x7e))
                throw new ArgumentException("Der Garmoth-Key darf keine Leerzeichen enthalten und muss ein gültiger API-Key sein.");
            if (classChanged && preferences.CharacterClassId is null)
            {
                await RefreshClassDetectionAsync();
                if (_shutdownStarted || _disposed) return;
            }
            if (apiKey is not null)
            {
                try { _garmothKeyStore.Save(nextKey); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                    CryptographicException or ArgumentException)
                {
                    // Never put a key or exception carrying key material into state.
                    SetStatus("Der Garmoth-Key konnte nicht sicher gespeichert werden.", true);
                    return;
                }
            }
            _garmothApiKey = nextKey;
            var regionChanged = region != Preferences.MarketRegion;
            var previousCaptureConfiguration = Preferences.CaptureConfigurationPath;
            var previousDebugLogging = Preferences.AutomaticDebugLogging;
            var previousDebugHours = Preferences.DebugLogRetentionHours;
            var wasAutoStartEnabled = Preferences.AutoStartGrinding;
            _settingsChangesPending = true;
            // Setup completion is published only after its setting is durable.
            Preferences = preferences with { SetupCompleted = Preferences.SetupCompleted,
                MarketRegion = region, AutoUpload = preferences.AutoUpload && nextKey.Length > 0 };
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
            if (wasAutoStartEnabled != Preferences.AutoStartGrinding || captureConfigurationChanged)
                ResetAutoStartRetry();
            if (!TrySaveSettings(preferences.SetupCompleted))
            {
                // A failed save must not silently switch the capture source for this run.
                Preferences = Preferences with
                {
                    CaptureConfigurationPath = previousCaptureConfiguration,
                    AutomaticDebugLogging = previousDebugLogging,
                    DebugLogRetentionHours = previousDebugHours,
                };
                _settings.CaptureConfigurationPath = previousCaptureConfiguration;
                _settings.AutomaticDebugLogging = previousDebugLogging;
                _settings.DebugLogRetentionHours = previousDebugHours;
                return;
            }
            if (captureConfigurationChanged) RebuildCaptureAnalyzer();
            if (resumeAutomaticUpload) _garmothIntervals.ResumeAutomatic();
            PersistCurrentSession(DateTimeOffset.UtcNow, throwOnError: true);
            SetStatus(_garmothIntervals.IsBlocked
                ? "Einstellungen gespeichert. Das unklare Upload-Ergebnis muss in Garmoth geprüft werden; diese Sitzung bleibt für Uploads gesperrt."
                : _garmothIntervals.CorrectionReviewRequired
                    ? GarmothUploadIntervals.CorrectionReviewMessage
                : Preferences.AutoUpload && _garmothIntervals.AutomaticSuspended
                    ? "Einstellungen gespeichert. Der automatische Upload bleibt angehalten. Korrigiere den Schlüssel oder setze die Automatik auf der Garmoth-Seite fort."
                    : Preferences.AutoUpload
                        ? "Einstellungen gespeichert. Jede volle Grindstunde wird einmal automatisch übertragen."
                        : "Einstellungen gespeichert.");
            // Valuation refresh must not keep the command gate occupied while
            // waiting for HTTP and delay the normal inactivity pause.
            if (regionChanged) _ = RefreshPricesAsync();
        });
        // A blocked capture has its own persistent error. It does not turn a
        // successfully persisted setting into a failed save.
        return new(result.Error);
    }

    private bool TrySaveSettings(bool? setupCompleted = null)
    {
        if (_settingsStore.LoadError is { } loadError)
        {
            _settingsSaveError = loadError;
            SetStatus(loadError, true);
            return false;
        }
        var previousSetupCompleted = _settings.SetupCompleted;
        _settings.SetupCompleted = setupCompleted ?? Preferences.SetupCompleted;
        _settings.UpdateCapturePreferences(Preferences.MonitorDeviceName);
        _settings.ThemeId = Preferences.ThemeId;
        _settings.OverlayThemeId = Preferences.OverlayThemeId;
        _settings.UiLanguage = Preferences.UiLanguage;
        _settings.CaptureConfigurationPath = Preferences.CaptureConfigurationPath;
        _settings.BuffRecognitionProfilePath = null;
        _settings.AutoPauseMinutes = Preferences.AutoPauseMinutes;
        _settings.AutoStartGrinding = Preferences.AutoStartGrinding;
        _settings.AutomaticDebugLogging = Preferences.AutomaticDebugLogging;
        _settings.DebugLogRetentionHours = Preferences.DebugLogRetentionHours;
        _settings.RotationIncludeSpecialEvents = Preferences.IncludeSpecialEventRotations;
        _settings.GameLanguage = Preferences.GameLanguage;
        _settings.FavoriteItems = Preferences.FavoriteItems.ToArray();
        _settings.LootColumnOrders = Preferences.LootColumnOrders.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        _settings.CharacterClassId = Preferences.CharacterClassId;
        _settings.GarmothAutoUploadEnabled = Preferences.AutoUpload;
        _settings.UpdateSilverPreferences(Preferences.MarketRegion, Preferences.Tax);
        try
        {
            _settingsStore.Save(_settings);
            ConfigureDebugLogging();
            Preferences = Preferences with { SetupCompleted = _settings.SetupCompleted };
            _settingsChangesPending = false;
            _settingsSaveError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _settings.SetupCompleted = previousSetupCompleted;
            // Settings persistence is optional for local capture. Keep the error
            // visible across producer updates, while explicit Save still fails.
            _settingsSaveError = "Einstellungen nicht gespeichert: " + exception.Message;
            SetStatus(_settingsSaveError, true);
            return false;
        }
    }

    private void RecoverSettingsIfNeeded()
    {
        if (_settingsStore.LoadError is null) return;
        var previousCaptureConfiguration = Preferences.CaptureConfigurationPath;
        var recovered = _settingsStore.Load();
        if (_settingsStore.LoadError is { } error) throw new IOException(error);
        _settings = recovered;
        _settings.BuffRecognitionProfilePath = null;
        ResetAutoStartRetry();
        Preferences = Preferences with
        {
            SetupCompleted = recovered.SetupCompleted,
            ThemeId = recovered.ThemeId,
            OverlayThemeId = recovered.OverlayThemeId,
            UiLanguage = recovered.UiLanguage,
            MonitorDeviceName = _hasSession ? Preferences.MonitorDeviceName
                : Monitors.FirstOrDefault(monitor => monitor.DeviceName == recovered.MonitorDeviceName)?.DeviceName
                    ?? Monitors.FirstOrDefault(monitor => monitor.IsPrimary)?.DeviceName ?? Monitors.FirstOrDefault()?.DeviceName,
            GameLanguage = _hasSession ? Preferences.GameLanguage : recovered.GameLanguage,
            CaptureConfigurationPath = _hasSession ? Preferences.CaptureConfigurationPath : recovered.CaptureConfigurationPath,
            BuffRecognitionProfilePath = null,
            AutoPauseMinutes = recovered.AutoPauseMinutes,
            AutoStartGrinding = recovered.AutoStartGrinding,
            AutomaticDebugLogging = recovered.AutomaticDebugLogging,
            DebugLogRetentionHours = recovered.DebugLogRetentionHours,
            IncludeSpecialEventRotations = recovered.RotationIncludeSpecialEvents,
            FavoriteItems = recovered.FavoriteItems ?? [],
            LootColumnOrders = recovered.LootColumnOrders ?? new(),
            CharacterClassId = _hasSession ? Preferences.CharacterClassId
                : CompanionCharacterClassCatalog.FindById(recovered.CharacterClassId ?? "")?.Id,
            AutoUpload = recovered.GarmothAutoUploadEnabled,
            MarketRegion = recovered.MarketRegion,
            ValuePack = recovered.SilverValuePack,
            MerchantRing = recovered.SilverMerchantRing,
            FamilyFame = recovered.SilverFamilyFame,
        };
        if (!_hasSession) _sessionClass = SelectedCharacterClass;
        ConfigureDebugLogging();
        if (Preferences.AutoUpload) _garmothIntervals.SuspendAutomatic();
        Prices = _priceProvider.GetCachedSnapshot(Preferences.MarketRegion);
        _priceStatus = FormatPriceStatus(Prices);
        _nextPriceRefreshAt = DateTimeOffset.MinValue;
        _settingsSaveError = null;
        if (!_hasSession && !string.Equals(previousCaptureConfiguration, Preferences.CaptureConfigurationPath,
            StringComparison.OrdinalIgnoreCase))
        {
            _captureConfigurationReloadPending = true;
            if (!_captureSession.HasPendingAnalysis) RebuildCaptureAnalyzer();
        }
        RefreshMissingOcrLanguageOffer();
    }

    private Task RefreshClassDetectionAsync()
    {
        if (_shutdownStarted || _disposed) return Task.CompletedTask;
        if (_classDetectionTask is { IsCompleted: false } pending) return pending;
        _nextClassDetectionAt = DateTimeOffset.UtcNow.AddSeconds(30);
        return _classDetectionTask = RefreshClassDetectionCoreAsync();
    }

    private async Task RefreshClassDetectionCoreAsync()
    {
        try
        {
            var detected = await Task.Run(_detectCharacterClass);
            if (_shutdownStarted || _disposed) return;
            _classDetection = detected;
            // Start and preference commands apply the detection themselves after
            // awaiting it. Periodic recovery must not save over an in-flight
            // session operation or upload; an unknown class can retry afterwards.
            if (!_sessionSubmitted && (!_hasSession || (_sessionClass is null && !IsBusy)))
            {
                _sessionClass = SelectedCharacterClass;
                if (_hasSession && _sessionClass is not null)
                {
                    // A paused session has no periodic history save. Commit its
                    // recovered class to history and checkpoint together now.
                    RefreshPendingState(publish: false);
                    PersistCurrentSession(DateTimeOffset.UtcNow);
                }
            }
            PublishState();
        }
        catch
        {
            if (_shutdownStarted || _disposed) return;
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
