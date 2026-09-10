using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Integrations.Garmoth;

/// <summary>
/// Public Garmoth display values verified on 2026-09-10. These are a dated
/// reference, not a live feed. Average values use Garmoth's displayed rounding;
/// High/Top are moderator benchmarks and are absent for some spots.
/// </summary>
internal static class GarmothGrindBenchmarks
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
    internal const string Conditions = "Loot-Scroll Lv.2 · ohne Agris";
    internal static IReadOnlyList<GrindBenchmark> All { get; } = Array.AsReadOnly(new[]
    {
        Create(LootSpotCatalog.AphrodonId, 213, 12_144, 13_500, 16_300, "2026-08-06"),
        Create(LootSpotCatalog.HermesiaId, 214, 12_837, 16_000, 18_000, "2026-08-06"),
        Create(LootSpotCatalog.MagaiaId, 215, 13_946, 16_300, 18_500, "2026-08-13"),
        Create(LootSpotCatalog.AresionId, 216, 12_339, 13_100, 14_100, "2026-08-20"),
        Create(LootSpotCatalog.ScalesOfJudgmentId, 217, 13_535, null, null, "2026-08-20"),
        Create(LootSpotCatalog.EventHorizonId, 218, 12_267, null, null, "2026-09-03"),
    });

    internal static GrindBenchmark? Find(string? spotId) => All.FirstOrDefault(benchmark => benchmark.SpotId == spotId);

    private static GrindBenchmark Create(string spotId, int garmothId, decimal average, decimal? high, decimal? top, string startDate)
        => new(spotId, average, high, top, VerifiedAt,
            $"https://garmoth.com/grind-tracker/best-grind-spots/{garmothId}?startDate={startDate}&endDate=2026-09-10", Conditions);
}
