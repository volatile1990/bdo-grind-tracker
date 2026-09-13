namespace BdoGrindTracker.App.UI;

internal static partial class LootSpotPresentationCatalog
{
    // AP caps/regions: Garmoth's public Monster Zone AP Caps, checked 2026-09-13.
    // Recommendations are total stats, matching the existing Edania profiles.
    // Unverified recommendations, crystals and scene images stay absent.
    private static IEnumerable<LootSpotPresentation> ScreenshotProfiles()
    {
        yield return Profile("gavinya-coastal-cliff", "Valencia", 1990, 2020, 820,
            "Sulfur Golem Fragment", null, "gavinya-coastal-cliff.png", "Adamantine", "adamantine.png",
            "#Knockdown/Bound", "#AllanSerbinsLandscape");
        yield return Profile("stars-end", "Calpheon", 1920, 1950, 800,
            "Corrupted Sanguine Crystal", null, "stars-end.png", "Giant", "giant.png",
            "#Stun/Stiffness/Freezing", "#MarnisRealmPrivate", "#AllanSerbinsLandscape");
        yield return ScreenshotProfile("sycraia-abyssal-ruins-lower", "Balenos", 1935, "Underwater Ancient Weapon Power Stone");
        yield return ScreenshotProfile("elvia-orzekea", "O'dyllita", 1595, "Thorn-Entwined Weapon Fragment", "#Elvia");
        yield return ScreenshotProfile("dehkia-gyfin-rhasia-temple-upper", "Kamasylvia", 1680, "Tainted Bronze Fragment", "#Dehkia");
        yield return ScreenshotProfile("tungrad-ruins", "Ulukita", 1395, "Tungrad Ruins Fragment");
        yield return ScreenshotProfile("darkseekers-retreat", "Ulukita", 1490, "Decayed Cloth");
        yield return ScreenshotProfile("fortunate-golden-pig-cave", "Land of the Morning Light", 1490, "Shiny Treasure");
        yield return ScreenshotProfile("dehkia-mirumok-ruins", "Kamasylvia", 1595, "Tainted Wood Fragment", "#Dehkia");
        yield return ScreenshotProfile("winter-tree-fossil-280", "Mountain of Eternal Winter", 856, "Winter Tree Snow Crystal");
        yield return ScreenshotProfile("unlucky-golden-pig-cave", "Land of the Morning Light", 1540, "Shattered Treasures");
        yield return ScreenshotProfile("dehkia-ii-ash-forest", "Kamasylvia", 1540, "Tainted Specter's Cloth", "#DehkiaII");
        yield return ScreenshotProfile("dehkia-ii-oluns-valley", "O'dyllita", 1490, "Tainted Golem's Heart Fragment", "#DehkiaII", "#PartyOf3");
        yield return ScreenshotProfile("yzrahid-highlands", "Ulukita", 1180, "Corrupt Power Source");
        yield return ScreenshotProfile("elvia-hexe-sanctuary", "Calpheon", 1130, "Howling Bone Fragment", "#Elvia");
        yield return ScreenshotProfile("elvia-quint-hill", "Calpheon", 1295, "Fiery Troll Hide", "#Elvia");
        yield return ScreenshotProfile("dokkebi-forest", "Land of the Morning Light", 1445, "Discarded Kkebicap");
        yield return ScreenshotProfile("dehkia-thornwood-forest", "O'dyllita", 1180, "Tainted Moonlight Spirit Powder", "#Dehkia");
        yield return ScreenshotProfile("city-of-the-dead", "Ulukita", 1295, "Mark of the Black Sands");
        yield return ScreenshotProfile("dehkia-cadry-ruins", "Valencia", 1395, "Tainted Cadry's Token", "#Dehkia");
        yield return ScreenshotProfile("dehkia-ash-forest", "Kamasylvia", 1350, "Tainted Specter's Cloth", "#Dehkia");
        yield return ScreenshotProfile("dehkia-crescent-shrine", "Valencia", 1350, "Tainted Token of Crescent", "#Dehkia");
        yield return ScreenshotProfile("dehkia-cyclops-land", "Calpheon", 1180, "Tainted Huge Spear", "#Dehkia");
        yield return ScreenshotProfile("jade-starlight-forest", "Mountain of Eternal Winter", 950, "Starlit Jade Powder");
        yield return ScreenshotProfile("dehkia-tunkuta", "O'dyllita", 1180, "Tainted Broken Horn Fragment", "#Dehkia");
        yield return ScreenshotProfile("dehkia-hystria-ruins", "Valencia", 1130, "Tainted Ruins Fragment", "#Dehkia");
        foreach (var region in new[] { "zephyros", "orbita", "great-red-sea" })
            yield return Profile("dark-energy-floodlands-" + region, "Outer Edania", 1850, 1880, 760,
                "Tainted Armor Fragment", "dark-energy-floodlands.jpg", "dark-energy-floodlands.png",
                "Adamantine", "adamantine.png", "#CombatEXP", "#PartyOf3", "#Knockdown/Bound");
        yield return Profile("dehkia-ash-forest-unspecified", "Kamasylvia", null, null, null,
            "Tainted Specter's Cloth", null, "dehkia-ash-forest.png", null, null, "#Dehkia");
        yield return Profile("winter-tree-fossil-unspecified", "Mountain of Eternal Winter", null, null, null,
            "Winter Tree Snow Crystal", null, "winter-tree-fossil-280.png", null, null);
    }

    private static LootSpotPresentation ScreenshotProfile(string id, string region, int cap, string trash, params string[] traits) =>
        Profile(id, region, null, cap, null, trash, null, id + ".png", null, null, traits);
}
