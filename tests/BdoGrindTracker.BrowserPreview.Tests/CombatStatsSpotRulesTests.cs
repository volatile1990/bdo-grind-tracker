using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class CombatStatsSpotRulesTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 21, 12, 0, 20, TimeSpan.Zero);

    // Independent table of the 43 reviewed catalog IDs, including unresolved variants.
    private static readonly IReadOnlyDictionary<CombatStatsCategory, string[]> ReviewedSpots =
        new Dictionary<CombatStatsCategory, string[]>
        {
            [CombatStatsCategory.General] =
            [
                "gavinya-coastal-cliff", "sycraia-abyssal-ruins-lower", "elvia-orzekea",
                "winter-tree-fossil-280", "winter-tree-fossil-unspecified", "yzrahid-highlands",
                "elvia-hexe-sanctuary", "city-of-the-dead", "dehkia-hystria-ruins", "dehkia-cadry-ruins",
            ],
            [CombatStatsCategory.Demihuman] =
            [
                "stars-end", "tungrad-ruins", "darkseekers-retreat", "fortunate-golden-pig-cave",
                "unlucky-golden-pig-cave", "elvia-quint-hill", "dokkebi-forest", "dehkia-crescent-shrine",
                "dehkia-cyclops-land", "jade-starlight-forest",
            ],
            [CombatStatsCategory.Kamasylvian] =
            [
                "dehkia-gyfin-rhasia-temple-upper", "dehkia-mirumok-ruins", "dehkia-ash-forest",
                "dehkia-ash-forest-unspecified", "dehkia-ii-ash-forest", "dehkia-ii-oluns-valley",
                "dehkia-thornwood-forest", "dehkia-tunkuta",
            ],
            [CombatStatsCategory.Edania] =
            [
                "aetherion", "nymphamare", "orbita", "tenebraum", "zephyros", "dark-energy-floodlands",
                "aphrodon", "hermesia", "magaia", "aresion", "scales-of-judgment", "event-horizon",
                "dark-energy-floodlands-great-red-sea", "dark-energy-floodlands-orbita", "dark-energy-floodlands-zephyros",
            ],
        };

    public static IEnumerable<object[]> SpotAndCategoryCases() =>
        from mapping in ReviewedSpots
        from spotId in mapping.Value
        from observedCategory in Enum.GetValues<CombatStatsCategory>()
        select new object[] { spotId, mapping.Key, observedCategory };

    [Theory]
    [MemberData(nameof(SpotAndCategoryCases))]
    public void EverySpotAcceptsGeneralOrItsOwnObservedCategory(
        string spotId, CombatStatsCategory expectedCategory, CombatStatsCategory observedCategory)
    {
        var value = new CombatStatsState(2401, 841, observedCategory, ObservedAt);
        var applicable = observedCategory == CombatStatsCategory.General || observedCategory == expectedCategory;

        Assert.Equal(expectedCategory, CombatStatsSpotRules.CategoryForSpot(spotId));
        Assert.Equal(applicable, CombatStatsSpotRules.IsApplicable(value, spotId));
        if (applicable) Assert.Same(value, CombatStatsSpotRules.ForSpot(value, spotId));
        else Assert.Null(CombatStatsSpotRules.ForSpot(value, spotId));
    }

    [Fact]
    public void ReviewedMappingCoversEverySupportedCatalogEntryExactlyOnce()
    {
        var ids = ReviewedSpots.Values.SelectMany(static ids => ids).ToArray();
        Assert.Equal(43, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(LootSpotCatalog.Spots.Select(static spot => spot.Id).Order(StringComparer.Ordinal),
            ids.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("future-spot")]
    [InlineData("dehkia-future-forest")]
    [InlineData("dark-energy-floodlands-future-variant")]
    public void UnknownSpotAcceptsOnlyGeneral(string? spotId)
    {
        Assert.Null(CombatStatsSpotRules.CategoryForSpot(spotId));
        foreach (var category in Enum.GetValues<CombatStatsCategory>())
        {
            var value = new CombatStatsState(2401, 841, category, ObservedAt);
            Assert.Equal(category == CombatStatsCategory.General, CombatStatsSpotRules.IsApplicable(value, spotId));
            Assert.Equal(category == CombatStatsCategory.General ? value : null,
                CombatStatsSpotRules.ForSpot(value, spotId));
        }
    }

    [Theory]
    [InlineData("dehkia-ash-forest-unspecified", CombatStatsCategory.Kamasylvian)]
    [InlineData("dehkia-ash-forest", CombatStatsCategory.Kamasylvian)]
    [InlineData("dehkia-ii-ash-forest", CombatStatsCategory.Kamasylvian)]
    [InlineData("winter-tree-fossil-unspecified", CombatStatsCategory.General)]
    [InlineData("winter-tree-fossil-280", CombatStatsCategory.General)]
    [InlineData("dark-energy-floodlands", CombatStatsCategory.Edania)]
    [InlineData("dark-energy-floodlands-great-red-sea", CombatStatsCategory.Edania)]
    [InlineData("dark-energy-floodlands-orbita", CombatStatsCategory.Edania)]
    [InlineData("dark-energy-floodlands-zephyros", CombatStatsCategory.Edania)]
    public void UnresolvedVariantsKeepTheSharedVerifiedCategory(string spotId, CombatStatsCategory expected) =>
        Assert.Equal(expected, CombatStatsSpotRules.CategoryForSpot(spotId));

    [Fact]
    public void UnconfirmedOrInvalidObservationsNeverApplyEvenToMatchingSpots()
    {
        CombatStatsState?[] invalid =
        [
            null, CombatStatsState.Unknown,
            new(2401, 841, CombatStatsCategory.Edania),
            new(2401, 841, CombatStatsCategory.General),
            new(0, 841, CombatStatsCategory.Edania, ObservedAt),
            new(2401, 10001, CombatStatsCategory.General, ObservedAt),
            new(2401, 841, (CombatStatsCategory)99, ObservedAt),
        ];
        foreach (var value in invalid)
        {
            Assert.False(CombatStatsSpotRules.IsApplicable(value, LootSpotCatalog.HermesiaId));
            Assert.Null(CombatStatsSpotRules.ForSpot(value, LootSpotCatalog.HermesiaId));
        }
    }
}
