using System.Text;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class CompanionItemMatcherTests
{
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

    [Fact]
    public void NativeCountOneTrashFilterIsPreserved()
    {
        CompanionItemMatcher matcher = new(["Decayed Cloth"]);
        Assert.False(matcher.TryMatch("Decayed Cloth", 1, false, out _));
        Assert.True(matcher.TryMatch("Decayed Cloth", 2, false, out _));
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
