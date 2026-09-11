namespace BdoGrindTracker.App.Pricing;

/// <summary>
/// Item classification and fixed values from Companion 0.7.4's metadata cache.
/// Market prices are deliberately not bundled. NPC junk prices were additionally
/// checked against the publisher's 2026-08-13 patch notes. See pricing documentation.
/// Empty Picture Frame's NPC value was verified in BDO Codex on 2026-09-11.
/// </summary>
internal static class LootPriceCatalog
{
    public const string DefaultRegion = "eu";
    public static IReadOnlyList<string> SupportedRegions { get; } = Array.AsReadOnly(new[] { "eu", "na" });
    public static IReadOnlyList<LootPriceDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new LootPriceDefinition("Ancient Spirit Dust", LootPriceKind.AncientSpiritDust),
        Market("Apeiron Belt", 12298),
        Market("Apeiron Earring", 11898),
        Market("Apeiron Necklace", 11733),
        Market("Apeiron Ring", 12144),
        Fixed("BON Origin Shard", 15_000_000),
        Fixed("BON Wandering Origin Crystal", 1_500_000_000),
        Fixed("Black Crystal Fragment", 160_539),
        Market("Black Gem Fragment", 4999),
        Market("Black Stone", 16001),
        Fixed("Branch of Abundance", 155_127),
        Fixed("Broken Gloves of the Void", 196_501),
        Fixed("Broken Vestige of Crimsonflare", 3_300_000_000),
        Fixed("Broken Vestige of Ebonmere", 3_100_000_000),
        Fixed("Broken Vestige of Everlight", 3_200_000_000),
        Fixed("Broken Vestige of Goldroot", 3_000_000_000),
        Fixed("Broken Vestige of Voidreach", 4_000_000_000),
        Market("Caphras Stone", 721003),
        Market("Corrupt Oil of Immortality", 1178),
        Market("Crimson Primordial Luster - Sovereign", 821341),
        Market("Crimson Primordial Pigment - Sovereign", 767293),
        Fixed("Elion Follower's Helmet", 181_042),
        Fixed("Elion Follower's Mark", 186_458),
        Fixed("Embers of Ynix - Armor", 0),
        Fixed("Embers of Ynix - Gloves", 0),
        Fixed("Embers of Ynix - Helmet", 0),
        Fixed("Embers of Ynix - Shoes", 0),
        Fixed("Empty Picture Frame", 15_348),
        new LootPriceDefinition("[Event] Mysterious Ore", LootPriceKind.Unknown),
        Market("Fusion Shard", 821471),
        Fixed("HAN Origin Shard", 20_000_000),
        Fixed("HAN Wandering Origin Crystal", 2_000_000_000),
        Fixed("JIN Origin Shard", 17_000_000),
        Fixed("JIN Wandering Origin Crystal", 1_700_000_000),
        Fixed("Laila's Petal", 500_000),
        Market("Nev's Fragment", 821460),
        new LootPriceDefinition("Pure Black Stone", LootPriceKind.Unknown),
        Market("Refined Essence of Devouring", 767338),
        Market("Refined Origin of Hunger", 767337),
        Fixed("Scorched Belt Ornament", 182_049),
        Market("Silent Crystal of Origin", 761803),
        Market("Silent Fragment of Origin", 821318),
        Market("Sunset Primordial Luster - Edana", 821459),
        Market("Sunset Primordial Pigment - Edana", 767353),
        Market("Twilight of the End - Belt", 821424),
        Market("Twilight of the End - Earring", 821422),
        Market("Twilight of the End - Necklace", 821421),
        Market("Twilight of the End - Ring", 821423),
        Market("Violet Primordial Luster - Edana", 821343),
        Market("Violet Primordial Luster - Sovereign", 821342),
        Market("Violet Primordial Pigment - Edana", 767296),
        Market("Violet Primordial Pigment - Sovereign", 767294),
        Fixed("WON Origin Shard", 12_000_000),
        Fixed("WON Wandering Origin Crystal", 1_200_000_000),
        Market("White Primordial Luster - Edana", 821420),
        Market("White Primordial Luster - Sovereign", 821419),
        Market("White Primordial Pigment - Edana", 767344),
        Market("White Primordial Pigment - Sovereign", 767343),
    });

    public static IReadOnlyList<int> MarketItemIds { get; } = Array.AsReadOnly(Definitions
        .Where(static definition => definition.Kind == LootPriceKind.Market)
        .Select(static definition => definition.MarketItemId!.Value).Distinct().Order().ToArray());

    public static string NormalizeRegion(string? region)
    {
        var normalized = region?.Trim().ToLowerInvariant();
        if (normalized is null || !SupportedRegions.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException("Unterstützte Preisregionen sind EU und NA.", nameof(region));
        return normalized;
    }

    public static LootPriceSnapshot FixedSnapshot(string region) => new(region,
        Definitions.Where(static definition => definition.Kind == LootPriceKind.Fixed)
            .Select(static definition => new LootPriceQuote(definition.ItemName, 0,
                definition.FixedUnitPrice!.Value, LootPriceOrigin.FixedCatalog, null)),
        statusMessage: "NPC-/Festwerte verfügbar; Marktpreise noch nicht geladen.");

    private static LootPriceDefinition Market(string name, int id) => new(name, LootPriceKind.Market, id);
    private static LootPriceDefinition Fixed(string name, long price) => new(name, LootPriceKind.Fixed, FixedUnitPrice: price);
}
