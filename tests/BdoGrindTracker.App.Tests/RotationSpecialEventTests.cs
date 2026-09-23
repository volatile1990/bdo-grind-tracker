using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationSpecialEventTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    // "s" replaces the regular "a"; "x" and "y" form an extra branch like the Event Horizon mini AFK.
    private static RotationDefinition Definition(string spot = "test") => new(spot,
        [new("a", ["a", "s"]), new("x", ["x"], true, true), new("y", ["y"], true, false, "x", true), new("b", ["b"]),
            new("afk", ["afk"], Afk: true)],
        ["start"], ["end"], ["failure"], SpecialMessages: ["s", "x", "y"], SpecialStartMessages: ["s", "x"]);

    private static void Message(RotationPlatform tracker, string kind, double seconds) => tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));

    private static void Run(RotationPlatform tracker, double offset, double pace, string first, bool extra = false)
    {
        Message(tracker, "start", offset);
        Message(tracker, first, offset + 10 * pace);
        if (extra) { Message(tracker, "x", offset + 12 * pace); Message(tracker, "y", offset + 15 * pace); }
        Message(tracker, "b", offset + 20 * pace);
        Message(tracker, "afk", offset + 30 * pace);
        Message(tracker, "end", offset + 40 * pace);
    }

    [Fact]
    public void AgrisAndTheMiniAfkAreTheSpecialEventsOfTheirSpots()
    {
        Assert.Equal(["agris"], RotationDefinition.Aphrodon.SpecialStartMessages!);
        Assert.True(RotationDefinition.Aphrodon.IsSpecial("agris"));
        Assert.False(RotationDefinition.Aphrodon.IsSpecial("hog"));
        Assert.Equal(["debris"], RotationDefinition.EventHorizon.SpecialStartMessages!);
        Assert.All(["debris", "distortion", "spacetime"], kind => Assert.True(RotationDefinition.EventHorizon.IsSpecial(kind)));
        Assert.False(RotationDefinition.EventHorizon.IsSpecial("halted"));
        Assert.False(RotationDefinition.Hermesia.HasSpecialEvents);

        // A replacing special event keeps its own section reference; an extra branch already has its own step.
        var wave = RotationDefinition.Aphrodon.Steps.First(step => step.Id == "wave-3");
        Assert.Equal("wave-3-special", RotationDefinition.Aphrodon.SectionId(wave, "agris"));
        Assert.Equal("wave-3", RotationDefinition.Aphrodon.SectionId(wave, "hog"));
        var debris = RotationDefinition.EventHorizon.Steps.First(step => step.Id == "wormhole-2-debris");
        Assert.Equal("wormhole-2-debris", RotationDefinition.EventHorizon.SectionId(debris, "debris"));
    }

    [Fact]
    public void SpecialEventRotationsCountButFormTheirOwnComparisonPool()
    {
        var tracker = new RotationPlatform(Definition());
        Run(tracker, 0, 1.5, "a");
        Message(tracker, "start", 100);
        Message(tracker, "s", 110);
        var replacing = tracker.Snapshot(Epoch.AddSeconds(111));
        Assert.True(replacing.SpecialEventActive);
        Assert.Equal(1, replacing.SpecialEvents);
        Message(tracker, "b", 120);
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(121)).SpecialEventActive);
        Message(tracker, "afk", 130);
        Message(tracker, "end", 140);

        var runs = tracker.DrainCompleted();
        Assert.All(runs, run => Assert.Equal("complete", run.Run.Outcome));
        Assert.Contains(runs[1].Run.Sections, section => section.Id == "a-special");
        Assert.DoesNotContain(runs[0].Run.Sections, section => section.Id == "a-special");

        var snapshot = tracker.Snapshot(Epoch.AddSeconds(150));
        Assert.True(snapshot.SupportsSpecialEvents);
        Assert.Equal(40, snapshot.Best!.Duration);
        Assert.Equal(2, snapshot.Completed);
        Assert.Equal(60, snapshot.WithoutSpecialEvents!.Best!.Duration);
        Assert.Equal(1, snapshot.WithoutSpecialEvents.Completed);
    }

    [Fact]
    public void TheSessionCountsEverySpecialEventAndTheSettingSwitchesTheComparison()
    {
        var tracker = new RotationPlatform(Definition(LootSpotCatalog.AphrodonId) with
        {
            SpecialMessages = RotationDefinition.Aphrodon.SpecialMessages,
            SpecialStartMessages = RotationDefinition.Aphrodon.SpecialStartMessages,
            Steps = [new("a", ["a", "agris"]), new("b", ["b"]), new("afk", ["afk"], Afk: true)],
        });
        using var monitor = new RotationMonitor(spot => spot == LootSpotCatalog.AphrodonId ? new PlatformProfile(tracker) : null);
        monitor.Snapshot(Epoch, LootSpotCatalog.AphrodonId);
        Run(tracker, 0, 1.5, "a");
        Run(tracker, 100, 1, "agris");
        // A running rotation with an Agris wave already counts.
        Message(tracker, "start", 200);
        Message(tracker, "agris", 210);

        var all = monitor.Snapshot(Epoch.AddSeconds(215), LootSpotCatalog.AphrodonId);
        Assert.Equal(2, all.SessionSpecialEvents);
        // Both finished rotations and the running one, which already has its Agris wave.
        Assert.Equal([false, true, true], all.SessionRotations.Select(rotation => rotation.Special));
        Assert.Equal(["complete", "complete", "active"], all.SessionRotations.Select(rotation => rotation.Outcome));
        Assert.Equal(40, all.Best!.Duration);
        Assert.False(all.ExcludesSpecialEvents);

        var regular = monitor.Snapshot(Epoch.AddSeconds(215), LootSpotCatalog.AphrodonId, includeSpecialEvents: false);
        Assert.True(regular.ExcludesSpecialEvents);
        Assert.Equal(60, regular.Best!.Duration);
        Assert.Equal(1, regular.Completed);
        Assert.Equal(2, regular.SessionSpecialEvents);
    }

    [Fact]
    public void SpecialEventModulesShowTheSessionCountAndRate()
    {
        var state = new TrackerState
        {
            SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, SpotId = LootSpotCatalog.EventHorizonId,
            Elapsed = TimeSpan.FromMinutes(90),
            Rotation = new() { SpotId = LootSpotCatalog.EventHorizonId, SupportsSpecialEvents = true, SessionSpecialEvents = 3,
                SessionRotations = [new(500, 10, Special: true), new(400, 10), new(410, 10)] },
        };
        var metrics = new OverlayMetrics().Update(state, new() { UiLanguage = "de" }).Metrics;
        Assert.Equal(("Special Events", "3", "In dieser Session"), (metrics["special-events"].Label, metrics["special-events"].Value, metrics["special-events"].Detail));
        Assert.Contains("zusätzlich oder ersetzend", metrics["special-events"].Tooltip);
        Assert.Equal(("Special Events / h", "2,0", "3 in 01:30:00"),
            (metrics["special-events-hour"].Label, metrics["special-events-hour"].Value, metrics["special-events-hour"].Detail));
        Assert.Equal(("8,1", "Ø 7:27 · letzte 3"), (metrics["rotations-hour"].Value, metrics["rotations-hour"].Detail));

        // Without special events, the tempo follows the regular rotations only.
        var regular = new OverlayMetrics().Update(state, new() { UiLanguage = "de", IncludeSpecialEventRotations = false }).Metrics;
        Assert.Equal(("8,7", "Ø 6:55 · letzte 2"), (regular["rotations-hour"].Value, regular["rotations-hour"].Detail));
        var onlySpecial = new OverlayMetrics().Update(state with { Rotation = state.Rotation with { SessionRotations = [new(500, 10, Special: true)] } },
            new() { UiLanguage = "de", IncludeSpecialEventRotations = false }).Metrics["rotations-hour"];
        Assert.Equal(("—", "Nach der ersten Rotation ohne Special Event"), (onlySpecial.Value, onlySpecial.Detail));

        var hermesia = new OverlayMetrics().Update(state with { SpotId = LootSpotCatalog.HermesiaId,
            Rotation = new() { SpotId = LootSpotCatalog.HermesiaId } }, new() { UiLanguage = "de" }).Metrics["special-events"];
        Assert.Equal(("0", "Keine Special Events an diesem Spot"), (hermesia.Value, hermesia.Detail));
        Assert.NotNull(OverlayCatalog.Find("special-events"));
        Assert.NotNull(OverlayCatalog.Find("special-events-hour"));
    }

    [Fact]
    public void SpecialEventPhasesAreMarked()
    {
        var eventHorizon = RotationPhases.Create(LootSpotCatalog.EventHorizonId, EventHorizonRotationDemo.Reference.Events,
            EventHorizonRotationDemo.Reference.Duration);
        Assert.Equal(["wormhole-2-debris", "wormhole-3-debris"], eventHorizon.Where(phase => phase.Special).Select(phase => phase.Id));

        RotationEvent[] aphrodon = [new("start", "Start", 0), new("hog", "Hog", 10), new("agris", "Agris", 60), new("afk", "AFK", 120)];
        var phases = RotationPhases.Create(LootSpotCatalog.AphrodonId, aphrodon, 150);
        Assert.Equal(["2. Agris + Scarecrow"], phases.Where(phase => phase.Special).Select(phase => phase.Name));
    }

    [Fact]
    public void RotationsWithSpecialEventsCountByDefault()
    {
        Assert.True(new AppSettings().RotationIncludeSpecialEvents);
        Assert.True(new TrackerPreferences().IncludeSpecialEventRotations);
    }

    private sealed class PlatformProfile(RotationPlatform platform) : IRotationProfileMonitor
    {
        public void Observe(Bitmap frame, DateTimeOffset at) { }
        public void Interrupt(string status) => platform.Interrupt(status);
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => platform.Snapshot(now);
        public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted() => platform.DrainCompleted();
        public RotationTimelineEntry[] DrainTimeline() => platform.DrainTimeline();
        public (DateTimeOffset StartedAt, RotationRun Run)? ActiveRun() => platform.ActiveRun();
        public void Dispose() { }
    }
}
