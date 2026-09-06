using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Applies every event on the producer thread, but retains only the latest visual
/// state. A busy UI cannot queue captures, events, or full-resolution bitmaps.
/// </summary>
internal sealed class FrameUiMailbox : IDisposable
{
    private readonly object _sync = new();
    private readonly LootSessionAggregate _aggregate = new();
    private FrameAnalysisResult? _latestAnalysis;
    private LiveDetectionDebugSnapshot? _latestDebugSnapshot;
    private Bitmap? _latestThumbnail;
    private bool _totalsChanged;
    private bool _disposed;

    // Reports newly applied positive outputs, not repeated OCR rows or signed corrections.
    public bool Publish(
        FrameAnalysisResult analysis,
        LiveDetectionDebugSnapshot? debugSnapshot = null,
        Bitmap? thumbnail = null,
        Action<IReadOnlyDictionary<string, long>, bool>? onPublished = null)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                thumbnail?.Dispose();
                return false;
            }

            var previousEventCount = _aggregate.ConfirmedEventCount;
            var previousQuantity = _aggregate.TotalQuantity;
            foreach (var lootEvent in analysis.NewEvents)
                _aggregate.Apply(lootEvent);
            _totalsChanged |= _aggregate.ConfirmedEventCount != previousEventCount ||
                _aggregate.TotalQuantity != previousQuantity;
            // Observe the same cumulative state before the UI can consume it.
            // The callback must copy any values retained beyond this call.
            onPublished?.Invoke(_aggregate.Totals, _aggregate.ConfirmedEventCount > previousEventCount);
            _latestAnalysis = analysis;
            if (debugSnapshot is not null && thumbnail is not null)
            {
                _latestThumbnail?.Dispose();
                _latestDebugSnapshot = debugSnapshot;
                _latestThumbnail = thumbnail;
            }
            else
            {
                thumbnail?.Dispose();
            }
            return _aggregate.ConfirmedEventCount > previousEventCount;
        }
    }

    public FrameUiUpdate? TakeLatest()
    {
        lock (_sync)
        {
            if (_latestAnalysis is null)
                return null;

            var totals = _totalsChanged
                ? new LootSessionSnapshot(
                    new Dictionary<string, long>(_aggregate.Totals, StringComparer.OrdinalIgnoreCase),
                    _aggregate.TotalQuantity,
                    _aggregate.ConfirmedEventCount)
                : null;
            var update = new FrameUiUpdate(
                _latestAnalysis, totals, _latestDebugSnapshot, _latestThumbnail);
            _latestAnalysis = null;
            _latestDebugSnapshot = null;
            _latestThumbnail = null;
            _totalsChanged = false;
            return update;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _aggregate.Reset();
            _latestAnalysis = null;
            _latestDebugSnapshot = null;
            _latestThumbnail?.Dispose();
            _latestThumbnail = null;
            _totalsChanged = false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            Reset();
            _disposed = true;
        }
    }
}

internal sealed record LootSessionSnapshot(
    IReadOnlyDictionary<string, long> Totals,
    long TotalQuantity,
    int ConfirmedEventCount)
{
    public static LootSessionSnapshot Empty { get; } = new(
        new Dictionary<string, long>(), 0, 0);

    public int ItemTypeCount => Totals.Count;
}

internal sealed record FrameUiUpdate(
    FrameAnalysisResult Analysis,
    LootSessionSnapshot? Totals,
    LiveDetectionDebugSnapshot? DebugSnapshot,
    Bitmap? Thumbnail) : IDisposable
{
    public void Dispose() => Thumbnail?.Dispose();
}
