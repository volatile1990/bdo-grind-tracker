namespace BdoGrindTracker.Core.Tests;

public sealed class GrindStartConfirmationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IndependentVisualArrivalAcceptsTheFirstRecognizedMonsterDropImmediately()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.True(detector.Observe(Projection(5, 1, Start.AddMilliseconds(-50))));
        Assert.Equal(LootSpotCatalog.AetherionId, detector.SpotId);
    }

    [Fact]
    public void IndependentVisualArrivalCanBeRecognizedLaterWithoutAnotherArrival()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.False(detector.Observe(Projection(0, 0, null)));
        Assert.False(detector.Observe(Projection(0, 0, null)));
        Assert.True(detector.Observe(Projection(5, 1, Start.AddMilliseconds(-50))));
        Assert.Equal(LootSpotCatalog.AetherionId, detector.SpotId);
    }

    [Theory]
    [InlineData("Tainted Armor Fragment", LootSpotCatalog.DarkEnergyFloodlandsId)]
    [InlineData("Faded Dark Energy", LootSpotCatalog.DarkEnergyFloodlandsId)]
    [InlineData("Tainted Specter's Cloth", "dehkia-ash-forest-unspecified")]
    [InlineData("Winter Tree Snow Crystal", "winter-tree-fossil-unspecified")]
    public void SharedTrashIdentifiesTheExistingSpotFamily(string trash, string spotId)
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.True(detector.Observe(Projection(5, 1, Start, trash)));
        Assert.Equal(spotId, detector.SpotId);
    }

    [Fact]
    public void TwoTrashNamesFromOneSpotIdentifyTheSameFamily()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.True(detector.Observe(new LootTotalsProjection(2, new Dictionary<string, long>
        {
            ["Tainted Armor Fragment"] = 5,
            ["Faded Dark Energy"] = 5,
        }, 2, Start)));
        Assert.Equal(LootSpotCatalog.DarkEnergyFloodlandsId, detector.SpotId);
    }

    [Theory]
    [InlineData("Chilled Soul Piece", "Elion Follower's Helmet")]
    [InlineData("Elion Follower's Helmet", "Chilled Soul Piece")]
    public void MixedSpotTrashConfirmsActivityWithoutChoosingAnArbitrarySpot(string first, string second)
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.True(detector.Observe(new LootTotalsProjection(2, new Dictionary<string, long>
        {
            [first] = 5,
            [second] = 5,
        }, 2, Start)));
        Assert.Null(detector.SpotId);
    }

    [Fact]
    public void LaterArrivalIdentifiesOnlyTheGrowingTrashDespiteAnotherSpotInTheBaseline()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(50, 10, Start)));
        Assert.Null(detector.SpotId);
        Assert.True(detector.Observe(new LootTotalsProjection(11, new Dictionary<string, long>
        {
            ["Chilled Soul Piece"] = 50,
            ["Elion Follower's Helmet"] = 5,
        }, 11, Start.AddSeconds(1))));
        Assert.Equal(LootSpotCatalog.MagaiaId, detector.SpotId);
    }

    [Fact]
    public void LaterArrivalWithMultipleGrowingSpotFamiliesDoesNotChooseOne()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(50, 10, Start)));
        Assert.True(detector.Observe(new LootTotalsProjection(12, new Dictionary<string, long>
        {
            ["Chilled Soul Piece"] = 55,
            ["Elion Follower's Helmet"] = 5,
        }, 12, Start.AddSeconds(1))));
        Assert.Null(detector.SpotId);
    }

    [Fact]
    public void SubsequentObservationsPreserveTheSpotOfTheConfirmingArrival()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.True(detector.Observe(Projection(5, 1, Start)));
        Assert.True(detector.Observe(Projection(10, 2, Start.AddSeconds(1), "Elion Follower's Helmet")));
        Assert.Equal(LootSpotCatalog.AetherionId, detector.SpotId);
    }

    [Theory]
    [InlineData("Silver", 1)]
    [InlineData("Black Stone", 1)]
    [InlineData("[Event] Mysterious Ore", 1)]
    [InlineData("Chilled Soul Piece", 0)]
    public void VisualArrivalStillNeedsPositiveMonsterTrashAndAPhysicalDrop(string item, int drops)
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.False(detector.Observe(Projection(5, drops, Start, item)));
        Assert.Null(detector.SpotId);
    }

    [Fact]
    public void KnownCorrectionClosesInitialVisualAllowanceUntilAnIndependentNewArrival()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.False(detector.Observe(Projection(0, 0, null) with { QuantityCorrectionRevision = 0 }));
        var corrected = Projection(20, 5, Start) with { QuantityCorrectionRevision = 1 };
        Assert.False(detector.Observe(corrected));
        Assert.False(detector.Observe(corrected));
        Assert.Null(detector.SpotId);
        Assert.True(detector.Observe(Projection(25, 6, Start.AddSeconds(1)) with { QuantityCorrectionRevision = 1 }));
        Assert.Equal(LootSpotCatalog.AetherionId, detector.SpotId);
    }

    [Fact]
    public void PreexistingLogAndDelayedConfirmationOfItDoNotStartAGrind()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(0, 0, null)));
        Assert.False(detector.Observe(Projection(20, 5, Start)));
        Assert.False(detector.Observe(Projection(25, 6, Start)));
        Assert.False(detector.Observe(Projection(30, 7, Start)));
    }

    [Fact]
    public void FirstLaterTrashArrivalConfirmsTheProvisionalGrind()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start)));
        Assert.True(detector.Observe(Projection(25, 6, Start.AddSeconds(1))));
        Assert.True(detector.Observe(Projection(30, 7, Start.AddSeconds(2))));
    }

    [Fact]
    public void EmptyInitialLogNeedsOnlyOneNewMonsterDrop()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(0, 0, null)));
        Assert.True(detector.Observe(Projection(5, 1, Start.AddMilliseconds(400))));
    }

    [Fact]
    public void FirstProjectionRemainsTheBaselineEvenIfItsArrivalIsLaterThanConstruction()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start.AddSeconds(1))));
        Assert.False(detector.Observe(Projection(25, 6, Start.AddSeconds(1))));
        Assert.True(detector.Observe(Projection(30, 7, Start.AddSeconds(2))));
    }

    [Fact]
    public void QuantityCorrectionsAndRepeatedFramesDoNotCountAsNewDrops()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start)));
        Assert.False(detector.Observe(Projection(25, 5, Start)));
        Assert.False(detector.Observe(Projection(30, 5, Start.AddSeconds(1))));
        Assert.False(detector.Observe(Projection(35, 5, Start.AddSeconds(1))));
        Assert.False(detector.Observe(Projection(35, 5, Start.AddSeconds(1))));
        Assert.True(detector.Observe(Projection(40, 6, Start.AddSeconds(2))));
    }

    [Fact]
    public void NewNonMonsterArrivalDoesNotStartTrackingWhileTrashQuantityStaysUnchanged()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start)));
        Assert.False(detector.Observe(Projection(20, 6, Start.AddSeconds(1))));
        Assert.True(detector.Observe(Projection(25, 7, Start.AddSeconds(2))));
    }

    [Fact]
    public void KnownTrashCorrectionWithSimultaneousNewArrivalWaitsForUnambiguousDrop()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start) with { QuantityCorrectionRevision = 0 }));
        Assert.False(detector.Observe(Projection(25, 6, Start.AddSeconds(1)) with { QuantityCorrectionRevision = 1 }));
        Assert.True(detector.Observe(Projection(30, 7, Start.AddSeconds(2)) with { QuantityCorrectionRevision = 1 }));
    }

    [Theory]
    [InlineData(null, 0L)]
    [InlineData(0L, null)]
    public void ChangedCorrectionEvidenceIsNotProofOfANewMonsterDrop(long? before, long? after)
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start) with { QuantityCorrectionRevision = before }));
        Assert.False(detector.Observe(Projection(25, 6, Start.AddSeconds(1)) with { QuantityCorrectionRevision = after }));
        Assert.True(detector.Observe(Projection(30, 7, Start.AddSeconds(2)) with { QuantityCorrectionRevision = after }));
    }

    [Theory]
    [InlineData("Silver")]
    [InlineData("[Event] Mysterious Ore")]
    [InlineData("Black Stone")]
    public void CurrencySharedDropsAndEventItemsAloneCannotStartTracking(string item)
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(1, 1, Start, item)));
        Assert.False(detector.Observe(Projection(2, 2, Start.AddSeconds(1), item)));
        Assert.False(detector.Observe(Projection(3, 3, Start.AddSeconds(2), item)));
    }

    private static LootTotalsProjection Projection(long quantity, int drops, DateTimeOffset? arrival,
        string name = "Chilled Soul Piece") => new(drops, new Dictionary<string, long> { [name] = quantity }, drops, arrival);
}
