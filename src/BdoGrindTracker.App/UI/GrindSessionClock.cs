using System.Globalization;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Measures active grind time with monotonic timestamps, excluding pauses.
/// The system provider uses Stopwatch timestamps rather than the wall clock.
/// </summary>
internal sealed class GrindSessionClock(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private TimeSpan _accumulated;
    private long _startedAt;

    public bool IsRunning { get; private set; }

    public TimeSpan Elapsed => IsRunning
        ? _accumulated + _timeProvider.GetElapsedTime(_startedAt)
        : _accumulated;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _startedAt = _timeProvider.GetTimestamp();
        IsRunning = true;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _accumulated += _timeProvider.GetElapsedTime(_startedAt);
        IsRunning = false;
    }

    public void Reset()
    {
        IsRunning = false;
        _accumulated = TimeSpan.Zero;
        _startedAt = 0;
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        var totalHours = elapsed.Ticks / TimeSpan.TicksPerHour;
        return string.Create(CultureInfo.InvariantCulture,
            $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }
}
