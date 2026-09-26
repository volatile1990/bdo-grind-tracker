namespace BdoGrindTracker.App.UI;

/// <summary>
/// The horizontal axis of the session timeline: active session time, with a gap of one fixed width at every pause.
/// A gap does not grow with the length of its pause; the time between the gaps shares the remaining width. A moment
/// exactly at a pause belongs to the stretch before its gap, the stretch after it starts behind the gap.
/// </summary>
public sealed class SessionTimelineAxis
{
    private readonly TimeSpan _from;
    private readonly double _scale;

    /// <param name="pauses">Active session times of the pauses; those outside the window are left out.</param>
    /// <param name="width">Width of the whole axis in the caller's units.</param>
    /// <param name="gapWidth">Width of one gap in the same units.</param>
    public SessionTimelineAxis(TimeSpan from, TimeSpan to, IEnumerable<TimeSpan> pauses, double width, double gapWidth)
    {
        ArgumentNullException.ThrowIfNull(pauses);
        _from = from;
        Width = Math.Max(0, width);
        Gaps = [.. pauses.Where(at => at >= from && at <= to).Distinct().Order()];
        // Many pauses on a narrow timeline share at most half of it, so the grind itself stays readable.
        GapWidth = Gaps.Count == 0 ? 0 : Math.Clamp(Math.Min(gapWidth, Width / 2 / Gaps.Count), 0, Width);
        var window = (to - from).TotalSeconds;
        _scale = window <= 0 ? 0 : (Width - GapWidth * Gaps.Count) / window;
    }

    public double Width { get; }
    public double GapWidth { get; }

    /// <summary>The distinct pause positions inside the window, in order; gap i belongs to Gaps[i].</summary>
    public IReadOnlyList<TimeSpan> Gaps { get; }

    /// <summary>Position of a moment; one exactly at a pause lies before its gap.</summary>
    public double X(TimeSpan at) => Linear(at) + GapWidth * Count(at, inclusive: false);

    /// <summary>Position of a moment that starts something; one exactly at a pause lies behind its gap.</summary>
    public double XAfter(TimeSpan at) => Linear(at) + GapWidth * Count(at, inclusive: true);

    /// <summary>Left edge of gap i; the gap reaches <see cref="GapWidth"/> to the right.</summary>
    public double GapLeft(int index) => X(Gaps[index]);

    /// <summary>
    /// The visible parts of a stretch of time: pieces between the gaps it crosses. A stretch that starts at a pause
    /// starts behind its gap; one that ends at a pause ends before it.
    /// </summary>
    public IReadOnlyList<(double Left, double Right)> Pieces(TimeSpan start, TimeSpan end)
    {
        if (end < start) return [];
        List<(double Left, double Right)> pieces = [];
        var left = XAfter(start);
        foreach (var gap in Gaps)
        {
            if (gap <= start || gap >= end) continue;
            pieces.Add((left, X(gap)));
            left = X(gap) + GapWidth;
        }
        pieces.Add((left, Math.Max(left, X(end))));
        return pieces;
    }

    /// <summary>The stretch of the axis between two gaps a moment belongs to; 0 before the first gap.</summary>
    public int Segment(TimeSpan at) => Count(at, inclusive: false);

    private double Linear(TimeSpan at) => (at - _from).TotalSeconds * _scale;

    private int Count(TimeSpan at, bool inclusive)
    {
        var count = 0;
        foreach (var gap in Gaps)
        {
            if (inclusive ? gap > at : gap >= at) break;
            count++;
        }
        return count;
    }
}
