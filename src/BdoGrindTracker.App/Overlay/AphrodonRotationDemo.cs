using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Overlay;

internal static class AphrodonRotationDemo
{
    // Relative to Golden Fragrance at frame 224946 in the supplied 60 fps timeline.
    internal static readonly RotationRun Reference = Build();
    private static RotationRun Build()
    {
        List<RotationEvent> events = [new("start", "Rotationsstart", 0)];
        int[] waves = [228057, 231837, 235946, 239857, 243367, 246865, 250543, 254170, 258042];
        int[] scarecrows = [228962, 232742, 236851, 240762, 244272, 247497, 251448, 255075, 258947];
        var hogs = 0;
        for (var i = 0; i < 9; i++)
        {
            events.Add(new(i == 5 ? "agris" : "hog", i == 5 ? "Agris-Event" : "Hog", (waves[i] - 224946) / 60d, i == 5 ? 1 : ++hogs));
            events.Add(new("big-scarecrow", "Große Vogelscheuche", (scarecrows[i] - 224946) / 60d, i + 1));
        }
        events.Add(new("afk", "AFK-Phase", (261607 - 224946) / 60d));
        events.Add(new("end", "AFK-Ende", (270810 - 224946) / 60d));
        return new(events[^1].Seconds, events) { TimingVersion = 2 };
    }
    internal static RotationMonitorSnapshot At(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, Reference.Duration);
        return new() { SpotId = LootSpotCatalog.AphrodonId, SpotName = "Aphrodon Temple", HasProfile = true,
            Elapsed = seconds, Synchronized = true, IsAfk = seconds >= Reference.Events[^2].Seconds,
            Events = Reference.Events.Where(e => e.Seconds <= seconds).ToArray(), Best = Reference,
            Ideal = Reference, SectorBests = Reference.Events.Skip(1).Select((e, i) => (e.Key, Duration: e.Seconds - Reference.Events[i].Seconds))
                .ToDictionary(e => e.Key, e => e.Duration),
            SessionRotations = [new(Reference.Duration * 1.03, 0), new(Reference.Duration, 0)],
            Status = "Demo · Aufnahme als Beispielreferenz" };
    }
}
