using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Services;

internal sealed record TrackerMonitor(string DeviceName, string Label, Rectangle Bounds, bool IsPrimary);

internal sealed record PreferenceSaveResult(string? Error = null)
{
    public bool Succeeded => Error is null;
}

internal sealed record TrackerCommandResult(string? Error = null)
{
    public bool Succeeded => Error is null;
    public static TrackerCommandResult Success { get; } = new();
}

internal sealed record TrackerPreferences
{
    public IReadOnlyList<string> FavoriteItems { get; init; } = [];
    public IReadOnlyDictionary<string, string[]> LootColumnOrders { get; init; } = new Dictionary<string, string[]>();
    public string? MonitorDeviceName { get; init; }
    public string GameLanguage { get; init; } = "auto";
    public string? CharacterClassId { get; init; }
    public int AutoPauseMinutes { get; init; } = 3;
    public bool RecordLoot { get; init; }
    public bool AutoUpload { get; init; }
    public string MarketRegion { get; init; } = "eu";
    public bool ValuePack { get; init; }
    public bool MerchantRing { get; init; }
    public int FamilyFame { get; init; }
    public SilverTaxOptions Tax => new(ValuePack, MerchantRing, FamilyFame);
}

internal sealed record TrackerState
{
    public Guid SessionId { get; init; }
    public bool HasSession { get; init; }
    public bool IsRunning { get; init; }
    public bool IsBusy { get; init; }
    public bool CanEditLoot { get; init; } = true;
    public bool CanPause { get; init; }
    public string? PersistenceError { get; init; }
    public GarmothUploadPreview CurrentGarmothUpload { get; init; } = GarmothUploadPreview.Unavailable("Keine Session vorhanden.");
    public bool IsDemo { get; init; }
    public bool IsSubmitted { get; init; }
    public bool AnalyzerAvailable { get; init; }
    public string? TrackingBlockedReason { get; init; }
    public string? MissingOcrLanguageTag { get; init; }
    public bool IsInstallingOcrLanguage { get; init; }
    public string? OcrInstallationStatus { get; init; }
    public bool OcrRestartRequired { get; init; }
    public string? DetectedGameLanguage { get; init; }
    public string GameLanguageStatus { get; init; } = "Noch nicht erkannt.";
    public string? SpotId { get; init; }
    public string? CharacterClassId { get; init; }
    public string CharacterLabel { get; init; } = "Automatische Erkennung";
    public TimeSpan Elapsed { get; init; }
    public LootSessionSnapshot Loot { get; init; } = LootSessionSnapshot.Empty;
    public LootScrollState LootScroll { get; init; } = LootScrollState.Unknown;
    public IReadOnlyList<string> ManualLootItems { get; init; } = [];
    public SilverValuationResult Silver { get; init; } = new(0, 0, 0, [], [], false);
    public IReadOnlyList<SessionSilverSample> SilverHistory { get; init; } = [];
    public string PriceStatus { get; init; } = "NPC- und Festwerte";
    public string Status { get; init; } = "Bereit für deine nächste Session.";
    public bool IsError { get; init; }
    public string? RecordingPath { get; init; }
    public bool IsRecording { get; init; }
    public bool HasApiKey { get; init; }
    public bool UploadBlocked { get; init; }
    public bool AutomaticSuspended { get; init; }
    public bool ShutdownFailed { get; init; }
}

/// <summary>Desktop actions and immutable state consumed by the Blazor frontend.</summary>
internal interface ITrackerSession : IAsyncDisposable
{
    event Action? Changed;
    TrackerState State { get; }
    TrackerPreferences Preferences { get; }
    IReadOnlyList<TrackerMonitor> Monitors { get; }
    IReadOnlyList<LootHistoryEntry> History { get; }
    LootPriceSnapshot Prices { get; }
    Task<TrackerCommandResult> ToggleTrackingAsync();
    Task<TrackerCommandResult> PauseAsync();
    Task<TrackerCommandResult> NewSessionAsync();
    Task<TrackerCommandResult> SetDemoAsync(bool enabled);
    Task<TrackerCommandResult> InstallOcrLanguageAsync();
    Task<TrackerCommandResult> RecheckOcrLanguageAsync();
    // null keeps the encrypted key; empty string removes it. Never expose a saved key to markup.
    // Only a deliberate key/toggle change or resume action on the Garmoth page resumes a rejected upload.
    Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null,
        bool resumeAutomaticUpload = false);
    Task<TrackerCommandResult> UploadAsync();
    Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId);
    Task<TrackerCommandResult> UploadConfirmedAsync(GarmothUploadPreview preview) =>
        preview.Draft?.SourceSessionId is { } id ? UploadHistoryAsync(id) :
            Task.FromResult(new TrackerCommandResult("Die Upload-Vorschau ist nicht mehr verfügbar."));
    Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals,
        string? characterClass = null);
    // Apply an editor's change against its starting value without losing later drops.
    Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity);
    Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId);
    Task<TrackerCommandResult> SaveSessionAsync() => Task.FromResult(TrackerCommandResult.Success);
    Task RefreshPricesAsync();
    Task TickAsync();
    // Persist before irreversible shutdown; failure leaves the paused tracker usable.
    Task PrepareUpdateRestartAsync();
    // Keep session commands blocked until the installer completes or is canceled.
    Task RunPreparedUpdateAsync(Func<Task> install);
    Task ShutdownAsync();
}
