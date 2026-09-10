namespace BdoGrindTracker.App.Analysis;

internal interface ILootFrameAnalyzer : IDisposable
{
    bool IsAvailable { get; }

    string Status { get; }

    string? MissingOcrLanguageTag => null;

    bool RequiresLootPanel => false;

    void ValidateCaptureSetup(Size frameSize) { }
    // Called before capture starts, while no frame analysis can be running.
    void ConfigureGameLanguage(string language) { }

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

    Task<FrameAnalysisResult> AnalyzeAsync(
        Bitmap frame,
        DateTimeOffset capturedAt,
        bool isHdr,
        bool isToneMapped,
        CancellationToken cancellationToken) =>
        AnalyzeAsync(frame, capturedAt, isHdr, cancellationToken);

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
