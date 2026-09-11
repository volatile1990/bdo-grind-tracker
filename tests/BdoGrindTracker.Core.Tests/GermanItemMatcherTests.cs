namespace BdoGrindTracker.Core.Tests;

public sealed class GermanItemMatcherTests
{
    public static IEnumerable<object[]> Items() => ItemLocalizationCatalog.GermanNames
        .SelectMany(pair => new[] { false, true }.Select(rare => new object[] { pair.Key, pair.Value, rare }));

    [Theory]
    [MemberData(nameof(Items))]
    public void EveryGermanNameAndItsNormalizedOcrFormResolveToTheSameCanonicalItem(string canonical, string german, bool rare)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        foreach (var text in new[] { german, ItemLocalizationCatalog.NormalizeGermanName(german) })
        {
            Assert.True(matcher.TryMatch(text, 1, rare, out var match), text);
            Assert.Equal(canonical, match!.CanonicalName);
            Assert.True(match.IsExact);
        }
        Assert.Equal(58, matcher.CatalogEntries.Count);
        Assert.DoesNotContain(matcher.CatalogEntries, entry => entry.Name == german && german != canonical);
    }

    [Fact]
    public void EveryAllowedDropHasAGermanNameIncludingEvents()
    {
        Assert.Equal(58, ItemLocalizationCatalog.GermanNames.Count);
        foreach (var name in LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems).Concat(LootSpotCatalog.EventItems))
            Assert.True(ItemLocalizationCatalog.GermanNames.ContainsKey(name), name);
    }

    [Theory]
    [InlineData("Helm eines Anhangers EIions", "Elion Follower's Helmet")]
    [InlineData("Schwarzkristallfragmenl", "Black Crystal Fragment")]
    [InlineData("Veredelte Essenz des Verschlingcns", "Refined Essence of Devouring")]
    [InlineData("Verrusste Gurtelverzierung", "Scorched Belt Ornament")]
    [InlineData("Apeiron Earrlng", "Apeiron Earring")]
    public void UnambiguousOcrTyposKeepTheRightIdentity(string text, string canonical)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        Assert.True(matcher.TryMatch(text, 5, false, out var match));
        Assert.Equal(canonical, match!.CanonicalName);
    }

    [Theory]
    [InlineData("ON-Kristall des wandernden Ursprungs")]
    [InlineData("Kristall des wandernden Ursprungs")]
    [InlineData("Weißer Glanz der Urklasse")]
    [InlineData("Dämmerung des Endes")]
    [InlineData("Unbekannter Gegenstand")]
    public void IncompleteRareVariantsAreNotGuessed(string text)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        Assert.False(matcher.TryMatch(text, 1, true, out _));
    }

    [Fact]
    public void GermanAliasesCannotIntroduceItemsOutsideTheProvidedCatalog()
    {
        var matcher = new CompanionItemMatcher(["Black Stone"]);
        Assert.False(matcher.TryMatch("BON-Kristall des wandernden Ursprungs", 1, false, out _));
    }
}
