namespace BdoGrindTracker.App.Integrations.Garmoth;

/// <summary>
/// Names/IDs verified against Companion's public-metadata cache and public
/// Garmoth/BDO metadata on 2026-09-06.
/// The application never reads Companion settings, credentials or caches at runtime.
/// See docs/GARMOTH_INTEGRATION.md for sources and native contract anchors.
/// </summary>
internal static class GarmothCatalog
{
    private static readonly IReadOnlyDictionary<string, int> Spots =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["aphrodon"] = 213, ["hermesia"] = 214, ["magaia"] = 215,
            ["aresion"] = 216, ["scales-of-judgment"] = 217, ["event-horizon"] = 218,
        };

    private static readonly IReadOnlyDictionary<string, string> DropKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ancient Spirit Dust"] = "721002_0",
            ["Apeiron Belt"] = "12298_0",
            ["Apeiron Earring"] = "11898_0",
            ["Apeiron Necklace"] = "11733_0",
            ["Apeiron Ring"] = "12144_0",
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
            ["Corrupt Oil of Immortality"] = "1178_0",
            ["Crimson Primordial Luster - Sovereign"] = "821341_0",
            ["Crimson Primordial Pigment - Sovereign"] = "767293_0",
            ["Elion Follower's Helmet"] = "980129_0",
            ["Elion Follower's Mark"] = "980130_0",
            ["Embers of Ynix - Armor"] = "821462_0",
            ["Embers of Ynix - Gloves"] = "821463_0",
            ["Embers of Ynix - Helmet"] = "821461_0",
            ["Embers of Ynix - Shoes"] = "821464_0",
            ["Fusion Shard"] = "821471_0",
            ["HAN Origin Shard"] = "821433_0",
            ["HAN Wandering Origin Crystal"] = "15297_0",
            ["JIN Origin Shard"] = "821432_0",
            ["JIN Wandering Origin Crystal"] = "15296_0",
            ["Laila's Petal"] = "54031_0",
            ["Nev's Fragment"] = "821460_0",
            ["Refined Essence of Devouring"] = "767338_0",
            ["Refined Origin of Hunger"] = "767337_0",
            ["Scorched Belt Ornament"] = "980131_0",
            ["Silent Crystal of Origin"] = "761803_0",
            ["Silent Fragment of Origin"] = "821318_0",
            ["Sunset Primordial Luster - Edana"] = "821459_0",
            ["Sunset Primordial Pigment - Edana"] = "767353_0",
            ["Twilight of the End - Belt"] = "821424_0",
            ["Twilight of the End - Earring"] = "821422_0",
            ["Twilight of the End - Necklace"] = "821421_0",
            ["Twilight of the End - Ring"] = "821423_0",
            ["Violet Primordial Luster - Edana"] = "821343_0",
            ["Violet Primordial Luster - Sovereign"] = "821342_0",
            ["Violet Primordial Pigment - Edana"] = "767296_0",
            ["Violet Primordial Pigment - Sovereign"] = "767294_0",
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
    // These are the 25 / 27 / 29 keys in the verified public metadata cache.
    private static readonly string[] CommonSpotDropKeys =
    [
        "16001_0", "721002_0", "721003_0", "1178_0", "821460_0", "821471_0",
        "821318_0", "761803_0", "767337_0", "767338_0", "767353_0", "821459_0",
        "821341_0", "767293_0", "821342_0", "767294_0", "767296_0", "821343_0",
        "821422_0", "11898_0",
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<string>> SpotDropKeys =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
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
        return name is not null && Spots.TryGetValue(name, out id);
    }

    public static bool TryGetDropKey(string name, out string key) =>
        DropKeys.TryGetValue(name, out key!);

    public static bool TryGetDropKeyForSpot(string? spotId, string name, out string key)
    {
        key = string.Empty;
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
}
