using System.Collections.Frozen;
using System.Text.Json;

namespace BdoGrindTracker.Core;

/// <summary>User-confirmed UI channel for every supported OCR item.</summary>
public static class LootSourceCatalog
{
    public const string WrongSourceReason = "wrong-loot-source";

    public static IReadOnlyDictionary<string, LootSource> Entries { get; } = Load();

    public static LootSource GetRequired(string itemName) =>
        GetAllowedSource(itemName) ?? throw new KeyNotFoundException($"No loot source for '{itemName}'.");

    public static LootSource? GetAllowedSource(string itemName)
    {
        ArgumentNullException.ThrowIfNull(itemName);
        return Entries.TryGetValue(itemName, out var source) ? source : null;
    }

    public static bool Allows(string itemName, LootSource source) =>
        source is LootSource.Normal or LootSource.Rare && GetAllowedSource(itemName) == source;

    private sealed record SourceData(int SchemaVersion, string Source, string SourceSha256, SourceEntry[] Entries);
    private sealed record SourceEntry(string ItemName, string Source);

    private static FrozenDictionary<string, LootSource> Load()
    {
        using var stream = typeof(LootSourceCatalog).Assembly.GetManifestResourceStream(
            "BdoGrindTracker.Core.LootSources.json") ?? throw new InvalidDataException("Missing loot source catalog.");
        var data = JsonSerializer.Deserialize<SourceData>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("Invalid loot source catalog.");
        if (data.SchemaVersion != 1 || string.IsNullOrWhiteSpace(data.Source) || data.SourceSha256 is not { Length: 64 } ||
            data.SourceSha256.Any(character => !char.IsAsciiHexDigit(character)) || data.Entries is null)
            throw new InvalidDataException("Invalid loot source catalog provenance.");

        var expected = LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems)
            .Concat(LootSpotCatalog.EventItems).ToHashSet(StringComparer.Ordinal);
        // This name remains in the current OCR vocabulary without a supported spot.
        expected.Add("Black Gem Fragment");
        var entries = new Dictionary<string, LootSource>(StringComparer.Ordinal);
        foreach (var entry in data.Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.ItemName) || !expected.Remove(entry.ItemName))
                throw new InvalidDataException("Loot sources must cover each supported item exactly once.");
            var source = entry.Source switch
            {
                "normal" => LootSource.Normal,
                "rare" => LootSource.Rare,
                _ => throw new InvalidDataException($"Invalid loot source for '{entry.ItemName}'."),
            };
            entries.Add(entry.ItemName, source);
        }
        if (expected.Count > 0)
            throw new InvalidDataException("Loot sources must cover every supported item.");
        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
