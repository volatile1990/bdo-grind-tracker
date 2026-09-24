using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Tests;

/// <summary>
/// Tracking that begins in the middle of a rotation, for the spots whose repeated mechanics are no single message's
/// own: the run stays uncertain until what follows leaves one place it can have begun, then it is placed from its
/// first message on and ends with the rotation.
/// </summary>
public sealed class RotationPickedUpTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    // The recorded reference from the picked-up message on; the run's own "end" is the spot's AFK end banner.
    private static RotationPlatform PickUp(RotationDefinition definition, RotationRun reference, string kind, int occurrence,
        double until = double.MaxValue)
    {
        var tracker = new RotationPlatform(definition);
        var from = reference.Events.First(e => e.Kind == kind && e.Occurrence == occurrence).Seconds;
        foreach (var e in reference.Events.Where(e => e.Kind != "start" && e.Seconds >= from && e.Seconds <= until))
            tracker.Observe(e.Kind == "end" ? definition.AfkEndMessages[0] : e.Kind, e.Label, Epoch.AddSeconds(1000 + e.Seconds));
        return tracker;
    }

    private static double Seconds(RotationRun reference, string kind, int occurrence = 1) =>
        reference.Events.First(e => e.Kind == kind && e.Occurrence == occurrence).Seconds;

    [Fact]
    public void HermesiaPickedUpInItsFirstMineIsPlacedByTheSecondMine()
    {
        var definition = RotationDefinition.Hermesia;
        var reference = HermesiaRotationDemo.Reference;
        // The first mine: both mines open with the same banner.
        var uncertain = PickUp(definition, reference, "mine-enter", 1, until: Seconds(reference, "mine-enter", 2) - 1);
        var early = uncertain.Snapshot(Epoch.AddSeconds(1000 + Seconds(reference, "mine-enter", 2) - 1));
        Assert.True(early.Synchronized);
        Assert.Null(early.AlignedAt);
        // The offerings after Drakania stay side messages, even though this run never saw Drakania.
        Assert.DoesNotContain(uncertain.DrainCompleted(), run => run.Run.Outcome == "aborted");
        Assert.DoesNotContain("Startup", early.Status);

        // A second mine can only follow the first.
        var tracker = PickUp(definition, reference, "mine-enter", 1);
        var run = Assert.Single(tracker.DrainCompleted(), r => r.Run.Outcome != "superseded");
        Assert.Equal("incomplete", run.Run.Outcome);
        Assert.Equal(["mine-1", "mine-1-second", "mine-1-cleared", "mine-2", "mine-2-second", "dragon", "afk"],
            run.Run.Sections.Select(section => section.Id));
        Assert.Equal(reference.Duration - Seconds(reference, "mine-enter"), run.Run.Duration, 3);
        Assert.Contains(tracker.DrainTimeline(), entry => entry.Kind == "placed" && entry.Detail.EndsWith("mine-1", StringComparison.Ordinal));
    }

    [Fact]
    public void AphrodonPickedUpInItsFifthWaveIsPlacedByTheAfkPhaseFromItsFirstWave()
    {
        var definition = RotationDefinition.Aphrodon;
        var reference = AphrodonRotationDemo.Reference;
        // Nine waves open with the same banners: until the AFK phase, any of them could have been the fifth.
        var early = PickUp(definition, reference, "hog", 5, until: Seconds(reference, "afk") - 1)
            .Snapshot(Epoch.AddSeconds(1000 + Seconds(reference, "afk") - 1));
        Assert.Null(early.AlignedAt);

        var tracker = PickUp(definition, reference, "hog", 5, until: Seconds(reference, "afk"));
        var placed = tracker.Snapshot(Epoch.AddSeconds(1000 + Seconds(reference, "afk") + 1));
        // Placed from the picked-up wave on, not only from the AFK phase.
        Assert.Equal(("wave-5", 0.0), (placed.AlignedSection, placed.AlignedAt!.Value));
        Assert.Equal("afk", placed.CurrentPhaseId);
        Assert.Contains(placed.Events, e => e.Kind == "agris");
        Assert.Empty(placed.MissingSections);

        var run = Assert.Single(PickUp(definition, reference, "hog", 5).DrainCompleted(), r => r.Run.Outcome != "superseded");
        Assert.Equal("incomplete", run.Run.Outcome);
        Assert.Equal("wave-5", run.Run.Sections[0].Id);
    }

    [Fact]
    public void EventHorizonPickedUpInItsSecondWormholeIsPlacedByTheBoss()
    {
        var definition = RotationDefinition.EventHorizon;
        var reference = EventHorizonRotationDemo.Reference;
        var tracker = PickUp(definition, reference, "anomaly", 2, until: Seconds(reference, "boss"));
        var placed = tracker.Snapshot(Epoch.AddSeconds(1000 + Seconds(reference, "boss") + 1));
        Assert.Equal(("wormhole-2-waves", 0.0), (placed.AlignedSection, placed.AlignedAt!.Value));
        // The recording's mini AFKs in the second and third wormhole both count.
        Assert.Equal(2, placed.SpecialEvents);
        Assert.Empty(placed.MissingSections);
    }
}

