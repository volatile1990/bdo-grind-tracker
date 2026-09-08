using System.Collections.Frozen;

namespace BdoGrindTracker.Core;

/// <summary>
/// A minimum base drop from any regular enemy at a spot, before loot bonuses.
/// Null means unverified, never zero. See docs/TRASH_MINIMUMS.md for sources.
/// </summary>
public sealed record TrashLootMinimum(string SpotId, string ItemName, uint? MinimumQuantity);

public static class TrashLootMinimumCatalog
{
    // Historical v4 compatibility table. Current live quantities are supplied
    // per observation by DropQuantityCatalog, including the user's trash values.
    // Research on 2026-09-07 found item/spot associations, but no verified
    // spot-wide minima. Elite-only ranges and observed buffed drops are not
    // safe replacements. Leave entries null until that evidence is available.
    public static IReadOnlyList<TrashLootMinimum> Entries { get; } = Array.AsReadOnly<TrashLootMinimum>(
    [
        new(LootSpotCatalog.AphrodonId, "Branch of Abundance", null),
        new(LootSpotCatalog.HermesiaId, "Black Crystal Fragment", null),
        new(LootSpotCatalog.MagaiaId, "Elion Follower's Helmet", null),
        new(LootSpotCatalog.AresionId, "Scorched Belt Ornament", null),
        new(LootSpotCatalog.ScalesOfJudgmentId, "Elion Follower's Mark", null),
        new(LootSpotCatalog.EventHorizonId, "Broken Gloves of the Void", null),
    ]);

    /// <summary>Only verified values participate in the final estimate.</summary>
    public static IReadOnlyDictionary<string, uint> MinimumQuantities { get; } = Entries
        .Where(entry => entry.MinimumQuantity.HasValue)
        .ToFrozenDictionary(entry => entry.ItemName, entry => entry.MinimumQuantity!.Value, StringComparer.Ordinal);
}
