namespace BdoGrindTracker.App.Analysis;

internal interface ILootFrameAnalyzer : IDisposable
{
    bool IsAvailable { get; }

    string Status { get; }

    Task<FrameAnalysisResult> AnalyzeAsync(
        Bitmap frame,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken);

    Task<FrameAnalysisResult> AnalyzeAsync(
        Bitmap frame,
        DateTimeOffset capturedAt,
        bool isHdr,
        CancellationToken cancellationToken) =>
        AnalyzeAsync(frame, capturedAt, cancellationToken);

    /// <summary>Flushes pending native batches and finalizes session diagnostics.</summary>
    FrameAnalysisResult CompleteSession(DateTimeOffset completedAt) =>
        new(
            Array.Empty<LootEventView>(),
            Array.Empty<string>(),
            0,
            "session-complete",
            0,
            0,
            0,
            0,
            null);

    void Reset();
}
