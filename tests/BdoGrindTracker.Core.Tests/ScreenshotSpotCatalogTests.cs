namespace BdoGrindTracker.Core.Tests;

public sealed class ScreenshotSpotCatalogTests
{
    public static IEnumerable<object[]> ScreenshotSpots() =>
    [
        new object[] { "gavinya-coastal-cliff", "Sulfur Golem Fragment" },
        new object[] { "stars-end", "Corrupted Sanguine Crystal" },
        new object[] { "sycraia-abyssal-ruins-lower", "Underwater Ancient Weapon Power Stone" },
        new object[] { "elvia-orzekea", "Thorn-Entwined Weapon Fragment" },
        new object[] { "dehkia-gyfin-rhasia-temple-upper", "Tainted Bronze Fragment" },
        new object[] { "tungrad-ruins", "Tungrad Ruins Fragment" },
        new object[] { "darkseekers-retreat", "Decayed Cloth" },
        new object[] { "fortunate-golden-pig-cave", "Shiny Treasure" },
        new object[] { "dehkia-mirumok-ruins", "Tainted Wood Fragment" },
        new object[] { "winter-tree-fossil-280", "Winter Tree Snow Crystal" },
        new object[] { "unlucky-golden-pig-cave", "Shattered Treasures" },
        new object[] { "dehkia-ii-ash-forest", "Tainted Specter's Cloth" },
        new object[] { "dehkia-ii-oluns-valley", "Tainted Golem's Heart Fragment" },
        new object[] { "yzrahid-highlands", "Corrupt Power Source" },
        new object[] { "elvia-hexe-sanctuary", "Howling Bone Fragment" },
        new object[] { "elvia-quint-hill", "Fiery Troll Hide" },
        new object[] { "dokkebi-forest", "Discarded Kkebicap" },
        new object[] { "dehkia-thornwood-forest", "Tainted Moonlight Spirit Powder" },
        new object[] { "city-of-the-dead", "Mark of the Black Sands" },
        new object[] { "dehkia-cadry-ruins", "Tainted Cadry's Token" },
        new object[] { "dehkia-ash-forest", "Tainted Specter's Cloth" },
        new object[] { "dehkia-crescent-shrine", "Tainted Token of Crescent" },
        new object[] { "dehkia-cyclops-land", "Tainted Huge Spear" },
        new object[] { "jade-starlight-forest", "Starlit Jade Powder" },
        new object[] { "dehkia-tunkuta", "Tainted Broken Horn Fragment" },
        new object[] { "dehkia-hystria-ruins", "Tainted Ruins Fragment" },
        new object[] { "dark-energy-floodlands-great-red-sea", "Tainted Armor Fragment" },
        new object[] { "dark-energy-floodlands-orbita", "Tainted Armor Fragment" },
        new object[] { "dark-energy-floodlands-zephyros", "Tainted Armor Fragment" },
    ];

    [Theory]
    [MemberData(nameof(ScreenshotSpots))]
    public void EveryRequestedSpotAcceptsItsEnglishAndGermanSingleTrashDrop(string id, string trash)
    {
        var spot = LootSpotCatalog.GetRequired(id);
        Assert.Equal(trash, spot.PrimaryTrashItemName);
        Assert.True(spot.Allows(trash));
        var matcher = new CompanionItemMatcher(spot.AllowedItems);
        foreach (var observed in new[] { trash, ItemLocalizationCatalog.DisplayName(trash, "de") })
        {
            Assert.True(matcher.TryMatch(observed, 1, false, out var match), observed);
            Assert.Equal(trash, match!.CanonicalName);
        }
        Assert.Equal(new DropQuantityBounds(1, 1000), DropQuantityCatalog.GetBounds(id, trash));
    }

    [Theory]
    [MemberData(nameof(ScreenshotSpots))]
    public void ConfirmedTrashDetectsTheSpotOrItsUnresolvedFamilyAfterThreeDistinctDrops(string id, string trash)
    {
        var filter = new AutomaticLootSpotLock();
        var first = Drop(trash);
        filter.ObserveConfirmed([first, first, Drop(trash)]);
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed([Drop(trash)]);
        var expected = trash switch
        {
            "Tainted Specter's Cloth" => "dehkia-ash-forest-unspecified",
            "Tainted Armor Fragment" => LootSpotCatalog.DarkEnergyFloodlandsId,
            "Winter Tree Snow Crystal" => "winter-tree-fossil-unspecified",
            _ => id,
        };
        Assert.Equal(expected, filter.Spot!.Id);
        Assert.True(filter.Allows(trash));
        Assert.False(filter.Allows("Branch of Abundance"));
    }

    [Fact]
    public void IdenticalAshTrashKeepsBothTierPoolsAvailableUntilSelection()
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed([Drop("Tainted Specter's Cloth"), Drop("Tainted Specter's Cloth"), Drop("Tainted Specter's Cloth")]);
        Assert.Equal("dehkia-ash-forest-unspecified", filter.Spot!.Id);
        Assert.Equal(new[] { "dehkia-ash-forest", "dehkia-ii-ash-forest" },
            LootSpotCatalog.VariantsFor(filter.Spot.Id).Select(spot => spot.Id));
        foreach (var variant in LootSpotCatalog.VariantsFor(filter.Spot.Id))
            Assert.All(variant.AllowedItems, item => Assert.True(filter.Allows(item), item));
        Assert.True(filter.Allows("Dehkia's Artifact - All Damage Reduction"));
    }

    [Fact]
    public void FloodlandsLocationChoicesRemainDistinctWhileTrashDetectionIsShared()
    {
        var variants = LootSpotCatalog.VariantsFor(LootSpotCatalog.DarkEnergyFloodlandsId);
        Assert.Equal(new[] { "dark-energy-floodlands-zephyros", "dark-energy-floodlands-orbita", "dark-energy-floodlands-great-red-sea" },
            variants.Select(spot => spot.Id));
        Assert.All(variants, spot =>
        {
            Assert.True(spot.Allows("Tainted Armor Fragment"));
            Assert.True(spot.Allows("Faded Dark Energy"));
            Assert.Equal(variants.Select(variant => variant.Id), LootSpotCatalog.VariantsFor(spot.Id).Select(variant => variant.Id));
        });
    }

    [Fact]
    public void WinterTreeTrashCannotProveTheRequested280ApVariant()
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe(["Winter Tree Snow Crystal"]);
        Assert.Equal("winter-tree-fossil-unspecified", filter.Spot!.Id);
        Assert.NotEqual("winter-tree-fossil-280", filter.Spot.Id);
        Assert.Equal("winter-tree-fossil-280", Assert.Single(LootSpotCatalog.VariantsFor(filter.Spot.Id)).Id);
        Assert.True(filter.Allows("Winter Tree Snow Crystal"));
        Assert.Equal(new DropQuantityBounds(1, 1000),
            DropQuantityCatalog.GetBounds(filter.Spot.Id, "Winter Tree Snow Crystal"));
    }

    [Theory]
    [InlineData("Ancient Magic Crystal of Nature - Sturdiness")]
    [InlineData("Scorching Sun Shard")]
    [InlineData("Any Artifact")]
    [InlineData("[EVENT] Eternal Darkseeker")]
    [InlineData("Ulutuka (Boss)")]
    [InlineData("Silver")]
    public void RemovedItemsAndTrackerCountersDoNotEnterTheOcrLootPools(string item) =>
        Assert.All(LootSpotCatalog.Spots, spot => Assert.False(spot.Allows(item)));

    [Theory]
    [InlineData("Silver")]
    [InlineData("Silber")]
    public void DirectSilverDropsDoNotMatchAnyTrackedLoot(string observed)
    {
        var names = LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems)
            .Concat(LootSpotCatalog.EventItems).Distinct().ToArray();
        var matcher = new CompanionItemMatcher(names);

        Assert.False(matcher.TryMatch(observed, 100, false, out _));
        Assert.False(matcher.TryMatch(observed, 100, true, out _));
        Assert.DoesNotContain(DropQuantityCatalog.Entries, entry => entry.ItemName == "Silver");
    }

    private static CompanionRecognizedEntry Drop(string name) => new(name, 1)
    {
        EventId = Guid.NewGuid(),
        QuantityDelta = 1,
    };
}
