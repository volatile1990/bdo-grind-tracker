using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task LiveStateUsesTheDetectedSpotReferenceAcrossPauseAndNewSession()
    {
        await using var fixture = new Fixture(autoUpload: false);
        Assert.Null(fixture.Service.State.GrindBenchmark);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 16_000));
        var live = fixture.Service.State;
        Assert.Equal(LootSpotCatalog.HermesiaId, live.SpotId);
        Assert.Equal(GarmothGrindBenchmarks.Find(live.SpotId), live.GrindBenchmark);
        Assert.Equal(GrindRatingTier.High, GrindRatingEvaluator.Evaluate(live.SpotId, 16_000,
            live.Elapsed, live.GrindBenchmark).Tier);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(live.GrindBenchmark, fixture.Service.State.GrindBenchmark);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.GrindBenchmark);
        Assert.Empty(fixture.Service.State.Loot.Totals);
    }

    [Fact]
    public async Task SpotChangesNeverReuseThePreviousBenchmark()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var old = fixture.Service.State.GrindBenchmark;
        Assert.Equal(LootSpotCatalog.HermesiaId, old?.SpotId);
        SetField(fixture.Service, "_sessionSpotId", LootSpotCatalog.MagaiaId);
        fixture.Service.RefreshPendingState();
        Assert.Equal(LootSpotCatalog.MagaiaId, fixture.Service.State.GrindBenchmark?.SpotId);
        Assert.NotEqual(old, fixture.Service.State.GrindBenchmark);
        SetField(fixture.Service, "_sessionSpotId", "unknown");
        fixture.Service.RefreshPendingState();
        Assert.Null(fixture.Service.State.GrindBenchmark);
    }
}
