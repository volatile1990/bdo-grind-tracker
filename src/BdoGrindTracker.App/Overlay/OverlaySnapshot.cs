using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Presentation-only values shared by the editor and the native overlay.</summary>
public sealed record OverlaySnapshot
{
    public DateTimeOffset ClockUtcNow { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, OverlayMetric> Metrics { get; init; } =
        new Dictionary<string, OverlayMetric>();
    public IReadOnlyList<OverlayLootItem> Drops { get; init; } = [];
    public IReadOnlyList<OverlayLootItem> RareDrops { get; init; } = [];
    public IReadOnlyList<OverlayLootItem> ItemCatalog { get; init; } = [];
    public IReadOnlyList<SessionSilverSample> SilverHistory { get; init; } = [];
    public IReadOnlyList<OverlayDropMarker> DropMarkers { get; init; } = [];
    public RotationMonitorSnapshot Rotation { get; init; } = new();
    public DailyGoalProgress DailyGoal { get; init; } = new();
    public LootScrollState LootScroll { get; init; } = LootScrollState.Unknown;
    public string Status { get; init; } = "Bereit";
    public bool IsRunning { get; init; }
    public bool CanToggleTracking { get; init; }
    public bool CanNewSession { get; init; }
    public string TrackingButtonLabel { get; init; } = "Tracking starten";

    public static OverlaySnapshot Demo => OverlayMetrics.Demo;
}

public enum OverlayMetricTone { Default, Muted, Positive, Accent }

public sealed record OverlayDropMarker(TimeSpan Elapsed, OverlayLootItem Item);

public sealed record OverlayMetric(string Label, string Value, string? Detail = null, bool IsWarning = false,
    OverlayMetricTone Tone = OverlayMetricTone.Default, string? Tooltip = null);

/// <param name="CanonicalName">The original ledger key, independent of the display language.</param>
/// <param name="Name">Localized display name.</param>
/// <param name="IconPath">Relative web asset path; the native renderer resolves it within bundled assets.</param>
public sealed record OverlayLootItem(
    string CanonicalName,
    string Name,
    string QuantityText,
    string? IconPath = null,
    bool IsRare = false,
    long Quantity = 0,
    bool IsTrash = false);
