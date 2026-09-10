using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Runs the optional level/percentage OCR once a minute, independently of loot analysis.</summary>
internal sealed class ExperienceMonitor : IDisposable
{
    internal static readonly TimeSpan SamplingInterval = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(150);
    private readonly object _sync = new();
    private readonly IExperienceFrameReader _reader;
    private readonly TimeSpan _samplingInterval;
    private Task _analysis = Task.CompletedTask;
    private CancellationTokenSource? _cancellation;
    private DateTimeOffset? _lastScheduledAt;
    private ExperienceState _state = ExperienceState.Unknown;
    private long _epoch;
    private bool _running, _disposed, _readerDisposed;

    internal ExperienceMonitor(IExperienceFrameReader reader, TimeSpan? samplingInterval = null)
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
                _analysis = Task.Run(() => Read(ownedCopy, capturedAt, epoch, ownedCancellation));
            }
            catch (Exception)
            {
                copy?.Dispose();
                cancellation?.Dispose();
                _cancellation = null;
                _running = false;
                _state = ExperienceState.Unknown;
                _epoch++;
            }
        }
    }

    internal ExperienceState Snapshot(DateTimeOffset now) => Snapshot(now, out _);

    internal ExperienceState Snapshot(DateTimeOffset now, out long generation)
    {
        lock (_sync)
        {
            generation = _epoch;
            return !_disposed && _state.ObservedAt is { } observedAt && now >= observedAt &&
                now - observedAt < MaximumObservationAge ? _state : ExperienceState.Unknown;
        }
    }

    internal void Reset()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed) return;
            _epoch++;
            _state = ExperienceState.Unknown;
            _lastScheduledAt = null;
            cancellation = _cancellation;
        }
        Cancel(cancellation);
    }

    private void Read(Bitmap frame, DateTimeOffset capturedAt, long epoch, CancellationTokenSource cancellation)
    {
        try
        {
            var reading = _reader.Read(frame, cancellation.Token);
            var state = reading is null ? ExperienceState.Unknown : new ExperienceState(reading.Level, reading.Percent, capturedAt);
            lock (_sync)
            {
                if (_disposed || epoch != _epoch || cancellation.IsCancellationRequested) return;
                _state = state.IsKnown ? state : ExperienceState.Unknown;
                if (!_state.IsKnown) _epoch++;
            }
        }
        catch (Exception)
        {
            lock (_sync)
                if (!_disposed && epoch == _epoch)
                {
                    _state = ExperienceState.Unknown;
                    _epoch++;
                }
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

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        var disposeReader = false;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _epoch++;
            _state = ExperienceState.Unknown;
            cancellation = _cancellation;
            if (!_running && !_readerDisposed) { _readerDisposed = true; disposeReader = true; }
        }
        Cancel(cancellation);
        if (disposeReader) DisposeReader();
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (Exception) { /* Optional OCR must not interrupt session commands. */ }
    }

    private void DisposeReader()
    {
        try { _reader.Dispose(); }
        catch (Exception) { /* Optional OCR cleanup cannot prevent shutdown. */ }
    }
}
