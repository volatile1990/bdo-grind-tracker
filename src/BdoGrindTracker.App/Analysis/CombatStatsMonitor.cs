using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Confirms two matching HUD observations without blocking capture or loot analysis.</summary>
internal sealed class CombatStatsMonitor : IDisposable
{
    internal static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly ICombatStatsFrameReader _reader;
    private readonly TimeSpan _samplingInterval;
    private Task _analysis = Task.CompletedTask;
    private CancellationTokenSource? _cancellation;
    private DateTimeOffset? _lastScheduledAt, _candidateAt;
    private CombatStatsReading? _candidate;
    private CombatStatsState _state = CombatStatsState.Unknown;
    private long _epoch;
    private bool _running, _disposed, _readerDisposed;

    internal CombatStatsMonitor(ICombatStatsFrameReader reader, TimeSpan? samplingInterval = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _samplingInterval = samplingInterval ?? SamplingInterval;
        if (_samplingInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(samplingInterval));
    }

    internal Task CurrentAnalysis { get { lock (_sync) return _analysis; } }

    internal void Observe(Bitmap frame, DateTimeOffset capturedAt)
    {
        lock (_sync)
        {
            if (_disposed || _running || capturedAt <= DateTimeOffset.UnixEpoch ||
                _lastScheduledAt is { } previous && capturedAt - previous < _samplingInterval) return;
            _lastScheduledAt = capturedAt;
            Bitmap? copy = null;
            CancellationTokenSource? cancellation = null;
            try
            {
                // Preserve frame dimensions for saved UI-scale matching. Only one worker
                // owns a frame copy, and the reader crops before performing OCR.
                copy = (Bitmap)frame.Clone();
                cancellation = new CancellationTokenSource(AnalysisTimeout);
                _cancellation = cancellation;
                _running = true;
                var epoch = _epoch;
                var ownedCopy = copy;
                var ownedCancellation = cancellation;
                _analysis = Task.Run(() => Read(ownedCopy, capturedAt, epoch, ownedCancellation));
            }
            catch (Exception)
            {
                copy?.Dispose(); cancellation?.Dispose();
                _cancellation = null; _running = false;
                Clear();
            }
        }
    }

    internal CombatStatsState Snapshot(DateTimeOffset now)
    {
        lock (_sync)
            return !_disposed && _state.ObservedAt is { } at && now >= at && now - at < MaximumObservationAge
                ? _state : CombatStatsState.Unknown;
    }

    internal void Reset()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed) return;
            _epoch++; Clear(); _lastScheduledAt = null;
            cancellation = _cancellation;
        }
        Cancel(cancellation);
    }

    private void Read(Bitmap frame, DateTimeOffset capturedAt, long epoch, CancellationTokenSource cancellation)
    {
        try
        {
            var reading = _reader.Read(frame, cancellation.Token);
            lock (_sync)
            {
                if (_disposed || epoch != _epoch) return;
                if (cancellation.IsCancellationRequested || reading is null ||
                    !new CombatStatsState(reading.Ap, reading.Dp, reading.Category, capturedAt).IsKnown)
                {
                    Clear(); return;
                }
                var confirms = _candidate == reading && _candidateAt is { } previous &&
                    capturedAt > previous && capturedAt - previous < MaximumObservationAge;
                _candidate = reading; _candidateAt = capturedAt;
                _state = confirms ? new(reading.Ap, reading.Dp, reading.Category, capturedAt) : CombatStatsState.Unknown;
            }
        }
        catch (Exception)
        {
            lock (_sync) if (!_disposed && epoch == _epoch) Clear();
        }
        finally
        {
            frame.Dispose();
            var disposeReader = false;
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                _running = false;
                if (_disposed && !_readerDisposed) { _readerDisposed = true; disposeReader = true; }
            }
            cancellation.Dispose();
            if (disposeReader) DisposeReader();
        }
    }

    private void Clear() { _state = CombatStatsState.Unknown; _candidate = null; _candidateAt = null; }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        var disposeReader = false;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _epoch++; Clear();
            cancellation = _cancellation;
            if (!_running && !_readerDisposed) { _readerDisposed = true; disposeReader = true; }
        }
        Cancel(cancellation);
        if (disposeReader) DisposeReader();
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (Exception) { /* Optional HUD recognition must not interrupt tracking. */ }
    }

    private void DisposeReader()
    {
        try { _reader.Dispose(); }
        catch (Exception) { /* Optional HUD recognition must not interrupt shutdown. */ }
    }
}
