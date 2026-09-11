using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

internal sealed record LootEventView(
    Guid EventId,
    DateTimeOffset DetectedAt,
    string ItemName,
    int Quantity)
{
    public int Revision { get; init; }
    public int? TotalDropQuantity { get; init; }
}

/// <summary>
/// OCR observations, lifecycle decisions and confirmed loot from one pass through
/// the fixed normal and optional rare panels.
/// </summary>
internal sealed record FrameAnalysisResult(
    IReadOnlyList<LootEventView> NewEvents,
    IReadOnlyList<string> RecognizedLines,
    double MeanMatchConfidence,
    string VariantName,
    int PreparedRowCount,
    int NonBlankRowCount,
    int OcrRowCount,
    int CatalogMatchCount,
    Rectangle? PanelRegion)
{
    public Size FrameSize { get; init; }

    public IReadOnlyList<Rectangle> SlotRegions { get; init; } = [];

    public Rectangle? RarePanelRegion { get; init; }

    public Rectangle? RareBandRegion { get; init; }

    public string TextRecognitionBackend { get; init; } = "unknown";

    public string? TextRecognitionLanguage { get; init; }

    public string? SpotId { get; init; }

    public IReadOnlyList<LootObservation> Observations { get; init; } = [];

    public LootTotalsProjection? LootProjection { get; init; }
    public LifetimeParsingContext? LifetimeParsingContext { get; init; }

    public TrackerFrameResult TrackingResult { get; init; } = new([], []);

    public NormalLootRecoveryDiagnostics Recovery { get; init; } = NormalLootRecoveryDiagnostics.Empty;

    public IReadOnlyList<LootRowReviewDiagnostics> RowReviews { get; init; } = [];
}

// Fixed-size per-frame counters only; no growing UI log or retained screenshots.
internal sealed record NormalLootRecoveryDiagnostics(
    int RowsAttempted, int OcrCalls, int QuantitiesRecovered, int RowsRecovered, int Errors)
{
    public static NormalLootRecoveryDiagnostics Empty { get; } = new(0, 0, 0, 0, 0);
}
