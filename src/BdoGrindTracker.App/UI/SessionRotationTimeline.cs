using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Places completed rotations on the session's active-time axis, the one the drop history uses. Rotation starts are
/// absolute times, and the session clock stops during pauses, so the difference to the wall clock grows with every
/// break. Each rotation's session time is therefore recorded once, when it first appears, and kept from then on.
/// </summary>
internal sealed class SessionRotationTimeline
{
    private readonly Dictionary<Guid, TimeSpan> _starts = [];
    private Guid _sessionId;

    internal RotationMonitorSnapshot Update(Guid sessionId, TimeSpan elapsed, DateTimeOffset observedAt,
        RotationMonitorSnapshot snapshot)
    {
        if (_sessionId != sessionId) { _starts.Clear(); _sessionId = sessionId; }
        if (snapshot.SessionRotations.Count == 0) return snapshot;
        return snapshot with
        {
            SessionRotations = [.. snapshot.SessionRotations.Select(timing =>
                timing with { StartedAfter = StartOf(timing, elapsed, observedAt) })],
        };
    }

    private TimeSpan? StartOf(SessionRotationTiming timing, TimeSpan elapsed, DateTimeOffset observedAt)
    {
        if (timing.RunId != Guid.Empty && _starts.TryGetValue(timing.RunId, out var known)) return known;
        if (timing.StartedAt == default || observedAt == default) return timing.StartedAfter;
        // A rotation may well begin before the session's first active second: the automatic start measures from a
        // banner seen before the clock ran. Its true place is kept, negative and all, and the timeline clips it.
        var start = elapsed - (observedAt - timing.StartedAt);
        if (timing.RunId != Guid.Empty) _starts[timing.RunId] = start;
        return start;
    }

    internal void Reset()
    {
        _starts.Clear();
        _sessionId = Guid.Empty;
    }
}
