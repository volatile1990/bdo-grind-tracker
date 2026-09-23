using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class SessionTimelineChartTests
{
    private static SessionDropSample Drop(double second, string item, long quantity) =>
        new(TimeSpan.FromSeconds(second), item, quantity);

    private static SessionTimelineSeries Bars(IEnumerable<SessionDropSample> drops, TimeSpan from, TimeSpan to,
        Func<SessionDropSample, decimal>? value = null) =>
        SessionTimelineChart.Series("trash", "Trash", "#78A88B", drops, from, to, value);

    [Fact]
    public void EachIntervalHoldsWhatArrivedInItAndIsNormalizedToTheSeriesPeak()
    {
        var window = TimeSpan.FromSeconds(SessionTimelineChart.MaximumIntervals); // One second per interval.
        var series = Bars([Drop(.5, "Trash", 4), Drop(.9, "Trash", 6), Drop(10.2, "Trash", 5), Drop(200, "Trash", 99)],
            TimeSpan.Zero, window);

        Assert.Equal(10, series.Peak);
        var filled = series.Bars.Where(bar => bar.Value > 0).ToArray();
        Assert.Equal(2, filled.Length);
        // Both drops of the first second share one interval, at the full height of the series' peak.
        Assert.Equal((TimeSpan.Zero, 10m, 1d), (filled[0].At, filled[0].Value, filled[0].Filled));
        Assert.Equal((TimeSpan.FromSeconds(10), 5m, .5), (filled[1].At, filled[1].Value, filled[1].Filled));
        // Drops outside the visible window are not drawn.
        Assert.All(series.Bars, bar => Assert.InRange(bar.End, 0, 1));
    }

    [Fact]
    public void IntervalsKeepTheirPlaceWhileTheSessionGrows()
    {
        // The same drops seen through a window that grows by a second must not move between intervals.
        IReadOnlyList<SessionDropSample> drops = [Drop(12, "Trash", 3), Drop(70, "Trash", 9), Drop(130, "Trash", 5)];
        var first = Bars(drops, TimeSpan.Zero, TimeSpan.FromSeconds(200));
        var later = Bars(drops, TimeSpan.Zero, TimeSpan.FromSeconds(201));

        Assert.Equal([3, 5, 9], first.Bars.Where(bar => bar.Value > 0).Select(bar => (int)bar.Value).Order());
        Assert.Equal(first.Bars.Where(bar => bar.Value > 0).Select(bar => bar.At),
            later.Bars.Where(bar => bar.Value > 0).Select(bar => bar.At));
        Assert.Equal(first.Bars.Where(bar => bar.Value > 0).Select(bar => bar.Filled),
            later.Bars.Where(bar => bar.Value > 0).Select(bar => bar.Filled));
    }

    [Fact]
    public void AValuationTurnsTheQuantitiesIntoSilverOfThatInterval()
    {
        var window = TimeSpan.FromSeconds(SessionTimelineChart.MaximumIntervals);
        var series = Bars([Drop(.5, "Trash", 4), Drop(10.2, "Gem", 1)], TimeSpan.Zero, window,
            drop => drop.ItemName == "Gem" ? 500_000_000m : 1_000_000m * drop.Quantity);

        Assert.Equal(500_000_000m, series.Peak);
        var filled = series.Bars.Where(bar => bar.Value > 0).ToArray();
        Assert.Equal([4_000_000m, 500_000_000m], filled.Select(bar => bar.Value));
        Assert.Equal([.008, 1], filled.Select(bar => bar.Filled));
    }

    [Fact]
    public void AFlattenedSeriesKeepsSmallIntervalsVisibleWithoutLevellingThem()
    {
        var window = TimeSpan.FromSeconds(SessionTimelineChart.MaximumIntervals);
        IReadOnlyList<SessionDropSample> drops = [Drop(.5, "Trash", 1), Drop(10.5, "Gem", 1)];
        decimal Value(SessionDropSample drop) => drop.ItemName == "Gem" ? 1_000_000_000m : 1_000_000m;

        var linear = SessionTimelineChart.Series("silver", "Silber", "#D8BD75", drops, TimeSpan.Zero, window, Value);
        var flattened = SessionTimelineChart.Series("silver", "Silber", "#D8BD75", drops, TimeSpan.Zero, window, Value,
            flatten: true);

        // A thousandth of the peak is a hairline on a linear axis and stays visible on a flattened one, without
        // being lifted so far that the valuable drop stops standing out.
        Assert.Equal(.001, linear.Bars.First(bar => bar.Value > 0).Filled, 4);
        Assert.Equal(.063, flattened.Bars.First(bar => bar.Value > 0).Filled, 3);
        // The peak still reaches the top, and empty intervals stay on the axis.
        Assert.Equal(1, flattened.Bars.Single(bar => bar.Value == 1_000_000_000m).Filled, 6);
        Assert.All(flattened.Bars.Where(bar => bar.Value == 0), bar => Assert.Equal(0, bar.Filled));
        Assert.True(flattened.IsFlattened);
        // Ordinary intervals keep their differences: ten times the value is still clearly higher.
        Assert.Equal(Math.Pow(10, SessionTimelineChart.FlattenExponent),
            Math.Pow(.01, SessionTimelineChart.FlattenExponent) / Math.Pow(.001, SessionTimelineChart.FlattenExponent), 6);
    }

    [Fact]
    public void TheTopOfAMomentIsTheHighestOfEveryVisibleLayer()
    {
        var window = TimeSpan.FromSeconds(SessionTimelineChart.MaximumIntervals);
        var trash = Bars([Drop(1.5, "Trash", 4), Drop(30.5, "Trash", 8)], TimeSpan.Zero, window);
        var silver = Bars([Drop(1.5, "Gem", 1), Drop(60.5, "Gem", 1)], TimeSpan.Zero, window,
            drop => drop.Elapsed < TimeSpan.FromSeconds(2) ? 200m : 1000m);

        // At 1.5 s the bar reaches half of its own peak and the silver of that interval a fifth: the bar wins.
        Assert.Equal(.5, SessionTimelineChart.Top(TimeSpan.FromSeconds(1.5), [trash, silver], TimeSpan.Zero, window), 3);
        // At 60.5 s only the silver layer has anything, and it is at its own peak.
        Assert.Equal(1, SessionTimelineChart.Top(TimeSpan.FromSeconds(60.5), [trash, silver], TimeSpan.Zero, window), 3);
        // At 30.5 s the tallest bar wins.
        Assert.Equal(1, SessionTimelineChart.Top(TimeSpan.FromSeconds(30.5), [trash], TimeSpan.Zero, window), 3);
        // Without any layer a mark sits on the axis.
        Assert.Equal(0, SessionTimelineChart.Top(TimeSpan.FromSeconds(30.5), [], TimeSpan.Zero, window));
    }
}
