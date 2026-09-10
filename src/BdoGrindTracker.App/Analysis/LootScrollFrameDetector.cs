namespace BdoGrindTracker.App.Analysis;

/// <summary>Locates the gauge visually and reads its adjacent countdown in the same frame.</summary>
internal sealed class LootScrollFrameDetector : ILootScrollFrameDetector
{
    private readonly LootScrollGaugeDetector _gauge = new();
    private readonly ILootScrollTimerReader _timer;

    internal LootScrollFrameDetector(ILootScrollTimerReader? timer = null) => _timer = timer ?? new LootScrollTimerReader();

    public LootScrollReading Analyze(Bitmap frame, CancellationToken cancellationToken)
    {
        var match = _gauge.FindGauge(frame, cancellationToken);
        if (match is null) return LootScrollReading.Unknown;
        try
        {
            if (_timer.Read(frame, match.Bounds, cancellationToken) is { } time)
                return match.Reading with { RemainingTime = time.RemainingTime, TimerResolution = time.Resolution };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { /* Optional timer OCR cannot discard a reliable visible symbol. */ }
        return match.Reading;
    }

    public void Dispose()
    {
        try { _timer.Dispose(); }
        finally { _gauge.Dispose(); }
    }
}
