namespace BdoGrindTracker.App.Overlay;

public sealed record OverlayChartMarker(OverlayDropMarker Drop, double X, double Y);

public static class OverlayChartMarkers
{
    public static long FirstTick(OverlaySnapshot snapshot) => snapshot.SilverHistory.Count == 0 ? 0 :
        Math.Min(snapshot.SilverHistory[0].Elapsed.Ticks,
            snapshot.DropMarkers.Where(drop => drop.Elapsed >= TimeSpan.Zero)
                .Select(drop => drop.Elapsed.Ticks).DefaultIfEmpty(snapshot.SilverHistory[0].Elapsed.Ticks).Min());

    public static IReadOnlyList<OverlayChartMarker> Create(OverlaySnapshot snapshot)
    {
        var samples = snapshot.SilverHistory;
        if (samples.Count < 2) return [];
        var first = TimeSpan.FromTicks(FirstTick(snapshot));
        var last = samples[^1].Elapsed;
        var span = (last - first).Ticks;
        if (span <= 0) return [];
        var maximum = Math.Max(1m, samples.Max(sample => sample.SilverPerHour));
        return snapshot.DropMarkers.Where(drop => drop.Elapsed >= first && drop.Elapsed <= last)
            .Select(drop =>
            {
                var right = 1;
                while (right < samples.Count - 1 && samples[right].Elapsed < drop.Elapsed) right++;
                var a = samples[right - 1];
                var b = samples[right];
                var fraction = Math.Clamp((decimal)(drop.Elapsed - a.Elapsed).Ticks / Math.Max(1, (b.Elapsed - a.Elapsed).Ticks), 0, 1);
                var rate = a.SilverPerHour + (b.SilverPerHour - a.SilverPerHour) * fraction;
                return new OverlayChartMarker(drop, (double)(drop.Elapsed - first).Ticks / span,
                    (double)(70m - Math.Clamp(rate / maximum, 0, 1) * 64m) / 72);
            }).ToArray();
    }
}
