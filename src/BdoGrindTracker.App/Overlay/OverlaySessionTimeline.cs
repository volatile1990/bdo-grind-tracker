using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Overlay;

/// <param name="Left">Left edge as a share of the plot width, 0..1.</param>
/// <param name="Right">Right edge as a share of the plot width, 0..1.</param>
/// <param name="Color">The phase's own colour; empty in the simplified view, which colours by the rotation's state.</param>
public sealed record OverlayTimelinePhase(double Left, double Right, string Name, string Color, bool IsAfk, bool IsSpecial);

/// <param name="Phases">Every phase, or in the simplified view the mechanics as one stretch between the AFK phases.</param>
public sealed record OverlayTimelineRotation(SessionRotationSpan Span, double Left, double Right,
    IReadOnlyList<OverlayTimelinePhase> Phases)
{
    /// <summary>The rotation's state, named after the session timeline's colour tokens.</summary>
    public string Status => Span.IsActive ? "active" : Span.IsFailed ? "failed" :
        Span.IsFastest ? "fastest" : Span.IsComplete ? "complete" : "partial";
}

/// <param name="Height">The tallest layer at this moment as a share of the plot height; the icons sit above it.</param>
public sealed record OverlayTimelineMarker(double X, double Height, IReadOnlyList<OverlayDropMarker> Drops);

public sealed record OverlayTimelineTick(double X, string Label);

/// <summary>
/// The session timeline as an overlay module, shared by the editor preview and the native overlay. Both renderers
/// draw it from these shares of the plot, so they agree on every position.
/// </summary>
public sealed record OverlaySessionTimeline(TimeSpan From, TimeSpan To, string Caption,
    IReadOnlyList<OverlayTimelineTick> Ticks, SessionTimelineSeries? Silver, SessionTimelineSeries? Trash,
    IReadOnlyList<OverlayTimelineRotation> Rotations, IReadOnlyList<double> SpecialEvents,
    IReadOnlyList<OverlayTimelineMarker> Markers, bool ShowsRotations, bool IsSimplified)
{
    // Heights in layout pixels at font scale 1, the same for both renderers.
    public const double HeaderHeight = 20, TickHeight = 12, BandGap = 4, SimpleBandHeight = 14, PhaseBandHeight = 18;
    public const double IconSize = 20, DetailHeight = 16;
    // The highest bar stops below the top, so an icon above it still fits into the plot.
    private const double PlotRange = .82;

    public bool HasData => Silver is not null || Trash is not null || Rotations.Count > 0 ||
        SpecialEvents.Count > 0 || Markers.Count > 0;

    /// <summary>Height of the rotation band; the band keeps its place while the layer is on, so the plot never jumps.</summary>
    public double BandHeight => !ShowsRotations ? 0 : (IsSimplified ? SimpleBandHeight : PhaseBandHeight) + BandGap;

    /// <summary>Vertical position of a layer's height as a share of the plot, from 0 at the top to 1 on the baseline.</summary>
    public static double Y(double height) => 1 - Math.Clamp(height, 0, 1) * PlotRange;

    public static OverlaySessionTimeline Create(OverlayWidget widget, OverlaySnapshot snapshot, double plotWidth)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(snapshot);
        var layers = OverlayTimelineLayers.Normalize(widget.TimelineLayers);
        bool IsOn(string id) => layers.Contains(id, StringComparer.Ordinal);
        var range = OverlayTimelineLayers.RangeMinutes.Contains(widget.ChartRangeMinutes) ? widget.ChartRangeMinutes : 0;
        var simplified = widget.TimelineRotationView != OverlayTimelineLayers.PhasesView;
        var caption = range == 0
            ? AppText.Translate("ganze Session", snapshot.UiLanguage)
            : AppText.Format("letzte {0} min", snapshot.UiLanguage, range);
        var to = snapshot.SessionElapsed;
        var from = range > 0 && to > TimeSpan.FromMinutes(range) ? to - TimeSpan.FromMinutes(range) : TimeSpan.Zero;
        var empty = new OverlaySessionTimeline(from, to, caption, [], null, null, [], [], [], IsOn("rotations"), simplified);
        if (to <= TimeSpan.Zero) return empty;

        double X(TimeSpan at) => Math.Clamp((at - from).TotalSeconds / (to - from).TotalSeconds, 0, 1);
        var silver = !IsOn("silver") ? null : SessionTimelineChart.Series("silver", "Silber je Abschnitt",
            SessionTimelineLayers.ColorOf("silver"), snapshot.SilverDrops, drop => drop.Elapsed, drop => drop.Silver,
            from, to, isSilver: true, flatten: true);
        var trash = !IsOn("trash") ? null : SessionTimelineChart.Series("trash", "Trashloot",
            SessionTimelineLayers.ColorOf("trash"), snapshot.TrashDrops, from, to);
        if (silver is { Peak: <= 0 }) silver = null;
        if (trash is { Peak: <= 0 }) trash = null;
        SessionTimelineSeries[] series = [.. new[] { trash, silver }.OfType<SessionTimelineSeries>()];

        var spans = IsOn("rotations") || IsOn("special")
            ? SessionRotationStats.Spans(snapshot.Rotation, to, snapshot.ObservedAt) : [];
        var rotations = !IsOn("rotations") ? [] : spans.Where(span => span.End > from && span.Start < to).Select(span =>
        {
            var phases = RotationPhases.Create(snapshot.Rotation.SpotId, span.Events ?? [], span.Duration);
            IEnumerable<OverlayTimelinePhase> parts = simplified ? Simplify(span, phases, X) : phases.Select(phase =>
                new OverlayTimelinePhase(X(span.Start + TimeSpan.FromSeconds(phase.Start)),
                    X(span.Start + TimeSpan.FromSeconds(phase.End)), phase.Name, phase.Color,
                    RotationPhases.IsAfk(phase), phase.Special));
            return new OverlayTimelineRotation(span, X(span.Start), X(span.End), [.. parts.Where(part => part.Right > part.Left)]);
        }).ToArray();
        var special = !IsOn("special") ? [] : spans.SelectMany(span => span.SpecialEvents)
            .Where(at => at >= from && at <= to).Select(X).ToArray();

        return empty with
        {
            Ticks = TicksFor(from, to, plotWidth), Silver = silver, Trash = trash, Rotations = rotations,
            SpecialEvents = special, Markers = !IsOn("rare") ? [] : MarkersFor(snapshot.DropMarkers, series, from, to, plotWidth,
                IconSize * Math.Clamp(widget.FontScale, .7, 2)),
        };
    }

    /// <summary>
    /// The simplified rotation: its mechanics as one stretch, interrupted only by the AFK phases. A rotation without
    /// recorded mechanics is one stretch from start to end.
    /// </summary>
    private static IEnumerable<OverlayTimelinePhase> Simplify(SessionRotationSpan span, IReadOnlyList<RotationPhase> phases,
        Func<TimeSpan, double> x)
    {
        var at = 0d;
        foreach (var afk in phases.Where(RotationPhases.IsAfk).OrderBy(phase => phase.Start))
        {
            if (afk.Start > at) yield return Part(at, afk.Start, "Mechaniken", false);
            yield return Part(Math.Max(at, afk.Start), afk.End, afk.Name, true);
            at = Math.Max(at, afk.End);
        }
        if (span.Duration > at) yield return Part(at, span.Duration, "Mechaniken", false);

        OverlayTimelinePhase Part(double start, double end, string name, bool isAfk) => new(
            x(span.Start + TimeSpan.FromSeconds(start)), x(span.Start + TimeSpan.FromSeconds(end)), name, "", isAfk, false);
    }

    /// <summary>
    /// Rare drops and favorites, each above the tallest layer of its moment. Drops whose icons would cover each other
    /// share one mark, which then names how many it holds.
    /// </summary>
    private static IReadOnlyList<OverlayTimelineMarker> MarkersFor(IReadOnlyList<OverlayDropMarker> drops,
        IReadOnlyList<SessionTimelineSeries> series, TimeSpan from, TimeSpan to, double plotWidth, double iconSize)
    {
        var window = (to - from).TotalSeconds;
        var visible = drops.Where(drop => drop.Elapsed >= from && drop.Elapsed <= to).OrderBy(drop => drop.Elapsed).ToArray();
        var width = Math.Max(1, plotWidth);
        List<OverlayTimelineMarker> markers = [];
        for (var index = 0; index < visible.Length;)
        {
            List<OverlayDropMarker> group = [visible[index]];
            var left = (visible[index].Elapsed - from).TotalSeconds / window * width;
            while (++index < visible.Length && (visible[index].Elapsed - from).TotalSeconds / window * width - left < iconSize)
                group.Add(visible[index]);
            markers.Add(new(left / width, SessionTimelineChart.Top(group[0].Elapsed, series, from, to), group));
        }
        return markers;
    }

    /// <summary>Session times along the top, about one per 80 pixels, written as hours and minutes.</summary>
    private static IReadOnlyList<OverlayTimelineTick> TicksFor(TimeSpan from, TimeSpan to, double plotWidth)
    {
        var window = (to - from).TotalSeconds;
        var most = Math.Max(1, Math.Floor(plotWidth / 80));
        var step = new[] { 60d, 120, 300, 600, 900, 1800, 3600, 7200, 14400 }.FirstOrDefault(value => window / value <= most, 14400);
        List<OverlayTimelineTick> ticks = [];
        for (var second = Math.Ceiling(from.TotalSeconds / step) * step; second <= to.TotalSeconds; second += step)
        {
            var at = TimeSpan.FromSeconds(second);
            ticks.Add(new((second - from.TotalSeconds) / window, $"{(int)at.TotalHours}:{at.Minutes:00}"));
        }
        return ticks;
    }
}

/// <summary>What the overlay's session timeline can show; the layer ids are the session timeline's own.</summary>
public static class OverlayTimelineLayers
{
    public const string SimplifiedView = "simple";
    public const string PhasesView = "phases";
    public static IReadOnlyList<string> Views { get; } = Array.AsReadOnly(new[] { SimplifiedView, PhasesView });

    /// <summary>Everything except the trash bars, which would crowd a module of overlay size.</summary>
    public static IReadOnlyList<string> Default { get; } = Array.AsReadOnly(new[] { "rotations", "rare", "silver", "special" });

    // Zero is the complete session.
    public static IReadOnlyList<int> RangeMinutes { get; } = Array.AsReadOnly(new[] { 0, 10, 20, 30, 40, 50, 60 });

    /// <summary>Known layers in their fixed order; an unchanged selection keeps its instance, so a saved layout compares equal.</summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? layers)
    {
        if (layers is null) return Default;
        var known = SessionTimelineLayers.All.Select(layer => layer.Id).Where(layers.Contains).ToArray();
        if (known.SequenceEqual(Default)) return Default;
        return layers.SequenceEqual(known) && layers is System.Collections.ObjectModel.ReadOnlyCollection<string>
            ? layers : Array.AsReadOnly(known);
    }
}
