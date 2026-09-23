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
        // The next rotation's first cycle: its orbs identify Elion's Tears and realign the cycles.
        Cycle(tracker, 1280, 600, tears: true);
        Cycle(tracker, 1880, 640, tears: false);
        Cycle(tracker, 2520, 640, tears: false);
        Rotation(tracker, 3160, 1900, 10);

        var runs = tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").ToArray();
        Assert.DoesNotContain(runs.SkipLast(1), r => r.Run.EligibleForStatistics);
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
