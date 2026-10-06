using System.Text.Json;
using System.Text.Json.Serialization;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Integrations.Garmoth;

/// <summary>
/// Public Garmoth values retrieved and verified anonymously. These are a dated
/// fallback when no fetched reference is available. Average values use Garmoth's displayed rounding;
/// High/Top are moderator benchmarks and are absent for some spots.
/// The original six Inner Edania trash references retain their 2026-09-12 date.
/// Rare rates and other supported spots use the public 2026-10-02 snapshot.
/// Unknown averages are never estimated from another spot.
/// </summary>
internal static class GarmothGrindBenchmarks
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, GrindBenchmark> Current = LoadCurrent();
    internal const string Conditions = "Loot-Scroll Lv.2 · ohne Agris";
    internal const string NoScrollConditions = "Keine Loot-Scroll-Boni · ohne Agris";
    internal static IReadOnlyList<GrindBenchmark> All { get; } = BuildAll();
    internal static string ConditionsForSpot(string spotId) =>
        GarmothCatalog.RareDropRateScalingApplies(spotId) ? Conditions : NoScrollConditions;

    private static IReadOnlyList<GrindBenchmark> BuildAll()
    {
        var original = new[]
        {
            Create(LootSpotCatalog.AphrodonId, 213, 12_472, 13_500, 15_400, "2026-09-10"),
            Create(LootSpotCatalog.HermesiaId, 214, 12_893, 16_000, 18_000, "2026-08-06"),
            Create(LootSpotCatalog.MagaiaId, 215, 12_803, 14_000, 15_200, "2026-09-10"),
            Create(LootSpotCatalog.AresionId, 216, 13_323, 14_000, 15_500, "2026-09-10"),
            Create(LootSpotCatalog.ScalesOfJudgmentId, 217, 15_243, null, null, "2026-09-10"),
            Create(LootSpotCatalog.EventHorizonId, 218, 12_590, null, null, "2026-09-03"),
        };
        return Array.AsReadOnly(original.Concat(Current.Values.Where(reference =>
            original.All(old => old.SpotId != reference.SpotId))).ToArray());
    }

    internal static GrindBenchmark? Find(string? spotId) => All.FirstOrDefault(benchmark => benchmark.SpotId == spotId);

    private static GrindBenchmark Create(string spotId, int garmothId, decimal average, decimal? high, decimal? top, string startDate)
    {
        var rare = Current.GetValueOrDefault(spotId);
        return new(spotId, average, high, top, VerifiedAt,
            $"https://garmoth.com/grind-tracker/best-grind-spots/{garmothId}?startDate={startDate}&endDate=2026-09-17", Conditions)
        {
            RareDropHourlyRates = rare?.RareDropHourlyRates,
            RareDropReferenceTrashPerHour = rare?.RareDropReferenceTrashPerHour,
            RareDropRateScalingApplies = GarmothCatalog.RareDropRateScalingApplies(spotId),
            RareDropUpdatedAt = rare?.UpdatedAt,
            RareDropSourceUrl = rare?.SourceUrl,
        };
    }

    private static IReadOnlyDictionary<string, GrindBenchmark> LoadCurrent()
    {
        using var stream = typeof(GrindBenchmark).Assembly.GetManifestResourceStream("BdoGrindTracker.App.GarmothBenchmarks.json")
            ?? throw new InvalidDataException("Missing public Garmoth reference snapshot.");
        var data = JsonSerializer.Deserialize<BundledData>(stream, new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        }) ?? throw new InvalidDataException("Invalid public Garmoth reference snapshot.");
        if (data.SchemaVersion != 1 || data.CapturedAt <= DateTimeOffset.UnixEpoch ||
            data.NoScrollSpotIds is null || !data.NoScrollSpotIds.SequenceEqual(new[] { 32, 89, 112, 113, 149, 150 }) ||
            data.Benchmarks is null || data.Benchmarks.Length > GarmothCatalog.SupportedSpotCount)
            throw new InvalidDataException("Invalid public Garmoth reference provenance.");
        var result = new Dictionary<string, GrindBenchmark>(StringComparer.Ordinal);
        foreach (var row in data.Benchmarks)
        {
            if (!GarmothCatalog.TryGetLocalSpotId(row.GarmothSpotId, out var spotId) || row.AverageTrashPerHour <= 0 ||
                !Uri.TryCreate(row.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https" ||
                source.Host != "garmoth.com" || source.AbsolutePath != $"/grind-tracker/best-grind-spots/{row.GarmothSpotId}" ||
                row.RareDropHourlyRates is null) throw new InvalidDataException("Invalid bundled Garmoth spot.");
            var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (key, rate) in row.RareDropHourlyRates)
                if (rate >= 0 && GarmothCatalog.TryGetRareDropNameForSpot(spotId, key, out var name))
                    rates.Add(name, rate);
            var reference = new GrindBenchmark(spotId, row.AverageTrashPerHour, row.HighTrashPerHour,
                row.TopTrashPerHour, data.CapturedAt, row.SourceUrl, ConditionsForSpot(spotId))
            {
                RareDropHourlyRates = rates.Count == 0 ? null : rates,
                RareDropReferenceTrashPerHour = rates.Count == 0 ? null : row.AverageTrashPerHour,
                RareDropRateScalingApplies = GarmothCatalog.RareDropRateScalingApplies(spotId),
                RareDropUpdatedAt = data.CapturedAt,
                RareDropSourceUrl = row.SourceUrl,
            };
            if (GrindRatingEvaluator.Evaluate(spotId, 0, TimeSpan.FromHours(1), reference).Tier == GrindRatingTier.Unavailable)
                throw new InvalidDataException("Invalid bundled Garmoth thresholds.");
            result.Add(spotId, reference);
        }
        return result;
    }

    private sealed record BundledData(int SchemaVersion, DateTimeOffset CapturedAt, int[] NoScrollSpotIds, BundledRow[] Benchmarks);
    private sealed record BundledRow(int GarmothSpotId, decimal AverageTrashPerHour, decimal? HighTrashPerHour,
        decimal? TopTrashPerHour, string SourceUrl, Dictionary<string, decimal> RareDropHourlyRates);
}
