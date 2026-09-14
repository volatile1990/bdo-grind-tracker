using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Overlay;

public sealed record RotationPhase(string Id, string Group, string Name, double Start, double End,
    string Color, string GroupColor);

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
            if (e.Kind == "end") { Finish(e.Seconds); active = null; break; }
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
            result.Add(new(e.Key, e.Kind is "start" or "afk" ? e.Kind : $"wave-{wave}", name, e.Seconds, end, color, color));
        }
        return result;
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
