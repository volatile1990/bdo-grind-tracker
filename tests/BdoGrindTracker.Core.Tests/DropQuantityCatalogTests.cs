namespace BdoGrindTracker.Core.Tests;

public sealed class DropQuantityCatalogTests
{
    [Fact]
    public void CompletedWorkbookContainsAll207ConfirmedPairsAnd175FixedUnits()
    {
        Assert.Equal(207, DropQuantityCatalog.Entries.Count);
        Assert.Equal(175, DropQuantityCatalog.Entries.Count(entry => entry.Bounds!.IsFixedUnit));
        Assert.All(DropQuantityCatalog.Entries, entry =>
        {
            Assert.NotNull(entry.MinimumQuantity);
            Assert.NotNull(entry.MaximumQuantity);
            Assert.Contains("Nutzervorgabe", entry.Source);
            Assert.Contains("Eingabe!C", entry.Source);
        });
    }

    [Theory]
    [InlineData("aphrodon", "Branch of Abundance", 4u, 1000u)]
    [InlineData("hermesia", "Black Crystal Fragment", 4u, 1000u)]
    [InlineData("magaia", "Elion Follower's Helmet", 7u, 1000u)]
    [InlineData("aresion", "Scorched Belt Ornament", 2u, 1000u)]
    [InlineData("scales-of-judgment", "Elion Follower's Mark", 2u, 2000u)]
    [InlineData("event-horizon", "Broken Gloves of the Void", 2u, 1000u)]
    [InlineData("aphrodon", "Black Stone", 1u, 15u)]
    [InlineData("hermesia", "Black Stone", 1u, 20u)]
    [InlineData("magaia", "Black Stone", 1u, 50u)]
    [InlineData("scales-of-judgment", "Caphras Stone", 1u, 1u)]
    [InlineData("hermesia", "Caphras Stone", 1u, 20u)]
    [InlineData("event-horizon", "Caphras Stone", 1u, 50u)]
    [InlineData("hermesia", "BON Wandering Origin Crystal", 1u, 1u)]
    public void WorkbookValuesRetainTheirExactItemAndSpot(string spot, string item, uint minimum, uint maximum) =>
        Assert.Equal(new DropQuantityBounds(minimum, maximum), DropQuantityCatalog.GetBounds(spot, item));

    [Fact]
    public void BeforeSpotLockNeverUsesOneSpotsFixedCountForASharedVariableDrop()
    {
        Assert.Equal(new DropQuantityBounds(1, 50), DropQuantityCatalog.GetBounds(null, "Caphras Stone"));
        Assert.Equal(new DropQuantityBounds(1, 5), DropQuantityCatalog.GetBounds(null, "Laila's Petal"));
        Assert.True(DropQuantityCatalog.GetBounds(null, "BON Wandering Origin Crystal")!.IsFixedUnit);
    }

    [Fact]
    public void EveryAllowedItemAndOptionalEventHasExactlyOneEntryPerSpot()
    {
        var expected = LootSpotCatalog.Spots.SelectMany(spot =>
            spot.AllowedItems.Concat(LootSpotCatalog.EventItems).Select(item => (spot.Id, item))).ToHashSet();
        Assert.Equal(expected.Count, DropQuantityCatalog.Entries.Count);
        Assert.True(expected.SetEquals(DropQuantityCatalog.Entries.Select(entry => (entry.SpotId, entry.ItemName))));
        foreach (var entry in DropQuantityCatalog.Entries)
        {
            Assert.Same(entry, DropQuantityCatalog.GetRequired(entry.SpotId, entry.ItemName));
            if (entry.MinimumQuantity.HasValue || entry.MaximumQuantity.HasValue)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Source));
                Assert.NotNull(entry.Bounds);
            }
            else Assert.Null(entry.Bounds);
        }
    }

    [Fact]
    public void ExcludedOrUnknownItemsNeverAcquireAnotherItemsBounds()
    {
        Assert.Null(DropQuantityCatalog.GetBounds(LootSpotCatalog.HermesiaId, "Black Gem Fragment"));
        Assert.Null(DropQuantityCatalog.GetBounds(LootSpotCatalog.HermesiaId, "Branch of Abundance"));
        Assert.Null(DropQuantityCatalog.GetBounds(null, "new unknown drop"));
    }
}
