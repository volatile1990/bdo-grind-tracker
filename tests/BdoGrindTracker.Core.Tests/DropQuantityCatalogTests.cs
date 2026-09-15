namespace BdoGrindTracker.Core.Tests;

public sealed class DropQuantityCatalogTests
{
    [Fact]
    public void WorkbookValuesNotOverriddenByLaterUserBoundsKeepTheirProvenance()
    {
        var workbookEntries = DropQuantityCatalog.Entries
            .Where(entry => entry.Source!.Contains("Eingabe!C", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(workbookEntries);
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
    [InlineData("scales-of-judgment", "Elion Follower's Mark", 2u, 1000u)]
    [InlineData("event-horizon", "Broken Gloves of the Void", 2u, 1000u)]
    [InlineData("aphrodon", "Black Stone", 1u, 100u)]
    [InlineData("hermesia", "Black Stone", 1u, 100u)]
    [InlineData("magaia", "Black Stone", 1u, 100u)]
    [InlineData("scales-of-judgment", "Caphras Stone", 1u, 100u)]
    [InlineData("hermesia", "Caphras Stone", 1u, 100u)]
    [InlineData("event-horizon", "Caphras Stone", 1u, 100u)]
    [InlineData("hermesia", "BON Wandering Origin Crystal", 1u, 1u)]
    [InlineData("aetherion", "Chilled Soul Piece", 1u, 1000u)]
    [InlineData("nymphamare", "Caphras Stone", 1u, 100u)]
    [InlineData("orbita", "Laila's Petal", 1u, 10u)]
    [InlineData("tenebraum", "Laila's Petal", 1u, 10u)]
    [InlineData("zephyros", "Sealed Black Magic Crystal", 1u, 15u)]
    [InlineData("dark-energy-floodlands", "Caphras Stone", 1u, 100u)]
    [InlineData("dark-energy-floodlands", "Faded Dark Energy", 1u, 1000u)]
    [InlineData("dark-energy-floodlands", "Tainted Armor Fragment", 1u, 1000u)]
    [InlineData("dark-energy-floodlands", "[Event] Mysterious Ore", 1u, 1u)]
    public void CurrentUserValuesRetainTheirExactItemAndSpot(string spot, string item, uint minimum, uint maximum) =>
        Assert.Equal(new DropQuantityBounds(minimum, maximum), DropQuantityCatalog.GetBounds(spot, item));

    [Fact]
    public void BeforeSpotLockNeverUsesOneSpotsFixedCountForASharedVariableDrop()
    {
        Assert.Equal(new DropQuantityBounds(1, 100), DropQuantityCatalog.GetBounds(null, "Caphras Stone"));
        Assert.Equal(new DropQuantityBounds(1, 10), DropQuantityCatalog.GetBounds(null, "Laila's Petal"));
        Assert.Equal(new DropQuantityBounds(1, 10), DropQuantityCatalog.GetBounds(null, "Empty Picture Frame"));
        Assert.True(DropQuantityCatalog.GetBounds(null, "BON Wandering Origin Crystal")!.IsFixedUnit);
        Assert.Equal(new DropQuantityBounds(1, null), DropQuantityCatalog.GetBounds(null, "Deboreka Necklace"));
        Assert.Equal(new DropQuantityBounds(1, 1), DropQuantityCatalog.GetBounds(null, "Silent Crystal of Origin"));
    }

    [Theory]
    [InlineData("Black Stone", 100u)]
    [InlineData("Caphras Stone", 100u)]
    [InlineData("Ancient Spirit Dust", 100u)]
    [InlineData("Laila's Petal", 10u)]
    [InlineData("Intricately Patterned Mystical Shard", 1u)]
    public void LatestGlobalUserRangesApplyBeforeAndAfterLockInEverySpot(string item, uint maximum)
    {
        var expected = new DropQuantityBounds(1, maximum);
        Assert.Equal(expected, DropQuantityCatalog.GetBounds(null, item));
        foreach (var spot in LootSpotCatalog.Spots)
        {
            Assert.True(spot.Allows(item), spot.Id);
            Assert.Equal(expected, DropQuantityCatalog.GetBounds(spot.Id, item));
        }
    }

    [Fact]
    public void ThousandTrashCapPreservesEveryPreviouslyConfirmedMinimum()
    {
        foreach (var trash in TrashLootMinimumCatalog.Entries)
        {
            var minimum = trash.SpotId switch
            {
                "aphrodon" or "hermesia" => 4u,
                "magaia" or "aresion" or "scales-of-judgment" or "event-horizon" => 2u,
                _ => 1u,
            };
            Assert.Equal(new DropQuantityBounds(minimum, 1000),
                DropQuantityCatalog.GetBounds(trash.SpotId, trash.ItemName));
        }
    }

    [Fact]
    public void UnchangedRareAndEventWorkbookCountsRemainExact()
    {
        Assert.Equal(new DropQuantityBounds(1, 1), DropQuantityCatalog.GetBounds("hermesia", "BON Wandering Origin Crystal"));
        Assert.Equal(new DropQuantityBounds(1, 5), DropQuantityCatalog.GetBounds("aphrodon", "[Event] Mysterious Ore"));
        Assert.Equal(new DropQuantityBounds(1, 15), DropQuantityCatalog.GetBounds("zephyros", "Sealed Black Magic Crystal"));
        Assert.Null(DropQuantityCatalog.GetBounds("gavinya-coastal-cliff", "Deboreka Necklace"));
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

    [Fact]
    public void CompletedOuterWorkbookSuppliesAll110PreviouslyMissingPairs()
    {
        var outer = new[] { LootSpotCatalog.AetherionId, LootSpotCatalog.NymphamareId,
            LootSpotCatalog.OrbitaId, LootSpotCatalog.TenebraumId, LootSpotCatalog.ZephyrosId,
            LootSpotCatalog.DarkEnergyFloodlandsId };
        var entries = DropQuantityCatalog.Entries.Where(entry => outer.Contains(entry.SpotId)
            && entry.ItemName is not ("Empty Picture Frame" or "Intricately Patterned Mystical Shard")).ToArray();
        Assert.Equal(110, entries.Length);
        Assert.All(entries, entry =>
        {
            Assert.NotNull(entry.MinimumQuantity);
            Assert.NotNull(entry.MaximumQuantity);
            Assert.NotNull(DropQuantityCatalog.GetBounds(entry.SpotId, entry.ItemName));
            Assert.True(entry.Source!.Contains("Eingabe!D", StringComparison.Ordinal) ||
                entry.Source.Contains("bestehende Trash-Minima erhalten", StringComparison.Ordinal), entry.Source);
            Assert.Contains("Nutzervorgabe vom 13.09.2026", entry.Source);
        });
        Assert.Equal(new DropQuantityBounds(1, 1000), DropQuantityCatalog.GetBounds(null, "Chilled Soul Piece"));
        Assert.Equal(new DropQuantityBounds(1, 1), DropQuantityCatalog.GetBounds("aetherion", "Deboreka Necklace"));
        Assert.Equal(new DropQuantityBounds(1, 1), DropQuantityCatalog.GetBounds("aetherion", "Silent Crystal of Origin"));
    }
}
