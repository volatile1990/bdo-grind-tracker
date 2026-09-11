namespace BdoGrindTracker.Core.Tests;

public sealed class DropQuantityCatalogTests
{
    [Fact]
    public void CompletedWorkbookContainsAll207ConfirmedPairsAnd175FixedUnits()
    {
        var workbookEntries = DropQuantityCatalog.Entries
            .Where(entry => entry.Source!.Contains("Eingabe!C", StringComparison.Ordinal)).ToArray();
        Assert.Equal(207, workbookEntries.Length);
        Assert.Equal(175, workbookEntries.Count(entry => entry.Bounds!.IsFixedUnit));
        Assert.All(workbookEntries, entry =>
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
    [InlineData("magaia", "Elion Follower's Helmet", 2u, 1000u)]
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
        Assert.Equal(new DropQuantityBounds(1, 10), DropQuantityCatalog.GetBounds(null, "Empty Picture Frame"));
        Assert.True(DropQuantityCatalog.GetBounds(null, "BON Wandering Origin Crystal")!.IsFixedUnit);
    }

    [Fact]
    public void EmptyPictureFrameUsesUserSpecifiedBoundsInEverySpot()
    {
        foreach (var spot in LootSpotCatalog.Spots)
        {
            Assert.True(spot.Allows("Empty Picture Frame"));
            var entry = DropQuantityCatalog.GetRequired(spot.Id, "Empty Picture Frame");
            Assert.Equal(new DropQuantityBounds(1, 10), entry.Bounds);
            Assert.Contains("11.09.2026", entry.Source);
        }
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
