namespace BdoGrindTracker.App.UI;

internal static partial class LootSpotPresentationCatalog
{
    // User-supplied scenes. Edania 1 castle images are provisional.
    // Dehkia variants share the scene of their geographical location.
    private static string? SuppliedBackground(string spotId) => spotId switch
    {
        "aetherion" => "aetherion-castle.png",
        "nymphamare" => "nymphamare-castle.png",
        "orbita" => "obita-castle.png",
        "tenebraum" => "tenebraum-castle.png",
        "zephyros" => "zephyros-castle.png",
        "sycraia-abyssal-ruins-lower" => "sycraia-underwater-ruins-abyssal.png",
        "dehkia-gyfin-rhasia-temple-upper" => "gyfin-rhasia-temple.png",
        "tungrad-ruins" => "tungrad-ruins.jpg",
        "darkseekers-retreat" => "darkseekers-retreat.jpg",
        "fortunate-golden-pig-cave" => "fortunes-golden-pig-cave.png",
        "unlucky-golden-pig-cave" => "unfortunes-golden-pig-cave.png",
        "stars-end" => "stars-end.png",
        "elvia-orzekea" => "elvia-orzekea.jpg",
        "elvia-hexe-sanctuary" => "hexe-sanctuary.png",
        "elvia-quint-hill" => "quint-hill.jpg",
        "dehkia-cadry-ruins" => "cadry-ruins.jpg",
        "dehkia-crescent-shrine" => "crescent-shrine.jpg",
        "dehkia-hystria-ruins" => "hystria-ruins.jpg",
        "dehkia-cyclops-land" => "cyclops-land.jpg",
        "gavinya-coastal-cliff" => "gavinya-coastal-cliff.webp",
        "dehkia-mirumok-ruins" => "mirumok-ruins.png",
        "winter-tree-fossil-280" or "winter-tree-fossil-unspecified" => "winter-tree-fossil.jpg",
        "dehkia-ii-ash-forest" or "dehkia-ash-forest" or "dehkia-ash-forest-unspecified" => "ash-forest.png",
        "dehkia-ii-oluns-valley" => "oluns-valley.jpg",
        "yzrahid-highlands" => "yzrahid-highlands.png",
        "dokkebi-forest" => "dokkebi-forest.png",
        "dehkia-thornwood-forest" => "thornwood-forest.jpg",
        "city-of-the-dead" => "city-of-the-dead.jpg",
        "jade-starlight-forest" => "jade-starlight-forest.jpg",
        "dehkia-tunkuta" => "Tunkuta.jpg",
        _ => null
    };
}
