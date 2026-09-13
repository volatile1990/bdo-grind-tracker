namespace BdoGrindTracker.Core.Tests;

public sealed class GermanItemMatcherTests
{
    public static IEnumerable<object[]> Items() => ItemLocalizationCatalog.GermanNames
        .SelectMany(pair => new[] { false, true }.Select(rare => new object[] { pair.Key, pair.Value, rare }));

    [Theory]
    [MemberData(nameof(Items))]
    public void EnglishGermanAndNormalizedNamesResolveToTheSameCanonicalItem(string canonical, string german, bool rare)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        Assert.True(matcher.TryMatch(canonical, 5, rare, out var englishMatch));
        Assert.Equal(canonical, englishMatch!.CanonicalName);
        foreach (var text in new[] { german, ItemLocalizationCatalog.NormalizeGermanName(german) })
        {
            Assert.True(matcher.TryMatch(text, 5, rare, out var match), text);
            Assert.Equal(canonical, match!.CanonicalName);
            Assert.True(match.IsExact);
        }
        Assert.Equal(ItemLocalizationCatalog.GermanNames.Count, matcher.CatalogEntries.Count);
        Assert.DoesNotContain(matcher.CatalogEntries, entry => entry.Name == german && german != canonical);
    }

    [Fact]
    public void EveryAllowedDropHasAGermanNameIncludingEvents()
    {
        Assert.NotEmpty(ItemLocalizationCatalog.GermanNames);
        foreach (var name in LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems).Concat(LootSpotCatalog.EventItems))
            Assert.True(ItemLocalizationCatalog.GermanNames.ContainsKey(name), name);
    }

    [Theory]
    [InlineData("Chilled Soul Piece", "Eisiges Seelenstück")]
    [InlineData("Chilled Soul Piece", "eisigesseelenstuck")]
    [InlineData("Chilled Soul Piece", "Eisiges Seelenstuckk")]
    [InlineData("Contaminated Coral Piece", "Kontaminiertes Korallenstück")]
    [InlineData("Contaminated Coral Piece", "kontaminierteskorallenstuck")]
    [InlineData("Contaminated Coral Piece", "Kontaminiertes Korallenstuckk")]
    public void UserConfirmedSingleTrashDropsMatchInBothLanguages(string canonical, string observed)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        foreach (var rare in new[] { false, true })
        {
            foreach (var quantity in new[] { 1, 2 })
            {
                Assert.True(matcher.TryMatch(canonical, quantity, rare, out var english));
                Assert.True(matcher.TryMatch(observed, quantity, rare, out var localized));
                Assert.Equal(canonical, english!.CanonicalName);
                Assert.Equal(english.CanonicalName, localized!.CanonicalName);
            }
        }
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
