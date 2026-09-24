using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

/// <summary>
/// The monster name bar at the top center names Magaia's first and third final mechanic. Texts as the Windows OCR read
/// them from the supplied recording (the Grindcrest overlay shares the area).
/// </summary>
public sealed class MagaiaNameBarTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("Grind rating Priest of the End", "priest")]
    [InlineData("Grind rating Elion's Tear Silver per 30s History", "tear")]
    [InlineData("rating g Elion's Tear Silverper", "tear")]
    [InlineData("Grind rating Knight of Famine [SJ Silver per 30s", "")]
    [InlineData("Grind rating Sinner of Blind Faith L SJ Silver per 30s. History", "")]
    [InlineData("Grind rating Aetos [SJ Silver per 30s History", "")]
    public void TheNameBarNamesOnlyTheFinalMechanicsOfTheFirstAndThirdCycle(string text, string kind)
    {
        Assert.Equal(kind.Length == 0 ? [] : [kind], MagaiaNames.Parse(text).Select(name => name.Kind));
    }

    [Fact]
    public void TheNameBarIsReadAtTheTopCenter()
    {
        // Measured on the supplied recording: "Priest of the End" at 2560 × 1440.
        var bar = RotationNameProfile.TopCenter(2560, 1440);
        Assert.Equal((896, 0, 1664, 65), (bar.Left, bar.Top, bar.Right, bar.Bottom));
        Assert.Equal("priest", Assert.Single(RotationMessageProfile.Magaia.Names!.Parse("Priest of the End")).Kind);
        Assert.Null(RotationMessageProfile.EventHorizon.Names);
    }

    [Fact]
    public async Task ANameCountsOncePerSightingWhileTheFightKeepsItInTheBar()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        var name = "Priest of the End";
        using var monitor = new BufferedRotationProfileMonitor(tracker, RotationMessageProfile.Magaia, _ => "", _ => name);
        using var frame = new Bitmap(640, 360);
        async Task Play(double from, double to)
        {
            for (var second = from; second < to; second += .5)
            {
                monitor.Observe(frame, Epoch.AddSeconds(second));
                await monitor.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }
        await Play(0, 60);
        name = "Knight of Famine";
        await Play(60, 100);
        name = "Priest of the End";
        await Play(100, 110);

        var sightings = monitor.DrainTimeline().Where(entry => entry.Type == "recognition" && entry.Kind == "priest").ToArray();
        // Read once per probe (every three seconds): the second sighting lands on the first probe after it began.
        Assert.Equal([Epoch, Epoch.AddSeconds(102)], sightings.Select(entry => entry.At));
    }

    [Fact]
    public void PriestOfTheEndPlacesARotationPickedUpInTheThirdCycle()
    {
        int Step(string id) => Array.FindIndex(RotationDefinition.Magaia.Steps, step => step.Id == id);
        int[] prayers = [Step("cycle-1-prayer"), Step("cycle-2-prayer"), Step("cycle-3-prayer")];
        string[] finalPhase = ["prayer", "knight", "knight", "knight", "doubt"];
        // Until the final phase ends without a name, the second and third cycle look the same.
        Assert.Null(RotationAlignment.Resolve(RotationDefinition.Magaia, prayers, finalPhase));
        Assert.Equal(Step("cycle-2-prayer"), RotationAlignment.Resolve(RotationDefinition.Magaia, prayers, [.. finalPhase, "afk"]));
        Assert.Equal(Step("cycle-3-prayer"), RotationAlignment.Resolve(RotationDefinition.Magaia, prayers, [.. finalPhase, "priest"]));
        Assert.Equal(Step("cycle-1-prayer"), RotationAlignment.Resolve(RotationDefinition.Magaia, prayers, [.. finalPhase, "tear"]));
    }

    [Fact]
    public void AfterAPauseTheNameOfThePriestPlacesTheRotationRightAway()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        void Message(string kind, double seconds) => tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));
        Message("start", 0);
        tracker.InterruptAt("Tracking pausiert · warte auf Rotationsstart", Epoch.AddSeconds(10));
        // Picked up in the third cycle.
        foreach (var (kind, seconds) in new[] { ("prayer", 1500.0), ("knight", 1610), ("knight", 1675), ("knight", 1739), ("doubt", 1745) })
            Message(kind, seconds);
        Assert.Null(tracker.Snapshot(Epoch.AddSeconds(1747)).AlignedAt);

        Message("priest", 1749);
        var placed = tracker.Snapshot(Epoch.AddSeconds(1750));
        Assert.Equal(("cycle-3-prayer", 0.0), (placed.AlignedSection, placed.AlignedAt!.Value));
        Assert.Equal("cycle-3-doubt", placed.CurrentPhaseId);
        // The name is no step: the run's own events stay the banners.
        Assert.DoesNotContain(placed.Events, e => e.Kind == "priest");
    }

    // Live test 24.09.2026, tracking begun in the first cycle's AFK phase at 16:24:10 (seconds from there).
    private static readonly (string Kind, double Seconds)[] BegunInTheFirstAfkPhase =
    [
        ("tear", 0), ("end", 68), ("prayer", 324), ("knight", 434), ("knight", 487), ("knight", 555), ("doubt", 563),
        ("afk", 624), ("end", 700), ("prayer", 949), ("knight", 1066), ("knight", 1113), ("knight", 1180), ("doubt", 1187),
        ("priest", 1191), ("afk", 1253), ("end", 1330),
    ];

    private static RotationPlatform PickedUp(bool lingeringName, double until)
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        tracker.Observe("start", "start", Epoch.AddSeconds(-3000));
        tracker.InterruptAt("Tracking pausiert · warte auf Rotationsstart", Epoch.AddSeconds(-2000));
        foreach (var (kind, seconds) in BegunInTheFirstAfkPhase.Where(e => e.Seconds <= until && (lingeringName || e.Seconds > 0)))
            tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));
        return tracker;
    }

    [Fact]
    public void AnElionsTearLingeringInTheAfkPhasePlacesTheNextCycleRightAway()
    {
        // The name still showed from the first final phase: the AFK end after it opens the second cycle.
        var placed = PickedUp(lingeringName: true, until: 68).Snapshot(Epoch.AddSeconds(70));
        Assert.Equal(("cycle-2", 0.0), (placed.AlignedSection, placed.AlignedAt!.Value));
    }

    [Fact]
    public void AFinalPhaseThatReachesTheAfkPhaseWithoutAnyNameIsTheSecondCycle()
    {
        // Without the lingering name: nothing is certain through the second cycle's final phase ...
        Assert.Null(PickedUp(lingeringName: false, until: 563).Snapshot(Epoch.AddSeconds(600)).AlignedAt);
        // ... until it reaches the AFK phase without Elion's Tears or Priest of the End.
        var placed = PickedUp(lingeringName: false, until: 624).Snapshot(Epoch.AddSeconds(630));
        Assert.Equal(("cycle-2", 0.0), (placed.AlignedSection, placed.AlignedAt!.Value));
        Assert.Equal("cycle-2-afk", placed.CurrentPhaseId);

        // The third cycle's Priest of the End agrees, and the rotation's end opens a clean next rotation.
        var tracker = PickedUp(lingeringName: false, until: 1330);
        var last = tracker.Snapshot(Epoch.AddSeconds(1335));
        Assert.Equal(0, last.AlignedAt);
        Assert.Null(last.AlignedSection);
        Assert.Equal("incomplete", tracker.DrainCompleted().Where(r => r.Run.Outcome != "superseded").OrderBy(r => r.StartedAt).Last().Run.Outcome);
    }

    [Fact]
    public void ANameSeenLateNeverAbortsACleanRotation()
    {
        var tracker = new RotationPlatform(RotationDefinition.Magaia);
        void Message(string kind, double seconds) => tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));
        Message("start", 0);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var offset = cycle * 630.0;
            Message("prayer", offset + 250);
            Message("knight", offset + 330); Message("knight", offset + 390); Message("knight", offset + 430);
            Message("doubt", offset + 442);
            if (cycle == 0) { Message("sacred", offset + 446); Message("tear", offset + 450); }
            Message("afk", offset + 553);
            // The name bar keeps the last target into the AFK phase, and a stray target may show a name out of place.
            if (cycle == 0) Message("tear", offset + 590);
            if (cycle == 1) Message("priest", offset + 300);
            Message("end", offset + 630);
        }

        var run = Assert.Single(tracker.DrainCompleted(), r => r.Run.Outcome != "superseded");
        Assert.Equal(("complete", 1890.0), (run.Run.Outcome, run.Run.Duration));
        Assert.Contains(RotationPhases.Create(LootSpotCatalog.MagaiaId, run.Run.Events, run.Run.Duration),
            phase => phase.Name == "Priest of the End");
    }
}
