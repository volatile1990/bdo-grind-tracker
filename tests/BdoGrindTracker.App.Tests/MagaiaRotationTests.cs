using System.IO.Compression;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class MagaiaRotationTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Theory]
    // OCR lines read from the supplied recording, including its usual misreads.
    [InlineData("History begins to repeat itself. The sinners are sumlnoned", "")]
    [InlineData("History begins to repeat itself. The sinners are summoned.", "start")]
    [InlineData("Aetos drops a Fragrnent of Divinity. The Fragment of Divinity returns the reenacted sinner to nothingness.", "fragment")]
    [InlineData("Flarnes ot prayer rise from the brazier.", "prayer")]
    [InlineData("The Knight of Apocalypse passes judgrnenr upon history.", "knight")]
    [InlineData("The Knight of Apocalypse fixes their gaze upon the speaker.", "")]
    [InlineData("Harnes of doubt srnolder within the brazier.", "doubt")]
    [InlineData("Sacred power begins to fill the battlefield.", "sacred")]
    [InlineData("Flalnes of sin blaze atop the brazier. Judgnnent falls upon the sinners who have forsaken their faith,", "afk")]
    [InlineData("Judgrnent falls upon the sinners who have forsaken their faith.", "afk")]
    [InlineData("The history of sin begins to repeat itself once rnore.", "end")]
    [InlineData("The reen history begins to fade.", "away")]
    [InlineData("The voice Df the speaker awakens history once rnore.", "back")]
    [InlineData("You may take damage if you approach the brazier of judgment.", "")]
    [InlineData("The sin-stained history begins to crumble.", "failure")]
    public void MessagesAreRecognizedAroundTheUsualMisreads(string text, string kinds)
    {
        Assert.Equal(kinds.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(),
            MagaiaMessages.Parse(text).Select(message => message.Kind).Order());
    }

    [Fact]
    public void StackedFragmentBannersCountOneByOne()
    {
        Assert.Equal(2, MagaiaMessages.Lines("Aetos drops a Fragment of Divinity. Aetos drops a Fragment of Divinity.\nAetos drops a Fragrnent", "fragment"));
        // Three fragments within eleven seconds: every new banner line counts, the continuing ones do not.
        var times = Enumerable.Range(0, 60).Select(i => Epoch.AddSeconds(i * .5)).ToArray();
        string Text(int i)
        {
            var lines = new[] { 0, 10, 21 }.Count(start => i >= start && i < start + 14);
            return string.Join(" ", Enumerable.Repeat("Aetos drops a Fragment of Divinity.", lines));
        }
        var search = NewSearch();
        var found = new List<DateTimeOffset>();
        for (var i = 5; i < times.Length; i += 6)
            found.AddRange(search.Read(times[Math.Max(0, i - 20)..(i + 1)], j => Text(Math.Max(0, i - 20) + j)).Select(e => e.At));
        Assert.Equal([Epoch, Epoch.AddSeconds(5), Epoch.AddSeconds(10.5)], found);
    }

    [Fact]
    public void TheSuppliedRecordingIsOneRotationOfThreeCycles()
    {
        var tracker = Replay();
        var run = Assert.Single(tracker.DrainCompleted(), r => r.Run.Outcome != "superseded");

        Assert.Equal("complete", run.Run.Outcome);
        Assert.Equal(12.5, Math.Round((run.StartedAt - Epoch).TotalSeconds, 1));
        Assert.Equal(1878, Math.Round(run.Run.Duration, 1));
        Assert.Equal(16, RotationDefinition.Magaia.SpecialEventCount(run.Run.Events));
        Assert.Equal(9, run.Run.Events.Count(e => e.Kind == "knight"));
        Assert.Equal((2, 2), (run.Run.Events.Count(e => e.Kind == "away"), run.Run.Events.Count(e => e.Kind == "back")));
        // Cycle boundaries and the three final phases.
        Assert.Equal([599.5, 1240.5, 1878], run.Run.Events.Where(e => e.Kind == "end").Select(e => Math.Round(e.Seconds, 1)));
        Assert.Equal([599.5, 1240.5], run.Run.Sections.Where(s => s.Id is "cycle-2" or "cycle-3").Select(s => Math.Round(s.Start, 1)));

        // The last AFK end already opened the next rotation; it compares with every rotation, whatever its fragments.
        var next = tracker.Snapshot(Epoch.AddSeconds(1900));
        Assert.True(next.Synchronized);
        Assert.Equal(9.5, next.Elapsed, 1);
        Assert.Null(next.ComparedSpecialEvents);
        Assert.Equal(1878, next.Best!.Duration, 1);

        var phases = RotationPhases.Create(LootSpotCatalog.MagaiaId, run.Run.Events, run.Run.Duration);
        Assert.Equal(["Zyklus 1 · DPS-Check", "Zyklus 1 · Ritter 1", "Zyklus 1 · Ritter 2", "Zyklus 1 · Ritter 3",
            "Zyklus 1 · Restliche Packs", "Elion's Tears", "Zyklus 1 · AFK-Phase"], phases.Take(7).Select(phase => phase.Name));
        Assert.Equal(21, phases.Count);
        Assert.Equal(["Elion's Tears", "Unbroken Oath", "Priest of the End"],
            phases.Where(phase => phase.Id.EndsWith("-final", StringComparison.Ordinal)).Select(phase => phase.Name));
        Assert.Equal(["cycle-1", "cycle-2", "cycle-3"], phases.Select(phase => phase.Group).Distinct());
        // Short phases have no room for their own time; each cycle keeps a readable total above the band.
        Assert.Equal(["10:00", "10:41", "10:38"], phases.GroupBy(phase => phase.Group)
            .Select(group => RotationPhases.Duration(group.Last().End - group.First().Start)));
        Assert.Equal(run.Run.Duration, phases.Sum(phase => phase.End - phase.Start), 5);
    }

    [Theory]
    // The fragments fall into phases with a fixed timing: however many the current rotation has, all rotations compare.
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(20)]
    public void EveryRotationIsComparedWhateverItsFragments(int fragments)
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "Sünder beschworen", Epoch);
        Rotation(tracker, 0, 1900, 16);
        Rotation(tracker, 1900, 1860, 16);
        Rotation(tracker, 3760, 1920, 12);
        // The last AFK end opened the current rotation.
        for (var i = 0; i < fragments; i++) tracker.Observe("fragment", "Fragment", Epoch.AddSeconds(5690 + i * 15));

        var snapshot = tracker.Snapshot(Epoch.AddSeconds(6100));
        Assert.Null(snapshot.ComparedSpecialEvents);
        Assert.Equal(1860, snapshot.Best!.Duration);
        Assert.Equal(3, snapshot.Completed);
        Assert.Equal(fragments, snapshot.SpecialEvents);
        // No rotation counts as special, and none is excluded.
        Assert.Equal(1860, snapshot.WithoutSpecialEvents!.Best!.Duration);
        Assert.Equal(3, snapshot.WithoutSpecialEvents.Completed);
    }

    [Fact]
    public void LootNeverStartsARotationAndTheFirstBannerStartedOneCounts()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        // Live session 22.09.2026: tracking started at the spot, the first loot arrived before any banner.
        Assert.False(tracker.ObserveLoot(Epoch));
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(5)).Synchronized);

        tracker.Observe("start", "Sünder beschworen", Epoch.AddSeconds(30));
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(35)).Synchronized);
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(40)));
        Rotation(tracker, 30, 1900, 6);

        var run = Assert.Single(tracker.DrainCompleted(), r => r.Run.Outcome != "superseded");
        Assert.Equal("complete", run.Run.Outcome);
        Assert.Equal(Epoch.AddSeconds(30), run.StartedAt);
        Assert.Equal(1900, run.Run.Duration, 1);
        // It is the reference right away, without a second rotation.
        Assert.Equal(1900, tracker.Snapshot(Epoch.AddSeconds(1940)).Best!.Duration, 1);
    }

    [Fact]
    public void TheFinalPhaseCanBeginBeforeTheThirdKnightFalls()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "Sünder beschworen", Epoch);
        Rotation(tracker, 0, 1900, 6, knights: 2);

        var run = Assert.Single(tracker.DrainCompleted(), r => r.Run.Outcome != "superseded");
        Assert.Equal("complete", run.Run.Outcome);
        Assert.Equal(6, run.Run.Events.Count(e => e.Kind == "knight"));
        // The phase up to the third knight simply runs until the final phase begins.
        var phases = RotationPhases.Create(LootSpotCatalog.MagaiaId, run.Run.Events, run.Run.Duration);
        Assert.DoesNotContain(phases, phase => phase.Name == "Zyklus 1 · Restliche Packs");
        Assert.Equal(["Zyklus 1 · Ritter 3", "Elion's Tears"],
            phases.SkipWhile(phase => phase.Name != "Zyklus 1 · Ritter 3").Take(2).Select(phase => phase.Name));
    }

    [Fact]
    public void TrackingThatBeginsInALaterCycleRealignsAtElionsTears()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        // Tracking begins in the second cycle: the loot start cannot know which cycle it is.
        tracker.ObserveLoot(Epoch);
        Cycle(tracker, 0, 640, tears: false);
        Cycle(tracker, 640, 640, tears: false);
        // The next rotation's first cycle: its orbs identify Elion's Tears. Two cycles without them before it can only
        // have been the second and third, so the AFK end at 1280 was the rotation's end and started a full rotation.
        Cycle(tracker, 1280, 600, tears: true);
        Cycle(tracker, 1880, 640, tears: false);
        Cycle(tracker, 2520, 640, tears: false);
        Rotation(tracker, 3160, 1900, 10);

        var runs = tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").OrderBy(r => r.StartedAt).ToArray();
        Assert.False(runs[0].Run.EligibleForStatistics);
        Assert.Equal("cycle-2-prayer", runs[0].Run.Sections[0].Id);
        Assert.Equal((Epoch.AddSeconds(1280), "complete"), (runs[1].StartedAt, runs[1].Run.Outcome));
        Assert.Equal(1880, runs[1].Run.Duration, 1);
        Assert.Equal("complete", runs[^1].Run.Outcome);
        Assert.Equal(1900, runs[^1].Run.Duration, 1);
    }

    [Fact]
    public void TheDemoAndTheProfileCoverMagaia()
    {
        Assert.Contains(LootSpotCatalog.MagaiaId, RotationProfiles.SupportedSpotIds);
        Assert.False(RotationDefinition.Magaia.MarksSpecialRotations);
        Assert.Equal(RotationMessageProfile.Hermesia.Crop(2560, 1440), RotationMessageProfile.Magaia.Crop(2560, 1440));
        Assert.Equal(1878, MagaiaRotationDemo.Reference.Duration);
        Assert.Equal(16, RotationDefinition.Magaia.SpecialEventCount(MagaiaRotationDemo.Reference.Events));
        var demo = MagaiaRotationDemo.At(1000);
        Assert.Equal(12, demo.SpecialEvents);
        Assert.Contains(RotationPhases.Create(LootSpotCatalog.MagaiaId, demo.Events, demo.Elapsed), phase => phase.Name == "Zyklus 2 · Ritter 2");
        Assert.Equal(RotationPhases.SpecialColor, RotationPhases.MarkerStroke(LootSpotCatalog.MagaiaId, new("fragment", "Fragment", 71), null, true));
        Assert.Equal("#E87C79", RotationPhases.MarkerStroke(LootSpotCatalog.MagaiaId, new("away", "Tot", 71), null, true));
    }

    [Fact]
    public void AFinalPhaseThatBeginsHalfAMinuteAfterTheLastKnightKeepsTheRotation()
    {
        // Live session 23.09.2026: the day's first rotations saw the final phase of the third cycle 5 to 9 seconds after
        // the last knight. Twice that average aborted every later rotation whose final phase took 20 to 35 seconds.
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "Sünder beschworen", Epoch);
        foreach (var (gap, i) in new double[] { 4.9, 7.2, 7.8, 8.5 }.Select((gap, i) => (gap, i))) PlayedRotation(tracker, i, gap);
        PlayedRotation(tracker, 4, 35);

        var runs = tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").ToArray();
        Assert.Equal(5, runs.Length);
        Assert.All(runs, run => Assert.Equal("complete", run.Run.Outcome));
    }

    [Fact]
    public void AfterATimeoutTheFinalPhaseResumesInItsOwnCycle()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "Sünder beschworen", Epoch);
        for (var i = 0; i < 4; i++) PlayedRotation(tracker, i, 7);
        // A real stall past the average plus a minute aborts the rotation in its third cycle.
        PlayedRotation(tracker, 4, 100);
        var stalled = tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").ToArray();
        Assert.Equal("aborted", stalled[^2].Run.Outcome);
        Assert.Contains("cycle-3-knight-3", stalled[^2].Run.Reason);
        // The final phase that follows resumes in the third cycle, not the first: the AFK end is the rotation's end and
        // opens the next rotation cleanly.
        Assert.Equal("incomplete", stalled[^1].Run.Outcome);
        Assert.Equal("cycle-3-afk", stalled[^1].Run.Sections[^1].Id);
        PlayedRotation(tracker, 5, 7);
        Assert.Equal("complete", tracker.DrainCompleted().Last(r => r.Run.Outcome != "superseded").Run.Outcome);
    }

    [Fact]
    public void AfterATimeoutTheCurrentRowMeetsTheReferencesThirdCycle()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "Sünder beschworen", Epoch);
        for (var i = 0; i < 4; i++) PlayedRotation(tracker, i, 7);
        var clean = Shown(tracker, Epoch.AddSeconds(4 * RotationSeconds + 300));
        Assert.Equal(0, clean.AlignedAt);
        Assert.False(RotationCurrentRow.Create(clean, clean.Best).HasTrackingError);

        // The third cycle stalls past its timeout; its final phase resumes the third cycle.
        PlayedRotation(tracker, 4, 100, until: 2 * CycleSeconds + 530);
        var picked = Shown(tracker, Epoch.AddSeconds(4 * RotationSeconds + 2 * CycleSeconds + 540));
        Assert.Equal("partial", picked.TrackingState);
        Assert.Equal(("cycle-3-doubt", 0.0), (picked.AlignedSection, picked.AlignedAt!.Value));

        // Its first certain moment meets the same moment of the reference; what came before is a tracking error.
        var row = RotationCurrentRow.Create(picked, picked.Best);
        var reference = picked.Best!.Sections.Single(section => section.Id == "cycle-3-doubt").Start;
        Assert.Equal(2 * CycleSeconds + 437, reference, 3);
        Assert.Equal(reference, row.Offset, 3);
        Assert.Equal(reference, row.ErrorEnd, 3);
        Assert.Equal(reference + 10, row.End, 3);
        var phase = Assert.Single(row.Phases);
        Assert.Equal(("Priest of the End", "cycle-3"), (phase.Name, phase.Group));
        Assert.Equal(reference, phase.Start, 3);
        Assert.True(RotationTimelinePresentation.Extent(picked, "best") >= row.End);
        // The ideal keeps the sections, so the same holds against it.
        Assert.Contains(picked.Ideal!.Sections, section => section.Id == "cycle-3-doubt");
    }

    [Fact]
    public void APickedUpRotationIsATrackingErrorUntilAMessageFitsExactlyOnePhase()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "Sünder beschworen", Epoch);
        for (var i = 0; i < 4; i++) PlayedRotation(tracker, i, 7);
        tracker.Interrupt("Tracking pausiert");
        var at = 4 * RotationSeconds + 442;
        // "Flames of doubt" opens the final phase of every cycle: it says nothing about which one.
        tracker.Observe("doubt", "Schlussphase", Epoch.AddSeconds(at));
        var unsure = Shown(tracker, Epoch.AddSeconds(at + 2));
        Assert.Null(unsure.AlignedAt);
        var row = RotationCurrentRow.Create(unsure, unsure.Best);
        Assert.Empty(row.Phases);
        Assert.Equal(unsure.Elapsed, row.ErrorEnd, 3);

        // Elion's Tears exists in the first cycle only: the final phase before it was the first cycle's too.
        tracker.Observe("sacred", "Elion's Tears", Epoch.AddSeconds(at + 4));
        var sure = Shown(tracker, Epoch.AddSeconds(at + 20));
        Assert.Equal(("cycle-1-doubt", 0.0), (sure.AlignedSection, sure.AlignedAt!.Value));
        row = RotationCurrentRow.Create(sure, sure.Best);
        var reference = sure.Best!.Sections.Single(section => section.Id == "cycle-1-doubt").Start;
        Assert.Equal(reference, row.ErrorEnd, 3);
        Assert.Equal(("Elion's Tears", reference), (row.Phases[0].Name, row.Phases[0].Start));
    }

    [Fact]
    public void AfterAPauseTheOrderOfTheMessagesPlacesTheRotationInItsSecondCycle()
    {
        // Live session 24.09.2026: cycle 1, a short pause right after cycle 2 began, then cycles 2 and 3. Their final
        // phases have no banner of their own, so no single message tells which cycle it is.
        var tracker = References();
        var begun = 4 * RotationSeconds;
        PlayedRotation(tracker, 4, 7, until: CycleSeconds);
        tracker.InterruptAt("Tracking pausiert · warte auf Rotationsstart", Epoch.AddSeconds(begun + CycleSeconds + 6));
        PlayedRotation(tracker, 4, 7, from: CycleSeconds + 200, until: CycleSeconds + 500);
        Assert.Null(Shown(tracker, Epoch.AddSeconds(begun + CycleSeconds + 505)).AlignedAt);

        // The final phase reaches the AFK phase without Elion's Tears or Priest of the End: the second cycle.
        PlayedRotation(tracker, 4, 7, from: CycleSeconds + 501, until: CycleSeconds + 553);
        Assert.Equal("cycle-2-prayer", Shown(tracker, Epoch.AddSeconds(begun + CycleSeconds + 555)).AlignedSection);

        // Priest of the End in the next final phase agrees.
        PlayedRotation(tracker, 4, 7, from: CycleSeconds + 554, until: 2 * CycleSeconds + 553);
        var picked = Shown(tracker, Epoch.AddSeconds(begun + 2 * CycleSeconds + 560));
        Assert.Equal(("cycle-2-prayer", 0.0), (picked.AlignedSection, picked.AlignedAt!.Value));
        var row = RotationCurrentRow.Create(picked, picked.Best);
        Assert.Equal(CycleSeconds + 250, row.ErrorEnd, 3);
        Assert.Equal(["Zyklus 2 · Ritter 1", "Unbroken Oath", "Zyklus 3 · DPS-Check"],
            row.Phases.Select(phase => phase.Name).Where(name => name is "Zyklus 2 · Ritter 1" or "Unbroken Oath" or "Zyklus 3 · DPS-Check"));
        Assert.Empty(row.Gaps);

        // The rotation's real end closes it and opens the next one cleanly.
        tracker.Observe("end", "end", Epoch.AddSeconds(begun + RotationSeconds));
        PlayedRotation(tracker, 5, 7);
        var runs = tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").OrderBy(r => r.StartedAt).ToArray();
        var afterPause = runs.Single(r => r.StartedAt == Epoch.AddSeconds(begun + CycleSeconds + 250));
        Assert.Equal("cycle-2-prayer", afterPause.Run.Sections[0].Id);
        Assert.Equal(RotationSeconds - CycleSeconds - 250, afterPause.Run.Duration, 3);
        Assert.Equal("complete", runs[^1].Run.Outcome);
        Assert.Equal(Epoch.AddSeconds(begun + RotationSeconds), runs[^1].StartedAt);
    }

    [Fact]
    public void ElionsTearsPlacesARotationPickedUpAfterAResetFromItsFirstMessage()
    {
        // The spot reset during the pause; tracking resumed in a fresh first cycle whose start banner was missed.
        var tracker = References();
        var begun = 4 * RotationSeconds;
        PlayedRotation(tracker, 4, 7, until: CycleSeconds);
        tracker.InterruptAt("Tracking pausiert · warte auf Rotationsstart", Epoch.AddSeconds(begun + CycleSeconds + 6));
        var fresh = begun + CycleSeconds + 100;
        foreach (var (kind, seconds) in new[] { ("prayer", 250.0), ("knight", 330), ("knight", 390), ("knight", 430), ("doubt", 442) })
            tracker.Observe(kind, kind, Epoch.AddSeconds(fresh + seconds));
        Assert.Null(Shown(tracker, Epoch.AddSeconds(fresh + 444)).AlignedAt);

        tracker.Observe("sacred", "Elion's Tears", Epoch.AddSeconds(fresh + 446));
        var placed = Shown(tracker, Epoch.AddSeconds(fresh + 450));
        // Everything seen since the pause belongs to the first cycle, not only what came after Elion's Tears.
        Assert.Equal(("cycle-1-prayer", 0.0), (placed.AlignedSection, placed.AlignedAt!.Value));
        Assert.Equal("Zyklus 1 · Ritter 1", RotationCurrentRow.Create(placed, placed.Best).Phases[0].Name);
    }

    [Fact]
    public void APauseInTheAfkPhaseEndsWithTheRotationsEndAndACleanNextRotation()
    {
        var tracker = References();
        var begun = 4 * RotationSeconds;
        PlayedRotation(tracker, 4, 7, until: 2 * CycleSeconds + 560);
        tracker.InterruptAt("Tracking pausiert · warte auf Rotationsstart", Epoch.AddSeconds(begun + 2 * CycleSeconds + 570));
        // The first banner after the pause is the AFK end: a cycle boundary or the rotation's end.
        tracker.Observe("end", "end", Epoch.AddSeconds(begun + RotationSeconds));
        PlayedRotation(tracker, 5, 7);

        var last = tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").OrderBy(r => r.StartedAt).Last();
        Assert.Equal(Epoch.AddSeconds(begun + RotationSeconds), last.StartedAt);
        Assert.Equal("complete", last.Run.Outcome);
    }

    [Fact]
    public void ARequiredPhaseThatWasNeverSeenIsMarkedWhereItFell()
    {
        var tracker = References();
        var begun = 4 * RotationSeconds;
        // The DPS check of the second cycle was not read.
        PlayedRotation(tracker, 4, 7, until: CycleSeconds + 400, unread: (cycle, kind) => cycle == 2 && kind == "prayer");
        var shown = Shown(tracker, Epoch.AddSeconds(begun + CycleSeconds + 410));
        var gap = Assert.Single(shown.MissingSections);
        Assert.Equal(("cycle-2-prayer", CycleSeconds, CycleSeconds + 330), (gap.Id, gap.Start, gap.End));
        Assert.Equal(gap.Start, Assert.Single(RotationCurrentRow.Create(shown, shown.Best).Gaps).Start);
    }

    private static RotationPlatform References()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "Sünder beschworen", Epoch);
        for (var i = 0; i < 4; i++) PlayedRotation(tracker, i, 7);
        tracker.DrainCompleted();
        return tracker;
    }

    // The monitor names the spot of the platform's snapshot.
    private static RotationMonitorSnapshot Shown(RotationPlatform tracker, DateTimeOffset at) =>
        tracker.Snapshot(at) with { SpotId = LootSpotCatalog.MagaiaId };

    private const double CycleSeconds = 630, RotationSeconds = CycleSeconds * RotationDefinition.MagaiaCycles;

    // One rotation as played on 23.09.2026, begun by the previous rotation's AFK end (or the start banner at zero). The
    // third cycle's last knight falls `lastKnight` seconds before its final phase.
    private static void PlayedRotation(RotationPlatform tracker, int number, double lastKnight, double until = double.MaxValue,
        double from = 0, Func<int, string, bool>? unread = null)
    {
        for (var cycle = 1; cycle <= RotationDefinition.MagaiaCycles; cycle++)
        {
            var offset = number * RotationSeconds + (cycle - 1) * CycleSeconds;
            void Message(string kind, double seconds)
            {
                var into = offset - number * RotationSeconds + seconds;
                if (into >= from && into <= until && unread?.Invoke(cycle, kind) != true)
                    tracker.Observe(kind, kind, Epoch.AddSeconds(offset + seconds));
            }
            Message("prayer", 250);
            Message("knight", 330); Message("knight", 390); Message("knight", 430);
            var doubt = 430 + (cycle == 3 ? lastKnight : 12);
            Message("doubt", doubt);
            if (cycle == 1) { Message("sacred", doubt + 4); Message("tear", doubt + 6); }
            // The name bar names the third cycle's final mechanic.
            if (cycle == 3) Message("priest", doubt + 4);
            Message("afk", CycleSeconds - 77);
            Message("end", CycleSeconds);
        }
    }

    // One cycle from its start to the AFK end that starts the next one.
    private static void Cycle(RotationPlatform tracker, double offset, double duration, bool tears, int fragments = 0, int knights = 3)
    {
        void Message(string kind, double seconds) => tracker.Observe(kind, kind, Epoch.AddSeconds(offset + seconds));
        for (var i = 0; i < fragments; i++) Message("fragment", 20 + i * 20);
        Message("prayer", 240);
        for (var knight = 0; knight < knights; knight++) Message("knight", 340 + knight * 60);
        Message("doubt", 470);
        // Elion's Tears repeats its orb banners during the phase.
        if (tears) { Message("sacred", 480); Message("sacred", 495); Message("sacred", 510); }
        Message("afk", duration - 77);
        Message("end", duration);
    }

    // One rotation of three cycles; the fragments are spread over its cycles.
    private static void Rotation(RotationPlatform tracker, double offset, double duration, int fragments, int knights = 3)
    {
        var first = duration - 1200;
        Cycle(tracker, offset, first, tears: true, fragments - 2 * (fragments / 3), knights);
        Cycle(tracker, offset + first, 600, tears: false, fragments / 3, knights);
        Cycle(tracker, offset + first + 600, 600, tears: false, fragments / 3, knights);
    }

    private static BufferedRotationSearch NewSearch() => new(RotationMessageProfile.Magaia.Parse, RotationMessageProfile.Magaia.GapSamples,
        RotationMessageProfile.Magaia.DuplicateSeconds, RotationMessageProfile.Magaia.CountLines, RotationMessageProfile.Magaia.CountedKinds);

    /// <summary>The recording's OCR every 0.5 s, probed every 3 s over the 10-second buffer like the live monitor.</summary>
    private static RotationPlatform Replay()
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "rotation", "magaia-video.ocr.tsv.gz"));
        using var reader = new StreamReader(new GZipStream(file, CompressionMode.Decompress));
        var samples = new List<(DateTimeOffset At, string Text)>();
        while (reader.ReadLine() is { } line)
        {
            var (seconds, text) = (line[..line.IndexOf('\t')], line[(line.IndexOf('\t') + 1)..]);
            var split = text.IndexOf(" | ", StringComparison.Ordinal);
            samples.Add((Epoch.AddSeconds(double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture)),
                split < 0 ? text : text[..split] + "\n" + text[(split + 3)..]));
        }
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        var search = NewSearch();
        for (var i = 5; i < samples.Count; i += 6)
        {
            var window = samples[Math.Max(0, i - 20)..(i + 1)];
            foreach (var (kind, label, at) in search.Read(window.Select(s => s.At).ToArray(), j => window[j].Text))
                tracker.Observe(kind, label, at);
        }
        return tracker;
    }
}
