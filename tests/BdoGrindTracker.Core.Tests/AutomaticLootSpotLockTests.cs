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
    public void EveryGlobalDropRemainsAllowedAfterAnySpotLocks(string trash, string item)
    {
        var filter = new AutomaticLootSpotLock();
        filter.Observe([item]);
        Assert.Null(filter.Spot);
        Assert.True(filter.Allows(item));

        filter.Observe([trash]);
        Assert.NotNull(filter.Spot);
        Assert.True(filter.Spot.Allows(item));
        Assert.True(filter.Allows(item));

        filter.Reset();
        Assert.Null(filter.Spot);
        Assert.True(filter.Allows(item));
        filter.Observe([trash]);
        Assert.True(filter.Allows(item));
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
        Assert.True(filter.Allows("[Event] Mysterious Ore"));
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
    public void EveryKnownEventItemRemainsAllowedAcrossSpotsAndReset()
    {
        var filter = new AutomaticLootSpotLock();
        Assert.NotEmpty(LootSpotCatalog.EventItems);
        foreach (var trash in TrashItems)
        {
            Assert.All(LootSpotCatalog.EventItems, item => Assert.True(filter.Allows(item)));
            filter.Observe([trash]);
            Assert.All(LootSpotCatalog.EventItems, item => Assert.True(filter.Allows(item)));
            Assert.False(filter.Allows("Black Gem Fragment"));
            Assert.False(filter.Allows("[Event] Unknown Item"));
            filter.Reset();
        }
    }

    [Theory]
    [InlineData("Branch of Abundance", LootSpotCatalog.AphrodonId)]
    [InlineData("Black Crystal Fragment", LootSpotCatalog.HermesiaId)]
    [InlineData("Elion Follower's Helmet", LootSpotCatalog.MagaiaId)]
    [InlineData("Scorched Belt Ornament", LootSpotCatalog.AresionId)]
    [InlineData("Elion Follower's Mark", LootSpotCatalog.ScalesOfJudgmentId)]
    [InlineData("Broken Gloves of the Void", LootSpotCatalog.EventHorizonId)]
    public void ConfirmedTrashNeedsThreeDistinctFirstBookings(string trash, string expected)
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed([Drop(trash), Drop(trash)]);
        Assert.Null(filter.Spot);
        Assert.True(filter.Allows("Black Gem Fragment"));

        filter.ObserveConfirmed([Drop(trash)]);
        Assert.Equal(expected, filter.Spot!.Id);
        Assert.True(filter.Allows(trash));
        Assert.False(filter.Allows("Black Gem Fragment"));
        Assert.All(LootSpotCatalog.SharedGlobalItems, item => Assert.True(filter.Allows(item)));
        Assert.All(LootSpotCatalog.EventItems, item => Assert.True(filter.Allows(item)));
    }

    [Fact]
    public void RedeliveredIdsAndQuantityRevisionsCannotSupplyAdditionalEvidence()
    {
        var filter = new AutomaticLootSpotLock();
        var first = Drop(TrashItems[0]);
        filter.ObserveConfirmed(Enumerable.Repeat(first, 20));
        filter.ObserveConfirmed([first with { Revision = 1, QuantityDelta = 2, TotalDropQuantity = 6 }]);
        filter.ObserveConfirmed([Drop(TrashItems[0]) with { Revision = 1 }]);
        filter.ObserveConfirmed([Drop(TrashItems[0])]);
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed([Drop(TrashItems[0])]);
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
    }

    [Fact]
    public void PlaceholdersEstimatesAndUnbookedRowsCannotLockASpot()
    {
        var filter = new AutomaticLootSpotLock();
        for (var repeat = 0; repeat < 4; repeat++)
        {
            filter.ObserveConfirmed(
            [
                Drop(TrashItems[0]) with { EventId = null },
                Drop(TrashItems[0]) with { EventId = Guid.Empty },
                Drop(TrashItems[0]) with { Revision = -1 },
                Drop(TrashItems[0]) with { Revision = 1 },
                Drop(TrashItems[0]) with { QuantityDelta = 0 },
                Drop(TrashItems[0]) with { QuantityDelta = -2 },
                new(TrashItems[0], 0) { EventId = Guid.NewGuid(), QuantityDelta = 1 },
                Drop(TrashItems[0]) with { IsPlaceholder = true },
                Drop(TrashItems[0]) with { IsAlignmentAnchor = true },
                Drop(TrashItems[0]) with { IsMinimumQuantityEstimate = true },
                Drop("Black Stone"), Drop("[Event] Mysterious Ore"),
            ]);
        }
        filter.ObserveConfirmed([Drop(TrashItems[0]), Drop(TrashItems[0])]);
        Assert.Null(filter.Spot);
    }

    [Fact]
    public void PositiveCountWithoutExplicitDeltaIsAConcreteFirstBooking()
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed(Enumerable.Range(0, 3).Select(_ => Drop(TrashItems[0]) with { QuantityDelta = null }));
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
    }

    [Fact]
    public void LeadOfTwoLocksAndRemainsFixedUntilReset()
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed([Drop(TrashItems[1]), Drop(TrashItems[0]), Drop(TrashItems[0]), Drop(TrashItems[0])]);
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
        filter.ObserveConfirmed(Enumerable.Range(0, 20).Select(_ => Drop(TrashItems[1])));
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot.Id);
    }

    [Fact]
    public void CompetingTrashInSameBatchStaysOpenUntilLaterEvidenceBuildsALead()
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed(
        [
            Drop(TrashItems[0]), Drop(TrashItems[0]), Drop(TrashItems[0]),
            Drop(TrashItems[1]), Drop(TrashItems[1]),
        ]);
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed([Drop(TrashItems[0]), Drop(TrashItems[0])]);
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
    }

    [Fact]
    public void AlternatingEvidenceRemainsOpenAcrossManyWindows()
    {
        var filter = new AutomaticLootSpotLock();
        for (var index = 0; index < 1000; index++)
        {
            filter.ObserveConfirmed([Drop(TrashItems[index % 2])]);
            Assert.Null(filter.Spot);
        }
    }

    [Fact]
    public void OldConflictingEvidenceExpiresSoANewConsistentSpotCanWin()
    {
        var filter = new AutomaticLootSpotLock();
        filter.ObserveConfirmed(Enumerable.Range(0, 12).Select(index => Drop(TrashItems[index < 6 ? 0 : 1])));
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed(Enumerable.Range(0, 7).Select(_ => Drop(TrashItems[2])));
        Assert.Equal(LootSpotCatalog.MagaiaId, filter.Spot!.Id);
    }

    [Fact]
    public void RecentlySeenIdsCannotReenterAfterTheirEvidenceExpires()
    {
        var filter = new AutomaticLootSpotLock();
        var drops = Enumerable.Range(0, 24).Select(index => Drop(TrashItems[index % 2])).ToArray();
        filter.ObserveConfirmed(drops);
        Assert.Null(filter.Spot);

        var expiredLeaderEvidence = drops.Take(12).Where(drop => drop.Name == TrashItems[0]).ToArray();
        filter.ObserveConfirmed(expiredLeaderEvidence);
        filter.ObserveConfirmed(expiredLeaderEvidence.Select(drop => drop with { Revision = 1 }));
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed([Drop(TrashItems[0]), Drop(TrashItems[0])]);
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
    }

    [Fact]
    public void AnAlreadyCountedIdCannotAlsoSupportAnotherTrash()
    {
        var filter = new AutomaticLootSpotLock();
        var first = Drop(TrashItems[1]);
        filter.ObserveConfirmed([first]);
        filter.ObserveConfirmed([Drop(TrashItems[0]) with { EventId = first.EventId }, Drop(TrashItems[0]), Drop(TrashItems[0])]);
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed([Drop(TrashItems[0])]);
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
    }

    [Fact]
    public void ResetClearsPartialScoresAndPreviouslyAcceptedIds()
    {
        var filter = new AutomaticLootSpotLock();
        var entries = new[] { Drop(TrashItems[0]), Drop(TrashItems[0]), Drop(TrashItems[0]) };
        filter.ObserveConfirmed(entries.Take(2));
        filter.Reset();
        filter.ObserveConfirmed(entries.Skip(2));
        Assert.Null(filter.Spot);
        filter.ObserveConfirmed(entries.Take(2));
        Assert.Equal(LootSpotCatalog.AphrodonId, filter.Spot!.Id);
        filter.Reset();
        filter.ObserveConfirmed(Enumerable.Range(0, 3).Select(_ => Drop(TrashItems[1])));
        Assert.Equal(LootSpotCatalog.HermesiaId, filter.Spot!.Id);
    }

    private static CompanionRecognizedEntry Drop(string name) =>
        new(name, 4) { EventId = Guid.NewGuid(), QuantityDelta = 4, TotalDropQuantity = 4 };
}
