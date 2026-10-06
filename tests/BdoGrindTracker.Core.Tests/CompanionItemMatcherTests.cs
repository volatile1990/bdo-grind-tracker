using System.Text;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class CompanionItemMatcherTests
{
    [Fact]
    public void RepeatedFuzzyMatchReusesTheImmutableResultWithoutNormalizingTheCacheKey()
    {
        var matcher = new CompanionItemMatcher(["Black Crystal Fragment"]);
        Assert.True(matcher.TryMatch("Black CrystaI Fragment", 10, false, out var first));
        Assert.True(matcher.TryMatch("Black CrystaI Fragment", 10, false, out var second));
        Assert.Same(first, second);

        Assert.True(matcher.TryMatch("Black CrystaI  Fragment", 10, false, out var different));
        Assert.Equal("Black CrystaI  Fragment", different!.ObservedText);
        Assert.NotSame(first, different);
    }

    [Fact]
    public void CachedFailuresDoNotCrossQuantityModeOrCatalogBoundaries()
    {
        var trash = new CompanionItemMatcher(["Outlaw's Mark"]);
        Assert.False(trash.TryMatch("Outlaw's Mark", 1, false, out _));
        Assert.False(trash.TryMatch("Outlaw's Mark", 1, false, out _));
        Assert.True(trash.TryMatch("Outlaw's Mark", 2, false, out _));

        const string banner = "You obtained Caphras Stone today";
        var rare = new CompanionItemMatcher(["Caphras Stone"]);
        Assert.False(rare.TryMatch(banner, 1, false, out _));
        Assert.True(rare.TryMatch(banner, 1, true, out var match));
        Assert.Equal("Caphras Stone", match!.CanonicalName);
        var otherCatalog = new CompanionItemMatcher(["Memory Fragment"]);
        Assert.False(otherCatalog.TryMatch(banner, 1, true, out _));
    }

    [Fact]
    public void CachedMatchingEqualsTheOriginalAlgorithmAcrossKeysAndConcurrentReads()
    {
        var matcher = new CompanionItemMatcher([
            "Black Crystal Fragment", "Black Gem Fragment", "Caphras Stone", "Memory Fragment",
            "Outlaw's Mark", "Apeiron Ring", "Ominous Ring", "BON Origin Shard", "WON Origin Shard"]);
        string[] texts = ["Black Crystal Fragment", "Black CrystaI Fragment", "Black Gem Fragrnent",
            ItemLocalizationCatalog.GermanNames["Black Crystal Fragment"], "Outlaw's Mark",
            "You obtained Caphras Stone today", "TRI: Apeiron Ring", "Origin Shard", "", "ZZZZZZ"];
        var cases = texts.SelectMany(text => new[] { -1, 0, 1, 2 }.SelectMany(quantity =>
            new[] { false, true }.Select(rare =>
            {
                var success = matcher.TryMatchUncached(text, quantity, rare, out var match);
                return (Text: text, Quantity: quantity, Rare: rare, Success: success, Match: match);
            }))).ToArray();

        Parallel.For(0, cases.Length * 4, index =>
        {
            var expected = cases[index % cases.Length];
            Assert.Equal(expected.Success, matcher.TryMatch(expected.Text, expected.Quantity, expected.Rare, out var actual));
            Assert.Equal(expected.Match, actual);
        });
        Assert.Throws<ArgumentNullException>(() => matcher.TryMatch(null!, 1, false, out _));
    }

    [Theory]
    [InlineData("Lesha's Artifact - All Damage Reduction")]
    [InlineData("Lesha's Artifact - Melee Damage Reduction")]
    [InlineData("Dehkia's Artifact - All Damage Reduction")]
    [InlineData("Kehelle's Artifact - Black Spirit's Rage Max Increase")]
    public void FullEnglishArtifactNamesRetainIdentityAfterOcrRemovesPunctuation(string canonical)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        foreach (var rare in new[] { false, true })
        {
            Assert.True(matcher.TryMatch(canonical.Replace("-", ""), 1, rare, out var match));
            Assert.Equal(canonical, match!.CanonicalName);
            Assert.True(match.IsExact);
        }
    }

    [Theory]
    [InlineData("Broken Vestige of Ebonmere")]
    [InlineData("BON Origin Shard")]
    [InlineData("Black Gem Fragment")]
    public void ExactNamesUseNativeGlobalCatalogWithoutConfidenceGate(string name)
    {
        CompanionItemMatcher matcher = new([name]);
        Assert.True(matcher.TryMatch(name, 1, false, out CompanionItemMatch? match));
        Assert.NotNull(match);
        Assert.True(match.IsExact);
        Assert.Equal(0, match.NormalizedDistance);
    }

    [Fact]
    public void NormalModeDoesNotRequireRunnerUpMarginOrRepeatedRecognition()
    {
        CompanionItemMatcher matcher = new(["Black Crystal Fragment", "Black Gem Fragment"]);
        Assert.True(matcher.TryMatch("Black CrystaI Fragment", 10, false, out CompanionItemMatch? match));
        Assert.NotNull(match);
        Assert.Equal("Black Crystal Fragment", match.CanonicalName);
        Assert.False(match.IsExact);
    }

    [Fact]
    public void BonExactMatchDoesNotReceiveFuzzyPenalty()
    {
        CompanionItemMatcher matcher = new(["BON Origin Shard", "JIN Origin Shard", "WON Origin Shard"]);
        Assert.True(matcher.TryMatch("BON Origin Shard", 1, false, out CompanionItemMatch? match));
        Assert.NotNull(match);
        Assert.Equal(0, match.NormalizedDistance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCountOneTrashFilterIsPreservedForUnconfiguredItems(bool rare)
    {
        CompanionItemMatcher matcher = new(["Outlaw's Mark"]);
        Assert.Null(DropQuantityCatalog.GetBounds(null, "Outlaw's Mark"));
        Assert.False(matcher.TryMatch("Outlaw's Mark", 1, rare, out _));
        Assert.True(matcher.TryMatch("Outlaw's Mark", 2, rare, out _));
    }

    [Fact]
    public void RareModeCanMatchNativeWordBoundedSubstring()
    {
        CompanionItemMatcher matcher = new(["Caphras Stone"]);
        Assert.True(matcher.TryMatch("You obtained Caphras Stone today", 1, true, out CompanionItemMatch? match));
        Assert.NotNull(match);
        Assert.Equal("Caphras Stone", match.CanonicalName);
        Assert.Equal(0, match.NormalizedDistance);
    }

    [Fact]
    public void NamesOutsideNativeDistanceThresholdAreRejected()
    {
        CompanionItemMatcher matcher = new(["Caphras Stone"]);
        Assert.False(matcher.TryMatch("ZZZZZZ", 1, false, out _));
    }

    [Fact]
    public void LevenshteinUsesUtf8Bytes()
    {
        Assert.Equal(2, CompanionItemMatcher.LevenshteinDistance(Encoding.UTF8.GetBytes("ö"), []));
        Assert.Equal(3, CompanionItemMatcher.LevenshteinDistance("kitten"u8, "sitting"u8));
    }

    [Fact]
    public void MetadataCatalogRetainsIconPaths()
    {
        CompanionRareCatalogEntry item = new("Apeiron Ring", "new_icon/16_ring/example.dds");
        CompanionItemMatcher matcher = new([item]);
        Assert.Equal(item, Assert.Single(matcher.CatalogEntries));
    }
}
