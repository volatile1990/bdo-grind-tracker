using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Pricing;

/// <summary>
/// The selected cost-tracking whitelist. Item IDs, durations and tent purchase prices
/// were checked on 2026-09-21; see docs/BUFF_PRICE_SOURCES.md. The catalogue supplies
/// identities and prices, not proof that an uncalibrated HUD icon identifies an item.
/// </summary>
internal static class BuffPriceCatalog
{
    public static IReadOnlyList<BuffDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        // Cron-Mahlzeiten
        Market("exquisite-cron-meal", "Exquisite Cron Meal", 9693, 120,
            "Cron-Mahlzeit: Exquisit", "Cron-Mahlzeiten", "Standard", "exquisite-cron-meal"),
        Market("seafood-cron-meal", "Seafood Cron Meal", 9691, 120,
            "Cron-Mahlzeit: Meeresfrüchte", "Cron-Mahlzeiten", "Standard", "seafood-cron-meal"),
        Market("simple-cron-meal", "Simple Cron Meal", 9692, 120,
            "Cron-Mahlzeit: Einfach", "Cron-Mahlzeiten", "Standard", "simple-cron-meal"),
        // Harmony Draughts
        Market("harmony-draught-demihuman", "[Party] Harmony Draught - Demihuman", 1403, 20,
            "[Gruppe] Arznei der Harmonie – Halbmenschen", "Harmony Draughts", "Gruppe", "harmony-draught-demihuman", party: true),
        Market("harmony-draught-edania", "[Party] Harmony Draught - Edania", 1407, 20,
            "[Gruppe] Arznei der Harmonie – Edania", "Harmony Draughts", "Gruppe", "harmony-draught-edania", party: true),
        Market("harmony-draught-human", "[Party] Harmony Draught - Human", 1401, 20,
            "[Gruppe] Arznei der Harmonie – Menschen", "Harmony Draughts", "Gruppe", "harmony-draught-human", party: true),
        Market("harmony-draught-kamasylvia", "[Party] Harmony Draught - Kamasylvia", 1405, 20,
            "[Gruppe] Arznei der Harmonie – Kamasilvia", "Harmony Draughts", "Gruppe", "harmony-draught-kamasylvia", party: true),
        Market("immortal-harmony-draught-demihuman", "[Party] Immortal: Harmony Draught - Demihuman", 1404, 20,
            "[Gruppe] Unsterblich: Arznei der Harmonie – Halbmenschen", "Harmony Draughts", "Unsterblich", "harmony-draught-demihuman", party: true),
        Market("immortal-harmony-draught-edania", "[Party] Immortal: Harmony Draught - Edania", 1408, 20,
            "[Gruppe] Unsterblich: Arznei der Harmonie – Edania", "Harmony Draughts", "Unsterblich", "harmony-draught-edania", party: true),
        Market("immortal-harmony-draught-human", "[Party] Immortal: Harmony Draught - Human", 1402, 20,
            "[Gruppe] Unsterblich: Arznei der Harmonie – Menschen", "Harmony Draughts", "Unsterblich", "harmony-draught-human", party: true),
        Market("immortal-harmony-draught-kamasylvia", "[Party] Immortal: Harmony Draught - Kamasylvia", 1406, 20,
            "[Gruppe] Unsterblich: Arznei der Harmonie – Kamasilvia", "Harmony Draughts", "Unsterblich", "harmony-draught-kamasylvia", party: true),
        Market("harmony-draught", "Harmony Draught", 1399, 20,
            "Arznei der Harmonie", "Harmony Draughts", "Standard", "harmony-draught"),
        Market("immortal-harmony-draught", "Immortal: Harmony Draught", 1400, 20,
            "Unsterblich: Arznei der Harmonie", "Harmony Draughts", "Unsterblich", "harmony-draught"),
        // Parfüms
        Market("immortal-perfume-of-bracing-spirits", "Immortal: Perfume of Bracing Spirits", 875, 20,
            "Unsterblich: Belebendes Geisterparfüm", "Parfüms", "Unsterblich", "perfume-of-bracing-spirits"),
        Market("immortal-perfume-of-charm", "Immortal: Perfume of Charm", 877, 20,
            "Unsterblich: Parfüm des Charmes", "Parfüms", "Unsterblich", "perfume-of-charm"),
        Market("immortal-perfume-of-courage", "Immortal: Perfume of Courage", 1167, 20,
            "Unsterblich: Parfüm des Mutes", "Parfüms", "Unsterblich", "perfume-of-courage"),
        Market("immortal-perfume-of-deep-sea", "Immortal: Perfume of Deep Sea", 1169, 20,
            "Unsterblich: Tiefsee-Parfüm", "Parfüms", "Unsterblich", "perfume-of-deep-sea"),
        Market("immortal-perfume-of-envy", "Immortal: Perfume of Envy", 1412, 20,
            "Unsterblich: Parfüm der Sehnsucht", "Parfüms", "Unsterblich", "perfume-of-envy"),
        Market("immortal-perfume-of-insight", "Immortal: Perfume of Insight", 874, 20,
            "Unsterblich: Parfüm der Einsicht", "Parfüms", "Unsterblich", "perfume-of-insight"),
        Market("immortal-perfume-of-khalk", "Immortal: Perfume of Khalk", 1168, 20,
            "Unsterblich: Khalks Parfüm", "Parfüms", "Unsterblich", "perfume-of-khalk"),
        Market("immortal-perfume-of-spirits", "Immortal: Perfume of Spirits", 1166, 20,
            "Unsterblich: Geisterparfüm", "Parfüms", "Unsterblich", "perfume-of-spirits"),
        Market("immortal-perfume-of-tenacity", "Immortal: Perfume of Tenacity", 1414, 20,
            "Unsterblich: Parfüm der Beharrlichkeit", "Parfüms", "Unsterblich", "perfume-of-tenacity"),
        Market("perfume-of-bracing-spirits", "Perfume of Bracing Spirits", 872, 20,
            "Belebendes Geisterparfüm", "Parfüms", "Standard", "perfume-of-bracing-spirits"),
        Market("perfume-of-charm", "Perfume of Charm", 1161, 20,
            "Parfüm des Charmes", "Parfüms", "Standard", "perfume-of-charm"),
        Market("perfume-of-courage", "Perfume of Courage", 734, 20,
            "Parfüm des Mutes", "Parfüms", "Standard", "perfume-of-courage"),
        Market("perfume-of-deep-sea", "Perfume of Deep Sea", 771, 20,
            "Tiefsee-Parfüm", "Parfüms", "Standard", "perfume-of-deep-sea"),
        Market("perfume-of-envy", "Perfume of Envy", 1411, 20,
            "Parfüm der Sehnsucht", "Parfüms", "Standard", "perfume-of-envy"),
        Market("perfume-of-insight", "Perfume of Insight", 1200, 20,
            "Parfüm der Einsicht", "Parfüms", "Standard", "perfume-of-insight"),
        Market("perfume-of-khalk", "Perfume of Khalk", 748, 20,
            "Khalks Parfüm", "Parfüms", "Standard", "perfume-of-khalk"),
        Market("perfume-of-spirits", "Perfume of Spirits", 781, 20,
            "Geisterparfüm", "Parfüms", "Standard", "perfume-of-spirits"),
        Market("perfume-of-swiftness", "Perfume of Swiftness", 735, 20,
            "Parfüm der Schnelligkeit", "Parfüms", "Standard", "perfume-of-swiftness"),
        Market("perfume-of-tenacity", "Perfume of Tenacity", 1413, 20,
            "Parfüm der Beharrlichkeit", "Parfüms", "Standard", "perfume-of-tenacity"),
        Market("perfume-of-verdure", "Perfume of Verdure", 890, 20,
            "Parfüm des Grüns", "Parfüms", "Standard", "perfume-of-verdure"),
        // Kostenpflichtige Zeltbuffs
        Tent("tent-adventurers-confidence", "[Camp] Adventurer's Confidence", 60, 600_000m,
            "Direktbuff", "tent-camp-adventurer-s-confidence", "https://bdocodex.com/us/item/43929/"),
        Tent("tent-adventures-boon-60", "Adventure's Boon (60 min)", 60, 1_750_000m,
            "Schriftrolle / 60 Min.", "tent-adventure-s-boon", "https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=120"),
        Tent("tent-adventures-boon-120", "Adventure's Boon (120 min)", 120, 3_500_000m,
            "Direktbuff / 120 Min.", "tent-adventure-s-boon", "https://www.tw.playblackdesert.com/zh-TW/Wiki?wikiNo=763"),
        Tent("tent-adventures-boon-300", "Adventure's Boon (300 min)", 300, 12_000_000m,
            "Direktbuff / 300 Min.", "tent-adventure-s-boon", "https://www.tw.playblackdesert.com/zh-TW/Wiki?wikiNo=763"),
        Tent("tent-adventurers-luck-i", "Adventurer's Luck I", 60, 10_000_000m,
            "Beuterate +10%", "tent-adventurer-s-luck-i", "https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=120"),
        Tent("tent-adventurers-luck-ii", "Adventurer's Luck II", 60, 20_000_000m,
            "Beuterate +20%", "tent-adventurer-s-luck-ii", "https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=120"),
        Tent("tent-adventurers-luck-iii", "Adventurer's Luck III", 60, 30_000_000m,
            "Beuterate +30%", "tent-adventurer-s-luck-iii", "https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=120"),
        Tent("tent-adventurers-luck-iv", "Adventurer's Luck IV", 60, 40_000_000m,
            "Beuterate +40%", "tent-adventurer-s-luck-iv", "https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=120"),
        Tent("tent-adventurers-luck-v", "Adventurer's Luck V", 60, 50_000_000m,
            "Beuterate +50%", "tent-adventurer-s-luck-v", "https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=120"),
        Tent("tent-body-enhancement-60", "Body Enhancement (60 min)", 60, 1_000_000m,
            "60 Min.", "tent-body-enhancement", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-body-enhancement-90", "Body Enhancement (90 min)", 90, 1_500_000m,
            "90 Min.", "tent-body-enhancement", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-body-enhancement-120", "Body Enhancement (120 min)", 120, 2_250_000m,
            "120 Min.", "tent-body-enhancement", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-body-enhancement-180", "Body Enhancement (180 min)", 180, 4_500_000m,
            "180 Min.", "tent-body-enhancement", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-body-enhancement-300", "Body Enhancement (300 min)", 300, 10_000_000m,
            "300 Min.", "tent-body-enhancement", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-turning-gates-60", "Turning Gates (60 min)", 60, 200_000m,
            "60 Min.", "tent-turning-gates", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-turning-gates-90", "Turning Gates (90 min)", 90, 300_000m,
            "90 Min.", "tent-turning-gates", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-turning-gates-120", "Turning Gates (120 min)", 120, 450_000m,
            "120 Min.", "tent-turning-gates", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-turning-gates-180", "Turning Gates (180 min)", 180, 900_000m,
            "180 Min.", "tent-turning-gates", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        Tent("tent-turning-gates-300", "Turning Gates (300 min)", 300, 2_000_000m,
            "300 Min.", "tent-turning-gates", "https://blackdesert.pearlabyss.com/TR/en-US/Game/Wiki?_masterWikiNo=74"),
        // Mystic-Beasts-Schriftrollen
        Market("mystic-beasts-accuracy", "Blessing of Mystic Beasts - Accuracy", 767970, 60,
            "Segen der mystischen Bestie – Präzision", "Mystic-Beasts-Schriftrollen", "Standard", "mystic-beasts-accuracy"),
        Market("mystic-beasts-all-ap", "Blessing of Mystic Beasts - All AP", 767969, 60,
            "Segen der mystischen Bestie – Gesamte AK", "Mystic-Beasts-Schriftrollen", "Standard", "mystic-beasts-all-ap"),
        Market("mystic-beasts-damage-reduction", "Blessing of Mystic Beasts - Damage Reduction", 767971, 60,
            "Segen der mystischen Bestie – Schadensreduktion", "Mystic-Beasts-Schriftrollen", "Standard", "mystic-beasts-damage-reduction"),
        Market("mystic-beasts-evasion", "Blessing of Mystic Beasts - Evasion", 767972, 60,
            "Segen der mystischen Bestie – Ausweichen", "Mystic-Beasts-Schriftrollen", "Standard", "mystic-beasts-evasion"),
        Market("mystic-beasts-life-skill-mastery", "Blessing of Mystic Beasts - Life Skill Mastery", 790781, 60,
            "Segen der mystischen Bestie – Meisterung der Arbeitstalente", "Mystic-Beasts-Schriftrollen", "Standard", "mystic-beasts-life-skill-mastery"),
        Market("mystic-beasts-max-hp", "Blessing of Mystic Beasts - Max HP", 767973, 60,
            "Segen der mystischen Bestie – Max. LP", "Mystic-Beasts-Schriftrollen", "Standard", "mystic-beasts-max-hp"),
    });

    /// <summary>Previously supported items remain valid when restoring saved sessions.</summary>
    public static IReadOnlyList<BuffDefinition> HistoryDefinitions { get; } = Array.AsReadOnly(Definitions.Concat(new[]
    {
        new BuffDefinition("beasts-draught", "Beast's Draught", 792, TimeSpan.FromMinutes(20)),
        new BuffDefinition("giants-draught", "Giant's Draught", 793, TimeSpan.FromMinutes(20)),
        new BuffDefinition("frenzy-draught", "Frenzy Draught", 795, TimeSpan.FromMinutes(20)),
    }).ToArray());

    /// <summary>
    /// A consumed item's cost is its full purchase price, without marketplace sale tax.
    /// Missing market prices remain unknown; offline quotes retain their age and region.
    /// NPC prices never enter the market lookup, including tent effects that have item IDs.
    /// </summary>
    public static BuffPrice? GetPrice(BuffDefinition definition, LootPriceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (definition.FixedUnitPrice is { } fixedPrice)
            return fixedPrice > 0
                ? new BuffPrice(fixedPrice, snapshot.Region, null, false) { Source = BuffPriceSource.FixedNpc }
                : null;
        if (definition.MarketItemId is not { } marketItemId) return null;
        var marketDefinition = Definitions.FirstOrDefault(item => item.MarketItemId == marketItemId && item.FixedUnitPrice is null);
        if (marketDefinition is null || !snapshot.TryGetQuote(marketDefinition.Name, out var quote) ||
            quote.UnitPrice <= 0) return null;
        return new BuffPrice(quote.UnitPrice, snapshot.Region, quote.FetchedAt, quote.IsStale);
    }

    internal static IEnumerable<LootPriceDefinition> MarketDefinitions() => Definitions
        .Where(static definition => definition.MarketItemId.HasValue && definition.FixedUnitPrice is null)
        .Select(static definition => new LootPriceDefinition(definition.Name,
            LootPriceKind.Market, definition.MarketItemId));

    private static BuffDefinition Market(string id, string name, int marketItemId, int durationMinutes,
        string localizedName, string category, string variant, string recognitionGroup, bool party = false) =>
        new(id, name, marketItemId, TimeSpan.FromMinutes(durationMinutes))
        {
            LocalizedName = localizedName,
            Category = category,
            Variant = variant,
            RecognitionGroup = recognitionGroup,
            RequiresConsumptionConfirmation = party,
            SourceUrl = $"https://bdocodex.com/us/item/{marketItemId}/",
        };

    private static BuffDefinition Tent(string id, string name, int durationMinutes, decimal fixedUnitPrice,
        string variant, string recognitionGroup, string sourceUrl) =>
        new(id, name, null, TimeSpan.FromMinutes(durationMinutes))
        {
            Category = "Kostenpflichtige Zeltbuffs",
            Variant = variant,
            FixedUnitPrice = fixedUnitPrice,
            RecognitionGroup = recognitionGroup,
            SourceUrl = sourceUrl,
        };
}
