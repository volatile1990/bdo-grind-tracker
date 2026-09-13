namespace BdoGrindTracker.Core.Tests;

public sealed class OuterEdaniaCatalogTests
{
    public static IEnumerable<object[]> TrashCases() =>
    [
        new object[] { LootSpotCatalog.AetherionId, "Chilled Soul Piece" },
        new object[] { LootSpotCatalog.NymphamareId, "Contaminated Coral Piece" },
        new object[] { LootSpotCatalog.OrbitaId, "Lightlost Core" },
        new object[] { LootSpotCatalog.TenebraumId, "Ancient Soldier Fragment" },
        new object[] { LootSpotCatalog.ZephyrosId, "Hardened Lava Chunk" },
        new object[] { LootSpotCatalog.DarkEnergyFloodlandsId, "Tainted Armor Fragment" },
        new object[] { LootSpotCatalog.DarkEnergyFloodlandsId, "Faded Dark Energy" },
    ];

    [Theory]
    [MemberData(nameof(TrashCases))]
    public void ConfirmedOuterTrashSelectsItsOwnPoolAndRejectsOtherSpots(string spotId, string trash)
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed(Enumerable.Range(0, 3).Select(_ => Drop(trash)));

        Assert.Equal(spotId, filter.Spot!.Id);
        Assert.True(filter.Allows(trash));
        Assert.All(LootSpotCatalog.SharedGlobalItems, item => Assert.True(filter.Allows(item)));
        Assert.All(LootSpotCatalog.EventItems, item => Assert.True(filter.Allows(item)));
        Assert.All(LootSpotCatalog.SharedHighestTierItems, item => Assert.False(filter.Allows(item)));
        var sameFamily = LootSpotCatalog.VariantsFor(spotId).Select(spot => spot.Id).Append(spotId).ToHashSet();
        Assert.All(TrashLootMinimumCatalog.Entries.Where(entry => !sameFamily.Contains(entry.SpotId)),
            entry => Assert.False(filter.Allows(entry.ItemName)));
        Assert.Contains(TrashLootMinimumCatalog.Entries,
            entry => entry.SpotId == spotId && entry.ItemName == trash && entry.MinimumQuantity == null);
        Assert.False(filter.Allows("Apeiron Earring"));
        Assert.True(filter.Allows("Deboreka Earring"));
    }

    [Fact]
    public void FloodlandsBothTrashTypesContributeToOneSpotAndRemainAllowed()
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed([Drop("Tainted Armor Fragment"), Drop("Faded Dark Energy")]);
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed([Drop("Tainted Armor Fragment")]);
        Assert.Equal(LootSpotCatalog.DarkEnergyFloodlandsId, filter.Spot!.Id);
        Assert.True(filter.Allows("Tainted Armor Fragment"));
        Assert.True(filter.Allows("Faded Dark Energy"));
        Assert.False(filter.Allows("Silent Fragment of Origin"));
        Assert.False(filter.Allows("HAN Crystal of Ruin"));
    }

    [Theory]
    [InlineData(LootSpotCatalog.AetherionId, "WON", "Primordial Fragment")]
    [InlineData(LootSpotCatalog.NymphamareId, "BON", "Crystallized Energy of Endtimes")]
    [InlineData(LootSpotCatalog.OrbitaId, "JIN", "Crystallized Energy of Endtimes")]
    [InlineData(LootSpotCatalog.TenebraumId, "HAN", "Crystallized Energy of Endtimes")]
    [InlineData(LootSpotCatalog.ZephyrosId, "HAN", "Crystallized Energy of Endtimes")]
    public void CastleLootKeepsItsCrystalTierAndIncludesOriginProtection(string id, string tier, string material)
    {
        var spot = LootSpotCatalog.GetRequired(id);
        Assert.True(spot.Allows($"{tier} Crystal of Dusky Ruin"));
        Assert.True(spot.Allows($"{tier} Crystal of Ruin"));
        Assert.True(spot.Allows(material));
        Assert.True(spot.Allows("Silent Fragment of Origin"));
        Assert.True(spot.Allows("Distorted Fragment of Origin"));
        Assert.True(spot.Allows("Silent Crystal of Origin"));
        Assert.True(spot.Allows("Distorted Crystal of Origin"));
        Assert.Equal(id == LootSpotCatalog.ZephyrosId, spot.Allows("Sealed Black Magic Crystal"));
        foreach (var other in new[] { "WON", "BON", "JIN", "HAN" }.Where(other => other != tier))
        {
            Assert.False(spot.Allows($"{other} Crystal of Dusky Ruin"));
            Assert.False(spot.Allows($"{other} Crystal of Ruin"));
        }
    }

    [Theory]
    [MemberData(nameof(TrashCases))]
    public void EnglishAndGermanTrashMatchUserConfirmedSingleDrops(string spotId, string trash)
    {
        var matcher = new CompanionItemMatcher(LootSpotCatalog.GetRequired(spotId).AllowedItems);
        foreach (var text in new[] { trash, ItemLocalizationCatalog.DisplayName(trash, "de") })
        {
            Assert.True(matcher.TryMatch(text, 1, false, out var match));
            Assert.Equal(trash, match!.CanonicalName);
        }
    }

    private static CompanionRecognizedEntry Drop(string name) => new(name, 5)
    {
        EventId = Guid.NewGuid(),
        QuantityDelta = 5,
    };
}
