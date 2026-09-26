using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task TheDemoIsTheRecordedMagaiaSessionWithEveryPartOfTheLiveSession()
    {
        await using var fixture = new Fixture();

        await fixture.Service.SetDemoAsync(true);

        var state = fixture.Service.State;
        Assert.True(state.IsDemo);
        Assert.False(state.HasSession);
        Assert.Equal((LootSpotCatalog.MagaiaId, "shai"), (state.SpotId, state.CharacterClassId));
        Assert.Equal(MagaiaDemoSession.Elapsed, state.Elapsed);
        Assert.Equal(MagaiaDemoSession.Totals[MagaiaDemoSession.Trash], state.Loot.Totals[MagaiaDemoSession.Trash]);
        Assert.Equal(MagaiaDemoSession.DropHistory.Count, state.DropHistory.Count);
        Assert.Equal(MagaiaDemoSession.Rotations.Length, state.Rotation.SessionRotations.Count);
        // The rotations keep their place on the session's time axis.
        Assert.Equal(MagaiaDemoSession.Rotations.Select(rotation => Math.Round(rotation.StartedAt, 1)),
            state.Rotation.SessionRotations.Select(timing => Math.Round(timing.StartedAfter!.Value.TotalSeconds, 1)));
        Assert.Equal(37, state.Buffs!.Consumptions.Count);
        Assert.Equal((MagaiaDemoSession.Ap, MagaiaDemoSession.Dp), (state.CombatStats.Ap, state.CombatStats.Dp));
        Assert.Equal(MagaiaDemoSession.ExperienceGainedPercentagePoints, state.ExperienceGainedPercentagePoints);
        Assert.Equal(MagaiaDemoSession.AgrisActiveDuration, state.AgrisActiveDuration);
        Assert.Contains(MagaiaDemoSession.Date, state.Status);

        await fixture.Service.SetDemoAsync(false);

        var cleared = fixture.Service.State;
        Assert.False(cleared.IsDemo);
        Assert.Equal(TimeSpan.Zero, cleared.Elapsed);
        Assert.Empty(cleared.DropHistory);
        Assert.Empty(cleared.Rotation.SessionRotations);
        Assert.Null(cleared.Buffs);
    }

    [Fact]
    public async Task StartingToTrackFromTheDemoKeepsNothingOfIt()
    {
        await using var fixture = new Fixture();
        await fixture.Service.SetDemoAsync(true);

        await fixture.Service.ToggleTrackingAsync();

        var state = fixture.Service.State;
        Assert.True(state.IsRunning);
        Assert.False(state.IsDemo);
        Assert.Equal(TimeSpan.Zero, state.Elapsed);
        Assert.Empty(state.DropHistory);
        Assert.Empty(state.Rotation.SessionRotations);
        Assert.Empty(state.Loot.Totals);
        Assert.Null(state.Buffs);
    }
}
