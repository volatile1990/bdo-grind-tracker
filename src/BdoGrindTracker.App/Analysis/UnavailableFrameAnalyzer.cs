namespace BdoGrindTracker.App.Analysis;

internal sealed class UnavailableFrameAnalyzer(string status) : ILootFrameAnalyzer
{
    public bool IsAvailable => false;

    public string Status { get; } = status;

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
