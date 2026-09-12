using System.Globalization;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Measures active grind time with monotonic timestamps, excluding pauses.
/// The system provider uses Stopwatch timestamps rather than the wall clock.
/// </summary>
internal sealed class GrindSessionClock(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _sync = new();
    private TimeSpan _accumulated;
    private long _startedAt;
    private bool _isRunning;
    private bool _isWaitingForFirstDrop;

    public bool IsRunning { get { lock (_sync) return _isRunning; } }
    public bool IsWaitingForFirstDrop { get { lock (_sync) return _isWaitingForFirstDrop; } }

    public TimeSpan Elapsed => GetElapsedExcludingTrailingIdle(TimeSpan.Zero);

    // Capture also samples the clock for hourly uploads. Only confirmed active
    // time can be committed while automatic pause may still remove the idle tail.
    public TimeSpan GetElapsedExcludingTrailingIdle(TimeSpan idleDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(idleDuration, TimeSpan.Zero);
        lock (_sync)
        {
            if (!_isRunning || _isWaitingForFirstDrop) return _accumulated;
            var segment = _timeProvider.GetElapsedTime(_startedAt);
            return _accumulated + (segment > idleDuration ? segment - idleDuration : TimeSpan.Zero);
        }
    }

    public void Start(bool waitForFirstDrop = false)
    {
        lock (_sync)
        {
            if (_isRunning) return;
            _startedAt = _timeProvider.GetTimestamp();
            _isWaitingForFirstDrop = waitForFirstDrop;
            _isRunning = true;
        }
    }

    /// <summary>Starts an armed segment on its first new drop; later drops do not reset it.</summary>
    public bool RecordDrop()
    {
        lock (_sync)
        {
            // Final capture frames may still publish drops after Pause. They can
            // update loot totals, but must never restart a disarmed clock.
            if (!_isRunning || !_isWaitingForFirstDrop) return false;
            _startedAt = _timeProvider.GetTimestamp();
            _isWaitingForFirstDrop = false;
            return true;
        }
    }

    /// <summary>
    /// Stops this active segment, optionally excluding its trailing idle time.
    /// Never removes time accumulated before the most recent Start call.
    /// </summary>
    public void Pause(TimeSpan excludedTrailingDuration = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(excludedTrailingDuration, TimeSpan.Zero);
        lock (_sync)
        {
            if (!_isRunning) return;
            var segmentDuration = _isWaitingForFirstDrop ? TimeSpan.Zero : _timeProvider.GetElapsedTime(_startedAt);
            _accumulated += segmentDuration > excludedTrailingDuration
                ? segmentDuration - excludedTrailingDuration
                : TimeSpan.Zero;
            _isRunning = false;
            _isWaitingForFirstDrop = false;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _isRunning = false;
            _isWaitingForFirstDrop = false;
            _accumulated = TimeSpan.Zero;
            _startedAt = 0;
        }
    }

    public void RestorePaused(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        lock (_sync)
        {
            _accumulated = elapsed;
            _startedAt = 0;
            _isRunning = false;
            _isWaitingForFirstDrop = false;
        }
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        var totalHours = elapsed.Ticks / TimeSpan.TicksPerHour;
        return string.Create(CultureInfo.InvariantCulture,
            $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }
}
