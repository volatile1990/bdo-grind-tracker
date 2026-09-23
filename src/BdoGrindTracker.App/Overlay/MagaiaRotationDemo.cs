namespace BdoGrindTracker.App.Overlay;

/// <summary>
/// The supplied Magaia recording (2026-09-22) as example reference: one rotation of three cycles with Elion's Tears,
/// Unbroken Oath and Priest of the End, 16 fragments of divinity and one death in each of the last two cycles.
/// Seconds after "The sinners are summoned". Demonstration only; never a personal record.
/// </summary>
internal static class MagaiaRotationDemo
{
    private static readonly (string Kind, double Seconds)[] Messages =
    [
        ("fragment", 71), ("fragment", 92), ("fragment", 113), ("fragment", 149.5), ("fragment", 170.5), ("fragment", 192),
        ("prayer", 242), ("knight", 347.5), ("knight", 404.5), ("knight", 468), ("doubt", 480.5), ("sacred", 488),
        ("afk", 522.5), ("end", 599.5),
        ("fragment", 682.5), ("fragment", 719.5), ("fragment", 724.5), ("fragment", 730), ("fragment", 751), ("fragment", 819.5),
        ("prayer", 851.5), ("knight", 955), ("away", 994.5), ("back", 1002.5), ("knight", 1040.5), ("knight", 1074),
        ("doubt", 1101.5), ("afk", 1163), ("end", 1240.5),
        ("fragment", 1275.5), ("fragment", 1281), ("fragment", 1286), ("fragment", 1355),
        ("prayer", 1484.5), ("knight", 1597), ("knight", 1662), ("knight", 1726.5), ("doubt", 1732.5),
        ("away", 1767), ("back", 1775.5), ("afk", 1801),
    ];
    private const double Duration = 1878;
    internal static readonly RotationRun Reference = Run(1);

    private static RotationRun Run(double pace)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        RotationEvent Event(string kind, double seconds) =>
            new(kind, Label(kind), seconds * pace, occurrences[kind] = occurrences.GetValueOrDefault(kind) + 1);
        return new RotationRun(Duration * pace, [Event("start", 0), .. Messages.Select(m => Event(m.Kind, m.Seconds)), Event("end", Duration)])
            { TimingVersion = 3 };
    }

    private static string Label(string kind) => kind switch
    {
        "start" => "Sünder beschworen", "fragment" => "Fragment of Divinity", "prayer" => "DPS-Check bestanden",
        "knight" => "Ritter besiegt", "doubt" => "Schlussphase", "sacred" => "Elion's Tears", "afk" => "AFK-Phase",
        "away" => "Spieler tot oder abwesend", "back" => "Spieler zurück", _ => "Neuer Zyklus",
    };

    internal static RotationMonitorSnapshot At(double seconds)
    {
        var current = Run(.98);
        seconds = Math.Clamp(seconds, 0, current.Duration);
        var events = current.Events.Where(e => e.Seconds <= seconds).ToArray();
        return new() { SpotId = BdoGrindTracker.Core.LootSpotCatalog.MagaiaId, SpotName = "Magaia Temple", HasProfile = true,
            Elapsed = seconds, Synchronized = true, IsAfk = events.LastOrDefault(e => e.Kind is "afk" or "end" or "start")?.Kind == "afk",
            Events = events, Best = Reference, Ideal = Reference, SectorBests = new Dictionary<string, double>(), Completed = 1,
            SessionRotations = [new(Reference.Duration * 1.03)], SupportsSpecialEvents = true,
            SpecialEvents = RotationDefinition.Magaia.SpecialEventCount(events), ComparedSpecialEvents = 16,
            Status = "Beispieldaten · Magaia-Aufnahme als Referenz" };
    }
}
