using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Services;

internal sealed record TrackerMonitor(string DeviceName, string Label, Rectangle Bounds, bool IsPrimary);

internal sealed record TrackerPreferences
{
    public IReadOnlyList<string> FavoriteItems { get; init; } = [];
    public IReadOnlyDictionary<string, string[]> LootColumnOrders { get; init; } = new Dictionary<string, string[]>();
    public string? MonitorDeviceName { get; init; }
    public string? CharacterClassId { get; init; }
    public int AutoPauseMinutes { get; init; } = 3;
    public bool IncludeEventLoot { get; init; }
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
    public bool IsDemo { get; init; }
    public bool IsSubmitted { get; init; }
    public bool AnalyzerAvailable { get; init; }
    public string? SpotId { get; init; }
    public string? CharacterClassId { get; init; }
    public string CharacterLabel { get; init; } = "Automatische Erkennung";
    public TimeSpan Elapsed { get; init; }
    public LootSessionSnapshot Loot { get; init; } = LootSessionSnapshot.Empty;
    public SilverValuationResult Silver { get; init; } = new(0, 0, 0, [], [], false);
    public string PriceStatus { get; init; } = "NPC- und Festwerte";
    public string Status { get; init; } = "Bereit für deine nächste Session.";
    public bool IsError { get; init; }
    public string? RecordingPath { get; init; }
    public bool IsRecording { get; init; }
    public bool HasApiKey { get; init; }
    public bool UploadBlocked { get; init; }
    public bool AutomaticSuspended { get; init; }
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
    Task ToggleTrackingAsync();
    Task PauseAsync();
    Task NewSessionAsync();
    Task SetDemoAsync(bool enabled);
    // null keeps the encrypted key; empty string removes it. Never expose a saved key to markup.
    // Only an explicit save on the Garmoth page resumes a rejected automatic upload.
    Task SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null,
        bool resumeAutomaticUpload = false);
    Task UploadAsync();
    Task UploadHistoryAsync(Guid sessionId);
    Task UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals,
        string? characterClass = null);
    Task DeleteHistoryAsync(Guid sessionId);
    Task RefreshPricesAsync();
    Task TickAsync();
    Task ShutdownAsync();
}
