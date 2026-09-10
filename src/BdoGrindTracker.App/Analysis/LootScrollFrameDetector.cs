namespace BdoGrindTracker.App.Analysis;

/// <summary>Uses the gauge only to locate its countdown; the monitor classifies the measured timer change.</summary>
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
                return LootScrollReading.Unknown with { RemainingTime = time.RemainingTime, TimerResolution = time.Resolution };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { /* A visible symbol cannot substitute for an unreadable countdown. */ }
        return LootScrollReading.Unknown;
    }

    public void Dispose()
    {
        try { _timer.Dispose(); }
        finally { _gauge.Dispose(); }
    }
}
