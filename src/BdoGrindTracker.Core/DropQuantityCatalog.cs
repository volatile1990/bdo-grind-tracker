using System.Text.Json;

namespace BdoGrindTracker.Core;

/// <summary>
/// Amounts for one displayed monster drop, never limits on a session total.
/// Null is unverified; the existing missing-quantity fallback is one until supplied.
/// Sources and the complete item/spot audit are in docs/DROP_QUANTITIES_RESEARCH.md.
/// </summary>
public sealed record ItemDropQuantity(
    string SpotId, string ItemName, uint? MinimumQuantity, uint? MaximumQuantity, string? Source = null)
{
    public DropQuantityBounds? Bounds => MinimumQuantity.HasValue || MaximumQuantity.HasValue
        ? new(MinimumQuantity ?? 1, MaximumQuantity) : null;
}

public static class DropQuantityCatalog
{
    // The completed workbook is imported once into a bundled, validated snapshot.
    // Tracking never needs Excel or access to the original user's file.
    public static IReadOnlyList<ItemDropQuantity> Entries { get; } = Load();

    private static readonly IReadOnlyDictionary<(string SpotId, string ItemName), ItemDropQuantity> BySpotItem =
        Entries.ToDictionary(entry => (entry.SpotId, entry.ItemName));

    private static readonly IReadOnlyDictionary<string, DropQuantityBounds?> BeforeSpotLock = Entries
        .GroupBy(entry => entry.ItemName).ToDictionary(group => group.Key, group =>
            group.All(entry => entry.Bounds is null) ? null : new DropQuantityBounds(
                group.Min(entry => entry.MinimumQuantity ?? 1),
                group.All(entry => entry.MaximumQuantity.HasValue)
                    ? group.Max(entry => entry.MaximumQuantity!.Value) : null));

    private sealed record QuantityData(string Source, ItemDropQuantity[] Entries);

    private static IReadOnlyList<ItemDropQuantity> Load()
    {
        using var stream = typeof(DropQuantityCatalog).Assembly.GetManifestResourceStream(
            "BdoGrindTracker.Core.DropQuantities.json") ?? throw new InvalidDataException("Missing drop quantity catalog.");
        var data = JsonSerializer.Deserialize<QuantityData>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("Invalid drop quantity catalog.");
        var expected = LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems.Concat(LootSpotCatalog.EventItems)
            .Select(item => (spot.Id, item))).ToHashSet();
        if (string.IsNullOrWhiteSpace(data.Source) || data.Entries is null || data.Entries.Length != expected.Count ||
            data.Entries.Any(entry => entry is null || !expected.Remove((entry.SpotId, entry.ItemName)) ||
                string.IsNullOrWhiteSpace(entry.Source) || entry.MinimumQuantity is null || entry.MaximumQuantity is null))
            throw new InvalidDataException("Drop quantities must cover every item/spot pair exactly once.");
        foreach (var entry in data.Entries) _ = entry.Bounds; // Validate numerical ranges before tracking starts.
        return Array.AsReadOnly(data.Entries.Select(entry => entry with { Source = data.Source + "; " + entry.Source }).ToArray());
    }

    public static ItemDropQuantity GetRequired(string spotId, string itemName) =>
        BySpotItem.TryGetValue((spotId, itemName), out var entry) ? entry :
            throw new KeyNotFoundException($"No drop quantities for '{itemName}' at '{spotId}'.");

    public static DropQuantityBounds? GetBounds(string? spotId, string itemName)
    {
        if (spotId is not null)
            return BySpotItem.GetValueOrDefault((spotId, itemName))?.Bounds;

        // Before the first trash read, only use a range safe for every possible
        // supported spot. An unverified maximum must never become a hard cap.
        return BeforeSpotLock.GetValueOrDefault(itemName);
    }
}
