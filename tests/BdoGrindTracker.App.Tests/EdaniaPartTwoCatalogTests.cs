using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class EdaniaPartTwoCatalogTests
{
    private static readonly string[] SharedHighestTierLoot =
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

    private static readonly IReadOnlyDictionary<string, string[]> SpotLoot =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Aphrodon Temple"] =
            [
                "WON Wandering Origin Crystal",
                "WON Origin Shard",
                "Silent Fragment of Origin",
                "Silent Crystal of Origin",
                "Embers of Ynix - Armor",
                "Twilight of the End - Earring",
                "Apeiron Earring",
                "Sunset Primordial Pigment - Edana",
                "Sunset Primordial Luster - Edana",
                "Corrupt Oil of Immortality",
                "Broken Vestige of Goldroot",
                "Nev's Fragment",
                "Fusion Shard",
                "Branch of Abundance",
            ],
            ["Hermesia Inner Castle"] =
            [
                "BON Wandering Origin Crystal",
                "BON Origin Shard",
                "Embers of Ynix - Helmet",
                "Apeiron Earring",
                "Apeiron Ring",
                "Twilight of the End - Earring",
                "Twilight of the End - Ring",
                "Sunset Primordial Pigment - Edana",
                "Sunset Primordial Luster - Edana",
                "Broken Vestige of Ebonmere",
                "Nev's Fragment",
                "Fusion Shard",
                "Silent Fragment of Origin",
                "Silent Crystal of Origin",
                "Corrupt Oil of Immortality",
                "Black Crystal Fragment",
            ],
            ["Magaia Temple"] =
            [
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
                "Corrupt Oil of Immortality",
                "Sunset Primordial Pigment - Edana",
                "Sunset Primordial Luster - Edana",
                "Elion Follower's Helmet",
            ],
        };

    [Fact]
    public void BundledCatalogContainsKnownMainLootForFirstThreeEdaniaPartTwoSpots()
    {
        var catalog = LoadBundledCatalog();

        foreach (var (spot, expectedLoot) in SpotLoot)
        {
            var missing = expectedLoot.Except(catalog, StringComparer.Ordinal).ToArray();
            Assert.True(missing.Length == 0, $"{spot} is missing: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void BundledCatalogContainsCurrentSharedHighestTierLoot()
    {
        var catalog = LoadBundledCatalog();
        var missing = SharedHighestTierLoot.Except(catalog, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0,
            $"The shared #HighestTier loot pool is missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void SpotCatalogIncludesGlobalLootAsWellAsMainAndHighestTierLoot()
    {
        foreach (var (displayName, knownLoot) in SpotLoot)
        {
            var spot = Assert.Single(
                LootSpotCatalog.Spots,
                candidate => candidate.DisplayName == displayName);
            var expected = knownLoot
                .Concat(SharedHighestTierLoot)
                .Concat(LootSpotCatalog.SharedGlobalItems)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.Empty(expected.Except(spot.AllowedItems, StringComparer.Ordinal));
            Assert.Empty(spot.AllowedItems.Except(expected, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void EverySpotAllowedItemExistsInBundledGlobalVocabulary()
    {
        var globalVocabulary = LoadBundledCatalog();

        foreach (var spot in LootSpotCatalog.Spots)
        {
            Assert.Empty(
                spot.AllowedItems.Except(globalVocabulary, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void NoVocabularyEntryIsSilentlyMissingFromEveryLootPool()
    {
        // Black Gem Fragment intentionally remains a competing OCR candidate:
        // it must not be rematched into Black Crystal Fragment after spot lock.
        string[] explicitlyExcluded = ["Black Gem Fragment"];
        var classified = LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems)
            .Concat(LootSpotCatalog.EventItems).Concat(explicitlyExcluded)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Empty(LoadBundledCatalog().Except(classified, StringComparer.Ordinal));
    }

    [Fact]
    public void AllGlobalAndEventItemsArePresentAndExactlyRecognizedInBothChannels()
    {
        var matcher = new CompanionItemMatcher(LoadBundledCatalog());
        foreach (var name in LootSpotCatalog.SharedGlobalItems.Concat(LootSpotCatalog.EventItems))
        {
            foreach (var rare in new[] { false, true })
            {
                Assert.True(matcher.TryMatch(name, 1, rare, out var match), name);
                Assert.Equal(name, match!.CanonicalName);
                Assert.True(match.IsExact);
            }
        }
    }

    [Fact]
    public void BlackGemFragmentRemainsANegativeCandidateForSupportedSpots()
    {
        var globalVocabulary = LoadBundledCatalog();
        Assert.Contains("Black Gem Fragment", globalVocabulary);
        var matcher = new CompanionItemMatcher(globalVocabulary);

        foreach (var spot in LootSpotCatalog.Spots)
        {
            Assert.True(matcher.TryMatch("Black Gem Fragment", 10, false, out var result));
            Assert.Equal("Black Gem Fragment", result!.CanonicalName);
            Assert.False(spot.Allows(result.CanonicalName));
        }
    }

    [Theory]
    [InlineData(LootSpotCatalog.AphrodonId, "Branch of Abundance")]
    [InlineData(LootSpotCatalog.HermesiaId, "Black Crystal Fragment")]
    [InlineData(LootSpotCatalog.MagaiaId, "Elion Follower's Helmet")]
    public void EveryEdaniaPartTwoTrashLootNameResolvesExactly(
        string spotId,
        string trashLoot)
    {
        var matcher = new CompanionItemMatcher(LoadBundledCatalog());
        Assert.True(matcher.TryMatch(trashLoot, 2, false, out var result));
        Assert.True(result!.IsExact);
        Assert.Equal(trashLoot, result.CanonicalName);
        var spotLock = new AutomaticLootSpotLock();
        spotLock.Observe([result.CanonicalName]);
        Assert.Equal(spotId, spotLock.Spot!.Id);
    }

    private static string[] LoadBundledCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "items.en.txt");
        Assert.True(File.Exists(path), $"Bundled item catalog was not found at {path}.");

        return File.ReadLines(path)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
