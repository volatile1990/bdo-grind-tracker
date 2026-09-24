using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationAlignmentTests
{
    private static readonly RotationDefinition Magaia = RotationDefinition.Magaia;
    private static readonly string[] Cycle = ["prayer", "knight", "knight", "knight", "doubt", "afk", "end"];

    private static int Step(string id) => Array.FindIndex(Magaia.Steps, step => step.Id == id);
    private static int[] Prayers => [Step("cycle-1-prayer"), Step("cycle-2-prayer"), Step("cycle-3-prayer")];

    [Fact]
    public void TwoCyclesWithoutElionsTearsCanOnlyBeTheSecondAndThird()
    {
        Assert.Equal(Step("cycle-2-prayer"), RotationAlignment.Resolve(Magaia, Prayers, [.. Cycle, .. Cycle]));
    }

    [Fact]
    public void OneCycleWithoutElionsTearsLeavesTwoCandidates()
    {
        Assert.Null(RotationAlignment.Resolve(Magaia, Prayers, Cycle));
        Assert.Null(RotationAlignment.Resolve(Magaia, Prayers, ["prayer", "knight"]));
    }

    [Fact]
    public void ElionsTearsIsTheFirstCycle()
    {
        Assert.Equal(Step("cycle-1-prayer"),
            RotationAlignment.Resolve(Magaia, Prayers, ["prayer", "knight", "knight", "doubt", "sacred", "sacred"]));
    }

    [Fact]
    public void AnAfkEndFollowedByAFirstCycleWasTheRotationsEnd()
    {
        int[] candidates = [Step("cycle-2"), Step("cycle-3"), RotationAlignment.RotationEnd];
        Assert.Equal(RotationAlignment.RotationEnd,
            RotationAlignment.Resolve(Magaia, candidates, ["end", "prayer", "knight", "knight", "knight", "doubt", "sacred"]));
    }

    [Fact]
    public void EventHorizonsWormholeIsKnownOnceItsOrderAllowsOnlyOne()
    {
        var definition = RotationDefinition.EventHorizon;
        int[] waves = [.. definition.Steps.Select((step, index) => (step, index)).Where(s => s.step.Matches("anomaly")).Select(s => s.index)];
        // Picked up in the second wormhole: its expansion rules out the third, the boss rules out the first.
        Assert.Null(RotationAlignment.Resolve(definition, waves, ["anomaly", "halted", "expansion"]));
        Assert.Equal(waves[1], RotationAlignment.Resolve(definition, waves, ["anomaly", "halted", "expansion", "anomaly", "halted", "boss"]));
    }
}
