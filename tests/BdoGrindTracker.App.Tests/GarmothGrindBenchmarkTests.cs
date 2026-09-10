using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothGrindBenchmarkTests
{
    [Fact]
    public void EverySupportedSpotHasADatedOfficialReferenceWithNoInventedHighOrTopValues()
    {
        Assert.Equal(LootSpotCatalog.Spots.Select(spot => spot.Id).Order(),
            GarmothGrindBenchmarks.All.Select(benchmark => benchmark.SpotId).Order());
        foreach (var reference in GarmothGrindBenchmarks.All)
        {
            Assert.True(GarmothCatalog.TryGetSpot(reference.SpotId, out var id));
            var source = new Uri(reference.SourceUrl);
            Assert.Equal("garmoth.com", source.Host);
            Assert.Equal($"/grind-tracker/best-grind-spots/{id}", source.AbsolutePath);
            Assert.Contains("endDate=2026-09-10", source.Query);
            Assert.Equal(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), reference.UpdatedAt);
            Assert.Equal("Loot-Scroll Lv.2 · ohne Agris", reference.Conditions);
            var result = GrindRatingEvaluator.Evaluate(reference.SpotId, (long)reference.AverageTrashPerHour,
                TimeSpan.FromHours(1), reference);
            Assert.Equal(GrindRatingTier.Average, result.Tier);
        }
        foreach (var id in new[] { LootSpotCatalog.ScalesOfJudgmentId, LootSpotCatalog.EventHorizonId })
        {
            var reference = Assert.IsType<GrindBenchmark>(GarmothGrindBenchmarks.Find(id));
            Assert.Null(reference.HighTrashPerHour);
            Assert.Null(reference.TopTrashPerHour);
        }
        Assert.Null(GarmothGrindBenchmarks.Find(null));
        Assert.Null(GarmothGrindBenchmarks.Find("unknown"));
    }

    [Fact]
    public void MagaiaMatchesTheThreeUserScreenshots()
    {
        var reference = Assert.IsType<GrindBenchmark>(GarmothGrindBenchmarks.Find(LootSpotCatalog.MagaiaId));
        Assert.Equal(13_946m, reference.AverageTrashPerHour);
        Assert.Equal(16_300m, reference.HighTrashPerHour);
        Assert.Equal(18_500m, reference.TopTrashPerHour);
    }

    [Fact]
    public async Task InMemoryPreviewSelectsItsOwnSpotReferenceAndClearsItForANewSession()
    {
        await using var preview = new PreviewTrackerSession();
        Assert.Equal(preview.State.SpotId, preview.State.GrindBenchmark?.SpotId);
        Assert.Equal(GrindRatingTier.Top, GrindRatingEvaluator.Evaluate(preview.State.SpotId,
            preview.State.Loot.Totals["Branch of Abundance"], preview.State.Elapsed, preview.State.GrindBenchmark).Tier);
        await preview.NewSessionAsync();
        Assert.Null(preview.State.GrindBenchmark);
    }
}
