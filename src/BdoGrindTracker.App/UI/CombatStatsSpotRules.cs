using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

/// <summary>Matches the observed HUD category to the selected grind spot.</summary>
internal static class CombatStatsSpotRules
{
    // Species reviewed against Garmoth's public PvE caps table on 2026-09-21:
    // https://garmoth.com/character/coQx3sxaGL/pve/caps (mob-types icons).
    // Normal and Human have no separate colored HUD category here, so use General.
    // Keep explicit IDs: neither a geographic name nor an unrecognized future spot
    // is sufficient evidence that a colored AP/DP observation applies.
    public static CombatStatsCategory? CategoryForSpot(string? spotId) => spotId switch
    {
        "gavinya-coastal-cliff" or
        "sycraia-abyssal-ruins-lower" or
        "elvia-orzekea" or
        "winter-tree-fossil-280" or
        "winter-tree-fossil-unspecified" or
        "yzrahid-highlands" or
        "elvia-hexe-sanctuary" or
        "city-of-the-dead" or
        "dehkia-hystria-ruins" or
        "dehkia-cadry-ruins" => CombatStatsCategory.General,

        "stars-end" or
        "tungrad-ruins" or
        "darkseekers-retreat" or
        "fortunate-golden-pig-cave" or
        "unlucky-golden-pig-cave" or
        "elvia-quint-hill" or
        "dokkebi-forest" or
        "dehkia-crescent-shrine" or
        "dehkia-cyclops-land" or
        "jade-starlight-forest" => CombatStatsCategory.Demihuman,

        "dehkia-gyfin-rhasia-temple-upper" or
        "dehkia-mirumok-ruins" or
        "dehkia-ash-forest" or
        "dehkia-ash-forest-unspecified" or
        "dehkia-ii-ash-forest" or
        "dehkia-ii-oluns-valley" or
        "dehkia-thornwood-forest" or
        "dehkia-tunkuta" => CombatStatsCategory.Kamasylvian,

        LootSpotCatalog.AetherionId or
        LootSpotCatalog.NymphamareId or
        LootSpotCatalog.OrbitaId or
        LootSpotCatalog.TenebraumId or
        LootSpotCatalog.ZephyrosId or
        LootSpotCatalog.DarkEnergyFloodlandsId or
        LootSpotCatalog.AphrodonId or
        LootSpotCatalog.HermesiaId or
        LootSpotCatalog.MagaiaId or
        LootSpotCatalog.AresionId or
        LootSpotCatalog.ScalesOfJudgmentId or
        LootSpotCatalog.EventHorizonId or
        "dark-energy-floodlands-great-red-sea" or
        "dark-energy-floodlands-orbita" or
        "dark-energy-floodlands-zephyros" => CombatStatsCategory.Edania,

        _ => null,
    };

    public static bool IsApplicable(CombatStatsState? value, string? spotId) =>
        value is { IsKnown: true } &&
        (value.Category == CombatStatsCategory.General || CategoryForSpot(spotId) == value.Category);

    public static CombatStatsState? ForSpot(CombatStatsState? value, string? spotId) =>
        IsApplicable(value, spotId) ? value : null;
}
