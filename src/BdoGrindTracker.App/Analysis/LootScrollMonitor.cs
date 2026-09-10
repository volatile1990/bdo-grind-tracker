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
    internal static readonly TimeSpan DefaultSamplingInterval = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan InactiveConfirmationDuration = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(90);
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
    private DateTimeOffset _timerStableSince;
    private (TimeSpan Consumed, TimeSpan Elapsed)? _previousCountdown;
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
        _maximumObservationAge = _samplingInterval > DefaultSamplingInterval
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
                ClearObservation();
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
        if (_lastAppliedAt is { } previous)
        {
            if (capturedAt <= previous) return;
            if (capturedAt - previous >= _maximumObservationAge) ClearObservation();
        }
        _lastAppliedAt = capturedAt;

        // Only the countdown establishes activity. The selected level and bag
        // can remain visible while time is stopped, including on level zero.
        if (reading.RemainingTime is { } remaining && remaining >= TimeSpan.Zero &&
            reading.TimerResolution is { } resolution && resolution > TimeSpan.Zero)
        {
            ApplyTimer(reading, remaining, resolution, capturedAt);
            return;
        }

        ClearObservation();
    }

    private void ApplyTimer(LootScrollReading reading, TimeSpan remaining,
        TimeSpan resolution, DateTimeOffset capturedAt)
    {
        var previousRemaining = _previousRemainingTime;
        var samePrecision = _previousTimerResolution == resolution;
        var elapsed = capturedAt - _previousTimerAt;
        _previousRemainingTime = remaining;
        _previousTimerResolution = resolution;
        _previousTimerAt = capturedAt;
        _state = LootScrollState.Unknown;

        if (previousRemaining is not { } previous || !samePrecision || remaining > previous)
        {
            // Recharging or changing visible precision cannot prove that a timer
            // stopped, and an old warning must not survive a new baseline.
            _timerStableSince = capturedAt;
            _previousCountdown = null;
            return;
        }

        if (remaining < previous)
        {
            _timerStableSince = capturedAt;
            // OCR digit errors must not turn a stationary timer into activity.
            // Accept consumption near 1x/2x wall time with a rounding margin;
            // require two successive plausible decreases before confirming it.
            var consumed = previous - remaining;
            var plausible = remaining > TimeSpan.Zero && IsPlausibleCountdown(consumed, elapsed, resolution);
            if (_previousCountdown is { } preceding)
            {
                // Rounding uncertainty belongs to the endpoints, not to each
                // interval independently. Check the complete confirmation window.
                plausible &= IsPlausibleCountdown(consumed + preceding.Consumed,
                    elapsed + preceding.Elapsed, resolution);
                if (plausible)
                    _state = new(LootScrollStatus.Active,
                        reading.Level is 1 or 2 ? reading.Level : null, capturedAt);
            }
            _previousCountdown = plausible ? (consumed, elapsed) : null;
            return;
        }

        _previousCountdown = null;
        // A minute-only display may legitimately stay unchanged for almost a
        // minute. Preserve the first equal value across samples, and allow a
        // small margin beyond its precision before treating equality as stopped.
        var stableDuration = capturedAt - _timerStableSince;
        if (stableDuration >= InactiveConfirmationDuration &&
            stableDuration >= resolution && stableDuration - resolution >= TimeSpan.FromSeconds(2))
        {
            _state = new(LootScrollStatus.Inactive, null, capturedAt);
        }
    }

    private static bool IsPlausibleCountdown(TimeSpan consumed, TimeSpan elapsed, TimeSpan resolution)
    {
        var tolerance = resolution.TotalSeconds + 3;
        return elapsed >= InactiveConfirmationDuration && resolution <= elapsed &&
            consumed.TotalSeconds >= Math.Max(1, elapsed.TotalSeconds * .5 - tolerance) &&
            consumed.TotalSeconds <= elapsed.TotalSeconds * 2 + tolerance;
    }

    private void ClearTimer()
    {
        _previousRemainingTime = null;
        _previousTimerResolution = null;
        _previousTimerAt = default;
        _timerStableSince = default;
        _previousCountdown = null;
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
