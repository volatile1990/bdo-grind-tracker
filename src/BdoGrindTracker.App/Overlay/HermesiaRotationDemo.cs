namespace BdoGrindTracker.App.Overlay;

internal static class HermesiaRotationDemo
{
    // Relative timings from the supplied Resolve timeline. Demonstration only;
    // these are never written into personal records.
    private static readonly RotationRun Source = new(618.216667, [
        new("start", "Rotationsstart", 0),
        new("porter", "Träger-Spawn", 9.733333, 1),
        new("porter", "Träger-Spawn", 27.583333, 2),
        new("porter", "Träger-Spawn", 45.333333, 3),
        new("porter", "Träger-Spawn", 60.900000, 4),
        new("porter", "Träger-Spawn", 90.600000, 5),
        new("drakania", "Drakania-Spawn", 102.350000, 1),
        new("porter", "Träger-Spawn", 102.350000, 6),
        new("drakania-kill", "Drakania besiegt", 137.200000, 1),
        new("transfer", "Minenrechte übertragen", 137.916667, 1),
        new("mine-enter", "Mine betreten", 140.233333, 1),
        new("offer", "Opfergabe angeordnet", 150.000000, 1),
        new("porter", "Träger-Spawn", 167.816667, 7),
        new("porter", "Träger-Spawn", 186.383333, 8),
        new("porter", "Träger-Spawn", 201.983333, 9),
        new("porter", "Träger-Spawn", 220.183333, 10),
        new("mine-second", "Zweite Minenphase", 230.666667, 1),
        new("porter", "Träger-Spawn", 238.733333, 11),
        new("porter", "Träger-Spawn", 257.066667, 12),
        new("porter", "Träger-Spawn", 275.333333, 13),
        new("porter", "Träger-Spawn", 292.750000, 14),
        new("porter", "Träger-Spawn", 308.733333, 15),
        new("mine-cleared", "Mine abgeschlossen", 318.783333, 1),
        new("mine-enter", "Mine betreten", 324.533333, 2),
        new("porter", "Träger-Spawn", 334.283333, 16),
        new("porter", "Träger-Spawn", 352.233333, 17),
        new("porter", "Träger-Spawn", 371.216667, 18),
        new("porter", "Träger-Spawn", 387.866667, 19),
        new("porter", "Träger-Spawn", 407.916667, 20),
        new("mine-second", "Zweite Minenphase", 418.383333, 2),
        new("porter", "Träger-Spawn", 428.200000, 21),
        new("porter", "Träger-Spawn", 445.883333, 22),
        new("porter", "Träger-Spawn", 464.150000, 23),
        new("porter", "Träger-Spawn", 484.150000, 24),
        new("porter", "Träger-Spawn", 500.833333, 25),
        new("dragon", "Drachen-Spawn", 509.050000, 1),
        new("porter", "Träger-Spawn", 520.066667, 26),
        new("afk", "AFK-Beginn", 556.950000, 1),
        new("end", "AFK-Ende", 618.216667, 1)]);
    internal static readonly RotationRun Reference = HermesiaRotationTracker.FromFirstEvent(Source);
    internal static RotationMonitorSnapshot At(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, Reference.Duration);
        var actual = Reference.Events.Where(e => e.Seconds * .98 <= seconds)
            .Select(e => e with { Seconds = e.Seconds * .98 }).ToArray();
        var checkpoints = Reference.Events.Where(RotationTimelinePresentation.IsCheckpoint).ToArray();
        var sectors = checkpoints.Skip(1).Select((e,i) => (e.Key, Duration: e.Seconds-checkpoints[i].Seconds))
            .ToDictionary(e => e.Key, e => e.Duration * .97);
        var ideal = new RotationRun(Reference.Duration * .97,
            Reference.Events.Select(e => e with { Seconds = e.Seconds * .97 }).ToArray());
        return new() { SpotId = BdoGrindTracker.Core.LootSpotCatalog.HermesiaId, SpotName = "Hermesia Inner Castle", HasProfile = true,
            Elapsed = seconds, Synchronized = true, IsAfk = seconds >= Reference.Events.First(e => e.Kind == "afk").Seconds * .98,
            Events = actual, Best = Reference, Ideal = ideal, SectorBests = sectors, Completed = 3,
            SessionRotations = [new(Reference.Duration * 1.04, 18), new(Reference.Duration * 1.01, 21)],
            Status = "Demo · Aufnahme als Beispielreferenz" };
    }
}
