namespace BdoGrindTracker.Core.Tests;

public sealed class GrindStartConfirmationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

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
    public void TwoLaterTrashArrivalsConfirmTheGrind()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start)));
        Assert.False(detector.Observe(Projection(25, 6, Start.AddSeconds(1))));
        Assert.True(detector.Observe(Projection(30, 7, Start.AddSeconds(2))));
    }

    [Fact]
    public void QuantityCorrectionsAndRepeatedFramesDoNotCountAsNewDrops()
    {
        var detector = new GrindStartConfirmation(Start);
        Assert.False(detector.Observe(Projection(20, 5, Start)));
        Assert.False(detector.Observe(Projection(25, 5, Start)));
        Assert.False(detector.Observe(Projection(30, 6, Start.AddSeconds(1))));
        Assert.False(detector.Observe(Projection(35, 6, Start.AddSeconds(1))));
        Assert.False(detector.Observe(Projection(35, 6, Start.AddSeconds(1))));
        Assert.True(detector.Observe(Projection(40, 7, Start.AddSeconds(2))));
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
