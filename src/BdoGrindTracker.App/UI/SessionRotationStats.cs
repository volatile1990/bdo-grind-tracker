using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.UI;

/// <param name="Start">Session time of the start on the drop history's axis; negative when it began before it.</param>
/// <param name="SpecialEvents">Session time of each special event of this rotation.</param>
/// <param name="Outcome">complete, incomplete, aborted or active, as the rotation monitor decided it.</param>
/// <param name="Events">The rotation's mechanics, for drawing its phases like the rotation monitor does.</param>
public sealed record SessionRotationSpan(int Number, TimeSpan Start, TimeSpan End, double Duration,
    IReadOnlyList<TimeSpan> SpecialEvents, string Outcome = "complete",
    IReadOnlyList<RotationEvent>? Events = null)
{
    public bool IsFastest { get; init; }
    public bool IsComplete => Outcome == "complete";
    public bool IsFailed => Outcome == "aborted";
    public bool IsActive => Outcome == "active";
}

/// <summary>
/// The session's completed rotations, shared by the live session details and the session timeline. The tempo follows
/// the same rule as the overlay module: the latest rotations including the walk back to the next start.
/// </summary>
public static class SessionRotationStats
{
    // The recent tempo, not the whole session: earlier slow rotations stop affecting the estimate.
    public const int TempoSample = 3;

    public static int Count(RotationMonitorSnapshot rotation) => Timings(rotation).Length;

    public static double? Fastest(RotationMonitorSnapshot rotation) =>
        Timings(rotation) is { Length: > 0 } timings ? timings.Min(timing => timing.Duration) : null;

    public static double? Average(RotationMonitorSnapshot rotation) =>
        Timings(rotation) is { Length: > 0 } timings ? timings.Average(timing => timing.Duration) : null;

    /// <summary>Rotations per hour at the current tempo, including the walk back to the next rotation's start.</summary>
    public static double? PerHour(RotationMonitorSnapshot rotation, bool includeSpecialEvents = true) =>
        Tempo(rotation, includeSpecialEvents) is { } seconds and > 0 ? 3600 / seconds : null;

    /// <summary>Average seconds per rotation of the recent sample, including its walk back.</summary>
    public static double? Tempo(RotationMonitorSnapshot rotation, bool includeSpecialEvents = true)
    {
        var timings = Timings(rotation);
        var recent = timings.Where(timing => includeSpecialEvents || !timing.Special).TakeLast(TempoSample).ToArray();
        if (recent.Length == 0) return null;
        var walks = timings.Where(timing => timing.WalkBack is { } walk && double.IsFinite(walk))
            .Select(timing => timing.WalkBack!.Value).ToArray();
        double? averageWalk = walks.Length > 0 ? walks.Average() : null;
        return recent.Average(timing => timing.Duration + (timing.WalkBack ?? averageWalk ?? 0));
    }

    public static bool HasKnownWalkBack(RotationMonitorSnapshot rotation) =>
        Timings(rotation).Any(timing => timing.WalkBack is { } walk && double.IsFinite(walk));

    public static int RecentCount(RotationMonitorSnapshot rotation, bool includeSpecialEvents = true) =>
        Timings(rotation).Count(timing => includeSpecialEvents || !timing.Special) is var total && total > TempoSample
            ? TempoSample : total;

    public static double? PerHour(int events, TimeSpan elapsed) =>
        elapsed > TimeSpan.Zero ? events / elapsed.TotalHours : null;

    /// <summary>
    /// The rotations on the session's time axis, the one the drop and silver histories use. Rotations recorded on
    /// that axis keep their place; older ones fall back to the wall clock difference to the state's capture time.
    /// </summary>
    public static IReadOnlyList<SessionRotationSpan> Spans(RotationMonitorSnapshot rotation, TimeSpan elapsed, DateTimeOffset observedAt)
    {
        var timings = Attempts(rotation)
            .Where(timing => timing.StartedAfter is not null || timing.StartedAt != default && observedAt != default)
            .Select(timing => (Timing: timing, Start: timing.StartedAfter ?? elapsed - (observedAt - timing.StartedAt)))
            // A rotation that also ends before the axis begins ran during an offline gap the session clock never
            // counted; it has no place here. One that reaches into the session keeps its place and is clipped.
            .Where(entry => entry.Start + TimeSpan.FromSeconds(entry.Timing.Duration) > TimeSpan.Zero).ToArray();
        if (timings.Length == 0) return [];
        var fastest = timings.Where(entry => entry.Timing.IsComplete).Select(entry => entry.Timing.Duration)
            .DefaultIfEmpty(double.NaN).Min();
        var number = 0;
        return timings.Select(entry =>
        {
            var (timing, start) = entry;
            return new SessionRotationSpan(++number, start, start + TimeSpan.FromSeconds(timing.Duration),
                timing.Duration, [.. (timing.SpecialEventSeconds ?? []).Select(seconds => start + TimeSpan.FromSeconds(seconds))],
                timing.Outcome, timing.Events) { IsFastest = timing.IsComplete && timing.Duration <= fastest };
        }).ToArray();
    }

    /// <summary>
    /// The rotations that count as rotations: complete ones with a usable duration. An aborted attempt and the
    /// rotation that is still running say nothing about the tempo and are no achievement either.
    /// </summary>
    public static IReadOnlyList<SessionRotationTiming> Completed(RotationMonitorSnapshot rotation) => Timings(rotation);

    private static SessionRotationTiming[] Timings(RotationMonitorSnapshot rotation) =>
        rotation.SessionRotations.Where(timing => timing.IsComplete && double.IsFinite(timing.Duration) && timing.Duration > 0).ToArray();

    private static SessionRotationTiming[] Attempts(RotationMonitorSnapshot rotation) =>
        rotation.SessionRotations.Where(timing => double.IsFinite(timing.Duration) && timing.Duration > 0).ToArray();
}
