namespace BdoGrindTracker.App.UI;

/// <param name="At">Active session time at which the pause began; the session timeline places its gap there.</param>
/// <param name="StartedAt">When the pause began. An automatic pause began when its idle time did, not when it was noticed.</param>
/// <param name="EndedAt">When tracking resumed; null while the session is still paused.</param>
/// <param name="Kind">Why tracking stopped: <see cref="Manual"/>, <see cref="Automatic"/> or <see cref="Closed"/>.</param>
public sealed record SessionPause(TimeSpan At, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Kind)
{
    public const string Manual = "manual";
    public const string Automatic = "automatic";
    /// <summary>Grindcrest was closed, or ended unexpectedly, while tracking.</summary>
    public const string Closed = "closed";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOpen => EndedAt is null;

    /// <summary>How long the pause lasted, or has lasted so far while it is still open.</summary>
    public TimeSpan Duration(DateTimeOffset now)
    {
        var length = (EndedAt ?? now) - StartedAt;
        return length > TimeSpan.Zero ? length : TimeSpan.Zero;
    }
}

/// <summary>The pauses of one session, in the order they happened on its active-time axis.</summary>
public static class SessionPauses
{
    /// <summary>
    /// Pauses read back from a file: known kinds only, on the saved session's axis, in order, and only the last one
    /// still open. Anything else is dropped rather than drawn in a wrong place.
    /// </summary>
    public static IReadOnlyList<SessionPause> Normalize(IReadOnlyList<SessionPause>? pauses, TimeSpan duration)
    {
        if (pauses is not { Count: > 0 }) return [];
        var valid = pauses.Where(pause => pause is not null && pause.Kind is SessionPause.Manual or SessionPause.Automatic or SessionPause.Closed &&
                pause.At >= TimeSpan.Zero && pause.At <= duration && (pause.EndedAt is not { } end || end >= pause.StartedAt))
            .OrderBy(pause => pause.At).ThenBy(pause => pause.StartedAt).ToList();
        for (var index = 0; index < valid.Count - 1; index++)
            if (valid[index].IsOpen) valid[index] = valid[index] with { EndedAt = valid[index + 1].StartedAt };
        return valid.AsReadOnly();
    }
}
