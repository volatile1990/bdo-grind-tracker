using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

internal readonly record struct LiveDetectionDebugSnapshot(
    DateTimeOffset CapturedAt,
    long CapturedSequence,
    TimeSpan AnalysisDuration,
    Size FrameSize,
    Rectangle? PanelRegion,
    IReadOnlyList<Rectangle> SlotRegions,
    int PreparedRowCount,
    int NonBlankRowCount,
    int OcrRowCount,
    int CatalogMatchCount,
    IReadOnlyList<string> RecognizedLines,
    int NewEventCount,
    double MeanMatchConfidence,
    string VariantName,
    string TextRecognitionBackend,
    string? TextRecognitionLanguage,
    bool IsHdr = false)
{
    public string? SpotId { get; init; }
    public IReadOnlyList<LootTrackingDecision> Decisions { get; init; } = [];
}
