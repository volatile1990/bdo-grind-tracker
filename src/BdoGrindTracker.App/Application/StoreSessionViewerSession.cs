using System.Collections.ObjectModel;
using System.Net.Http;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Theming;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Services;

/// <summary>
/// Observes another Grindcrest installation's saved state. No capture, restoration,
/// persistence, key access or upload services are constructed in this mode.
/// </summary>
internal sealed class StoreSessionViewerSession : ITrackerSession
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    internal const string ReadOnlyReason = "Diese Ansicht liest die Store-Daten. Änderungen an Sessions sind nur in der Store-App möglich.";
    private const string ReadError = "Die Store-Daten konnten gerade nicht gelesen werden. Der letzte gültige Stand bleibt sichtbar; ein erneuter Leseversuch folgt.";
    private readonly SettingsStore _settingsStore;
    private readonly CurrentSessionStore _sessionStore;
    private readonly LootHistoryStore _historyStore;
    private readonly TimeProvider _time;
    private readonly GarmothBenchmarkSnapshot _benchmarks;
    private readonly Func<Task<TrackerCommandResult>>? _takeOver;
    private readonly SessionRotationTimeline _rotationTimeline = new();
    private FileStamp? _settingsStamp, _sessionStamp, _historyStamp;
    private string? _settingsError, _sessionError, _historyError;
    private DateTimeOffset _nextPoll;
    private CurrentSessionSnapshot? _saved;
    private bool _preferencesEdited;

    public StoreSessionViewerSession(string sourceDirectory, TimeProvider? timeProvider = null,
        Func<Task<TrackerCommandResult>>? takeOver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        SourceDirectory = Path.GetFullPath(sourceDirectory);
        _time = timeProvider ?? TimeProvider.System;
        _takeOver = takeOver;
        _settingsStore = new(SourceDirectory);
        _sessionStore = new(Path.Combine(SourceDirectory, CurrentSessionStore.FileName));
        _historyStore = new(Path.Combine(SourceDirectory, "loot-history-v1.json"));
        using var benchmarkProvider = new GarmothGrindBenchmarkProvider(new HttpClientHandler(),
            Path.Combine(SourceDirectory, GarmothGrindBenchmarkProvider.CacheFileName), _time);
        _benchmarks = benchmarkProvider.GetCachedSnapshot();
        Poll(force: true);
    }

    public string SourceDirectory { get; }
    public event Action? Changed;
    public TrackerState State { get; private set; } = new();
    public TrackerPreferences Preferences { get; private set; } = new() { SetupCompleted = true };
    public IReadOnlyList<TrackerMonitor> Monitors { get; } = [];
    public IReadOnlyList<LootHistoryEntry> History { get; private set; } = [];
    public LootPriceSnapshot Prices { get; private set; } = LootPriceCatalog.FixedSnapshot("eu");

    public Task TickAsync()
    {
        if (_time.GetUtcNow() >= _nextPoll) Poll();
        return Task.CompletedTask;
    }

    private void Poll(bool force = false)
    {
        _nextPoll = _time.GetUtcNow() + PollInterval;
        var changed = false;
        if (NeedsRead("settings.json", ref _settingsStamp, _settingsError, force))
        {
            var settings = _settingsStore.Load();
            changed |= _settingsError != _settingsStore.LoadError;
            _settingsError = _settingsStore.LoadError;
            if (_settingsError is null && !_preferencesEdited)
            {
                Preferences = FromSettings(settings);
                LoadPrices();
                changed = true;
            }
        }
        if (NeedsRead("loot-history-v1.json", ref _historyStamp, _historyError, force))
        {
            var entries = _historyStore.Load();
            changed |= _historyError != _historyStore.LoadError;
            _historyError = _historyStore.LoadError;
            if (_historyError is null)
            {
                History = Array.AsReadOnly(entries.ToArray());
                changed = true;
            }
        }
        if (NeedsRead(CurrentSessionStore.FileName, ref _sessionStamp, _sessionError, force))
        {
            var saved = _sessionStore.Load();
            changed |= _sessionError != _sessionStore.LoadError;
            _sessionError = _sessionStore.LoadError;
            if (_sessionError is null)
            {
                // An explicit null marker or deleted checkpoint means there is no
                // current session. Historical records never become current here.
                _saved = saved;
                changed = true;
            }
        }
        if (changed || force) Publish();
    }

    private bool NeedsRead(string fileName, ref FileStamp? known, string? error, bool force)
    {
        FileStamp stamp;
        try
        {
            var file = new FileInfo(Path.Combine(SourceDirectory, fileName));
            stamp = file.Exists ? new(true, file.Length, file.LastWriteTimeUtc.Ticks) : new(false, 0, 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Let the validated store report an access error, then retry next poll.
            return true;
        }
        var changed = force || error is not null || known != stamp;
        known = stamp;
        return changed;
    }

    private void Publish()
    {
        var saved = _saved;
        var hasError = _settingsError is not null || _historyError is not null || _sessionError is not null;
        var status = hasError ? AppText.Translate(ReadError, Preferences.UiLanguage)
            : saved is null ? AppText.Translate("Store-Ansicht · keine aktuelle Session gespeichert.", Preferences.UiLanguage)
            : AppText.Format("Store-Ansicht · gespeicherter Stand vom {0}.", Preferences.UiLanguage,
                saved.UpdatedAt.ToLocalTime().ToString("G", AppText.Culture(Preferences.UiLanguage)));
        var totals = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(saved?.Totals ?? [], StringComparer.OrdinalIgnoreCase));
        var stats = CombatStatsSpotRules.ForSpot(saved?.CombatStats, saved?.SpotId);
        var rotation = ProjectRotations(saved);
        State = new()
        {
            IsReadOnly = true,
            DataSourceStatus = status,
            HasSession = saved is not null,
            SessionId = saved?.SessionId ?? Guid.Empty,
            IsRunning = false,
            CanEditLoot = false,
            CanSelectSpotVariant = false,
            CanPause = false,
            AnalyzerAvailable = false,
            TrackingBlockedReason = ReadOnlyReason,
            CurrentGarmothUpload = GarmothUploadPreview.Unavailable(ReadOnlyReason),
            UploadBlocked = true,
            AutomaticSuspended = true,
            IsSubmitted = saved?.SessionSubmitted ?? false,
            SpotId = saved?.SpotId,
            CharacterClassId = saved?.CharacterClassId,
            CharacterLabel = CompanionCharacterClassCatalog.FindById(saved?.CharacterClassId)?.DisplayName
                ?? AppText.Translate("Automatische Erkennung", Preferences.UiLanguage),
            Elapsed = saved?.Duration ?? TimeSpan.Zero,
            ObservedAt = saved?.UpdatedAt ?? _time.GetUtcNow(),
            Loot = new(totals, totals.Values.Sum(), saved?.ConfirmedEventCount ?? 0),
            DropHistory = Array.AsReadOnly((saved?.DropHistory ?? []).ToArray()),
            Pauses = Array.AsReadOnly((saved?.Pauses ?? []).ToArray()),
            ManualLootItems = Array.AsReadOnly((saved?.ManualLootItems ?? []).ToArray()),
            CombatStats = stats ?? CombatStatsState.Unknown,
            SessionCombatStats = stats,
            ObservedCombatStatsCategory = stats?.Category,
            Buffs = saved?.Buffs,
            BuffStatus = AppText.Translate("Buffs aus dem gespeicherten Store-Stand.", Preferences.UiLanguage),
            AgrisActiveDuration = saved?.AgrisActiveDuration ?? TimeSpan.Zero,
            AgrisObservedDuration = saved?.AgrisObservedDuration ?? TimeSpan.Zero,
            ExperienceGainedPercentagePoints = saved?.ExperienceGainedPercentagePoints,
            ExperienceObservedDuration = saved?.ExperienceObservedDuration ?? TimeSpan.Zero,
            ExperienceStartLevel = saved?.ExperienceStartLevel,
            ExperienceEndLevel = saved?.ExperienceEndLevel,
            DetectedGameLanguage = saved?.GameLanguage is "en" or "de" ? saved.GameLanguage : null,
            GameLanguageStatus = AppText.Translate("Sprache aus dem gespeicherten Store-Stand.", Preferences.UiLanguage),
            GrindBenchmark = _benchmarks.Find(saved?.SpotId),
            GrindBenchmarkStatus = _benchmarks.Status,
            Silver = SilverValuation.Calculate(totals, Prices, Preferences.Tax),
            PriceStatus = AppText.Translate("Gespeicherte Marktpreise sowie NPC- und Festwerte.", Preferences.UiLanguage),
            Rotation = rotation,
            Status = status,
            IsError = hasError,
            PersistenceError = hasError ? ReadError : null,
        };
        Changed?.Invoke();
    }

    private RotationMonitorSnapshot ProjectRotations(CurrentSessionSnapshot? saved)
    {
        if (saved is null) return RotationProfiles.Present(null);
        var definition = RotationDefinition.Find(saved.SpotId);
        var rotations = (saved.Rotations ?? []).Where(rotation => rotation is not null &&
            rotation.SpotId == saved.SpotId && rotation.Run is { Events: not null } run &&
            run.Outcome != "superseded" && double.IsFinite(run.Duration) && run.Duration > 0)
            .OrderBy(rotation => rotation.StartedAt).ToArray();
        var timings = rotations.Select((rotation, index) =>
        {
            var next = rotations.Skip(index + 1).FirstOrDefault(value => value.Run.EligibleForStatistics || value.Run.Outcome == "active");
            var walk = next is null ? (double?)null : (next.StartedAt - rotation.StartedAt).TotalSeconds - rotation.Run.Duration;
            return new SessionRotationTiming(rotation.Run.Duration,
                rotation.Run.EligibleForStatistics && walk is >= -1 and <= 120 ? Math.Max(0, walk.Value) : null,
                definition is { MarksSpecialRotations: true } && definition.SpecialEventCount(rotation.Run.Events) > 0,
                rotation.StartedAt, definition?.SpecialEventSeconds(rotation.Run.Events) ?? [], rotation.Run.Id,
                Outcome: rotation.Run.Outcome, Events: rotation.Run.Events);
        }).ToArray();
        var presented = RotationProfiles.Present(saved.SpotId) with
        {
            SessionRotations = timings,
            Completed = timings.Count(timing => timing.IsComplete && (Preferences.IncludeSpecialEventRotations || !timing.Special)),
            SupportsSpecialEvents = definition?.HasSpecialEvents ?? false,
            SessionSpecialEvents = rotations.Sum(rotation => definition?.SpecialEventCount(rotation.Run.Events) ?? 0),
            ExcludesSpecialEvents = !Preferences.IncludeSpecialEventRotations,
        };
        return _rotationTimeline.Update(saved.SessionId, saved.Duration, saved.UpdatedAt, saved.StartedAt, presented);
    }

    private static TrackerPreferences FromSettings(AppSettings settings) => new()
    {
        SetupCompleted = true,
        UiLanguage = settings.UiLanguage,
        ThemeId = settings.ThemeId,
        OverlayThemeId = settings.OverlayThemeId,
        CloseBehaviorConfigured = true,
        FavoriteItems = Array.AsReadOnly((settings.FavoriteItems ?? []).ToArray()),
        LootColumnOrders = new ReadOnlyDictionary<string, string[]>((settings.LootColumnOrders ?? [])
            .Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())),
        GameLanguage = settings.GameLanguage,
        RotationMessageLanguage = settings.RotationMessageLanguage,
        CharacterClassId = settings.CharacterClassId,
        AutoPauseMinutes = settings.AutoPauseMinutes,
        DropRatePercent = settings.DropRatePercent,
        DebugLogRetentionHours = settings.DebugLogRetentionHours,
        IncludeSpecialEventRotations = settings.RotationIncludeSpecialEvents,
        MarketRegion = settings.MarketRegion,
        ValuePack = settings.SilverValuePack,
        MerchantRing = settings.SilverMerchantRing,
        FamilyFame = settings.SilverFamilyFame,
    };

    private void LoadPrices()
    {
        using var provider = new MarketLootPriceProvider(new HttpClientHandler(),
            Path.Combine(SourceDirectory, "market-prices-v1.json"), _time);
        Prices = provider.GetCachedSnapshot(Preferences.MarketRegion);
    }

    public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null,
        bool resumeAutomaticUpload = false)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (apiKey is not null || resumeAutomaticUpload)
            return Task.FromResult(new PreferenceSaveResult(ReadOnlyReason));
        if (!AppText.IsKnownLanguage(preferences.UiLanguage))
            return Task.FromResult(new PreferenceSaveResult("Bitte wähle Deutsch oder Englisch als App-Sprache."));
        if (!AppThemes.IsKnown(preferences.ThemeId) ||
            preferences.OverlayThemeId is not null && !AppThemes.IsKnown(preferences.OverlayThemeId))
            return Task.FromResult(new PreferenceSaveResult("Bitte wähle ein bekanntes Theme aus der Liste."));
        if (preferences.DropRatePercent is < AppSettings.MinimumDropRatePercent or > AppSettings.MaximumDropRatePercent)
            return Task.FromResult(new PreferenceSaveResult("Die Droprate muss zwischen 0 und 1000 % liegen."));
        if (preferences.FamilyFame < 0 || !LootPriceCatalog.SupportedRegions.Contains(preferences.MarketRegion))
            return Task.FromResult(new PreferenceSaveResult(ReadOnlyReason));
        var regionChanged = Preferences.MarketRegion != preferences.MarketRegion;
        Preferences = preferences with
        {
            SetupCompleted = true, AutoStartGrinding = false, AutoUpload = false,
            AutomaticDebugLogging = false, RecordLoot = false, RecordRotation = false,
            CloseToTray = false, MinimizeToTray = false, CloseBehaviorConfigured = true,
            CaptureConfigurationPath = null, BuffRecognitionProfilePath = null,
        };
        _preferencesEdited = true;
        if (regionChanged) LoadPrices();
        Publish();
        return Task.FromResult(new PreferenceSaveResult());
    }

    private static Task<TrackerCommandResult> Refuse() => Task.FromResult(new TrackerCommandResult(ReadOnlyReason));
    public Task<TrackerCommandResult> TakeOverSessionAsync() => _takeOver?.Invoke() ?? Refuse();
    public Task<TrackerCommandResult> ToggleTrackingAsync() => Refuse();
    public Task<TrackerCommandResult> PauseAsync() => Refuse();
    public Task<TrackerCommandResult> NewSessionAsync() => Refuse();
    public Task<TrackerCommandResult> SelectSpotVariantAsync(Guid sessionId, string spotId) => Refuse();
    public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Refuse();
    public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Refuse();
    public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Refuse();
    public Task<TrackerCommandResult> SelectCaptureConfigurationAsync(string? gameVariablePath) => Refuse();
    public Task<TrackerCommandResult> UploadAsync() => Refuse();
    public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => Refuse();
    public Task<TrackerCommandResult> UploadConfirmedAsync(GarmothUploadPreview preview) => Refuse();
    public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals,
        string? characterClass = null) => Refuse();
    public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity) => Refuse();
    public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Refuse();
    public Task<TrackerCommandResult> SaveSessionAsync() => Refuse();
    public Task RefreshPricesAsync() => Task.CompletedTask;
    public Task RunPreparedUpdateAsync(Func<Task> install) => Task.FromException(new InvalidOperationException(ReadOnlyReason));
    public Task ShutdownAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private readonly record struct FileStamp(bool Exists, long Length, long UpdatedTicks);
}
