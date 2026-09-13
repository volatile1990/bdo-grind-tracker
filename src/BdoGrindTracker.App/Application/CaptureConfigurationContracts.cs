namespace BdoGrindTracker.App.Services;

internal sealed record CaptureConfigurationOption(
    string Path,
    string Label,
    DateTime? LastWriteUtc,
    int ScreenWidth,
    int ScreenHeight,
    float UiScale,
    Rectangle? NormalBounds,
    Rectangle? RareBounds,
    string RareStatus,
    string? Error = null)
{
    public bool IsValid => Error is null;
}

internal sealed record CaptureConfigurationScan(
    IReadOnlyList<CaptureConfigurationOption> Candidates,
    string? ActivePath,
    string? Error = null);

internal sealed record CaptureConfigurationPreview(
    CaptureConfigurationOption? Configuration = null,
    string? ImageDataUrl = null,
    string? NormalImageDataUrl = null,
    string? RareImageDataUrl = null,
    DateTimeOffset? CapturedAt = null,
    string? Error = null)
{
    public bool UsesSessionGeometry { get; init; }
}
