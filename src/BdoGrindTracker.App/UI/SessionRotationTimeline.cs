using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Places rotations on the session's active-time axis, the one the drop history uses. Rotation starts are absolute
/// times, so they are measured from the session's own beginning. That anchor survives a restart, while the distance
/// to the current capture time would carry the whole time the tracker was closed. Each rotation's place is recorded
/// once, when it first appears, and kept from then on.
/// </summary>
internal sealed class SessionRotationTimeline
{
    private readonly Dictionary<Guid, TimeSpan> _starts = [];
    private Guid _sessionId;

    /// <param name="startedAt">When the session began; the anchor every rotation is measured from.</param>
    internal RotationMonitorSnapshot Update(Guid sessionId, TimeSpan elapsed, DateTimeOffset observedAt,
        DateTimeOffset? startedAt, RotationMonitorSnapshot snapshot)
    {
        if (_sessionId != sessionId) { _starts.Clear(); _sessionId = sessionId; }
        if (snapshot.SessionRotations.Count == 0) return snapshot;
        return snapshot with
        {
            SessionRotations = [.. snapshot.SessionRotations.Select(timing =>
                timing with { StartedAfter = StartOf(timing, elapsed, observedAt, startedAt) })],
        };
    }

    private TimeSpan? StartOf(SessionRotationTiming timing, TimeSpan elapsed, DateTimeOffset observedAt,
        DateTimeOffset? startedAt)
    {
        if (timing.RunId != Guid.Empty && _starts.TryGetValue(timing.RunId, out var known)) return known;
        if (timing.StartedAt == default) return timing.StartedAfter;
        // A rotation may well begin before the session's first active second: the automatic start measures from a
        // banner seen before the clock ran. Its true place is kept, negative and all, and the timeline clips it.
        var start = startedAt is { } session ? timing.StartedAt - session
            : observedAt == default ? (TimeSpan?)null : elapsed - (observedAt - timing.StartedAt);
        if (start is not { } placed) return timing.StartedAfter;
        if (timing.RunId != Guid.Empty) _starts[timing.RunId] = placed;
        return placed;
    }

    internal void Reset()
    {
        _starts.Clear();
        _sessionId = Guid.Empty;
    }
}
