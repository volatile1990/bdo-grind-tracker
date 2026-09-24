namespace BdoGrindTracker.App.Overlay;

/// <summary>
/// The current rotation as the Rotation Monitor's lower row draws it, on the reference's time axis. A rotation that
/// began with its start is drawn as measured. One picked up mid-way cannot know where it stands until a message fits
/// exactly one phase (Magaia's Elion's Tears, or the step a timeout left): that message is laid onto the same moment of
/// the reference, the rotation after it follows from there, and everything before it is a tracking error.
/// </summary>
/// <param name="Offset">Seconds the observed rotation moves to meet the reference.</param>
/// <param name="End">The rotation's current moment on the reference's axis: its total time and the playhead.</param>
/// <param name="ErrorEnd">The tracking error spans from the row's start to here; 0 without one.</param>
/// <param name="Phases">Only the phases that are certain.</param>
/// <param name="Events">Every observed event, moved along; markers stay where they were seen.</param>
/// <param name="Gaps">Stretches after that moment whose required phases were never seen, moved along as well.</param>
public sealed record RotationCurrentRow(double Offset, double End, double ErrorEnd,
    IReadOnlyList<RotationPhase> Phases, IReadOnlyList<RotationEvent> Events, IReadOnlyList<RotationSection> Gaps)
{
    public const string TrackingErrorColor = "#E87C79";
    public const string TrackingErrorTitle = "Tracking-Fehler · der Beginn dieser Rotation wurde nicht sicher erfasst";
    public const string GapTitle = "Tracking-Fehler · erwartete Phase nicht erkannt";

    public bool HasTrackingError => ErrorEnd > 0;

    public static RotationCurrentRow Create(RotationMonitorSnapshot state, RotationRun? reference, string? colors = "colored")
    {
        if (!IsPickedUp(state))
            return new(0, state.Elapsed, 0, RotationPhases.Create(state.SpotId, state.Events, state.Elapsed, colors), state.Events,
                state.MissingSections);
        if (state.AlignedAt is not { } aligned) return new(0, state.Elapsed, state.Elapsed, [], state.Events, []);
        var offset = OffsetOf(state, reference);
        var anchor = aligned + offset;
        var end = state.Elapsed + offset;
        RotationEvent[] events = [.. state.Events.Select(e => e with { Seconds = e.Seconds + offset })];
        // The reference up to that moment counts the cycles and wormholes the observed part continues; none of it is drawn.
        RotationEvent[] counted = [.. (reference?.Events ?? []).Where(e => e.Seconds < anchor),
            .. events.Where(e => e.Seconds >= anchor && e.Kind != "start")];
        var phases = RotationPhases.Create(state.SpotId, counted, end, colors)
            .Where(phase => phase.End > anchor)
            .Select(phase => phase.Start < anchor ? phase with { Start = anchor } : phase).ToArray();
        RotationSection[] gaps = [.. state.MissingSections.Where(gap => gap.End > aligned)
            .Select(gap => gap with { Start = Math.Max(gap.Start, aligned) + offset, End = gap.End + offset })];
        return new(offset, end, Math.Max(0, anchor), phases, events, gaps);
    }

    /// <summary>Seconds the current rotation moves to meet the reference; 0 while nothing is certain.</summary>
    public static double OffsetOf(RotationMonitorSnapshot state, RotationRun? reference) =>
        IsPickedUp(state) && state.AlignedAt is { } aligned &&
        reference?.Sections.FirstOrDefault(section => section.Id == state.AlignedSection) is { } section
            ? section.Start - aligned : 0;

    // A rotation that began with its start is certain from its first second and names no section to align on.
    private static bool IsPickedUp(RotationMonitorSnapshot state) =>
        state.TrackingState == "partial" && (state.AlignedAt is null || state.AlignedSection is not null);
}
