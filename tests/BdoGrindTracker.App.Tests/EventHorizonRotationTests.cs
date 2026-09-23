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
        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(-10));
        Assert.False(tracker.Snapshot(Epoch).Synchronized);

        Assert.True(tracker.ObserveLoot(Epoch));
        Assert.Equal("Warte auf Erkennung", tracker.Snapshot(Epoch.AddSeconds(5)).Status);
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(3)));
        Replay(tracker, EventHorizonRotationDemo.Reference, until: 450);
        var afk = tracker.Snapshot(Epoch.AddSeconds(460));
        Assert.True(afk.IsAfk);
        Assert.Contains("erkannt", afk.Status);

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
        tracker.Observe("spacetime", "Ende", Epoch.AddSeconds(90));
        tracker.Observe("halted", "Geräumt", Epoch.AddSeconds(50));

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
    public void TrackingThatBeginsMidRotationContinuesAndStoresAnIncompleteRun()
    {
        var tracker = new EventHorizonRotationTracker();
        tracker.ObserveLoot(Epoch);
        tracker.Observe("halted", "Geräumt", Epoch.AddSeconds(20));
        var partial = tracker.Snapshot(Epoch.AddSeconds(30));
        Assert.True(partial.Synchronized);
        Assert.Contains(partial.Events, e => e.Kind == "halted");
        Assert.Contains("unvollständig", partial.Status);
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(40)));
        tracker.Observe("anomaly", "Wurmloch", Epoch.AddSeconds(60));
        Assert.Contains(tracker.Snapshot(Epoch.AddSeconds(61)).Events, e => e.Kind == "anomaly");

        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(300));
        Assert.All(tracker.DrainCompleted(), run => Assert.False(run.Run.EligibleForStatistics));
        Assert.Equal("AFK beendet · Warte auf Erkennung", tracker.Snapshot(Epoch.AddSeconds(301)).Status);
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(302)));
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(304)));
        Assert.True(tracker.ObserveLoot(Epoch.AddSeconds(310)));
        // A repeated confirmation at the original capture time cannot close the new run.
        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(300));
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
    public void TheEndBannerAloneIsNoMiniAfk()
    {
        var tracker = new EventHorizonRotationTracker();
        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(-10));
        tracker.ObserveLoot(Epoch);
        tracker.Observe("anomaly", "Wurmloch", Epoch.AddSeconds(28));
        tracker.Observe("halted", "Geräumt", Epoch.AddSeconds(53));
        tracker.Observe("reception", "Will_Reception", Epoch.AddSeconds(53));
        tracker.Observe("spacetime", "Ende", Epoch.AddSeconds(55));
        var state = tracker.Snapshot(Epoch.AddSeconds(80));
        Assert.DoesNotContain(state.Events, e => e.Kind is "spacetime" or "debris");
        Assert.Equal(0, state.SpecialEvents);
    }

    [Fact]
    public void TheMiddleOfAMiniAfkFillsInItsUnreadBeginning()
    {
        // A loading screen swallowed both "Debris falling" banners; their middles were read.
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        var run = Rotation(tracker, 0, EventHorizonRotationDemo.Reference, e => e.Kind != "debris");

        Assert.Equal("complete", run.Outcome);
        Assert.Equal(2, RotationDefinition.EventHorizon.SpecialEventCount(run.Events));
        var debris = run.Events.Where(e => e.Kind == "debris").ToArray();
        Assert.All(debris, e => Assert.True(e.Inferred));
        // Half a branch before the middle, but never before the phase that was already recorded.
        Assert.Equal([161.517, 341.283], debris.Select(e => Math.Round(e.Seconds, 3)));
        Assert.Contains(RotationPhases.Create(LootSpotCatalog.EventHorizonId, run.Events, run.Duration),
            phase => phase.Name == "Wurmloch 2 · Trümmer-AFK");
    }

    [Fact]
    public void TheMiddleOfAMiniAfkFillsInItsUnreadEnd()
    {
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        var run = Rotation(tracker, 0, EventHorizonRotationDemo.Reference, e => e.Kind != "spacetime" || e.Occurrence != 1);

        Assert.Equal("complete", run.Outcome);
        var end = run.Events.First(e => e.Kind == "spacetime");
        Assert.True(end.Inferred);
        Assert.Equal(181.25 + 20, end.Seconds, 3);
        // An estimated time never borders a mechanic best.
        Assert.False(RotationTimelinePresentation.IsCheckpoint(end));
    }

    [Fact]
    public void AnUnreadEndDoesNotAbortTheRotationAtTheMiddlesTimeout()
    {
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        var reference = EventHorizonRotationDemo.Reference;
        var offset = 0.0;
        for (var i = 0; i < 3; i++, offset += reference.Duration + 10) Rotation(tracker, offset, reference, _ => true);
        tracker.DrainCompleted();

        // The next wormhole opens long after twice the average second half of the mini AFK.
        var run = Rotation(tracker, offset, reference, e => e.Kind != "spacetime" || e.Occurrence != 1);
        Assert.Equal("complete", run.Outcome);
        Assert.Equal(200.65, run.Events.First(e => e.Kind == "spacetime").Seconds, 2);
        Assert.Equal(4, tracker.Snapshot(Epoch.AddSeconds(offset + reference.Duration + 1)).Completed);
    }

    [Fact]
    public void WithoutItsMiddleAnUnreadEndStillLeavesTheRotationIncomplete()
    {
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        var run = Rotation(tracker, 0, EventHorizonRotationDemo.Reference,
            e => e.Kind is not "spacetime" and not "distortion" || e.Occurrence != 1);
        Assert.Equal("incomplete", run.Outcome);
    }

    [Fact]
    public void RotationsCompareWithThoseThatHadAsManyMiniAfks()
    {
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        var twice = EventHorizonRotationDemo.Reference;
        // A faster rotation whose second wormhole had no mini AFK.
        var once = twice with { Duration = twice.Duration - 40, Events = [.. twice.Events
            .Where(e => e.Kind is not "debris" and not "distortion" and not "spacetime" || e.Occurrence != 1)
            .Select(e => e.Seconds > 161.517 ? e with { Seconds = e.Seconds - 40 } : e)] };
        Rotation(tracker, 0, twice, _ => true);
        Rotation(tracker, 600, once, _ => true);
        Assert.False(RotationDefinition.EventHorizon.MarksSpecialRotations);

        // A fresh rotation without a mini AFK yet compares with the nearest higher number.
        tracker.ObserveLoot(Epoch.AddSeconds(1200));
        var fresh = tracker.Snapshot(Epoch.AddSeconds(1210));
        Assert.Equal(1, fresh.ComparedSpecialEvents);
        Assert.Equal(once.Duration, fresh.Best!.Duration, 3);

        foreach (var e in twice.Events.Where(e => e.Kind is not "start" && e.Seconds <= 345))
            tracker.Observe(e.Kind, e.Label, Epoch.AddSeconds(1200 + e.Seconds));
        var second = tracker.Snapshot(Epoch.AddSeconds(1200 + 346));
        Assert.Equal(2, second.SpecialEvents);
        Assert.Equal(2, second.ComparedSpecialEvents);
        Assert.Equal(twice.Duration, second.Best!.Duration, 3);
    }

    [Fact]
    public void CompletedRotationsSurviveARestartAsBestTime()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-event-horizon.json");
        try
        {
            var tracker = new EventHorizonRotationTracker(path);
            tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(-10));
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
        Assert.Equal("Warte auf Erkennung", state.Status);
    }

    // One rotation from its first loot to the AFK end, begun after the previous rotation's AFK end.
    private static RotationRun Rotation(RotationPlatform tracker, double offset, RotationRun run, Func<RotationEvent, bool> keep)
    {
        tracker.Observe("end", "AFK-Ende", Epoch.AddSeconds(offset - 10));
        tracker.ObserveLoot(Epoch.AddSeconds(offset));
        foreach (var e in run.Events.Where(e => e.Kind != "start" && keep(e)))
            tracker.Observe(e.Kind, e.Label, Epoch.AddSeconds(offset + e.Seconds));
        return tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").Last().Run;
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
