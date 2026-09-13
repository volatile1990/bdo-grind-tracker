namespace BdoGrindTracker.Core.Tests;

public sealed class RareItemIdentityTests
{
    [Theory]
    [InlineData("Deboreka Ring", "TRI")]
    [InlineData("Deboreka Necklace", "PRI")]
    [InlineData("Apeiron Ring", "DUO")]
    [InlineData("Apeiron Necklace", "TET")]
    public void EnhancedRareAccessoriesCannotResolveToBaseItemsInEitherLanguage(string name, string enhancement)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        var german = ItemLocalizationCatalog.GermanNames[name];
        Assert.False(matcher.TryMatch($"{enhancement}: {name}", 1, true, out _));
        Assert.False(matcher.TryMatch($"{enhancement}: {german}", 1, true, out _));
        Assert.False(matcher.TryMatch(ItemLocalizationCatalog.NormalizeGermanName(
            $"{enhancement}: {german}"), 1, true, out _));
    }

    [Theory]
    [InlineData("Deboreka Rlng", "Deboreka Ring")]
    [InlineData("Deborekas Rlng", "Deboreka Ring")]
    [InlineData("Deborekaas Ring", "Deboreka Ring")]
    [InlineData("Apeiron-Rlng", "Apeiron Ring")]
    [InlineData("WON Crystal of Ruln", "WON Crystal of Ruin")]
    [InlineData("BON Crystal of Ruln", "BON Crystal of Ruin")]
    [InlineData("JIN Crystal of Dusky Ruln", "JIN Crystal of Dusky Ruin")]
    [InlineData("HAN Crystal of Dusky Ruln", "HAN Crystal of Dusky Ruin")]
    [InlineData("HAN-Kristall des finsteren Rulns", "HAN Crystal of Dusky Ruin")]
    [InlineData("JIN-Kristall des Rulns", "JIN Crystal of Ruin")]
    [InlineData("H4N Crystal of Ruin", "HAN Crystal of Ruin")]
    [InlineData("W0N Crystal of Ruin", "WON Crystal of Ruin")]
    [InlineData("You obtained HAN Crystal of Ruin today", "HAN Crystal of Ruin")]
    public void FullUnambiguousRareNamesStillAllowOcrTypos(string observed, string expected)
    {
        var matcher = new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys);
        Assert.True(matcher.TryMatch(observed, 1, true, out var match));
        Assert.Equal(expected, match!.CanonicalName);
    }

    [Theory]
    [InlineData("Crystal of Ruin")]
    [InlineData("Crystal of Dusky Ruin")]
    [InlineData("ON Crystal of Ruin")]
    [InlineData("IN Crystal of Dusky Ruin")]
    [InlineData("BAN Crystal of Ruin")]
    [InlineData("Kristall des Ruins")]
    [InlineData("Kristall des finsteren Ruins")]
    [InlineData("ON-Kristall des Ruins")]
    public void MissingOrAmbiguousRuinCrystalTiersAreNotGuessed(string observed)
    {
        foreach (var names in new[] { ItemLocalizationCatalog.GermanNames.Keys,
            new[] { "WON Crystal of Ruin", "WON Crystal of Dusky Ruin" }.AsEnumerable() })
        {
            var matcher = new CompanionItemMatcher(names);
            Assert.False(matcher.TryMatch(observed, 1, true, out _));
        }
    }
}
