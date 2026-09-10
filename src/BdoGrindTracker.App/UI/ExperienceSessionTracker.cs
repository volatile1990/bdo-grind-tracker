namespace BdoGrindTracker.App.UI;

internal readonly record struct ExperienceSessionProgress(decimal? GainedPercentagePoints,
    TimeSpan ObservedDuration, int? StartLevel, int? EndLevel);

/// <summary>Net XP changes between observed endpoints on the active session clock.</summary>
internal sealed class ExperienceSessionTracker
{
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMilliseconds(250);
    private readonly List<Interval> _intervals = [];
    private TimeSpan _observedDuration;
    private TimeSpan _lastElapsed;
    private decimal _gainedPercentagePoints;
    private DateTimeOffset? _lastObservedAt;
    private Sample? _previous;
    private Sample? _pendingLevelUp;

    internal ExperienceSessionProgress Update(TimeSpan elapsed, ExperienceState state, bool isRunning, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        TrimTo(elapsed);
        if (!isRunning || !state.IsKnown || state.ObservedAt is not { } observedAt ||
            observedAt > now || now - observedAt > MaximumObservationAge)
        {
            ClearBaseline();
            // Before the first sample, Unknown is only startup state: a frame
            // captured just before that UI refresh may still be in OCR. Once
            // observations exist, keep fencing delayed frames across HUD loss.
            if (_lastObservedAt is { } lastObservedAt && now > lastObservedAt) _lastObservedAt = now;
            return Progress;
        }
        if (_lastObservedAt is { } last && observedAt <= last) return Progress;
        _lastObservedAt = observedAt;
        var age = now - observedAt;
        var current = new Sample(elapsed > age ? elapsed - age : TimeSpan.Zero,
            observedAt, state.Level!.Value, state.Percent!.Value);
        if (_previous is not { } previous)
        {
            _previous = current;
            return Progress;
        }

        if (_pendingLevelUp is { } pending)
        {
            // A single misread level must not manufacture a complete XP bar.
            // Confirm the new level with a later fresh sample before recording
            // either interval. Returning to the old level starts a new baseline.
            if (current.Level == pending.Level && CanConnect(previous, pending) && CanConnect(pending, current))
            {
                AddInterval(previous, pending, 100 - previous.Percent + pending.Percent);
                AddInterval(pending, current, current.Percent - pending.Percent);
            }
            _pendingLevelUp = null;
            _previous = current;
            return Progress;
        }

        if (CanConnect(previous, current))
        {
            if (current.Level == previous.Level)
            {
                // A decrease at the same level can be a death penalty. It is a
                // signed net change, never an inferred wrap to the next level.
                AddInterval(previous, current, current.Percent - previous.Percent);
            }
            else if (current.Level == previous.Level + 1)
            {
                _pendingLevelUp = current;
                return Progress;
            }
        }
        // Lower levels, larger jumps and time gaps can indicate a character
        // change or OCR uncertainty; none of them contributes a cross-gap gain.
        _previous = current;
        return Progress;
    }

    internal ExperienceSessionProgress Snapshot(TimeSpan elapsed)
    {
        TrimTo(elapsed);
        return Progress;
    }

    internal void Pause(TimeSpan elapsed)
    {
        TrimTo(elapsed);
        ClearBaseline();
    }

    internal void Reset()
    {
        _intervals.Clear();
        _observedDuration = _lastElapsed = TimeSpan.Zero;
        _gainedPercentagePoints = 0;
        _lastObservedAt = null;
        ClearBaseline();
    }

    private ExperienceSessionProgress Progress => _intervals.Count == 0
        ? new(null, TimeSpan.Zero, null, null)
        : new(_gainedPercentagePoints, _observedDuration, _intervals[0].StartLevel, _intervals[^1].EndLevel);

    private static bool CanConnect(Sample previous, Sample current)
    {
        var wallGap = current.ObservedAt - previous.ObservedAt;
        var activeGap = current.Elapsed - previous.Elapsed;
        return wallGap > TimeSpan.Zero && wallGap <= MaximumObservationAge && activeGap > TimeSpan.Zero &&
            activeGap <= wallGap + ClockTolerance;
    }

    private void AddInterval(Sample previous, Sample current, decimal gained)
    {
        var activeGap = current.Elapsed - previous.Elapsed;
        var wallGap = current.ObservedAt - previous.ObservedAt;
        var duration = activeGap < wallGap ? activeGap : wallGap;
        var start = current.Elapsed - duration;
        // An XP change cannot be apportioned to only the nonoverlapping part.
        if (_intervals.Count > 0 && start < _intervals[^1].End) return;
        _intervals.Add(new(start, current.Elapsed, gained, previous.Level, current.Level));
        _observedDuration += duration;
        _gainedPercentagePoints += gained;
    }

    private void TrimTo(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        if (elapsed < _lastElapsed)
        {
            ClearBaseline();
            // Automatic pause may discard the interval's final XP sample. Its
            // change has no known timestamp inside that interval, so drop the
            // whole interval instead of inventing a proportional XP gain.
            while (_intervals.Count > 0 && _intervals[^1].End > elapsed)
            {
                var removed = _intervals[^1];
                _intervals.RemoveAt(_intervals.Count - 1);
                _gainedPercentagePoints -= removed.Gained;
                _observedDuration -= removed.End - removed.Start;
            }
        }
        _lastElapsed = elapsed;
    }

    private void ClearBaseline() => _previous = _pendingLevelUp = null;

    private readonly record struct Sample(TimeSpan Elapsed, DateTimeOffset ObservedAt, int Level, decimal Percent);
    private readonly record struct Interval(TimeSpan Start, TimeSpan End, decimal Gained, int StartLevel, int EndLevel);
}
