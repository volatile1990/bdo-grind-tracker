using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Applies each projection or legacy event on the producer thread, but retains only the latest visual
/// state. A busy UI cannot queue captures, events, or full-resolution bitmaps.
/// </summary>
internal sealed class FrameUiMailbox : IDisposable
{
    private readonly object _sync = new();
    private readonly LootSessionAggregate _aggregate = new();
    private readonly LootProjectionBuffer _projectionBuffer = new();
    private FrameAnalysisResult? _latestAnalysis;
    private LiveDetectionDebugSnapshot? _latestDebugSnapshot;
    private Bitmap? _latestThumbnail;
    private bool _totalsChanged;
    private bool _disposed;

    // Projection activity follows new arrival times; historical modes use new positive outputs.
    public bool Publish(
        FrameAnalysisResult analysis,
        LiveDetectionDebugSnapshot? debugSnapshot = null,
        Bitmap? thumbnail = null,
        Action<IReadOnlyDictionary<string, long>, bool>? onPublished = null,
        DateTimeOffset? capturedAt = null,
        bool flushProjection = false)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                thumbnail?.Dispose();
                return false;
            }

            bool hasNewArrival;
            if (analysis.LootProjection is { } projection)
            {
                if (capturedAt is { } at)
                    projection = _projectionBuffer.Observe(projection, at, flushProjection);
                var applied = _aggregate.ApplyProjection(projection);
                _totalsChanged |= applied.TotalsChanged;
                hasNewArrival = applied.HasNewArrival;
            }
            else
            {
                var previousEventCount = _aggregate.ConfirmedEventCount;
                var previousQuantity = _aggregate.TotalQuantity;
                foreach (var lootEvent in analysis.NewEvents)
                    _aggregate.Apply(lootEvent);
                _totalsChanged |= _aggregate.ConfirmedEventCount != previousEventCount ||
                    _aggregate.TotalQuantity != previousQuantity;
                hasNewArrival = _aggregate.ConfirmedEventCount > previousEventCount;
            }
            // Observe the same cumulative state before the UI can consume it.
            // The callback must copy any values retained beyond this call.
            onPublished?.Invoke(_aggregate.Totals, hasNewArrival);
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
            return hasNewArrival;
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

    public void AdjustQuantity(string itemName, long quantity, long originalQuantity,
        Action<LootSessionSnapshot> beforeCommit,
        Action<IReadOnlyDictionary<string, long>, bool>? onPublished = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _aggregate.AdjustQuantity(itemName, quantity, originalQuantity, beforeCommit);
            _totalsChanged = true;
            // Corrections change future upload deltas, never the drop/idle clock.
            onPublished?.Invoke(_aggregate.Totals, false);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _aggregate.Reset();
            _projectionBuffer.Reset();
            _latestAnalysis = null;
            _latestDebugSnapshot = null;
            _latestThumbnail?.Dispose();
            _latestThumbnail = null;
            _totalsChanged = false;
        }
    }

    public void Restore(LootSessionSnapshot snapshot, IEnumerable<string> manualItems)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Aggregate validation completes before any queued UI data is lost.
            _aggregate.Restore(snapshot, manualItems);
            _projectionBuffer.Reset();
            _latestAnalysis = null;
            _latestDebugSnapshot = null;
            _latestThumbnail?.Dispose();
            _latestThumbnail = null;
            _totalsChanged = true;
        }
    }

    internal T ReadSnapshot<T>(Func<LootSessionSnapshot, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var snapshot = new LootSessionSnapshot(
                new Dictionary<string, long>(_aggregate.Totals, StringComparer.OrdinalIgnoreCase),
                _aggregate.TotalQuantity, _aggregate.ConfirmedEventCount);
            // The caller can pair these totals with another producer-owned
            // checkpoint using the same lock order as Publish.
            return read(snapshot);
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

    public int ItemTypeCount => Totals.Count(pair => pair.Value != 0);
}

internal sealed record FrameUiUpdate(
    FrameAnalysisResult Analysis,
    LootSessionSnapshot? Totals,
    LiveDetectionDebugSnapshot? DebugSnapshot,
    Bitmap? Thumbnail) : IDisposable
{
    public void Dispose() => Thumbnail?.Dispose();
}
