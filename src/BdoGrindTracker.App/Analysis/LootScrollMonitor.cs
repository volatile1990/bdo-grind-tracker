using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Analysis;

internal sealed record LootScrollReading(LootScrollStatus Status, int? Level = null)
{
    public TimeSpan? RemainingTime { get; init; }
    public TimeSpan? TimerResolution { get; init; }
    public static LootScrollReading Unknown { get; } = new(LootScrollStatus.Unknown);
}

internal interface ILootScrollFrameDetector : IDisposable
{
    LootScrollReading Analyze(Bitmap frame, CancellationToken cancellationToken);
}

/// <summary>
/// Samples independently of loot reconciliation. There is at most one owned bitmap
/// and one worker; capture never waits for recognition and all detection failures are optional.
/// </summary>
internal sealed class LootScrollMonitor : IDisposable
{
    internal static readonly TimeSpan DefaultSamplingInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan InactiveConfirmationDuration = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(120);
    private readonly object _sync = new();
    private readonly ILootScrollFrameDetector _detector;
    private readonly TimeSpan _samplingInterval;
    private readonly TimeSpan _maximumObservationAge;
    private Task _currentAnalysis = Task.CompletedTask;
    private CancellationTokenSource? _workerCancellation;
    private DateTimeOffset? _lastScheduledAt;
    private DateTimeOffset? _lastAppliedAt;
    private LootScrollState _state = LootScrollState.Unknown;
    private TimeSpan? _previousRemainingTime;
    private TimeSpan? _previousTimerResolution;
    private DateTimeOffset _previousTimerAt;
    private DateTimeOffset? _lastValidTimerAt;
    private (TimeSpan Remaining, TimeSpan Resolution, DateTimeOffset At)? _comparisonCandidate;
    private long _epoch;
    private bool _workerRunning;
    private bool _disposed;
    private bool _detectorDisposed;

    public LootScrollMonitor(ILootScrollFrameDetector detector, TimeSpan? samplingInterval = null)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _samplingInterval = samplingInterval ?? DefaultSamplingInterval;
        if (_samplingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(samplingInterval));
        _maximumObservationAge = _samplingInterval + TimeSpan.FromSeconds(30) > MaximumObservationAge
            ? _samplingInterval + TimeSpan.FromSeconds(30) : MaximumObservationAge;
    }

    // Lifecycle callers and tests may observe completion; capture never awaits it.
    internal Task CurrentAnalysis { get { lock (_sync) return _currentAnalysis; } }

    public void Observe(Bitmap frame, DateTimeOffset capturedAt)
    {
        lock (_sync)
        {
            if (_disposed || _workerRunning ||
                _lastScheduledAt is { } previous && capturedAt - previous < _samplingInterval)
                return;

            _lastScheduledAt = capturedAt;
            Bitmap? copy = null;
            CancellationTokenSource? cancellation = null;
            try
            {
                // The source belongs to capture and is disposed after this callback.
                copy = (Bitmap)frame.Clone();
                cancellation = new CancellationTokenSource();
                _workerCancellation = cancellation;
                _workerRunning = true;
                var epoch = _epoch;
                var ownedCopy = copy;
                var ownedCancellation = cancellation;
                _currentAnalysis = Task.Run(() => Analyze(ownedCopy, capturedAt, epoch, ownedCancellation));
            }
            catch (Exception)
            {
                copy?.Dispose();
                cancellation?.Dispose();
                _workerCancellation = null;
                _workerRunning = false;
                Apply(LootScrollReading.Unknown, capturedAt);
            }
        }
    }

    public LootScrollState Snapshot(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_disposed || _state.ObservedAt is not { } observedAt ||
                now < observedAt || now - observedAt >= _maximumObservationAge)
                return LootScrollState.Unknown;
            return _state;
        }
    }

    public void Reset()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed) return;
            _epoch++;
            _lastScheduledAt = null;
            _lastAppliedAt = null;
            ClearObservation();
            cancellation = _workerCancellation;
        }
        Cancel(cancellation);
    }

    private void Analyze(Bitmap frame, DateTimeOffset capturedAt, long epoch,
        CancellationTokenSource cancellation)
    {
        try
        {
            using (frame)
            {
                var reading = LootScrollReading.Unknown;
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    reading = _detector.Analyze(frame, cancellation.Token) ?? LootScrollReading.Unknown;
                }
                catch (Exception) { /* Optional HUD recognition must never stop loot capture. */ }

                lock (_sync)
                {
                    if (!_disposed && _epoch == epoch && !cancellation.IsCancellationRequested)
                        Apply(reading, capturedAt);
                }
            }
        }
        catch (Exception)
        {
            lock (_sync)
                if (!_disposed && _epoch == epoch) ClearObservation();
        }
        finally
        {
            var disposeDetector = false;
            lock (_sync)
            {
                _workerCancellation = null;
                _workerRunning = false;
                if (_disposed && !_detectorDisposed)
                {
                    _detectorDisposed = true;
                    disposeDetector = true;
                }
            }
            cancellation.Dispose();
            if (disposeDetector) DisposeDetector();
        }
    }

    private void Apply(LootScrollReading reading, DateTimeOffset capturedAt)
    {
        if (_lastAppliedAt is { } previous && capturedAt <= previous) return;
        _lastAppliedAt = capturedAt;

        // Only the countdown establishes activity. The selected level and bag
        // can remain visible while time is stopped, including on level zero.
        if (reading.RemainingTime is { } remaining && remaining >= TimeSpan.Zero &&
            reading.TimerResolution is { } resolution && resolution > TimeSpan.Zero)
        {
            if (_lastValidTimerAt is { } lastValid && capturedAt - lastValid > _maximumObservationAge)
                ClearObservation();
            ApplyTimer(remaining, resolution, capturedAt);
            return;
        }

        // An unreadable sample does not contradict a confirmed countdown. Its
        // timestamp must not extend either the observation or baseline lifetime.
        if (_lastValidTimerAt is { } validAt && capturedAt - validAt >= _maximumObservationAge)
            ClearObservation();
    }

    private void ApplyTimer(TimeSpan remaining,
        TimeSpan resolution, DateTimeOffset capturedAt)
    {
        var elapsed = capturedAt - _previousTimerAt;
        _lastValidTimerAt = capturedAt;
        if (_previousRemainingTime is not { } previous)
        {
            SetTimerBaseline(remaining, resolution, capturedAt);
            _state = LootScrollState.Unknown;
            return;
        }

        // A coarse display needs two precision units plus sampling jitter for a
        // useful pair. This does not extend the confirmed state's freshness or
        // bridge a gap without readable timer samples.
        var comparisonAgeSeconds = Math.Max(_maximumObservationAge.TotalSeconds,
            resolution.TotalSeconds * 2 + _samplingInterval.TotalSeconds * 2);
        if (elapsed.TotalSeconds > comparisonAgeSeconds)
        {
            if (_comparisonCandidate is { } candidate && candidate.Resolution == resolution &&
                capturedAt - candidate.At <= _maximumObservationAge)
            {
                previous = candidate.Remaining;
                elapsed = capturedAt - candidate.At;
                SetTimerBaseline(candidate.Remaining, candidate.Resolution, candidate.At);
            }
            else
            {
                SetTimerBaseline(remaining, resolution, capturedAt);
                _state = LootScrollState.Unknown;
                return;
            }
        }

        if (_previousTimerResolution != resolution)
        {
            // Different displayed precision is not a reliable consumption pair.
            SetTimerBaseline(remaining, resolution, capturedAt);
            return;
        }

        if (remaining > previous)
        {
            // A recharge changes the timer independently of consumption. Start a
            // new comparison here; no previous status survives the transition.
            SetTimerBaseline(remaining, resolution, capturedAt);
            _state = LootScrollState.Unknown;
            return;
        }

        if (remaining == TimeSpan.Zero && remaining != previous)
        {
            SetTimerBaseline(remaining, resolution, capturedAt);
            _state = LootScrollState.Unknown;
            return;
        }

        var level = ComparisonLevel(previous, remaining, elapsed, resolution);
        if (level is null && _comparisonCandidate is { } recent && recent.Resolution == resolution &&
            capturedAt - recent.At <= _maximumObservationAge)
            level = ComparisonLevel(recent.Remaining, remaining, capturedAt - recent.At, resolution);
        if (level is { } confirmed)
        {
            _state = confirmed == 0 ? new(LootScrollStatus.Inactive, null, capturedAt)
                : new(LootScrollStatus.Active, confirmed, capturedAt);
            SetTimerBaseline(remaining, resolution, capturedAt);
        }
        else if ((previous - remaining).TotalSeconds <= elapsed.TotalSeconds * 2 + resolution.TotalSeconds + 2)
        {
            // A switch halfway between samples can yield e.g. 1.5x. Retain that
            // plausible endpoint as an alternative for the next pure interval,
            // while an impossible OCR drop never replaces the trusted baseline.
            _comparisonCandidate = (remaining, resolution, capturedAt);
        }
        // Keep a usable baseline and the existing observation after one bad or
        // ambiguous OCR value. Neither becomes newer until a comparison succeeds.
    }

    private static int? ComparisonLevel(TimeSpan previous, TimeSpan remaining, TimeSpan elapsed, TimeSpan resolution)
    {
        if (remaining > previous) return null;
        if (remaining == previous)
            return elapsed >= InactiveConfirmationDuration && elapsed.TotalSeconds >= resolution.TotalSeconds + 2
                ? 0 : null;
        return remaining > TimeSpan.Zero ? ConsumptionLevel(previous - remaining, elapsed, resolution) : null;
    }

    private static int? ConsumptionLevel(TimeSpan consumed, TimeSpan elapsed, TimeSpan resolution)
    {
        // Seconds rounding plus a small OCR margin. Coarse displays need a longer
        // window so their rounding cannot disguise a 3x drop as level two.
        if (elapsed < InactiveConfirmationDuration || elapsed.TotalSeconds < resolution.TotalSeconds * 2)
            return null;
        var tolerance = resolution.TotalSeconds + 2;
        var levelOne = Math.Abs(consumed.TotalSeconds - elapsed.TotalSeconds) <= tolerance;
        var levelTwo = Math.Abs(consumed.TotalSeconds - elapsed.TotalSeconds * 2) <= tolerance;
        return levelOne == levelTwo ? null : levelOne ? 1 : 2;
    }

    private void SetTimerBaseline(TimeSpan remaining, TimeSpan resolution, DateTimeOffset capturedAt)
    {
        _previousRemainingTime = remaining;
        _previousTimerResolution = resolution;
        _previousTimerAt = capturedAt;
        _comparisonCandidate = null;
    }

    private void ClearTimer()
    {
        _previousRemainingTime = null;
        _previousTimerResolution = null;
        _previousTimerAt = default;
        _lastValidTimerAt = null;
        _comparisonCandidate = null;
    }

    private void ClearObservation()
    {
        ClearTimer();
        _state = LootScrollState.Unknown;
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
            ClearObservation();
            cancellation = _workerCancellation;
            if (!_workerRunning && !_detectorDisposed)
            {
                _detectorDisposed = true;
                disposeDetector = true;
            }
        }
        Cancel(cancellation);
        if (disposeDetector) DisposeDetector();
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (Exception) { /* A detector cancellation callback is also optional. */ }
    }

    private void DisposeDetector()
    {
        try { _detector.Dispose(); }
        catch (Exception) { /* HUD cleanup cannot fail session shutdown. */ }
    }
}
