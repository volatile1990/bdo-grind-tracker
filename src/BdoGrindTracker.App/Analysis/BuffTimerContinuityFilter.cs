using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Requires a second live reading before accepting an impossibly fast countdown drop.</summary>
internal sealed class BuffTimerContinuityFilter
{
    private static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(30);
    private const double TimerJitterSeconds = 2;
    private const int MaximumTrackedBuffs = 256;
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private DateTimeOffset? _lastCapturedAt;

    private sealed record Sample(BuffObservation Observation, DateTimeOffset CapturedAt);
    private sealed class State(Sample accepted)
    {
        internal Sample Accepted { get; set; } = accepted;
        internal Sample? PendingDrop { get; set; }
    }

    internal BuffFrameReading? Filter(BuffFrameReading? reading, DateTimeOffset? capturedAt)
    {
        // Screenshots and unavailable captures do not establish live continuity.
        if (reading is null || capturedAt is not { } at)
        {
            Reset();
            return reading;
        }

        if (_lastCapturedAt is { } previous)
        {
            // Replaying the same capture cannot confirm a pending low timer.
            if (at <= previous)
                return new([])
                {
                    UnknownBuffIds = reading.UnknownBuffIds.Concat(reading.Observations.Select(item => item.BuffId))
                        .Distinct(StringComparer.Ordinal).ToArray(),
                };
            if (at - previous >= MaximumGap) Reset();
        }
        _lastCapturedAt = at;

        var unknown = new HashSet<string>(reading.UnknownBuffIds, StringComparer.Ordinal);
        var groups = reading.Observations.GroupBy(item => item.BuffId, StringComparer.Ordinal).ToArray();
        foreach (var group in groups.Where(group => group.Count() > 1)) unknown.Add(group.Key);
        var observations = groups.Where(group => !unknown.Contains(group.Key)).Select(group => group.Single()).ToArray();
        var observedIds = observations.Select(item => item.BuffId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _states.Keys.Where(id => !observedIds.Contains(id)).ToArray()) _states.Remove(id);

        var accepted = new List<BuffObservation>(observations.Length);
        foreach (var observation in observations)
        {
            var current = new Sample(observation, at);
            if (!_states.TryGetValue(observation.BuffId, out var state))
            {
                if (_states.Count < MaximumTrackedBuffs) _states.Add(observation.BuffId, new(current));
                accepted.Add(observation);
                continue;
            }

            if (!DropsTooFast(state.Accepted, current) ||
                state.PendingDrop is { } pending && at > pending.CapturedAt &&
                CountdownIntervalsOverlap(pending, current))
            {
                state.Accepted = current;
                state.PendingDrop = null;
                accepted.Add(observation);
            }
            else
            {
                // Preserve the accepted timer only as private comparison evidence.
                // Accounting receives an unknown value, never a fabricated countdown.
                state.PendingDrop = current;
                unknown.Add(observation.BuffId);
            }
        }
        return new(accepted.ToArray()) { UnknownBuffIds = unknown.ToArray() };
    }

    internal void Reset()
    {
        _states.Clear();
        _lastCapturedAt = null;
    }

    private static bool DropsTooFast(Sample previous, Sample current) =>
        UpperBound(current.Observation) + TimerJitterSeconds < ProjectedLowerBound(previous, current.CapturedAt);

    private static bool CountdownIntervalsOverlap(Sample previous, Sample current) =>
        UpperBound(current.Observation) + TimerJitterSeconds >= ProjectedLowerBound(previous, current.CapturedAt) &&
        current.Observation.Remaining.TotalSeconds <= Math.Max(0,
            UpperBound(previous.Observation) - (current.CapturedAt - previous.CapturedAt).TotalSeconds) + TimerJitterSeconds;

    private static double ProjectedLowerBound(Sample sample, DateTimeOffset at) =>
        Math.Max(0, sample.Observation.Remaining.TotalSeconds - (at - sample.CapturedAt).TotalSeconds);

    // HUD timers are floored buckets: "2h" can legitimately become "119m"
    // in the next frame even though their displayed values differ by a minute.
    private static double UpperBound(BuffObservation observation) =>
        observation.Remaining.TotalSeconds + Math.Max(0, observation.TimerPrecision.TotalSeconds);
}
