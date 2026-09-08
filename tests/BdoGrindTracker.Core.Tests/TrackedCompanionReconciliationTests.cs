using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class TrackedCompanionReconciliationTests
{
    private static CompanionRecognizedEntry Row(string name, uint quantity, int slot = 0, double scale = 1) =>
        new(name, quantity, (int)Math.Round((250 - slot * 50) * scale))
        { Slot = slot, QuantityBounds = new(4, 1000) };

    [Fact]
    public void LateReadRevisesAnAlreadyBookedMinimumUsingTheSameDropId()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("Helmet", uint.MaxValue)]);
        var first = Assert.Single(counter.Complete());
        Assert.Equal(4u, first.Count);
        Assert.True(first.IsMinimumQuantityEstimate);
        Assert.NotNull(first.EventId);
        counter.ProcessFrame([Row("Helmet", 6)]);
        var correction = Assert.Single(counter.Complete());
        Assert.Equal(first.EventId, correction.EventId);
        Assert.Equal(1, correction.Revision);
        Assert.Equal(2, correction.QuantityDelta);
        Assert.Equal(6, correction.TotalDropQuantity);
        counter.ProcessFrame([Row("Helmet", 6)]);
        Assert.Empty(counter.Complete());
        Assert.Empty(counter.Complete());
    }

    [Theory]
    [InlineData(.75)]
    [InlineData(1)]
    [InlineData(1.49)]
    [InlineData(2)]
    public void InteriorUnreadRowDoesNotCompressOrRecountItsNeighbors(double scale)
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 4, 0, scale), Row("C", 6, 2, scale)]);
        var initial = counter.Complete();
        counter.ProcessFrame([Row("A", 4, 0, scale), Row("B", 8, 1, scale), Row("C", 6, 2, scale)]);
        var added = Assert.Single(counter.Complete());
        Assert.Equal("B", added.Name);
        Assert.Equal(8u, added.Count);
        Assert.Equal(3, initial.Append(added).Select(row => row.EventId).Distinct().Count());
    }

    [Fact]
    public void QuantityRecoveryAtLegacyTagWrapStillRevisesTheExistingDrop()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        for (var i = 0; i < 3; i++) counter.ProcessFrame([Row("A", uint.MaxValue)]);
        var initial = Assert.Single(counter.Complete());
        counter.ProcessFrame([Row("A", 6)]);
        var correction = Assert.Single(counter.Complete());
        Assert.Equal(initial.EventId, correction.EventId);
        Assert.Equal(2, correction.QuantityDelta);
    }

    [Fact]
    public void ReappearingDifferentItemCannotReuseAnIdCarriedThroughAnUnreadSlot()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 4, 0), Row("B", 4, 1), Row("C", 4, 2)]);
        var initial = counter.Complete();
        counter.ProcessFrame([Row("A", 4, 0), Row("C", 4, 2)]);
        Assert.Empty(counter.Complete());
        counter.ProcessFrame([Row("A", 4, 0), Row("D", 4, 1), Row("C", 4, 2)]);
        var added = Assert.Single(counter.Complete());
        Assert.Equal("D", added.Name);
        Assert.DoesNotContain(initial, row => row.EventId == added.EventId);
    }

    [Fact]
    public void NewRowsAndLaterQuantityReadingAreReconciledInOneOrderedMovement()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", uint.MaxValue, 0), Row("B", 4, 1)]);
        var initial = counter.Complete();
        counter.ProcessFrame([Row("C", 4, 0), Row("A", 6, 1), Row("B", 4, 2)]);
        var result = counter.Complete();
        Assert.Equal(2, result.Count);
        Assert.Equal("C", result[0].Name);
        Assert.Equal(initial.Single(row => row.Name == "A").EventId, result[1].EventId);
        Assert.Equal(2, result[1].QuantityDelta);
    }

    [Fact]
    public void QuantityBoundsAndFixedUnitsStillApplyPerDrop()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 1, 0), Row("B", 5000, 1),
            Row("Rare", 999, 2) with { QuantityBounds = new(1, 1) }]);
        var result = counter.Complete();
        Assert.Equal([4u, 1000u, 1u], result.Select(row => row.Count));
    }

    [Fact]
    public void ResetAndVisiblyClearedPanelStartNewIds()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 4)]);
        var first = Assert.Single(counter.Complete());
        counter.ProcessFrame([]);
        counter.ProcessFrame([Row("A", 4)]);
        var second = Assert.Single(counter.Complete());
        counter.Reset();
        counter.ProcessFrame([Row("A", 4)]);
        var third = Assert.Single(counter.Complete());
        Assert.Equal(3, new[] { first.EventId, second.EventId, third.EventId }.Distinct().Count());
    }
}
