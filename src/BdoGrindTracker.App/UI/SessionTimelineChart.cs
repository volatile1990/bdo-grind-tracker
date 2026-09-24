namespace BdoGrindTracker.App.UI;

/// <param name="At">Session time at which this interval begins.</param>
/// <param name="Start">Left edge as a share of the visible window, 0..1.</param>
/// <param name="Filled">Share of the plot height this interval reaches, 0..1.</param>
/// <param name="Value">What arrived in the interval: a quantity, or its silver value.</param>
public sealed record SessionTimelineBar(TimeSpan At, double Start, double End, double Filled, decimal Value);

/// <param name="Peak">The largest value of a single interval; the series is normalized to it.</param>
/// <param name="IsSilver">The values are silver, not a count, and are written as such.</param>
/// <param name="IsFlattened">The heights are flattened, so small intervals stay visible next to a spike.</param>
public sealed record SessionTimelineSeries(string Id, string Label, string Color,
    IReadOnlyList<SessionTimelineBar> Bars, decimal Peak, bool IsSilver = false, bool IsFlattened = false);

/// <summary>
/// The vertical axis of the session timeline: every layer shows its own tracked value per interval of the visible
/// window, each normalized to its own peak, because the layers share the plot area but not their units.
/// </summary>
public static class SessionTimelineChart
{
    /// <summary>At most this many intervals fill the window; the interval itself is one of the fixed steps below.</summary>
    public const int MaximumIntervals = 96;

    /// <summary>
    /// How hard a flattened series is compressed: the share of the peak raised to this power. A logarithm would lift
    /// a thousandth of the peak to two thirds of the height and leave the whole ordinary range as one flat line;
    /// this keeps a valuable drop clearly above everything while the ordinary intervals keep their shape.
    /// </summary>
    public const double FlattenExponent = .4;

    // Intervals are anchored at whole multiples of a fixed step, counted from the start of the session. A window
    // that grows with every second would otherwise move every drop into a different interval on every update, and
    // the bars of a running session would keep growing and shrinking without anything being dropped.
    private static readonly double[] Steps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];

    /// <summary>The interval length used for a visible window, in seconds.</summary>
    public static double IntervalFor(TimeSpan window) =>
        Steps.FirstOrDefault(step => window.TotalSeconds / step <= MaximumIntervals, Steps[^1]);

    /// <summary>
    /// What arrived in each interval of the window, every interval of the window included. Without a valuation the
    /// series counts items; with one it sums what they are worth. A flattened series compresses its heights, so the
    /// ordinary intervals stay readable beside a single interval that holds a fortune.
    /// </summary>
    public static SessionTimelineSeries Series(string id, string label, string color,
        IEnumerable<SessionDropSample> drops, TimeSpan from, TimeSpan to,
        Func<SessionDropSample, decimal>? value = null, bool isSilver = false, bool flatten = false) =>
        Series(id, label, color, drops, drop => drop.Elapsed, value ?? (drop => drop.Quantity), from, to, isSilver, flatten);

    /// <summary>The same intervals for any timed values, such as the overlay's already valued silver of each drop.</summary>
    public static SessionTimelineSeries Series<T>(string id, string label, string color,
        IEnumerable<T> samples, Func<T, TimeSpan> elapsed, Func<T, decimal> value, TimeSpan from, TimeSpan to,
        bool isSilver = false, bool flatten = false)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var window = (to - from).TotalSeconds;
        if (window <= 0) return new(id, label, color, [], 0, isSilver, flatten);
        var step = IntervalFor(to - from);
        var first = Math.Floor(from.TotalSeconds / step);
        var count = (int)Math.Ceiling(to.TotalSeconds / step) - (int)first + 1;
        var totals = new decimal[Math.Clamp(count, 1, MaximumIntervals * 2)];
        foreach (var sample in samples)
        {
            var at = elapsed(sample);
            if (at < from || at > to) continue;
            var index = (int)(Math.Floor(at.TotalSeconds / step) - first);
            if (index < 0 || index >= totals.Length) continue;
            try { totals[index] += value(sample); }
            catch (OverflowException) { /* One unpriceable drop must not take the whole series down. */ }
        }
        var peak = totals.Max();
        return new(id, label, color, [.. totals.Select((total, index) =>
        {
            var at = (first + index) * step;
            // A correction can leave an interval below zero; it stays on the baseline.
            var share = peak <= 0 ? 0 : Math.Max(0, (double)(total / peak));
            return new SessionTimelineBar(TimeSpan.FromSeconds(at),
                Math.Clamp((at - from.TotalSeconds) / window, 0, 1),
                Math.Clamp((at + step - from.TotalSeconds) / window, 0, 1),
                flatten ? Math.Pow(share, FlattenExponent) : share, total);
        })], peak, isSilver, flatten);
    }

    /// <summary>
    /// The highest point the visible layers reach at a moment. A drop's mark sits above it, so it never hides
    /// behind the tallest bar or curve of that moment.
    /// </summary>
    public static double Top(TimeSpan at, IEnumerable<SessionTimelineSeries> series, TimeSpan from, TimeSpan to)
    {
        ArgumentNullException.ThrowIfNull(series);
        var window = (to - from).TotalSeconds;
        if (window <= 0) return 0;
        var x = (at - from).TotalSeconds / window;
        var top = 0d;
        foreach (var entry in series)
            foreach (var bar in entry.Bars)
                if (x >= bar.Start && x <= bar.End) top = Math.Max(top, bar.Filled);
        return Math.Clamp(top, 0, 1);
    }
}
