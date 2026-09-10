namespace BdoGrindTracker.App.Analysis;

internal sealed class UnavailableFrameAnalyzer(string status, string? missingOcrLanguageTag = null) : ILootFrameAnalyzer
{
    public bool IsAvailable => false;

    public string Status { get; } = status;

    public string? MissingOcrLanguageTag { get; } = missingOcrLanguageTag;

    public Task<FrameAnalysisResult> AnalyzeAsync(
        Bitmap frame,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Status);

    public void Reset()
    {
    }

    public void Dispose()
    {
    }
}
