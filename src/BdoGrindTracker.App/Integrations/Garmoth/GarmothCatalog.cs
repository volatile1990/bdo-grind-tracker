namespace BdoGrindTracker.App.Integrations.Garmoth;

/// <summary>
/// Names/IDs verified against Companion's public-metadata cache and public
/// Garmoth/BDO metadata on 2026-09-06; Outer Edania added on 2026-09-13.
/// The application never reads Companion settings, credentials or caches at runtime.
/// See docs/GARMOTH_INTEGRATION.md for sources and native contract anchors.
/// </summary>
internal static partial class GarmothCatalog
{
    private static readonly IReadOnlyDictionary<string, int> Spots =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["aetherion"] = 183, ["nymphamare"] = 184, ["orbita"] = 185,
            ["tenebraum"] = 193, ["zephyros"] = 194,
            ["aphrodon"] = 213, ["hermesia"] = 214, ["magaia"] = 215,
            ["aresion"] = 216, ["scales-of-judgment"] = 217, ["event-horizon"] = 218,
        };

    private static readonly IReadOnlyDictionary<string, string> DropKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ancient Soldier Fragment"] = "767246_0",
            ["Ancient Spirit Dust"] = "721002_0",
            ["Apeiron Belt"] = "12298_0",
            ["Apeiron Earring"] = "11898_0",
            ["Apeiron Necklace"] = "11733_0",
            ["Apeiron Ring"] = "12144_0",
            ["BON Crystal of Dusky Ruin"] = "15287_0",
            ["BON Crystal of Ruin"] = "821254_0",
            ["BON Origin Shard"] = "821431_0",
            ["BON Wandering Origin Crystal"] = "15295_0",
            ["Black Crystal Fragment"] = "980128_0",
            ["Black Stone"] = "16001_0",
            ["Branch of Abundance"] = "980127_0",
            ["Broken Gloves of the Void"] = "980132_0",
            ["Broken Vestige of Crimsonflare"] = "980142_0",
            ["Broken Vestige of Ebonmere"] = "980140_0",
            ["Broken Vestige of Everlight"] = "980141_0",
            ["Broken Vestige of Goldroot"] = "980139_0",
            ["Broken Vestige of Voidreach"] = "980143_0",
            ["Caphras Stone"] = "721003_0",
            ["Chilled Soul Piece"] = "767244_0",
            ["Contaminated Coral Piece"] = "767245_0",
            ["Corrupt Oil of Immortality"] = "1178_0",
            ["Crimson Primordial Luster - Sovereign"] = "821341_0",
            ["Crimson Primordial Pigment - Sovereign"] = "767293_0",
            ["Crystallized Energy of Endtimes"] = "821252_0",
            ["Deboreka Belt"] = "12276_0",
            ["Deboreka Earring"] = "11882_0",
            ["Deboreka Necklace"] = "11653_0",
            ["Deboreka Ring"] = "12094_0",
            ["Distorted Crystal of Origin"] = "761802_0",
            ["Distorted Fragment of Origin"] = "821317_0",
            ["Elion Follower's Helmet"] = "980129_0",
            ["Elion Follower's Mark"] = "980130_0",
            ["Embers of Ynix - Armor"] = "821462_0",
            ["Embers of Ynix - Gloves"] = "821463_0",
            ["Embers of Ynix - Helmet"] = "821461_0",
            ["Embers of Ynix - Shoes"] = "821464_0",
            ["Faded Dark Energy"] = "767349_0",
            ["Flawless Herald's Crystal"] = "821251_0",
            ["Fusion Shard"] = "821471_0",
            ["HAN Crystal of Dusky Ruin"] = "15291_0",
            ["HAN Crystal of Ruin"] = "821320_0",
            ["HAN Origin Shard"] = "821433_0",
            ["HAN Wandering Origin Crystal"] = "15297_0",
            ["Hardened Lava Chunk"] = "767248_0",
            ["Herald's Crystal"] = "821250_0",
            ["Intricately Patterned Mystical Shard"] = "9776_0",
            ["JIN Crystal of Dusky Ruin"] = "15290_0",
            ["JIN Crystal of Ruin"] = "821319_0",
            ["JIN Origin Shard"] = "821432_0",
            ["JIN Wandering Origin Crystal"] = "15296_0",
            ["Laila's Petal"] = "54031_0",
            ["Lightlost Core"] = "767247_0",
            ["Nev's Fragment"] = "821460_0",
            ["Primordial Fragment"] = "821246_0",
            ["Refined Essence of Devouring"] = "767338_0",
            ["Refined Origin of Hunger"] = "767337_0",
            ["Scorched Belt Ornament"] = "980131_0",
            ["Sealed Black Magic Crystal"] = "768160_0",
            ["Silent Crystal of Origin"] = "761803_0",
            ["Silent Fragment of Origin"] = "821318_0",
            ["Sunset Primordial Luster - Edana"] = "821459_0",
            ["Sunset Primordial Pigment - Edana"] = "767353_0",
            ["Tainted Armor Fragment"] = "767348_0",
            ["Twilight of the End - Belt"] = "821424_0",
            ["Twilight of the End - Earring"] = "821422_0",
            ["Twilight of the End - Necklace"] = "821421_0",
            ["Twilight of the End - Ring"] = "821423_0",
            ["Violet Primordial Luster - Edana"] = "821343_0",
            ["Violet Primordial Luster - Sovereign"] = "821342_0",
            ["Violet Primordial Pigment - Edana"] = "767296_0",
            ["Violet Primordial Pigment - Sovereign"] = "767294_0",
            ["WON Crystal of Dusky Ruin"] = "15286_0",
            ["WON Crystal of Ruin"] = "821253_0",
            ["WON Origin Shard"] = "821430_0",
            ["WON Wandering Origin Crystal"] = "15294_0",
            ["White Primordial Luster - Edana"] = "821420_0",
            ["White Primordial Luster - Sovereign"] = "821419_0",
            ["White Primordial Pigment - Edana"] = "767344_0",
            ["White Primordial Pigment - Sovereign"] = "767343_0",
        };

    // Garmoth's spot metadata is intentionally separate from the local OCR
    // allow-lists. A real global drop may be trackable locally without having
    // a slot in Garmoth's corresponding session (for example Laila's Petal).
    // Spot keys come from the verified public metadata cache. Only base
    // Deboreka items are mapped because the OCR vocabulary has no enhancement level.
    private static readonly string[] CommonSpotDropKeys =
    [
        "16001_0", "721002_0", "721003_0", "1178_0", "821460_0", "821471_0",
        "821318_0", "761803_0", "767337_0", "767338_0", "767353_0", "821459_0",
        "821341_0", "767293_0", "821342_0", "767294_0", "767296_0", "821343_0",
        "821422_0", "11898_0",
    ];

    private static readonly string[] OuterSpotDropKeys =
    [
        "16001_0", "721002_0", "721003_0", "821317_0", "821318_0", "761802_0", "761803_0",
        "12276_0", "12094_0", "11653_0", "11882_0",
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<string>> SpotDropKeys =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["aetherion"] = WithOuterKeys("767244_0", "821246_0", "15286_0", "821253_0"),
            ["nymphamare"] = WithOuterKeys("767245_0", "15287_0", "821254_0", "821252_0"),
            ["orbita"] = WithOuterKeys("767247_0", "15290_0", "821319_0", "821252_0"),
            ["tenebraum"] = WithOuterKeys("767246_0", "15291_0", "821320_0", "821252_0", "821250_0", "821251_0"),
            ["zephyros"] = WithOuterKeys("767248_0", "15291_0", "821320_0", "821252_0", "821250_0", "821251_0", "768160_0"),
            ["aphrodon"] = WithCommonKeys("980127_0", "821430_0", "15294_0", "980139_0", "821462_0"),
            ["hermesia"] = WithCommonKeys("980128_0", "821431_0", "15295_0", "12144_0", "821423_0", "980140_0", "821461_0"),
            ["magaia"] = WithCommonKeys("980129_0", "821432_0", "15296_0", "821423_0", "821424_0", "12144_0", "12298_0", "980141_0", "821464_0"),
            ["aresion"] = WithCommonKeys(
                "980131_0", "821433_0", "15297_0", "821463_0", "12144_0", "12298_0", "11733_0",
                "821423_0", "821424_0", "821421_0", "980142_0", "767343_0", "767344_0", "821419_0", "821420_0"),
            ["scales-of-judgment"] = WithCommonKeys(
                "980130_0", "821433_0", "15297_0", "821464_0", "12144_0", "12298_0", "11733_0",
                "821423_0", "821424_0", "821421_0", "980141_0", "767343_0", "767344_0", "821419_0", "821420_0"),
            ["event-horizon"] = WithCommonKeys(
                "980132_0", "821433_0", "15297_0", "821462_0", "821461_0", "821463_0", "821464_0",
                "12144_0", "12298_0", "11733_0", "821423_0", "821424_0", "821421_0", "980143_0",
                "767343_0", "767344_0", "821419_0", "821420_0"),
        };

    private static readonly string[] ClassNames =
    [
        "Berserker", "Ranger", "Sorceress", "Tamer", "Valkyrie", "Warrior", "Witch",
        "Wizard", "Musa", "Maehwa", "Ninja", "Kunoichi", "Dark Knight", "Striker",
        "Mystic", "Lahn", "Archer", "Shai", "Guardian", "Hashashin", "Nova", "Sage",
        "Corsair", "Drakania", "Woosa", "Maegu", "Scholar", "Dosa", "Deadeye",
        "Wukong", "Seraph", "Agent",
    ];

    public static bool TryGetSpot(string? name, out int id)
    {
        id = default;
        return name is not null && (Spots.TryGetValue(name, out id) || ScreenshotSpots.TryGetValue(name, out id));
    }

    internal static int SupportedSpotCount => Spots.Count + ScreenshotSpots.Count;

    internal static bool TryGetLocalSpotId(int garmothId, out string spotId)
    {
        spotId = Spots.Concat(ScreenshotSpots).FirstOrDefault(spot => spot.Value == garmothId).Key!;
        return spotId is not null;
    }

    // All three Floodlands locations have the same loot. The OCR spot lock
    // cannot choose between Garmoth IDs 208/209/210 without a location input.
    internal static string? GetSpotUploadLimitation(string? spotId) => spotId switch
    {
        "dark-energy-floodlands" => "Für den Garmoth-Upload bitte das genaue Gebiet der Dark Energy Floodlands in der Live-Session auswählen: Great Red Sea, Orbita oder Zephyros.",
        "dehkia-ash-forest-unspecified" => "Für den Garmoth-Upload bitte die Dehkia-Stufe von Ash Forest in der Live-Session auswählen.",
        "winter-tree-fossil-unspecified" => "Für den Garmoth-Upload bitte Winter Tree Fossil (280ap) in der Live-Session bestätigen. Die 250-AP-Variante verwendet denselben Trashloot.",
        _ => null,
    };

    public static bool TryGetDropKey(string name, out string key) =>
        DropKeys.TryGetValue(name, out key!) || ScreenshotDropKeys.TryGetValue(name, out key!);

    public static bool TryGetDropKeyForSpot(string? spotId, string name, out string key)
    {
        key = string.Empty;
        if (spotId is not null && ScreenshotSpotDropKeys.TryGetValue(spotId, out var screenshotDrops))
            return screenshotDrops.TryGetValue(name, out key!);
        if (spotId is null || !SpotDropKeys.TryGetValue(spotId, out var allowed) ||
            !DropKeys.TryGetValue(name, out var mapped) || !allowed.Contains(mapped))
            return false;
        key = mapped;
        return true;
    }

    public static bool TryGetClass(string? name, GarmothSpecialization specialization,
        out int id, out int spec)
    {
        id = Array.IndexOf(ClassNames, name);
        spec = 0;
        if (id < 0 || !Enum.IsDefined(specialization)) return false;
        var unique = id is 16 or 17 or 26 or 28 or 29 or 30 or 31;
        if (unique != (specialization == GarmothSpecialization.Unique)) return false;
        // Native validation: 0 = awakening, 1 = succession. Unique classes use
        // their sole supported flag; this is not the game's numeric class ID.
        spec = specialization == GarmothSpecialization.Unique
            ? (id is 16 or 26 or 29 ? 0 : 1)
            : specialization == GarmothSpecialization.Succession ? 1 : 0;
        return true;
    }

    private static HashSet<string> WithCommonKeys(params string[] specificKeys) =>
        new(CommonSpotDropKeys.Concat(specificKeys), StringComparer.Ordinal);

    private static HashSet<string> WithOuterKeys(params string[] specificKeys) =>
        new(OuterSpotDropKeys.Concat(specificKeys), StringComparer.Ordinal);
}
