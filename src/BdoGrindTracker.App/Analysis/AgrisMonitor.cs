using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Optional, nonblocking HUD sampling; owns at most one frame and one detector worker.</summary>
internal sealed class AgrisMonitor : IDisposable
{
    internal static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(15);
    private readonly object _sync = new();
    private readonly IAgrisFrameDetector _detector;
    private readonly TimeSpan _samplingInterval;
    private Task _analysis = Task.CompletedTask;
    private CancellationTokenSource? _cancellation;
    private DateTimeOffset? _lastScheduledAt;
    private AgrisState _state = AgrisState.Unknown;
    private long _epoch;
    private bool _running, _disposed, _detectorDisposed;

    internal AgrisMonitor(IAgrisFrameDetector detector, TimeSpan? samplingInterval = null)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _samplingInterval = samplingInterval ?? SamplingInterval;
        if (_samplingInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(samplingInterval));
    }

    internal Task CurrentAnalysis { get { lock (_sync) return _analysis; } }

    internal void Observe(Bitmap frame, DateTimeOffset capturedAt)
    {
        lock (_sync)
        {
            if (_disposed || _running || _lastScheduledAt is { } previous && capturedAt - previous < _samplingInterval) return;
            _lastScheduledAt = capturedAt;
            Bitmap? copy = null;
            CancellationTokenSource? cancellation = null;
            try
            {
                copy = (Bitmap)frame.Clone();
                cancellation = new CancellationTokenSource();
                _cancellation = cancellation;
                _running = true;
                var epoch = _epoch;
                var ownedCopy = copy;
                var ownedCancellation = cancellation;
                _analysis = Task.Run(() => Analyze(ownedCopy, capturedAt, epoch, ownedCancellation));
            }
            catch (Exception)
            {
                copy?.Dispose();
                cancellation?.Dispose();
                _cancellation = null;
                _running = false;
                _state = AgrisState.Unknown;
                _epoch++;
            }
        }
    }

    internal AgrisState Snapshot(DateTimeOffset now) => Snapshot(now, out _);

    // The generation preserves HUD interruptions even when UI refresh skips
    // the short-lived Unknown state between two completed observations.
    internal AgrisState Snapshot(DateTimeOffset now, out long generation)
    {
        lock (_sync)
        {
            generation = _epoch;
            return !_disposed && _state.ObservedAt is { } observedAt && now >= observedAt &&
                now - observedAt < MaximumObservationAge ? _state : AgrisState.Unknown;
        }
    }

    internal void Reset()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed) return;
            _epoch++;
            _state = AgrisState.Unknown;
            _lastScheduledAt = null;
            cancellation = _cancellation;
        }
        Cancel(cancellation);
    }

    private void Analyze(Bitmap frame, DateTimeOffset capturedAt, long epoch, CancellationTokenSource cancellation)
    {
        try
        {
            var reading = _detector.Analyze(frame, cancellation.Token);
            lock (_sync)
            {
                if (_disposed || epoch != _epoch || cancellation.IsCancellationRequested) return;
                _state = reading.Status is AgrisStatus.Active or AgrisStatus.Inactive
                    ? new AgrisState(reading.Status, capturedAt) : AgrisState.Unknown;
                if (_state.Status == AgrisStatus.Unknown) _epoch++;
            }
        }
        catch (Exception)
        {
            lock (_sync)
                if (!_disposed && epoch == _epoch)
                {
                    _state = AgrisState.Unknown;
                    _epoch++;
                }
        }
        finally
        {
            frame.Dispose();
            var disposeDetector = false;
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                _running = false;
                if (_disposed && !_detectorDisposed) { _detectorDisposed = true; disposeDetector = true; }
            }
            cancellation.Dispose();
            if (disposeDetector) DisposeDetector();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        var disposeDetector = false;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _epoch++;
            _state = AgrisState.Unknown;
            cancellation = _cancellation;
            if (!_running && !_detectorDisposed) { _detectorDisposed = true; disposeDetector = true; }
        }
        Cancel(cancellation);
        if (disposeDetector) DisposeDetector();
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (Exception) { /* Recognition cannot interrupt capture or session commands. */ }
    }

    private void DisposeDetector()
    {
        try { _detector.Dispose(); }
        catch (Exception) { /* Optional HUD cleanup cannot prevent shutdown. */ }
    }
}
