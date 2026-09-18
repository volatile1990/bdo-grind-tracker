using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class EventHorizonRotationTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Theory]
    // OCR lines read from the supplied recording, including the game's deliberately glitched words.
    [InlineData("Anomaly detected: Flow violation. Removing anomaly.", "anomaly")]
    [InlineData("or]: Anomaly removal halted. Searching cause... Teleport required. ![Error]: Debris falling due to dimensional rift collapse.", "halted,debris")]
    [InlineData("![Error]: Anomaly removal halted. Searching cause... Teleport required. Anomaly Detected: Will_Reception ▶ Obtainable.", "halted,reception")]
    [InlineData("Distortion Escalated - Warn■g: Expulsion Imminent.", "distortion")]
    [InlineData("SpacetimO distorti�n detected. ![lncoming]: Loading � � from the destroyed timeline.", "spacetime")]
    // The incoming banner also follows Will_Reception; only the glitched spacetime line ends the mini AFK.
    [InlineData("Anomaly Detected: Will_Reception Obtainable. ![lncoming]: Loading from the destroyed timeline.", "reception")]
    [InlineData("Spacetim O distortin n detected.", "spacetime")]
    [InlineData("Expansion;[Create]=Wormhole acti○ted.", "expansion")]
    [InlineData("Hadum Vuhura... Kaheliak.", "boss")]
    [InlineData("Temporary damage to dimensional rift by ■■■■. ![Warning]: Falling debris.", "boss-kill")]
    [InlineData("Searching timeline... ▶ Reset: Reconstruct Space (Execute)", "end")]
    public void MessagesAreRecognizedAroundTheGlitchedWords(string text, string kinds)
    {
        Assert.Equal(kinds.Split(',').Order(), EventHorizonMessages.Parse(text).Select(message => message.Kind).Order());
    }

    [Fact]
    public void EventHorizonReadsTheSameBannerStackAsHermesia()
    {
        foreach (var (width, height) in new[] { (2560, 1440), (1920, 1080), (3840, 2160) })
            Assert.Equal(RotationMessageProfile.Hermesia.Crop(width, height), RotationMessageProfile.EventHorizon.Crop(width, height));
        Assert.Equal(7, RotationMessageProfile.EventHorizon.GapSamples);
    }

    [Fact]
    public void TheFirstLootStartsARotationThatCompletesAfterThreeWormholesBossAndAfk()
    {
        var tracker = new EventHorizonRotationTracker();
        tracker.Observe("anomaly", "Wurmloch", Epoch.AddSeconds(-30));
        Assert.False(tracker.Snapshot(Epoch).Synchronized);

        Assert.True(tracker.ObserveLoot(Epoch));
        Assert.Equal("Erstes Pack · warte auf Wurmloch 1", tracker.Snapshot(Epoch.AddSeconds(5)).Status);
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(3)));
        Replay(tracker, EventHorizonRotationDemo.Reference, until: 450);
        var afk = tracker.Snapshot(Epoch.AddSeconds(460));
        Assert.True(afk.IsAfk);
        Assert.Equal("AFK-Phase · Uhr läuft weiter", afk.Status);

        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(EventHorizonRotationDemo.Reference.Duration));
        var completed = Assert.Single(tracker.DrainCompleted());
        Assert.Equal(Epoch, completed.StartedAt);
        Assert.Equal(EventHorizonRotationDemo.Reference.Duration, completed.Run.Duration, 3);
        Assert.Equal(EventHorizonRotationDemo.Reference.Events.Select(e => (e.Kind, Math.Round(e.Seconds, 3))),
            completed.Run.Events.Select(e => (e.Kind, Math.Round(e.Seconds, 3))));
        var finished = tracker.Snapshot(Epoch.AddSeconds(515));
        Assert.False(finished.Synchronized);
        Assert.Equal(1, finished.Completed);
        Assert.Equal(EventHorizonRotationDemo.Reference.Duration, finished.Best!.Duration, 3);

        // The next rotation begins with the next loot after the AFK end.
        Assert.True(tracker.ObserveLoot(Epoch.AddSeconds(520)));
        Assert.Equal(5, tracker.Snapshot(Epoch.AddSeconds(525)).Elapsed);
    }

    [Fact]
    public void ABannerSplitByATeleportBlackScreenCountsOnceAtItsFirstSighting()
    {
        var tracker = new EventHorizonRotationTracker();
        tracker.ObserveLoot(Epoch);
        tracker.Observe("anomaly", "Wurmloch", Epoch.AddSeconds(28));
        tracker.Observe("halted", "Geräumt", Epoch.AddSeconds(50));
        tracker.Observe("debris", "Trümmer", Epoch.AddSeconds(50));
        tracker.Observe("spacetime", "Ende", Epoch.AddSeconds(90));
        tracker.Observe("spacetime", "Ende", Epoch.AddSeconds(99));
        tracker.Observe("halted", "Geräumt", Epoch.AddSeconds(59));

        var events = tracker.Snapshot(Epoch.AddSeconds(100)).Events;
        Assert.Equal(90, Assert.Single(events, e => e.Kind == "spacetime").Seconds);
        Assert.Single(events, e => e.Kind == "halted");

        // In the buffered search, the seven-sample gap keeps the first sighting's time.
        var times = Enumerable.Range(0, 21).Select(i => Epoch.AddSeconds(i * .5)).ToArray();
        string Text(int i) => i is >= 3 and <= 5 or >= 11 ? "Anomaly removal halted." : "";
        Assert.Equal(Epoch.AddSeconds(1.5), Assert.Single(new BufferedRotationSearch(EventHorizonMessages.Parse, 7).Read(times, Text)).At);
        Assert.Equal(Epoch.AddSeconds(5.5), Assert.Single(new BufferedRotationSearch(EventHorizonMessages.Parse).Read(times, Text)).At);
    }

    [Fact]
    public void TrackingThatBeginsMidRotationWaitsForTheNextAfkEnd()
    {
        var tracker = new EventHorizonRotationTracker();
        tracker.ObserveLoot(Epoch);
        tracker.Observe("halted", "Geräumt", Epoch.AddSeconds(20));
        var partial = tracker.Snapshot(Epoch.AddSeconds(30));
        Assert.False(partial.Synchronized);
        Assert.Empty(partial.Events);
        Assert.Contains("Mitten in der Rotation", partial.Status);
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(40)));
        tracker.Observe("anomaly", "Wurmloch", Epoch.AddSeconds(60));
        Assert.Empty(tracker.Snapshot(Epoch.AddSeconds(61)).Events);

        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(300));
        Assert.Empty(tracker.DrainCompleted());
        Assert.Equal("AFK beendet · nächste Rotation startet mit dem nächsten Loot", tracker.Snapshot(Epoch.AddSeconds(301)).Status);
        Assert.True(tracker.ObserveLoot(Epoch.AddSeconds(310)));
        // A late sighting of the reset banner cannot end the new rotation before its first wormhole.
        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(312));
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(315)).Synchronized);
    }

    [Theory]
    // A window or a cut can hide single banners without shortening the measured time.
    [InlineData("boss", 1, true)]
    [InlineData("spacetime", 1, true)]
    [InlineData("halted", 2, true)]
    [InlineData("anomaly", 2, false)]
    [InlineData("boss-kill", 1, false)]
    public void RotationsNeedAllWormholeStartsTheBossKillAndTheAfkEnd(string missing, int occurrence, bool valid)
    {
        var run = EventHorizonRotationDemo.Reference with
        {
            Events = EventHorizonRotationDemo.Reference.Events.Where(e => e.Kind != missing || e.Occurrence != occurrence).ToArray(),
        };
        Assert.Equal(valid, EventHorizonRotationTracker.Valid(run));
    }

    [Fact]
    public void BannersOutOfTheirOrderInvalidateTheRun()
    {
        var events = EventHorizonRotationDemo.Reference.Events.ToArray();
        // The first activation before the first wormhole was cleared.
        var swapped = events.Select(e => e.Kind == "expansion" && e.Occurrence == 1 ? e with { Seconds = 40 } : e).OrderBy(e => e.Seconds).ToArray();
        Assert.True(EventHorizonRotationTracker.Valid(EventHorizonRotationDemo.Reference));
        Assert.False(EventHorizonRotationTracker.Valid(EventHorizonRotationDemo.Reference with { Events = swapped }));
    }

    [Fact]
    public void TheMiniAfkIsOnlyTrackedAfterFallingDebris()
    {
        var tracker = new EventHorizonRotationTracker();
        tracker.ObserveLoot(Epoch);
        tracker.Observe("anomaly", "Wurmloch", Epoch.AddSeconds(28));
        tracker.Observe("halted", "Geräumt", Epoch.AddSeconds(53));
        tracker.Observe("reception", "Will_Reception", Epoch.AddSeconds(53));
        tracker.Observe("spacetime", "Ende", Epoch.AddSeconds(55));
        tracker.Observe("distortion", "Hälfte", Epoch.AddSeconds(70));
        Assert.DoesNotContain(tracker.Snapshot(Epoch.AddSeconds(80)).Events, e => e.Kind is "spacetime" or "distortion");
    }

    [Fact]
    public void CompletedRotationsSurviveARestartAsBestTime()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-event-horizon.json");
        try
        {
            var tracker = new EventHorizonRotationTracker(path);
            tracker.ObserveLoot(Epoch);
            Replay(tracker, EventHorizonRotationDemo.Reference, until: double.MaxValue);
            Assert.Single(tracker.DrainCompleted());
            Assert.Equal(EventHorizonRotationDemo.Reference.Duration, new EventHorizonRotationTracker(path).Snapshot(Epoch).Best!.Duration, 3);
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact]
    public void PhasesFollowTheWormholesAndTheMiniAfk()
    {
        var run = EventHorizonRotationDemo.Reference;
        var phases = RotationPhases.Create(LootSpotCatalog.EventHorizonId, run.Events, run.Duration);
        Assert.Equal(["Erstes Pack", "Wurmloch 1 · Wellen", "Wurmloch 1 · Mobs", "Wurmloch 2 · Anlauf", "Wurmloch 2 · Wellen",
            "Wurmloch 2 · Trümmer-AFK", "Wurmloch 2 · Mobs", "Wurmloch 3 · Anlauf", "Wurmloch 3 · Wellen", "Wurmloch 3 · Trümmer-AFK",
            "Wurmloch 3 · Mobs", "Bosskampf", "AFK-Phase"], phases.Select(phase => phase.Name));
        Assert.Equal(["startup", "wormhole-1", "wormhole-2", "wormhole-3", "boss", "afk"], phases.Select(phase => phase.Group).Distinct());
        Assert.Equal(run.Duration, phases.Sum(phase => phase.End - phase.Start), 5);
        Assert.Equal(2, run.Events.Count(RotationTimelinePresentation.IsMarker));
        Assert.DoesNotContain(run.Events.Where(RotationTimelinePresentation.IsCheckpoint), e => e.Kind is "debris" or "reception" or "distortion");
    }

    [Fact]
    public void TheFirstLootBeforeSpotDetectionReachesTheEventHorizonProfile()
    {
        var tracker = new EventHorizonRotationTracker();
        using var monitor = new RotationMonitor(spot => spot == LootSpotCatalog.EventHorizonId
            ? new BufferedRotationProfileMonitor(tracker, RotationMessageProfile.EventHorizon, _ => "") : null);

        monitor.ObserveLoot(Epoch, null);
        var state = monitor.Snapshot(Epoch.AddSeconds(4), LootSpotCatalog.EventHorizonId);

        Assert.True(state.Synchronized);
        Assert.Equal(4, state.Elapsed);
        Assert.Contains("Erstes Pack", state.Status);
    }

    private static void Replay(EventHorizonRotationTracker tracker, RotationRun run, double until)
    {
        foreach (var e in run.Events.Where(e => e.Kind is not "start" && e.Seconds <= until))
            tracker.Observe(e.Kind, e.Label, Epoch.AddSeconds(e.Seconds));
    }
}

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task NewLootReachesTheRotationMonitorAtItsCaptureTime()
    {
        await using var fixture = new Fixture(autoUpload: false);
        var profile = new LootRecordingProfile();
        SetField(fixture.Service, "_rotationMonitor", new RotationMonitor(spot => spot == LootSpotCatalog.EventHorizonId ? profile : null));
        fixture.Begin();
        SetField(fixture.Service, "_sessionSpotId", LootSpotCatalog.EventHorizonId);

        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        var looted = fixture.Time.GetUtcNow();
        fixture.Analyzer.NextResult = new FrameAnalysisResult([new LootEventView(Guid.NewGuid(), looted, "Broken Gloves of the Void", 3)],
            [], 1, "synthetic-service-test", 0, 0, 0, 0, null) { SpotId = LootSpotCatalog.EventHorizonId };
        using (var frame = new Bitmap(2, 2))
            await fixture.Service.ProcessFrameAsync(frame, new CapturedFrameMetadata(1, looted) { CanObserveHud = true }, CancellationToken.None);
        // The same totals again are no new arrival.
        using (var frame = new Bitmap(2, 2))
            await fixture.Service.ProcessFrameAsync(frame, new CapturedFrameMetadata(2, looted.AddSeconds(1)) { CanObserveHud = true }, CancellationToken.None);

        Assert.Equal([looted], profile.Loot);
    }

    private sealed class LootRecordingProfile : IRotationProfileMonitor
    {
        public List<DateTimeOffset> Loot { get; } = [];
        public void Observe(Bitmap frame, DateTimeOffset at) { }
        public void ObserveLoot(DateTimeOffset at) => Loot.Add(at);
        public void Interrupt(string status) { }
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => new();
        public void Dispose() { }
    }
}
