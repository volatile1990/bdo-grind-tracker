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
    string IconFileName,
    string RecommendedCrystalName,
    string RecommendedCrystalFileName,
    IReadOnlyList<string> Traits);

internal static class LootSpotPresentationCatalog
{
    private static readonly LootSpotPresentation[] AllProfiles =
    [
        Profile(LootSpotCatalog.AphrodonId, 2090, 2120, 810,
            "Branch of Abundance", "aphrodon.jpg", "aphrodon.png",
            "Adamantine", "adamantine.png",
            "#CombatEXP", "#MarnisRealmPrivate", "#Knockdown/Bound",
            "#AllanSerbinsLandscape", "#HighestTier"),
        Profile(LootSpotCatalog.HermesiaId, 2220, 2250, 830,
            "Black Crystal Fragment", "hermesia.jpg", "hermesia.png",
            "Fighting Spirit", "fighting-spirit.png",
            "#CombatEXP", "#MarnisRealmPrivate", "#Knockback/Floating",
            "#AllanSerbinsLandscape", "#HighestTier"),
        Profile(LootSpotCatalog.MagaiaId, 2340, 2370, 840,
            "Elion Follower's Helmet", "magaia.jpg", "magaia.png",
            "Giant", "giant.png",
            "#CombatEXP", "#MarnisRealmPrivate", "#Stun/Stiffness/Freezing",
            "#AllanSerbinsLandscape", "#HighestTier"),
        Profile(LootSpotCatalog.AresionId, 2455, 2485, 850,
            "Scorched Belt Ornament", "aresion.jpg", "aresion.png",
            "Adamantine", "adamantine.png",
            "#CombatEXP", "#MarnisRealmPrivate", "#Knockdown/Bound",
            "#AllanSerbinsLandscape", "#HighestTier", "#DivineAuthority"),
        Profile(LootSpotCatalog.ScalesOfJudgmentId, 2455, 2485, 860,
            "Elion Follower's Mark", "scales-of-judgment.jpg", "scales-of-judgment.png",
            "Giant", "giant.png",
            "#CombatEXP", "#PartyOf3", "#Stun/Stiffness/Freezing",
            "#HighestTier", "#DivineAuthority"),
        Profile(LootSpotCatalog.EventHorizonId, 2570, 2600, 870,
            "Broken Gloves of the Void", "event-horizon.jpg", "event-horizon.png",
            "Giant", "giant.png",
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

    public static bool IsResistanceTrait(string trait) => trait is
        "#Knockdown/Bound" or "#Knockback/Floating" or "#Stun/Stiffness/Freezing";

    private static LootSpotPresentation Profile(
        string spotId,
        int recommendedAp,
        int maxApLimit,
        int recommendedDp,
        string trashItemName,
        string backgroundFileName,
        string iconFileName,
        string recommendedCrystalName,
        string recommendedCrystalFileName,
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
            iconFileName,
            recommendedCrystalName,
            recommendedCrystalFileName,
            Array.AsReadOnly(traits));
    }
}
