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

/// <param name="Drops">Every drop of this item the icon stands for, in order.</param>
public sealed record OverlayTimelineMarkerItem(OverlayLootItem Item, IReadOnlyList<OverlayDropMarker> Drops);

/// <param name="X">The group's first drop as a share of the plot width; its stem stands there and its row of icons
/// starts there.</param>
/// <param name="Height">The tallest layer of every moment the row covers, as a share of the plot height; the icons sit
/// above it.</param>
/// <param name="Items">Different items side by side, each item once with all of its drops.</param>
public sealed record OverlayTimelineMarker(double X, double Height, IReadOnlyList<OverlayTimelineMarkerItem> Items);

public sealed record OverlayTimelineTick(double X, string Label);

/// <param name="Left">Left edge as a share of the plot width, 0..1.</param>
/// <param name="Height">Share of the plot height the value reaches, 0..1.</param>
public sealed record OverlayTimelineBar(double Left, double Right, double Height);

/// <param name="X">Position as a share of the plot width, 0..1.</param>
public sealed record OverlayTimelinePoint(double X, double Height);

/// <param name="Pauses">Every pause at this moment of active time; they share one gap.</param>
public sealed record OverlayTimelineGap(double Left, double Right, IReadOnlyList<SessionPause> Pauses);

/// <summary>
/// The session timeline as an overlay module, shared by the editor preview and the native overlay. Both renderers
/// draw it from these shares of the plot, so they agree on every position.
/// </summary>
public sealed record OverlaySessionTimeline(TimeSpan From, TimeSpan To, string Caption,
    IReadOnlyList<OverlayTimelineTick> Ticks, SessionTimelineSeries? Silver, SessionTimelineSeries? Trash,
    IReadOnlyList<OverlayTimelineRotation> Rotations, IReadOnlyList<double> SpecialEvents,
    IReadOnlyList<OverlayTimelineMarker> Markers, bool ShowsRotations, bool IsSimplified)
{
    /// <summary>Trash bars on the axis, cut where they hold a pause.</summary>
    public IReadOnlyList<OverlayTimelineBar> TrashBars { get; init; } = [];

    /// <summary>The silver curve, one stretch between two pauses per list, so no line crosses a gap.</summary>
    public IReadOnlyList<IReadOnlyList<OverlayTimelinePoint>> SilverCurves { get; init; } = [];

    /// <summary>The pieces of the baseline between the gaps.</summary>
    public IReadOnlyList<OverlayTimelineBar> Baseline { get; init; } = [];

    /// <summary>A gap of one fixed width for every pause, however long it lasted.</summary>
    public IReadOnlyList<OverlayTimelineGap> Gaps { get; init; } = [];

    // Heights in layout pixels at font scale 1, the same for both renderers.
    public const double HeaderHeight = 20, TickHeight = 12, BandGap = 4, SimpleBandHeight = 14, PhaseBandHeight = 18;
    public const double IconSize = 20, IconGap = 5, DetailHeight = 16;
    // Width of a pause's gap at font scale 1, the same for every pause.
    public const double PauseGap = 10;
    // The size the timeline is laid out for at font scale 1; below it the content is scaled down as a whole.
    public const double ReferenceWidth = 360, ReferenceHeight = 144;
    // Room for an icon above the tallest layer.
    private const double MinimumPlotHeight = 44;
    // The highest bar stops below the top, so an icon above it still fits into the plot.
    private const double PlotRange = .82;

    public bool HasData => Silver is not null || Trash is not null || Rotations.Count > 0 ||
        SpecialEvents.Count > 0 || Markers.Count > 0;

    /// <summary>
    /// The height the timeline needs at a font scale: header, times, rotation band and price note grow with the font,
    /// and the plot keeps room for an icon. Never less than the reference height.
    /// </summary>
    public static double MinimumHeight(OverlayWidget widget, double fontScale, bool hasDetail)
    {
        ArgumentNullException.ThrowIfNull(widget);
        var band = !OverlayTimelineLayers.Normalize(widget.TimelineLayers).Contains("rotations") ? 0
            : (widget.TimelineRotationView == OverlayTimelineLayers.PhasesView ? PhaseBandHeight : SimpleBandHeight) + BandGap;
        var lines = (widget.ShowLabel ? HeaderHeight : 0) + TickHeight + band + (hasDetail ? DetailHeight : 0) + MinimumPlotHeight;
        // 16 is the widget's vertical inset, the same in both renderers.
        return Math.Max(ReferenceHeight, 16 + lines * fontScale);
    }

    /// <summary>Width of a row of drop icons; the badge of a numbered icon reaches into the gap.</summary>
    public static double RowWidth(int items, double iconSize, double gap) => items * iconSize + Math.Max(0, items - 1) * gap;

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

        var width = Math.Max(1, plotWidth);
        var fontScale = Math.Clamp(widget.FontScale, .7, 2);
        var axis = new SessionTimelineAxis(from, to, snapshot.Pauses.Select(pause => pause.At), width, PauseGap * fontScale);
        double X(TimeSpan at) => Math.Clamp(axis.X(at) / width, 0, 1);
        IEnumerable<(double Left, double Right)> Pieces(TimeSpan start, TimeSpan end) => axis.Pieces(start, end)
            .Select(piece => (Math.Clamp(piece.Left / width, 0, 1), Math.Clamp(piece.Right / width, 0, 1)));
        TimeSpan At(double share) => from + TimeSpan.FromSeconds(share * (to - from).TotalSeconds);
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
            var phases = RotationPhases.Cached(snapshot.Rotation.SpotId, span.Events, span.Duration);
            IEnumerable<OverlayTimelinePhase> parts = simplified ? Simplify(span, phases, Pieces) : phases.SelectMany(phase =>
                Pieces(span.Start + TimeSpan.FromSeconds(phase.Start), span.Start + TimeSpan.FromSeconds(phase.End))
                    .Select(piece => new OverlayTimelinePhase(piece.Left, piece.Right, phase.Name, phase.Color,
                        RotationPhases.IsAfk(phase), phase.Special)));
            return new OverlayTimelineRotation(span, Math.Clamp(axis.XAfter(span.Start) / width, 0, 1), X(span.End),
                [.. parts.Where(part => part.Right > part.Left)]);
        }).ToArray();
        var special = !IsOn("special") ? [] : spans.SelectMany(span => span.SpecialEvents)
            .Where(at => at >= from && at <= to).Select(X).ToArray();

        return empty with
        {
            Ticks = TicksFor(from, to, axis), Silver = silver, Trash = trash, Rotations = rotations,
            SpecialEvents = special, Markers = !IsOn("rare") ? [] : MarkersFor(snapshot.DropMarkers, series, from, to, axis, fontScale),
            TrashBars = trash is null ? [] : [.. trash.Bars.Where(bar => bar.Value > 0).SelectMany(bar =>
                Pieces(At(bar.Start), At(bar.End)).Select(piece => new OverlayTimelineBar(piece.Left, piece.Right, bar.Filled)))],
            // A point in the middle of an interval that holds a pause belongs to the stretch its middle lies in.
            SilverCurves = silver is null ? [] : [.. silver.Bars.GroupBy(bar => axis.Segment(At((bar.Start + bar.End) / 2)))
                .Select(stretch => (IReadOnlyList<OverlayTimelinePoint>)[.. stretch.Select(bar =>
                    new OverlayTimelinePoint(X(At((bar.Start + bar.End) / 2)), bar.Filled))])],
            Baseline = [.. Pieces(from, to).Select(piece => new OverlayTimelineBar(piece.Left, piece.Right, 0))],
            Gaps = [.. axis.Gaps.Select((at, index) => new OverlayTimelineGap(axis.GapLeft(index) / width,
                (axis.GapLeft(index) + axis.GapWidth) / width, [.. snapshot.Pauses.Where(pause => pause.At == at)]))],
        };
    }

    /// <summary>
    /// The simplified rotation: its mechanics as one stretch, interrupted only by the AFK phases. A rotation without
    /// recorded mechanics is one stretch from start to end.
    /// </summary>
    private static IEnumerable<OverlayTimelinePhase> Simplify(SessionRotationSpan span, IReadOnlyList<RotationPhase> phases,
        Func<TimeSpan, TimeSpan, IEnumerable<(double Left, double Right)>> pieces)
    {
        var at = 0d;
        foreach (var afk in phases.Where(RotationPhases.IsAfk).OrderBy(phase => phase.Start))
        {
            if (afk.Start > at) foreach (var part in Part(at, afk.Start, "Mechaniken", false)) yield return part;
            foreach (var part in Part(Math.Max(at, afk.Start), afk.End, afk.Name, true)) yield return part;
            at = Math.Max(at, afk.End);
        }
        if (span.Duration > at) foreach (var part in Part(at, span.Duration, "Mechaniken", false)) yield return part;

        // A stretch that holds a pause is cut at its gap.
        IEnumerable<OverlayTimelinePhase> Part(double start, double end, string name, bool isAfk) =>
            pieces(span.Start + TimeSpan.FromSeconds(start), span.Start + TimeSpan.FromSeconds(end))
                .Select(piece => new OverlayTimelinePhase(piece.Left, piece.Right, name, "", isAfk, false));
    }

    /// <summary>
    /// Rare drops and favorites, each above the tallest layer of its moment. Drops whose icons would cover each other
    /// share a row: different items stand side by side, the same item appears once and names how many drops it holds.
    /// </summary>
    private static IReadOnlyList<OverlayTimelineMarker> MarkersFor(IReadOnlyList<OverlayDropMarker> drops,
        IReadOnlyList<SessionTimelineSeries> series, TimeSpan from, TimeSpan to, SessionTimelineAxis axis, double fontScale)
    {
        var visible = drops.Where(drop => drop.Elapsed >= from && drop.Elapsed <= to).OrderBy(drop => drop.Elapsed).ToArray();
        var width = Math.Max(1, axis.Width);
        var (size, gap) = (IconSize * fontScale, IconGap * fontScale);
        double Pixel(OverlayDropMarker drop) => axis.X(drop.Elapsed);
        List<OverlayTimelineMarker> markers = [];
        for (var index = 0; index < visible.Length;)
        {
            List<OverlayDropMarker> group = [visible[index]];
            HashSet<string> names = new(StringComparer.Ordinal) { visible[index].Item.CanonicalName };
            // The row starts at its first drop; a drop joins while its icon would touch the row.
            var left = Pixel(visible[index]) - size / 2;
            while (++index < visible.Length && Pixel(visible[index]) - size / 2 < left + RowWidth(names.Count, size, gap) + gap)
            {
                names.Add(visible[index].Item.CanonicalName);
                group.Add(visible[index]);
            }
            markers.Add(new((left + size / 2) / width, group.Max(drop => SessionTimelineChart.Top(drop.Elapsed, series, from, to)),
                [.. group.GroupBy(drop => drop.Item.CanonicalName, StringComparer.Ordinal)
                    .Select(item => new OverlayTimelineMarkerItem(item.First().Item, [.. item]))]));
        }
        return markers;
    }

    /// <summary>Session times along the top, about one per 80 pixels, written as hours and minutes.</summary>
    private static IReadOnlyList<OverlayTimelineTick> TicksFor(TimeSpan from, TimeSpan to, SessionTimelineAxis axis)
    {
        var window = (to - from).TotalSeconds;
        var most = Math.Max(1, Math.Floor(axis.Width / 80));
        var step = new[] { 60d, 120, 300, 600, 900, 1800, 3600, 7200, 14400 }.FirstOrDefault(value => window / value <= most, 14400);
        List<OverlayTimelineTick> ticks = [];
        for (var second = Math.Ceiling(from.TotalSeconds / step) * step; second <= to.TotalSeconds; second += step)
        {
            var at = TimeSpan.FromSeconds(second);
            ticks.Add(new(Math.Clamp(axis.X(at) / Math.Max(1, axis.Width), 0, 1), $"{(int)at.TotalHours}:{at.Minutes:00}"));
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
