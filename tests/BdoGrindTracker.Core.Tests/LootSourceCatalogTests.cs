using System.Text.Json;

namespace BdoGrindTracker.Core.Tests;

public sealed class LootSourceCatalogTests
{
    [Fact]
    public void UserAssignmentsCoverEverySpotEventAndTheVocabularyOnlyItemExactlyOnce()
    {
        var expected = LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems)
            .Concat(LootSpotCatalog.EventItems).Append("Black Gem Fragment")
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(expected.SetEquals(LootSourceCatalog.Entries.Keys));
        Assert.Equal(252, LootSourceCatalog.Entries.Count);
        Assert.Equal(163, LootSourceCatalog.Entries.Count(entry => entry.Value == LootSource.Normal));
        Assert.Equal(89, LootSourceCatalog.Entries.Count(entry => entry.Value == LootSource.Rare));
    }

    [Fact]
    public void DirectSilverDropsHaveNoActiveRecognitionChannel()
    {
        Assert.Null(LootSourceCatalog.GetAllowedSource("Silver"));
        Assert.False(LootSourceCatalog.Allows("Silver", LootSource.Normal));
        Assert.False(LootSourceCatalog.Allows("Silver", LootSource.Rare));
    }

    [Theory]
    [InlineData("Broken Vestige of Everlight", LootSource.Rare)]
    [InlineData("Silent Crystal of Origin", LootSource.Rare)]
    [InlineData("Elion Follower's Mark", LootSource.Normal)]
    [InlineData("Caphras Stone", LootSource.Normal)]
    [InlineData("Intricately Patterned Mystical Shard", LootSource.Normal)]
    [InlineData("Black Gem Fragment", LootSource.Normal)]
    [InlineData("Crimson Primordial Luster - Sovereign", LootSource.Normal)]
    [InlineData("Sunset Primordial Luster - Edana", LootSource.Normal)]
    [InlineData("Turquoise Primordial Luster - Edana", LootSource.Normal)]
    [InlineData("Turquoise Primordial Luster - Sovereign", LootSource.Normal)]
    [InlineData("Violet Primordial Luster - Edana", LootSource.Normal)]
    [InlineData("Violet Primordial Luster - Sovereign", LootSource.Normal)]
    [InlineData("White Primordial Luster - Edana", LootSource.Normal)]
    [InlineData("White Primordial Luster - Sovereign", LootSource.Normal)]
    public void UserDecisionsAllowOnlyTheAssignedChannel(string itemName, LootSource expected)
    {
        Assert.Equal(expected, LootSourceCatalog.GetRequired(itemName));
        Assert.True(LootSourceCatalog.Allows(itemName, expected));
        Assert.False(LootSourceCatalog.Allows(itemName,
            expected == LootSource.Normal ? LootSource.Rare : LootSource.Normal));
    }

    [Fact]
    public void UnclassifiedNamesAndInvalidChannelsFailClosed()
    {
        const string unknown = "Future unclassified loot";
        Assert.Null(LootSourceCatalog.GetAllowedSource(unknown));
        Assert.Throws<KeyNotFoundException>(() => LootSourceCatalog.GetRequired(unknown));
        Assert.False(LootSourceCatalog.Allows(unknown, LootSource.Normal));
        Assert.False(LootSourceCatalog.Allows(unknown, LootSource.Rare));
        Assert.False(LootSourceCatalog.Allows("Caphras Stone", (LootSource)2));
    }

    [Fact]
    public void HistoricalContextsWithoutChannelMetadataRemainUnrestricted()
    {
        const string json = """{"name":"Old item","aliases":[],"isFixedUnit":true}""";
        var oldEntry = JsonSerializer.Deserialize<LifetimeParsingCatalogEntry>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(oldEntry);
        Assert.Null(oldEntry.AllowedSource);
        Assert.True(oldEntry.IsFixedUnit);
    }

    [Fact]
    public void ContextEqualityAndSerializationPreserveChannelAssignments()
    {
        var normal = new LifetimeParsingContext(0, [new("Item", [], allowedSource: LootSource.Normal)]);
        var rare = new LifetimeParsingContext(0, [new("Item", [], allowedSource: LootSource.Rare)]);
        var legacy = new LifetimeParsingContext(0, [new("Item", [])]);
        Assert.False(normal.HasSameCatalog(rare));
        Assert.False(normal.HasSameCatalog(legacy));
        var restored = JsonSerializer.Deserialize<LifetimeParsingContext>(JsonSerializer.Serialize(rare));
        Assert.NotNull(restored);
        Assert.True(rare.HasSameCatalog(restored));
        Assert.Equal(LootSource.Rare, Assert.Single(restored.Catalog).AllowedSource);
        Assert.Throws<ArgumentException>(() => new LifetimeParsingCatalogEntry("Item", [], allowedSource: (LootSource)2));
    }
}
