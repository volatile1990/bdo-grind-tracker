using System.Runtime.CompilerServices;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Overlay;

/// <param name="Special">The phase belongs to a special event (random extra or replacing mechanic).</param>
public sealed record RotationPhase(string Id, string Group, string Name, double Start, double End,
    string Color, string GroupColor, bool Special = false);

/// <summary>Projects recorded boundaries without estimating future mechanic times.</summary>
public static class RotationPhases
{
    public static string NormalizeColors(string? mode) => mode is "gold" or "slate" or "minimal" ? mode : "colored";
    public static string MarkerColor(string? mode, bool current) => NormalizeColors(mode) switch
    {
        "gold" or "minimal" => current ? "#F1CC7A" : "#C0B18B",
        "slate" => current ? "#DCE3E8" : "#A0B0BD",
        _ => current ? "#66D8C7" : "#F1CC7A"
    };
    public static string Duration(double seconds)
    {
        var total = (int)Math.Round(Math.Max(0, seconds));
        return $"{total / 60}:{total % 60:00}";
    }
    public static IReadOnlyList<RotationPhase> Create(string? spotId, IReadOnlyList<RotationEvent> events, double elapsed, string? colors = "colored")
    {
        if (spotId == LootSpotCatalog.AphrodonId) return CreateAphrodon(events, elapsed, colors);
        if (spotId == LootSpotCatalog.EventHorizonId) return CreateEventHorizon(events, elapsed, colors);
        if (spotId == LootSpotCatalog.MagaiaId) return CreateMagaia(events, elapsed, colors);
        if (spotId != LootSpotCatalog.HermesiaId || events.Count == 0 || !double.IsFinite(elapsed) || elapsed <= 0) return [];
        var phases = new List<RotationPhase>();
        string? active = null;
        double start = 0;
        var mine = 0;
        var lastRank = -1;
        foreach (var e in events.Where(e => double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= elapsed).OrderBy(e => e.Seconds))
        {
            var next = e.Kind switch
            {
                "start" => "startup",
                "drakania" => "drakania",
                "mine-enter" when e.Occurrence is 1 or 2 => $"mine-{e.Occurrence}-1",
                "mine-second" when mine is 1 or 2 => $"mine-{mine}-2",
                "dragon" => "dragon",
                "afk" => "afk",
                _ => null,
            };
            if (e.Kind is "end" or "failure") { Finish(e.Seconds); active = null; break; }
            if (next is null || next == active) continue;
            var rank = Array.IndexOf(Order, next);
            // Repeated or late OCR messages must not send a phase backwards.
            if (rank <= lastRank) continue;
            Finish(e.Seconds);
            active = next; start = e.Seconds; lastRank = rank;
            if (e.Kind == "mine-enter") mine = e.Occurrence;
        }
        Finish(elapsed);
        return phases;

        void Finish(double end)
        {
            if (active is null || end <= start) return;
            var (group, name, color, groupColor) = Style(active);
            var index = Array.IndexOf(Order, active);
            (color, groupColor) = NormalizeColors(colors) switch
            {
                "gold" => (index % 2 == 0 ? "#78643C" : "#948052", "#D8BD75"),
                "slate" => (index % 2 == 0 ? "#45535E" : "#61717E", "#A0B0BD"),
                "minimal" => (index % 2 == 0 ? "#39434B" : "#505B64", "#C8AA67"),
                _ => (color, groupColor)
            };
            phases.Add(new(active, group, name, start, end, color, groupColor));
        }
    }

    private static readonly string[] Order = ["startup", "drakania", "mine-1-1", "mine-1-2", "mine-2-1", "mine-2-2", "dragon", "afk"];
    private static IReadOnlyList<RotationPhase> CreateAphrodon(IReadOnlyList<RotationEvent> events, double elapsed, string? colors)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0) return [];
        var result = new List<RotationPhase>();
        var boundaries = events.Where(e => e.Kind is "start" or "hog" or "agris" or "afk" or "end" or "failure")
            .Where(e => double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= elapsed).OrderBy(e => e.Seconds).ToArray();
        var wave = 0;
        for (var i = 0; i < boundaries.Length; i++)
        {
            var e = boundaries[i];
            if (e.Kind is "end" or "failure") break;
            if (e.Kind is "hog" or "agris") wave++;
            var end = i + 1 < boundaries.Length ? boundaries[i + 1].Seconds : elapsed;
            if (end <= e.Seconds) continue;
            var color = e.Kind switch { "agris" => "#9275BE", "hog" => "#A58B48", "big-scarecrow" => "#32788F", "afk" => "#4D6275", _ => "#78643C" };
            color = NormalizeColors(colors) switch {
                "gold" => i % 2 == 0 ? "#78643C" : "#948052",
                "slate" => i % 2 == 0 ? "#45535E" : "#61717E",
                "minimal" => i % 2 == 0 ? "#39434B" : "#505B64", _ => color };
            var name = e.Kind switch { "start" => "Anlauf", "hog" => $"{wave}. Hog + Scarecrow", "agris" => $"{wave}. Agris + Scarecrow", "big-scarecrow" => "Vogelscheuche", _ => "AFK" };
            result.Add(new(e.Key, e.Kind is "start" or "afk" ? e.Kind : $"wave-{wave}", name, e.Seconds, end, color, color,
                RotationDefinition.Aphrodon.IsSpecial(e.Kind)));
        }
        return result;
    }
    private static readonly string[] EventHorizonOrder = ["startup",
        "wormhole-1-waves", "wormhole-1-debris", "wormhole-1-mobs",
        "wormhole-2-approach", "wormhole-2-waves", "wormhole-2-debris", "wormhole-2-mobs",
        "wormhole-3-approach", "wormhole-3-waves", "wormhole-3-debris", "wormhole-3-mobs", "boss", "afk"];

    private static IReadOnlyList<RotationPhase> CreateEventHorizon(IReadOnlyList<RotationEvent> events, double elapsed, string? colors)
    {
        if (events.Count == 0 || !double.IsFinite(elapsed) || elapsed <= 0) return [];
        var phases = new List<RotationPhase>();
        string? active = null;
        double start = 0;
        var wormhole = 0;
        var lastRank = -1;
        foreach (var e in events.Where(e => double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= elapsed).OrderBy(e => e.Seconds))
        {
            if (e.Kind == "anomaly") wormhole = Math.Min(3, wormhole + 1);
            if (e.Kind == "end") { Finish(e.Seconds); active = null; break; }
            // Debris is announced together with the halted banner: the mini AFK replaces the mob phase it opened.
            if (e.Kind == "debris" && active == $"wormhole-{wormhole}-mobs" && e.Seconds - start < 3)
            {
                active = $"wormhole-{wormhole}-debris";
                lastRank = Array.IndexOf(EventHorizonOrder, active);
                continue;
            }
            var next = e.Kind switch
            {
                "start" => "startup",
                "anomaly" => $"wormhole-{wormhole}-waves",
                "halted" or "spacetime" => $"wormhole-{wormhole}-mobs",
                "debris" => $"wormhole-{wormhole}-debris",
                "expansion" when wormhole < 3 => $"wormhole-{wormhole + 1}-approach",
                "boss" => "boss",
                "boss-kill" => "afk",
                _ => null,
            };
            if (next is null || next == active) continue;
            var rank = Array.IndexOf(EventHorizonOrder, next);
            // Repeated or late OCR messages must not send a phase backwards.
            if (rank <= lastRank) continue;
            Finish(e.Seconds);
            active = next; start = e.Seconds; lastRank = rank;
        }
        Finish(elapsed);
        return phases;

        void Finish(double end)
        {
            if (active is null || end <= start) return;
            var (group, name, color, groupColor) = EventHorizonStyle(active);
            var index = Array.IndexOf(EventHorizonOrder, active);
            (color, groupColor) = NormalizeColors(colors) switch
            {
                "gold" => (index % 2 == 0 ? "#78643C" : "#948052", "#D8BD75"),
                "slate" => (index % 2 == 0 ? "#45535E" : "#61717E", "#A0B0BD"),
                "minimal" => (index % 2 == 0 ? "#39434B" : "#505B64", "#C8AA67"),
                _ => (color, groupColor)
            };
            phases.Add(new(active, group, name, start, end, color, groupColor, active.EndsWith("-debris", StringComparison.Ordinal)));
        }
    }

    private static readonly string[] MagaiaParts = ["dps", "knight-1", "knight-2", "knight-3", "clear", "final", "afk"];
    private static readonly string[] MagaiaOrder = [.. Enumerable.Range(1, RotationDefinition.MagaiaCycles)
        .SelectMany(cycle => MagaiaParts.Select(part => $"cycle-{cycle}-{part}"))];
    // The final mechanics in their fixed order; each names its cycle and colors its group.
    private static readonly (string Name, string Color, string GroupColor)[] MagaiaFinals =
        [("Elion's Tears", "#665493", "#B397E1"), ("Unbroken Oath", "#B7733E", "#E7AA6E"), ("Priest of the End", "#A25165", "#DB8398")];

    /// <summary>
    /// Magaia: three cycles, each with the DPS check until the flames of prayer, the double packs until each knight
    /// falls, the remaining packs, its final mechanic and the AFK phase until the next cycle begins.
    /// </summary>
    private static IReadOnlyList<RotationPhase> CreateMagaia(IReadOnlyList<RotationEvent> events, double elapsed, string? colors)
    {
        if (events.Count == 0 || !double.IsFinite(elapsed) || elapsed <= 0) return [];
        var phases = new List<RotationPhase>();
        string? active = null;
        double start = 0;
        var lastRank = -1;
        var cycle = 1;
        var knights = 0;
        foreach (var e in events.Where(e => double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= elapsed).OrderBy(e => e.Seconds))
        {
            if (e.Kind == "failure" || e.Kind == "end" && cycle == RotationDefinition.MagaiaCycles) { Finish(e.Seconds); active = null; break; }
            if (e.Kind == "end") { cycle++; knights = 0; }
            if (e.Kind == "knight") knights++;
            var part = e.Kind switch
            {
                "start" or "end" => "dps",
                "prayer" => "knight-1",
                "knight" => knights < 3 ? $"knight-{knights + 1}" : "clear",
                "doubt" => "final",
                "afk" => "afk",
                _ => null,
            };
            if (part is null) continue;
            var next = $"cycle-{cycle}-{part}";
            if (next == active) continue;
            var rank = Array.IndexOf(MagaiaOrder, next);
            // Repeated or late OCR messages must not send a phase backwards.
            if (rank <= lastRank) continue;
            Finish(e.Seconds);
            active = next; start = e.Seconds; lastRank = rank;
        }
        Finish(elapsed);
        return phases;

        void Finish(double end)
        {
            if (active is null || end <= start) return;
            var index = Array.IndexOf(MagaiaOrder, active);
            var number = index / MagaiaParts.Length + 1;
            var final = MagaiaFinals[number - 1];
            var group = $"cycle-{number}";
            var (name, color) = MagaiaParts[index % MagaiaParts.Length] switch
            {
                "dps" => ($"Zyklus {number} · DPS-Check", "#A58B48"),
                "clear" => ($"Zyklus {number} · Restliche Packs", "#45535E"),
                "final" => (final.Name, final.Color),
                "afk" => ($"Zyklus {number} · AFK-Phase", "#4D6275"),
                var knight => ($"Zyklus {number} · Ritter {knight[^1]}", index % 2 == 0 ? "#32788F" : "#519EB1"),
            };
            var groupColor = final.GroupColor;
            (color, groupColor) = NormalizeColors(colors) switch
            {
                "gold" => (index % 2 == 0 ? "#78643C" : "#948052", "#D8BD75"),
                "slate" => (index % 2 == 0 ? "#45535E" : "#61717E", "#A0B0BD"),
                "minimal" => (index % 2 == 0 ? "#39434B" : "#505B64", "#C8AA67"),
                _ => (color, groupColor)
            };
            phases.Add(new(active, group, name, start, end, color, groupColor));
        }
    }

    /// <summary>Stroke of a marker: special events in the special color, absences like failures.</summary>
    public static string MarkerStroke(string? spotId, RotationEvent e, string? colors, bool current) =>
        e.Kind is "failure" or "away" ? "#E87C79"
        : RotationDefinition.Find(spotId)?.IsSpecial(e.Kind) == true ? SpecialColor : MarkerColor(colors, current);

    private sealed record CachedPhases(string? SpotId, double Elapsed, IReadOnlyList<RotationPhase> Phases);
    // A completed rotation keeps its recorded event list, so its phases are derived once, not on every render.
    private static readonly ConditionalWeakTable<IReadOnlyList<RotationEvent>, CachedPhases> Cache = new();

    /// <summary>
    /// The phases in their own colours, remembered per recorded event list. The running rotation brings a new list
    /// with every event and a new length every second, so it is derived again whenever it changed.
    /// </summary>
    public static IReadOnlyList<RotationPhase> Cached(string? spotId, IReadOnlyList<RotationEvent>? events, double elapsed)
    {
        if (events is not { Count: > 0 }) return Create(spotId, [], elapsed);
        if (Cache.TryGetValue(events, out var cached) && cached.SpotId == spotId && cached.Elapsed == elapsed) return cached.Phases;
        var phases = Create(spotId, events, elapsed);
        Cache.AddOrUpdate(events, new(spotId, elapsed, phases));
        return phases;
    }

    /// <summary>
    /// The waiting time after a rotation's mechanics, for every spot. Magaia names one per cycle; Event Horizon's
    /// debris breaks are mechanics of their wormhole, not the rotation's AFK phase.
    /// </summary>
    public static bool IsAfk(RotationPhase phase) =>
        phase.Group == "afk" || phase.Id.EndsWith("-afk", StringComparison.Ordinal);

    /// <summary>Outline of special-event phases, identical for every spot and color scheme.</summary>
    public const string SpecialColor = "#F2C14E";

    private static (string Group, string Name, string Color, string GroupColor) EventHorizonStyle(string id)
    {
        if (id == "startup") return ("startup", "Erstes Pack", "#A58B48", "#D8BD75");
        if (id == "boss") return ("boss", "Bosskampf", "#B7733E", "#E7AA6E");
        if (id == "afk") return ("afk", "AFK-Phase", "#4D6275", "#91A3B4");
        var parts = id.Split('-');
        var wormhole = parts[1];
        var (dark, light, accent) = wormhole switch
        {
            "1" => ("#32788F", "#519EB1", "#69C1D5"),
            "2" => ("#665493", "#9275BE", "#B397E1"),
            _ => ("#A25165", "#C0708A", "#DB8398"),
        };
        return parts[2] switch
        {
            "approach" => ($"wormhole-{wormhole}", $"Wurmloch {wormhole} · Anlauf", dark, accent),
            "waves" => ($"wormhole-{wormhole}", $"Wurmloch {wormhole} · Wellen", light, accent),
            "debris" => ($"wormhole-{wormhole}", $"Wurmloch {wormhole} · Trümmer-AFK", "#4D6275", accent),
            _ => ($"wormhole-{wormhole}", $"Wurmloch {wormhole} · Mobs", dark, accent),
        };
    }

    private static (string Group, string Name, string Color, string GroupColor) Style(string id) => id switch
    {
        "startup" => ("startup", "Startup · 5 Porter", "#A58B48", "#D8BD75"),
        "drakania" => ("drakania", "Drakania-Kampf", "#A25165", "#DB8398"),
        "mine-1-1" => ("mine-1", "1. Mine · Phase 1", "#32788F", "#69C1D5"),
        "mine-1-2" => ("mine-1", "1. Mine · Phase 2", "#519EB1", "#69C1D5"),
        "mine-2-1" => ("mine-2", "2. Mine · Phase 1", "#665493", "#B397E1"),
        "mine-2-2" => ("mine-2", "2. Mine · Phase 2", "#9275BE", "#B397E1"),
        "dragon" => ("dragon", "Drachenkampf", "#B7733E", "#E7AA6E"),
        _ => ("afk", "AFK-Phase", "#4D6275", "#91A3B4"),
    };
}
