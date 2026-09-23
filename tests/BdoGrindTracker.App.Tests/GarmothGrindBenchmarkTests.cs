using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothGrindBenchmarkTests
{
    [Fact]
    public void BundledInnerEdaniaReferencesAreDatedAndDoNotInventHighOrTopValues()
    {
        Assert.Equal(new[] { LootSpotCatalog.AphrodonId, LootSpotCatalog.HermesiaId, LootSpotCatalog.MagaiaId,
                LootSpotCatalog.AresionId, LootSpotCatalog.ScalesOfJudgmentId, LootSpotCatalog.EventHorizonId }.Order(),
            GarmothGrindBenchmarks.All.Select(benchmark => benchmark.SpotId).Order());
        foreach (var reference in GarmothGrindBenchmarks.All)
        {
            Assert.True(GarmothCatalog.TryGetSpot(reference.SpotId, out var id));
            var source = new Uri(reference.SourceUrl);
            Assert.Equal("garmoth.com", source.Host);
            Assert.Equal($"/grind-tracker/best-grind-spots/{id}", source.AbsolutePath);
            Assert.Contains("endDate=2026-09-17", source.Query);
            Assert.Equal(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), reference.UpdatedAt);
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
    public void MagaiaMatchesTheUsersCurrentWeekScreenshotIncludingModeratorTiersAndDates()
    {
        var reference = Assert.IsType<GrindBenchmark>(GarmothGrindBenchmarks.Find(LootSpotCatalog.MagaiaId));
        Assert.Equal(12_803m, reference.AverageTrashPerHour);
        Assert.Equal(14_000m, reference.HighTrashPerHour);
        Assert.Equal(15_200m, reference.TopTrashPerHour);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), reference.UpdatedAt);
        Assert.Equal("?startDate=2026-09-10&endDate=2026-09-17", new Uri(reference.SourceUrl).Query);
    }

    [Fact]
    public async Task InMemoryPreviewSelectsItsOwnSpotReferenceAndClearsItForANewSession()
    {
        await using var preview = new PreviewTrackerSession();
        Assert.Equal(preview.State.SpotId, preview.State.GrindBenchmark?.SpotId);
        // The example session is the recorded Magaia hour: 14,308 helmets in 1:02 rate as average.
        Assert.Equal(GrindRatingTier.Average, GrindRatingEvaluator.Evaluate(preview.State.SpotId,
            preview.State.Loot.Totals[Overlay.MagaiaDemoSession.Trash], preview.State.Elapsed, preview.State.GrindBenchmark).Tier);
        await preview.NewSessionAsync();
        Assert.Null(preview.State.GrindBenchmark);
    }
}
