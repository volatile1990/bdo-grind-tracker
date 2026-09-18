namespace BdoGrindTracker.App.Overlay;

/// <summary>
/// The example session's latest rotation runs against the best and ideal rotation of the six before it.
/// Demonstration only; these are never written into personal records.
/// </summary>
internal static class HermesiaRotationDemo
{
    private static readonly RotationRun[] Earlier = DemoSession.Rotations[..^1].Select(rotation => rotation.Run).ToArray();
    private static readonly (RotationRun? Best, RotationRun? Ideal, IReadOnlyDictionary<string, double> Sectors) Comparison =
        RotationComparison.Compare(Earlier);
    private static readonly RotationRun Current = DemoSession.Rotations[^1].Run;
    internal static readonly RotationRun Reference = Comparison.Best!;

    internal static RotationMonitorSnapshot At(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, Current.Duration);
        return new() { SpotId = BdoGrindTracker.Core.LootSpotCatalog.HermesiaId, SpotName = "Hermesia Inner Castle", HasProfile = true,
            Elapsed = seconds, Synchronized = true, IsAfk = seconds >= Current.Events.First(e => e.Kind == "afk").Seconds,
            Events = Current.Events.Where(e => e.Seconds <= seconds).ToArray(), Best = Reference, Ideal = Comparison.Ideal,
            SectorBests = Comparison.Sectors, Completed = Earlier.Length,
            SessionRotations = DemoSession.CompletedBefore(Earlier.Length),
            Status = $"Beispieldaten · Hermesia-Session vom {DemoSession.Date}" };
    }
}
