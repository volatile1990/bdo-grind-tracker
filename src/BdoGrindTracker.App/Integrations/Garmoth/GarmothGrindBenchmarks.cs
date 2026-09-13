using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Integrations.Garmoth;

/// <summary>
/// Public Garmoth values retrieved and verified on 2026-09-12. These are a dated
/// fallback when no fetched reference is available. Average values use Garmoth's displayed rounding;
/// High/Top are moderator benchmarks and are absent for some spots.
/// Outer Edania has no bundled reference; the provider adds verified live rows
/// when available. Unknown averages are never estimated from another spot.
/// </summary>
internal static class GarmothGrindBenchmarks
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    internal const string Conditions = "Loot-Scroll Lv.2 · ohne Agris";
    internal static IReadOnlyList<GrindBenchmark> All { get; } = Array.AsReadOnly(new[]
    {
        Create(LootSpotCatalog.AphrodonId, 213, 12_472, 13_500, 15_400, "2026-09-10"),
        Create(LootSpotCatalog.HermesiaId, 214, 12_893, 16_000, 18_000, "2026-08-06"),
        Create(LootSpotCatalog.MagaiaId, 215, 12_803, 14_000, 15_200, "2026-09-10"),
        Create(LootSpotCatalog.AresionId, 216, 13_323, 14_000, 15_500, "2026-09-10"),
        Create(LootSpotCatalog.ScalesOfJudgmentId, 217, 15_243, null, null, "2026-09-10"),
        Create(LootSpotCatalog.EventHorizonId, 218, 12_590, null, null, "2026-09-03"),
    });

    internal static GrindBenchmark? Find(string? spotId) => All.FirstOrDefault(benchmark => benchmark.SpotId == spotId);

    private static GrindBenchmark Create(string spotId, int garmothId, decimal average, decimal? high, decimal? top, string startDate)
        => new(spotId, average, high, top, VerifiedAt,
            $"https://garmoth.com/grind-tracker/best-grind-spots/{garmothId}?startDate={startDate}&endDate=2026-09-17", Conditions);
}
