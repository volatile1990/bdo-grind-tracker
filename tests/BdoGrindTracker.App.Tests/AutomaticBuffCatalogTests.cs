using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffCatalogTests
{
    [Fact]
    public void SharedAdventurersLuckArtworkNamesItsFamilyWithoutChoosingATierOrPrice()
    {
        var catalog = AutomaticBuffCatalog.Default;
        var template = Assert.Single(catalog.Templates, item => item.GroupId == "tent-adventurers-luck");

        var definition = catalog.DefinitionFor(template);

        Assert.Equal(5, template.CandidateBuffIds.Length);
        Assert.Equal("automatic-tent-adventurers-luck", definition.Id);
        Assert.Equal("Adventurer's Luck (variant unknown)", definition.Name);
        Assert.Equal("Adventurer's Luck (Variante unbekannt)", definition.DisplayName);
        Assert.Null(definition.MarketItemId);
        Assert.Null(definition.FixedUnitPrice);
        Assert.Empty(definition.DurationVariantIds);
    }

    [Theory]
    [InlineData("tent-body-enhancement", 5)]
    [InlineData("tent-turning-gates", 5)]
    [InlineData("tent-adventures-boon", 3)]
    public void DurationFamiliesExposeEveryPurchaseDurationForRecognition(string family, int count)
    {
        var catalog = AutomaticBuffCatalog.Default;
        var template = Assert.Single(catalog.Templates, item => item.GroupId == family);
        var definition = catalog.DefinitionFor(template);

        Assert.Equal(count, definition.DurationVariantIds.Count);
        Assert.Equal(template.CandidateBuffIds.Order(), definition.DurationVariantIds.Order());
        Assert.Null(definition.FixedUnitPrice);
    }

    [Fact]
    public void EqualDurationPerfumeVariantsRemainUnresolved()
    {
        var groups = AutomaticBuffCatalog.Default.GroupDefinitions;
        Assert.All(groups.Where(item => item.Category == "Parfüms"), item => Assert.Empty(item.DurationVariantIds));
        Assert.Equal(3, groups.Count(item => item.DurationVariantIds.Count > 0));
    }
}
