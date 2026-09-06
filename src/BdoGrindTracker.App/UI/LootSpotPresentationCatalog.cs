using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

internal sealed record LootSpotPresentation(
    string SpotId,
    int RecommendedAp,
    int MaxApLimit,
    int RecommendedDp,
    string TrashItemName,
    long TrashSilver,
    string BackgroundFileName,
    IReadOnlyList<string> Traits);

internal static class LootSpotPresentationCatalog
{
    private static readonly LootSpotPresentation[] AllProfiles =
    [
        Profile(LootSpotCatalog.AphrodonId, 2090, 2120, 810,
            "Branch of Abundance", "aphrodon.jpg",
            "#CombatEXP", "#MarnisRealmPrivate", "#Knockdown/Bound",
            "#AllanSerbinsLandscape", "#HighestTier"),
        Profile(LootSpotCatalog.HermesiaId, 2220, 2250, 830,
            "Black Crystal Fragment", "hermesia.jpg",
            "#CombatEXP", "#MarnisRealmPrivate", "#Knockback/Floating",
            "#AllanSerbinsLandscape", "#HighestTier"),
        Profile(LootSpotCatalog.MagaiaId, 2340, 2370, 840,
            "Elion Follower's Helmet", "magaia.jpg",
            "#CombatEXP", "#MarnisRealmPrivate", "#Stun/Stiffness/Freezing",
            "#AllanSerbinsLandscape", "#HighestTier"),
        Profile(LootSpotCatalog.AresionId, 2455, 2485, 850,
            "Scorched Belt Ornament", "aresion.jpg",
            "#CombatEXP", "#MarnisRealmPrivate", "#Knockdown/Bound",
            "#AllanSerbinsLandscape", "#HighestTier", "#DivineAuthority"),
        Profile(LootSpotCatalog.ScalesOfJudgmentId, 2455, 2485, 860,
            "Elion Follower's Mark", "scales-of-judgment.jpg",
            "#CombatEXP", "#PartyOf3", "#Stun/Stiffness/Freezing",
            "#HighestTier", "#DivineAuthority"),
        Profile(LootSpotCatalog.EventHorizonId, 2570, 2600, 870,
            "Broken Gloves of the Void", "event-horizon.jpg",
            "#FeverPowerfulMobs", "#CombatEXP", "#MarnisRealmPrivate",
            "#Stun/Stiffness/Freezing", "#AllanSerbinsLandscape",
            "#HighestTier", "#DivineAuthority"),
    ];

    public static IReadOnlyList<LootSpotPresentation> Profiles { get; } =
        Array.AsReadOnly(AllProfiles);

    public static LootSpotPresentation GetRequired(string spotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spotId);
        return AllProfiles.FirstOrDefault(profile =>
                string.Equals(profile.SpotId, spotId, StringComparison.Ordinal)) ??
            throw new KeyNotFoundException($"Für Grindspot '{spotId}' fehlen UI-Metadaten.");
    }

    private static LootSpotPresentation Profile(
        string spotId,
        int recommendedAp,
        int maxApLimit,
        int recommendedDp,
        string trashItemName,
        string backgroundFileName,
        params string[] traits)
    {
        var trashPrice = LootPriceCatalog.Definitions.Single(definition =>
            string.Equals(definition.ItemName, trashItemName, StringComparison.Ordinal));
        if (trashPrice.Kind != LootPriceKind.Fixed || trashPrice.FixedUnitPrice is null)
            throw new InvalidOperationException($"Trashloot '{trashItemName}' besitzt keinen festen Silberwert.");

        return new LootSpotPresentation(
            spotId,
            recommendedAp,
            maxApLimit,
            recommendedDp,
            trashItemName,
            trashPrice.FixedUnitPrice.Value,
            backgroundFileName,
            Array.AsReadOnly(traits));
    }
}
