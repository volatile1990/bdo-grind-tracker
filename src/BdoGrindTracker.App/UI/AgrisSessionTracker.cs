namespace BdoGrindTracker.App.UI;

internal readonly record struct AgrisSessionDuration(TimeSpan ActiveDuration, TimeSpan ObservedDuration);

/// <summary>
/// Estimates Agris usage only between fresh HUD samples on the session's active
/// clock. Unknown gaps and unobserved tails are never extrapolated.
/// </summary>
internal sealed class AgrisSessionTracker
{
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMilliseconds(250);
    private readonly List<Interval> _intervals = [];
    private TimeSpan _activeDuration;
    private TimeSpan _observedDuration;
    private TimeSpan _lastElapsed;
    private DateTimeOffset? _lastObservedAt;
    private Sample? _previous;

    internal AgrisSessionDuration Update(TimeSpan elapsed, AgrisState state, bool isRunning, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        TrimTo(elapsed);
        if (!isRunning || state.Status is not (AgrisStatus.Active or AgrisStatus.Inactive) ||
            state.ObservedAt is not { } observedAt || observedAt > now || now - observedAt > MaximumObservationAge)
        {
            _previous = null;
            // A delayed frame from before HUD loss must not establish a new
            // baseline on the other side of the unknown/paused interval.
            if (_lastObservedAt is null || now > _lastObservedAt) _lastObservedAt = now;
            return Duration;
        }

        // UI refreshes can read one camera sample many times. They neither earn
        // additional time nor break the next genuine observation's interval.
        if (_lastObservedAt is { } last && observedAt <= last) return Duration;
        _lastObservedAt = observedAt;
        var age = now - observedAt;
        var sampleElapsed = elapsed > age ? elapsed - age : TimeSpan.Zero;
        if (_previous is { } previous)
        {
            var wallGap = observedAt - previous.ObservedAt;
            var activeGap = sampleElapsed - previous.Elapsed;
            if (wallGap > TimeSpan.Zero && wallGap <= MaximumObservationAge && activeGap > TimeSpan.Zero &&
                activeGap <= wallGap + ClockTolerance)
            {
                // Small differences between the monotonic clock and wall clock
                // must not turn delayed UI processing into extra Agris time.
                var start = sampleElapsed - (activeGap < wallGap ? activeGap : wallGap);
                AddInterval(start, sampleElapsed,
                    previous.Status == AgrisStatus.Active && state.Status == AgrisStatus.Active);
            }
        }
        _previous = new(sampleElapsed, observedAt, state.Status);
        return Duration;
    }

    internal AgrisSessionDuration Snapshot(TimeSpan elapsed)
    {
        TrimTo(elapsed);
        return Duration;
    }

    internal void Pause(TimeSpan elapsed)
    {
        TrimTo(elapsed);
        _previous = null;
    }

    internal void Reset()
    {
        _intervals.Clear();
        _activeDuration = _observedDuration = _lastElapsed = TimeSpan.Zero;
        _lastObservedAt = null;
        _previous = null;
    }

    private AgrisSessionDuration Duration => new(_activeDuration, _observedDuration);

    private void AddInterval(TimeSpan start, TimeSpan end, bool active)
    {
        if (_intervals.Count > 0 && start < _intervals[^1].End) start = _intervals[^1].End;
        if (end <= start) return;
        var duration = end - start;
        _observedDuration += duration;
        if (active) _activeDuration += duration;
        if (_intervals.Count > 0 && _intervals[^1] is var previous && previous.End == start && previous.Active == active)
            _intervals[^1] = previous with { End = end };
        else
            _intervals.Add(new(start, end, active));
    }

    private void TrimTo(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        if (elapsed < _lastElapsed)
        {
            // Automatic pause removes the idle tail from the active clock.
            // Keep the actual retained intervals, not a clamped aggregate that
            // could incorrectly retain Agris time from the discarded tail.
            _previous = null;
            while (_intervals.Count > 0 && _intervals[^1].End > elapsed)
            {
                var last = _intervals[^1];
                var end = elapsed > last.Start ? elapsed : last.Start;
                var removed = last.End - end;
                _observedDuration -= removed;
                if (last.Active) _activeDuration -= removed;
                if (end == last.Start) _intervals.RemoveAt(_intervals.Count - 1);
                else _intervals[^1] = last with { End = end };
            }
        }
        _lastElapsed = elapsed;
    }

    private readonly record struct Sample(TimeSpan Elapsed, DateTimeOffset ObservedAt, AgrisStatus Status);
    private readonly record struct Interval(TimeSpan Start, TimeSpan End, bool Active);
}
