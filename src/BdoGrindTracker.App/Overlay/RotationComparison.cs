namespace BdoGrindTracker.App.Overlay;

/// <summary>Best, ideal and mechanic-best comparison shared by rotation profiles.</summary>
internal static class RotationComparison
{
    /// <summary>The fastest run, the mechanic bests of runs with its checkpoint path and the ideal run built from them.</summary>
    internal static (RotationRun? Best, RotationRun? Ideal, IReadOnlyDictionary<string, double> Sectors) Compare(IReadOnlyList<RotationRun> runs)
    {
        var best = runs.MinBy(run => run.Duration);
        var sectors = new Dictionary<string, double>();
        // Compare identical checkpoints only. Missing or extra OCR events never
        // shift a sector onto an unrelated mechanic.
        if (best is not null)
            foreach (var run in runs.Where(run => Checkpoints(run).Select(e => e.Key).SequenceEqual(Checkpoints(best).Select(e => e.Key))))
            {
                var checkpoints = Checkpoints(run);
                for (var i = 1; i < checkpoints.Length; i++)
                {
                    var key = checkpoints[i].Key;
                    var duration = checkpoints[i].Seconds - checkpoints[i - 1].Seconds;
                    sectors[key] = Math.Min(sectors.GetValueOrDefault(key, double.MaxValue), duration);
                }
            }
        RotationRun? ideal = null;
        if (best is not null)
        {
            double total = 0;
            var checkpoints = Checkpoints(best);
            var idealTimes = checkpoints.ToDictionary(e => e.Key, e => e.Kind == "start" ? 0 : total += sectors[e.Key]);
            var events = best.Events.Select(e =>
            {
                if (idealTimes.TryGetValue(e.Key, out var time)) return e with { Seconds = time };
                var right = Array.FindIndex(checkpoints, p => p.Seconds >= e.Seconds);
                if (right <= 0) return e with { Seconds = 0 };
                var a = checkpoints[right - 1]; var b = checkpoints[right];
                var fraction = (e.Seconds - a.Seconds) / Math.Max(.001, b.Seconds - a.Seconds);
                return e with { Seconds = idealTimes[a.Key] + fraction * (idealTimes[b.Key] - idealTimes[a.Key]) };
            }).ToArray();
            ideal = new(total, events);
        }
        return (best, ideal, sectors);
    }

    private static RotationEvent[] Checkpoints(RotationRun run) => run.Events.Where(RotationTimelinePresentation.IsCheckpoint).ToArray();
}
