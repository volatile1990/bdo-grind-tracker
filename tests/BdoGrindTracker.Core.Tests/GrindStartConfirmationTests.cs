namespace BdoGrindTracker.Core.Tests;

public sealed class GrindStartConfirmationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IndependentVisualArrivalAcceptsTheFirstRecognizedMonsterDropImmediately()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.True(detector.Observe(Projection(5, 1, Start.AddMilliseconds(-50))));
    }

    [Fact]
    public void IndependentVisualArrivalCanBeRecognizedLaterWithoutAnotherArrival()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.False(detector.Observe(Projection(0, 0, null)));
        Assert.False(detector.Observe(Projection(0, 0, null)));
        Assert.True(detector.Observe(Projection(5, 1, Start.AddMilliseconds(-50))));
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
    }

    [Fact]
    public void KnownCorrectionClosesInitialVisualAllowanceUntilAnIndependentNewArrival()
    {
        var detector = new GrindStartConfirmation(Start, acceptInitialArrival: true);
        Assert.False(detector.Observe(Projection(0, 0, null) with { QuantityCorrectionRevision = 0 }));
        var corrected = Projection(20, 5, Start) with { QuantityCorrectionRevision = 1 };
        Assert.False(detector.Observe(corrected));
        Assert.False(detector.Observe(corrected));
        Assert.True(detector.Observe(Projection(25, 6, Start.AddSeconds(1)) with { QuantityCorrectionRevision = 1 }));
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
