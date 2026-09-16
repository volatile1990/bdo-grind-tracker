namespace BdoGrindTracker.App.Overlay;

public sealed record OverlaySectionPoint(TimeSpan Elapsed, decimal Silver);

/// <summary>The rare drops of one section, shown side by side and centered on that section's peak.</summary>
public sealed record OverlaySectionMarker(IReadOnlyList<OverlayDropMarker> Drops, double X, double Y);

/// <summary>Silver per section as one widget's curve, shared by the editor preview and the native overlay.</summary>
public sealed record OverlaySectionChart(string Title, string? Detail, string Description,
    IReadOnlyList<OverlaySectionPoint> Points, TimeSpan From, TimeSpan To, decimal Maximum,
    IReadOnlyList<OverlaySectionMarker> Markers, string PeakMode = OverlayChartSections.DefaultPeakMode)
{
    // On the logarithmic scale a hundredth of the highest section still reaches half the height.
    private const double LogarithmicRange = 10_000;

    public bool HasData => Points.Count >= 2;

    /// <summary>The section rises beyond the scale set by the sections without valuable drops.</summary>
    public bool IsClipped(OverlaySectionPoint point) => PeakMode == OverlayChartSections.ClipPeaks && point.Silver > Maximum;

    /// <summary>Horizontal position as a fraction of the plot width.</summary>
    public double X(TimeSpan elapsed)
    {
        var span = (To - From).Ticks;
        return span <= 0 ? 0 : Math.Clamp((double)(elapsed - From).Ticks / span, 0, 1);
    }

    /// <summary>Vertical position as a fraction of the plot height, with the average curve's padding.</summary>
    public double Y(decimal silver)
    {
        var share = (double)(Math.Max(0m, silver) / Math.Max(1m, Maximum));
        var height = PeakMode == OverlayChartSections.LogarithmicPeaks
            ? Math.Log(1 + share * LogarithmicRange) / Math.Log(1 + LogarithmicRange) : share;
        return (70 - Math.Clamp(height, 0, 1) * 64) / 72;
    }
}

public static class OverlayChartSections
{
    public const string AverageMode = "average";
    public const string SectionsMode = "sections";
    public const int DefaultSectionSeconds = 10;
    // How valuable drops (the chart markers) shape the curve.
    public const string ClipPeaks = "clip";
    public const string ExcludePeaks = "exclude";
    public const string LogarithmicPeaks = "log";
    public const string DefaultPeakMode = LogarithmicPeaks;
    public static IReadOnlyList<string> PeakModes { get; } = Array.AsReadOnly(new[] { ClipPeaks, ExcludePeaks, LogarithmicPeaks });
    public static IReadOnlyList<int> SectionSeconds { get; } = Array.AsReadOnly(new[] { 5, 10, 30 });
    // Zero is the complete session.
    public static IReadOnlyList<int> RangeMinutes { get; } = Array.AsReadOnly(new[] { 0, 10, 20, 30, 40, 50, 60 });
    // Above the pixel width of any overlay module; SVG and GDI+ stay responsive.
    private const int MaximumPoints = 800;
    // A silver history whose first sample is this early began with the session.
    private static readonly TimeSpan OriginTolerance = TimeSpan.FromSeconds(30);

    public static OverlaySectionChart Create(OverlayWidget widget, OverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(snapshot);
        var seconds = SectionSeconds.Contains(widget.ChartSectionSeconds) ? widget.ChartSectionSeconds : DefaultSectionSeconds;
        var range = RangeMinutes.Contains(widget.ChartRangeMinutes) ? widget.ChartRangeMinutes : 0;
        var peakMode = PeakModes.Contains(widget.ChartPeakMode) ? widget.ChartPeakMode : DefaultPeakMode;
        var section = TimeSpan.FromSeconds(seconds);
        var metric = snapshot.Metrics.GetValueOrDefault("chart");
        var rangeText = range == 0 ? "ganze Session" : $"letzte {range} min";
        // Missing prices replace the explanation, exactly as in the average curve.
        var detail = metric is null ? null :
            metric.Detail is { } warning && warning != "Session-Durchschnitt" ? warning : $"Zahl Ø Session/h · {rangeText}";
        var peakText = peakMode switch
        {
            ExcludePeaks => "ohne wertvolle Drops",
            LogarithmicPeaks => "logarithmische Höhe",
            _ => "Abschnitte mit wertvollen Drops oben gekappt",
        };
        var empty = new OverlaySectionChart($"Silber je {seconds} s · Verlauf", detail,
            $"Kurve des netto verdienten Silbers je {seconds} Sekunden aktiver Grindzeit, {rangeText}, {peakText}",
            [], default, default, 1, [], peakMode);

        var now = snapshot.SessionElapsed;
        // After an app restart the loot increases before the restored point in time are unknown.
        var origin = snapshot.SilverHistory.Count > 0 && snapshot.SilverHistory[0].Elapsed > OriginTolerance
            ? snapshot.SilverHistory[0].Elapsed : TimeSpan.Zero;
        var start = range > 0 && now - TimeSpan.FromMinutes(range) > origin ? now - TimeSpan.FromMinutes(range) : origin;
        var firstSection = origin > TimeSpan.Zero && start == origin
            ? (start.Ticks + section.Ticks - 1) / section.Ticks : start.Ticks / section.Ticks;
        var lastSection = (now.Ticks - 1) / section.Ticks;
        if (now <= TimeSpan.Zero || lastSection <= firstSection) return empty;

        var values = new decimal[lastSection - firstSection + 1];
        var valuable = new bool[values.Length];
        foreach (var drop in snapshot.SilverDrops)
        {
            if (drop.Elapsed > now) continue;
            var index = drop.Elapsed.Ticks / section.Ticks - firstSection;
            if (index < 0 || index >= values.Length) continue;
            valuable[index] |= drop.Valuable;
            if (!drop.Valuable || peakMode != ExcludePeaks) values[index] += drop.Silver;
        }
        var from = TimeSpan.FromTicks(firstSection * section.Ticks);
        var points = new OverlaySectionPoint[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var sectionStart = from + section * index;
            var sectionEnd = sectionStart + section < now ? sectionStart + section : now;
            // Each point stands at the middle of its section; the running section ends now.
            points[index] = new(sectionStart + (sectionEnd - sectionStart) / 2, Math.Max(0, values[index]));
        }
        var chart = empty with
        {
            Points = Downsample(points, from, now), From = from, To = now,
            Maximum = Math.Max(1m, Scale(values, valuable, peakMode)),
        };
        return chart with
        {
            // A marker belongs to the peak of its section, not to the drop's exact moment inside it.
            Markers = snapshot.DropMarkers.Where(drop => drop.Elapsed >= from && drop.Elapsed <= now)
                .GroupBy(drop => (int)Math.Clamp(drop.Elapsed.Ticks / section.Ticks - firstSection, 0, values.Length - 1))
                .OrderBy(group => group.Key)
                .Select(group => new OverlaySectionMarker(group.OrderBy(drop => drop.Elapsed).ToArray(),
                    chart.X(points[group.Key].Elapsed), chart.Y(values[group.Key])))
                .ToArray(),
        };
    }

    /// <summary>
    /// Clipping scales to the sections without valuable drops, so regular loot keeps the full height.
    /// Without such income, the highest section sets the scale as in the other modes.
    /// </summary>
    private static decimal Scale(decimal[] values, bool[] valuable, string peakMode)
    {
        var highest = values.Max();
        if (peakMode != ClipPeaks) return highest;
        var regular = values.Where((_, index) => !valuable[index]).DefaultIfEmpty(0).Max();
        return regular > 0 ? regular : highest;
    }

    /// <summary>
    /// Limits long curves to the plot resolution. Each time slice keeps its lowest and highest
    /// value in order, so a single valuable drop is never averaged away.
    /// </summary>
    private static IReadOnlyList<OverlaySectionPoint> Downsample(IReadOnlyList<OverlaySectionPoint> points, TimeSpan from, TimeSpan to)
    {
        if (points.Count <= MaximumPoints) return points;
        var slices = MaximumPoints / 2;
        var span = Math.Max(1, (to - from).Ticks);
        var result = new List<OverlaySectionPoint>(MaximumPoints + 2) { points[0] };
        var index = 1;
        for (var slice = 1; slice <= slices && index < points.Count - 1; slice++)
        {
            var end = from.Ticks + span * slice / slices;
            OverlaySectionPoint? low = null, high = null;
            for (; index < points.Count - 1 && (points[index].Elapsed.Ticks <= end || slice == slices); index++)
            {
                if (low is null || points[index].Silver < low.Silver) low = points[index];
                if (high is null || points[index].Silver > high.Silver) high = points[index];
            }
            if (low is null || high is null) continue;
            if (ReferenceEquals(low, high)) result.Add(low);
            else if (low.Elapsed <= high.Elapsed) result.AddRange([low, high]);
            else result.AddRange([high, low]);
        }
        result.Add(points[^1]);
        return result;
    }
}
