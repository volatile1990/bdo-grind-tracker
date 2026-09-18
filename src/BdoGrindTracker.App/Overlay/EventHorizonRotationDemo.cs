namespace BdoGrindTracker.App.Overlay;

/// <summary>
/// The annotated Event Horizon recording as example reference: wormhole 1 with Will_Reception, wormholes 2 and 3
/// with the debris mini AFK. Seconds after the first pack's loot. Demonstration only; never a personal record.
/// </summary>
internal static class EventHorizonRotationDemo
{
    private static readonly (string Kind, double Seconds)[] Messages =
    [
        ("anomaly", 27.467), ("halted", 52.65), ("reception", 52.65), ("expansion", 92.317),
        ("anomaly", 138.6), ("halted", 161.517), ("debris", 161.517), ("distortion", 181.25), ("spacetime", 200.65), ("expansion", 252.717),
        ("anomaly", 311.067), ("halted", 341.283), ("debris", 341.283), ("distortion", 361.083), ("spacetime", 380.567),
        ("boss", 418.133), ("boss-kill", 447.717),
    ];
    private const double Duration = 509.783;
    internal static readonly RotationRun Reference = Run(1);

    private static RotationRun Run(double pace)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        RotationEvent Event(string kind, double seconds) =>
            new(kind, Label(kind), seconds * pace, occurrences[kind] = occurrences.GetValueOrDefault(kind) + 1);
        return new RotationRun(Duration * pace, [Event("start", 0), .. Messages.Select(m => Event(m.Kind, m.Seconds)), Event("end", Duration)])
            { TimingVersion = 2 };
    }

    private static string Label(string kind) => kind switch
    {
        "start" => "Rotationsstart", "anomaly" => "Wurmloch gestartet", "halted" => "Wurmloch geräumt",
        "reception" => "Will_Reception erhältlich", "debris" => "Trümmer-AFK", "distortion" => "Trümmer-AFK · Hälfte",
        "spacetime" => "Trümmer-AFK beendet", "expansion" => "Wurmloch aktiviert", "boss" => "Boss-Spawn",
        "boss-kill" => "AFK-Beginn", _ => "AFK-Ende",
    };

    internal static RotationMonitorSnapshot At(double seconds)
    {
        var current = Run(.98);
        seconds = Math.Clamp(seconds, 0, current.Duration);
        return new() { SpotId = BdoGrindTracker.Core.LootSpotCatalog.EventHorizonId, SpotName = "Event Horizon", HasProfile = true,
            Elapsed = seconds, Synchronized = true, IsAfk = seconds >= current.Events.First(e => e.Kind == "boss-kill").Seconds,
            Events = current.Events.Where(e => e.Seconds <= seconds).ToArray(), Best = Reference, Ideal = Reference,
            SectorBests = new Dictionary<string, double>(), Completed = 1,
            SessionRotations = [new(Reference.Duration * 1.03, 11)],
            Status = "Beispieldaten · Event-Horizon-Aufnahme als Referenz" };
    }
}
