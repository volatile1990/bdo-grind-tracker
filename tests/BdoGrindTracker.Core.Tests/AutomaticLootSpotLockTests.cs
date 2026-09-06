using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class AutomaticLootSpotLockTests
{
    private static readonly string[] TrashItems =
    [
        "Branch of Abundance",
        "Black Crystal Fragment",
        "Elion Follower's Helmet",
        "Scorched Belt Ornament",
        "Elion Follower's Mark",
        "Broken Gloves of the Void",
    ];

    public static IEnumerable<object[]> SharedGlobalDropCases()
    {
        foreach (var trash in TrashItems)
        {
            foreach (var item in LootSpotCatalog.SharedGlobalItems)
            {
                yield return [trash, item];
            }
        }
    }

    [Fact]
    public void SharedGlobalPoolIncludesStandardDropsAndStaysSeparateFromSpecialPools()
    {
        Assert.Contains("Ancient Spirit Dust", LootSpotCatalog.SharedGlobalItems);
        Assert.Contains("Black Stone", LootSpotCatalog.SharedGlobalItems);
        Assert.Contains("Caphras Stone", LootSpotCatalog.SharedGlobalItems);
        Assert.Contains("Laila's Petal", LootSpotCatalog.SharedGlobalItems);
        Assert.Empty(LootSpotCatalog.SharedGlobalItems.Intersect(LootSpotCatalog.SharedHighestTierItems));
        Assert.Empty(LootSpotCatalog.SharedGlobalItems.Intersect(LootSpotCatalog.EventItems));
    }

    [Theory]
    [MemberData(nameof(SharedGlobalDropCases))]
    public void EveryGlobalDropRemainsAllowedAfterAnySpotLocksWithoutEventOptIn(string trash, string item)
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe([item]);
        Assert.Null(filter.Spot);
        Assert.True(filter.Allows(item, includeEventLoot: false));

        filter.Observe([trash]);
        Assert.NotNull(filter.Spot);
        Assert.True(filter.Spot.Allows(item));
        Assert.True(filter.Allows(item, includeEventLoot: false));

        filter.Reset();
        Assert.Null(filter.Spot);
        Assert.True(filter.Allows(item, includeEventLoot: false));
        filter.Observe([trash]);
        Assert.True(filter.Allows(item, includeEventLoot: false));
    }

    [Theory]
    [InlineData("Branch of Abundance", "Black Crystal Fragment", "Elion Follower's Helmet")]
    [InlineData("Black Crystal Fragment", "Branch of Abundance", "Elion Follower's Helmet")]
    [InlineData("Elion Follower's Helmet", "Branch of Abundance", "Black Crystal Fragment")]
    public void GlobalDropsDoNotUnlockForeignTrashOrKnownWrongItems(string trash, string foreignTrash, string otherForeignTrash)
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe([trash]);
        filter.Observe(LootSpotCatalog.SharedGlobalItems);

        Assert.True(filter.Allows(trash));
        Assert.False(filter.Allows(foreignTrash));
        Assert.False(filter.Allows(otherForeignTrash));
        Assert.False(filter.Allows("Black Gem Fragment"));
        Assert.False(filter.Allows("[Event] Mysterious Ore", includeEventLoot: false));
        Assert.True(filter.Allows("[Event] Mysterious Ore", includeEventLoot: true));
    }

    [Fact]
    public void EveryLockedSpotRejectsEveryForeignTrash()
    {
        foreach (var trash in TrashItems)
        {
            var filter = new AutomaticLootSpotLock();
            filter.Observe([trash]);

            Assert.All(TrashItems.Where(candidate => candidate != trash),
                foreignTrash => Assert.False(filter.Allows(foreignTrash)));
        }
    }

    [Theory]
    [InlineData("Branch of Abundance", LootSpotCatalog.AphrodonId)]
    [InlineData("Black Crystal Fragment", LootSpotCatalog.HermesiaId)]
    [InlineData("Elion Follower's Helmet", LootSpotCatalog.MagaiaId)]
    [InlineData("Scorched Belt Ornament", LootSpotCatalog.AresionId)]
    [InlineData("Elion Follower's Mark", LootSpotCatalog.ScalesOfJudgmentId)]
    [InlineData("Broken Gloves of the Void", LootSpotCatalog.EventHorizonId)]
    public void RecognizedTrashLocksItsSpot(string trash, string expected)
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe(["BON Origin Shard", trash]);
        Assert.Equal(expected, filter.Spot!.Id);
        Assert.True(filter.Allows(trash));
        Assert.False(filter.Allows("Black Gem Fragment"));
    }

    [Fact]
    public void UntilFirstNativeTrashMatchThereIsNoExtraRejection()
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe(["BON Origin Shard", "Black CrystaI Fragment"]);
        Assert.Null(filter.Spot);
        Assert.True(filter.Allows("Black Gem Fragment"));
        Assert.True(filter.Allows("BON Origin Shard"));
    }

    [Fact]
    public void NewestMatchWinsThenRemainsLockedAcrossFrames()
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe(["Branch of Abundance", "Black Crystal Fragment"]);
        filter.Observe(["Black Crystal Fragment"]);
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
        Assert.False(filter.Allows("Black Crystal Fragment"));
        filter.Reset();
        Assert.Null(filter.Spot);
        filter.Observe(["Black Crystal Fragment"]);
        Assert.Equal(LootSpotCatalog.HermesiaId, filter.Spot!.Id);
    }

    [Fact]
    public void EventOptInDoesNotAllowForeignRegularLoot()
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe(["Black Crystal Fragment"]);
        Assert.False(filter.Allows("[Event] Mysterious Ore"));
        Assert.True(filter.Allows("[Event] Mysterious Ore", includeEventLoot: true));
        Assert.False(filter.Allows("Black Gem Fragment", includeEventLoot: true));
    }
}
