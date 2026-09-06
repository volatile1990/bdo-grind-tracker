namespace BdoGrindTracker.Core;

/// <summary>
/// A grind spot and the non-event item names that may be resolved while it is active.
/// </summary>
public sealed class LootSpot
{
    private readonly HashSet<string> allowedItemSet;

    public LootSpot(
        string id,
        string displayName,
        IEnumerable<string> allowedItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(allowedItems);

        var items = allowedItems
            .Select(static item =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(item);
                return item;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (items.Length == 0)
        {
            throw new ArgumentException(
                "A loot spot must allow at least one item.",
                nameof(allowedItems));
        }

        Id = id;
        DisplayName = displayName;
        AllowedItems = Array.AsReadOnly(items);
        allowedItemSet = new HashSet<string>(items, StringComparer.Ordinal);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> AllowedItems { get; }

    public bool Allows(string canonicalItemName)
    {
        ArgumentNullException.ThrowIfNull(canonicalItemName);
        return allowedItemSet.Contains(canonicalItemName);
    }
}

/// <summary>
/// Compile-time loot pools for the supported Edania Part 2 spots, including shared drops.
/// Event loot is deliberately kept outside every spot's default allow-list.
/// </summary>
public static class LootSpotCatalog
{
    public const string AphrodonId = "aphrodon";
    public const string HermesiaId = "hermesia";
    public const string MagaiaId = "magaia";

    private static readonly string[] SharedGlobalItemNames =
    [
        "Ancient Spirit Dust",
        "Black Stone",
        "Caphras Stone",
        "Laila's Petal",
        // Historically documented world drop; included for possible loot rather
        // than assuming an incomplete Main Loot table proves it impossible.
        "Pure Black Stone",
    ];

    private static readonly string[] SharedHighestTierItemNames =
    [
        "Refined Origin of Hunger",
        "Refined Essence of Devouring",
        "Crimson Primordial Pigment - Sovereign",
        "Violet Primordial Pigment - Sovereign",
        "Violet Primordial Pigment - Edana",
        "Sunset Primordial Pigment - Edana",
        "Crimson Primordial Luster - Sovereign",
        "Violet Primordial Luster - Sovereign",
        "Violet Primordial Luster - Edana",
        "Sunset Primordial Luster - Edana",
        "Corrupt Oil of Immortality",
    ];

    private static readonly string[] EventItemNames =
    [
        "[Event] Mysterious Ore",
    ];

    private static readonly LootSpot[] SupportedSpots =
    [
        new(
            AphrodonId,
            "Aphrodon Temple",
            WithSharedItems(
                "WON Wandering Origin Crystal",
                "WON Origin Shard",
                "Silent Fragment of Origin",
                "Silent Crystal of Origin",
                "Embers of Ynix - Armor",
                "Twilight of the End - Earring",
                "Apeiron Earring",
                "Broken Vestige of Goldroot",
                "Nev's Fragment",
                "Fusion Shard",
                "Branch of Abundance")),
        new(
            HermesiaId,
            "Hermesia Inner Castle",
            WithSharedItems(
                "BON Wandering Origin Crystal",
                "BON Origin Shard",
                "Embers of Ynix - Helmet",
                "Apeiron Earring",
                "Apeiron Ring",
                "Twilight of the End - Earring",
                "Twilight of the End - Ring",
                "Broken Vestige of Ebonmere",
                "Nev's Fragment",
                "Fusion Shard",
                "Silent Fragment of Origin",
                "Silent Crystal of Origin",
                "Black Crystal Fragment")),
        new(
            MagaiaId,
            "Magaia Temple",
            WithSharedItems(
                "Embers of Ynix - Shoes",
                "Twilight of the End - Earring",
                "Twilight of the End - Ring",
                "Twilight of the End - Belt",
                "Apeiron Earring",
                "Apeiron Ring",
                "Apeiron Belt",
                "JIN Wandering Origin Crystal",
                "JIN Origin Shard",
                "Broken Vestige of Everlight",
                "Nev's Fragment",
                "Fusion Shard",
                "Silent Fragment of Origin",
                "Silent Crystal of Origin",
                "Elion Follower's Helmet")),
    ];

    private static readonly HashSet<string> EventItemSet = new(
        EventItemNames,
        StringComparer.Ordinal);

    /// <summary>
    /// Common non-event drops allowed independently of a supported spot's exclusive loot.
    /// </summary>
    public static IReadOnlyList<string> SharedGlobalItems { get; } =
        Array.AsReadOnly(SharedGlobalItemNames);

    public static IReadOnlyList<string> SharedHighestTierItems { get; } =
        Array.AsReadOnly(SharedHighestTierItemNames);

    public static IReadOnlyList<string> EventItems { get; } =
        Array.AsReadOnly(EventItemNames);

    public static IReadOnlyList<LootSpot> Spots { get; } =
        Array.AsReadOnly(SupportedSpots);

    public static LootSpot GetRequired(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SupportedSpots.FirstOrDefault(
                spot => string.Equals(spot.Id, id, StringComparison.Ordinal)) ??
            throw new KeyNotFoundException($"Unknown loot spot '{id}'.");
    }

    public static bool IsEventItem(string canonicalItemName)
    {
        ArgumentNullException.ThrowIfNull(canonicalItemName);
        return EventItemSet.Contains(canonicalItemName);
    }

    private static string[] WithSharedItems(params string[] spotItems) =>
        spotItems
            .Concat(SharedGlobalItemNames)
            .Concat(SharedHighestTierItemNames)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
