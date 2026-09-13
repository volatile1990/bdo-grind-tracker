using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationPhasesTests
{
    [Fact]
    public void ReferenceHasSixGroupsAndTwoSubphasesPerMine()
    {
        var run = HermesiaRotationDemo.Reference;
        var phases = RotationPhases.Create(LootSpotCatalog.HermesiaId, run.Events, run.Duration);
        Assert.Equal(8, phases.Count);
        Assert.Equal(6, phases.Select(p => p.Group).Distinct().Count());
        Assert.Equal(2, phases.Count(p => p.Group == "mine-1"));
        Assert.Equal(2, phases.Count(p => p.Group == "mine-2"));
        Assert.Equal(run.Duration, phases.Sum(p => p.End-p.Start), 5);
        Assert.Equal(5, run.Events.Count(e => e.Kind == "porter" && e.Seconds < phases[0].End));
    }

    [Fact]
    public void CurrentPhaseEndsAtPlayheadWithoutFuturePhases()
    {
        var run = HermesiaRotationDemo.Reference;
        var phases = RotationPhases.Create(LootSpotCatalog.HermesiaId, run.Events, 350);
        Assert.Equal("mine-2-1", phases[^1].Id);
        Assert.Equal(350, phases[^1].End);
        Assert.DoesNotContain(phases, p => p.Id == "dragon");
    }

    [Fact]
    public void UnsupportedSpotHasNoHermesiaPhases()
        => Assert.Empty(RotationPhases.Create("other", HermesiaRotationDemo.Reference.Events, 350));
}
