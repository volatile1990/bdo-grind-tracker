using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

// Preserve the Companion counting contract. Identical OCR rows alone do not
// reveal whether the game has produced new identical drops; these expectations
// guard compatibility and are not independently measured inventory totals.
public sealed class CompanionFrameReconcilerTests
{
    [Fact]
    public void KeepsNineFramesPendingAndFlushesAtTen()
    {
        CompanionFrameReconciler reconciler = new();
        for (int frame = 0; frame < 9; frame++)
        {
            Assert.Empty(reconciler.ProcessFrame([new("Trash", 10, 250)]));
        }
        IReadOnlyList<CompanionRecognizedEntry> result = reconciler.ProcessFrame([new("Trash", 10, 250)]);
        Assert.Equal(4, result.Count);
        Assert.All(result, item => Assert.Equal(10u, item.Count));
    }

    [Fact]
    public void DenseIdenticalInputsRetainCompanionRenewalAcrossBatchesAndCompletion()
    {
        var reconciler = new CompanionFrameReconciler();
        CompanionRecognizedEntry[] rows = [new("Trash", 8, 220)];
        var events = new List<CompanionRecognizedEntry>();
        for (var frame = 0; frame < 37; frame++)
            events.AddRange(reconciler.ProcessFrame(rows));
        events.AddRange(reconciler.Complete());
        Assert.Equal(13, events.Count);
        Assert.Equal(104L, events.Sum(entry => (long)entry.Count));
        Assert.Empty(reconciler.Complete());

        // A flush does not reset the historical renewal cadence. In particular,
        // repeated inputs must not collapse to a single lifetime-long booking.
        for (var frame = 0; frame < 13; frame++)
            events.AddRange(reconciler.ProcessFrame(rows));
        events.AddRange(reconciler.Complete());
        Assert.Equal(17, events.Count);
        Assert.Equal(136L, events.Sum(entry => (long)entry.Count));
        Assert.Empty(reconciler.Complete());
    }

    [Fact]
    public void NewIdenticalDropsInsertedAheadOfContinuingRowsAreStillCounted()
    {
        var reconciler = new CompanionFrameReconciler();
        var events = new List<CompanionRecognizedEntry>();
        events.AddRange(reconciler.ProcessFrame([new("Trash", 8, 250), new("Black Stone", 1, 200)]));

        events.AddRange(reconciler.ProcessFrame([
            new("Trash", 8, 250), new("Trash", 8, 200), new("Black Stone", 1, 150)]));
        events.AddRange(reconciler.ProcessFrame([
            new("Trash", 8, 250), new("Trash", 8, 200), new("Trash", 8, 150), new("Black Stone", 1, 100)]));
        events.AddRange(reconciler.Complete());

        Assert.Equal(3, events.Count(entry => entry.Name == "Trash"));
        Assert.Equal(24L, events.Where(entry => entry.Name == "Trash").Sum(entry => (long)entry.Count));
        Assert.Single(events, entry => entry.Name == "Black Stone");
    }

    [Fact]
    public void ScrollingFullPanelKeepsANewIdenticalDropSeparateFromTheOldOne()
    {
        var reconciler = new CompanionFrameReconciler();
        var events = new List<CompanionRecognizedEntry>();
        events.AddRange(reconciler.ProcessFrame([
            new("Trash", 8, 250), new("Black Stone", 1, 200), new("Trash", 4, 150)]));
        events.AddRange(reconciler.ProcessFrame([
            new("Trash", 8, 250), new("Trash", 8, 200), new("Black Stone", 1, 150)]));
        events.AddRange(reconciler.Complete());

        Assert.Equal(new uint[] { 8, 4, 8 }, events.Where(entry => entry.Name == "Trash").Select(entry => entry.Count));
        Assert.Single(events, entry => entry.Name == "Black Stone");
    }

    [Fact]
    public void IdenticalDropAfterAnEmptyPanelCountsAgain()
    {
        var reconciler = new CompanionFrameReconciler();
        reconciler.ProcessFrame([new("Trash", 8, 220)]);
        reconciler.ProcessFrame([]);
        reconciler.ProcessFrame([new("Trash", 8, 220)]);

        var events = reconciler.Complete();
        Assert.Equal(2, events.Count);
        Assert.All(events, entry => Assert.Equal(8u, entry.Count));
    }

    [Fact]
    public void CompleteFlushesPartialBatchOnlyOnce()
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("Trash", 18, 250)]);
        reconciler.ProcessFrame([new("Trash", 18, 250)]);
        Assert.Equal(new CompanionRecognizedEntry("Trash", 18, 250), Assert.Single(reconciler.Complete()));
        Assert.Empty(reconciler.Complete());
    }

    [Fact]
    public void PushUpAlignsOldPrefixToNewSuffix()
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("A", 1, 250), new("B", 2, 200), new("C", 3, 150)]);
        reconciler.ProcessFrame([new("D", 4, 250), new("A", 1, 200), new("B", 2, 150)]);
        Assert.Equal(["A", "B", "C", "D"], reconciler.Complete().Select(entry => entry.Name));
    }

    [Fact]
    public void MultipleNewRowsAreKeptAheadOfOverlappingRows()
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("A", 10, 250), new("B", 20, 200), new("C", 30, 150)]);
        reconciler.ProcessFrame([new("D", 40, 250), new("E", 50, 200), new("A", 10, 150)]);
        Assert.Equal(["A", "B", "C", "D", "E"], reconciler.Complete().Select(entry => entry.Name));
    }

    [Fact]
    public void MissingMiddleRowIsRepairedWithLastYProgression()
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("A", 1, 250), new("C", 3, 150), new("D", 4, 100)]);
        reconciler.ProcessFrame([new("A", 1, 250), new("B", 2, 200), new("C", 3, 150), new("D", 4, 100)]);
        Assert.Equal(["A", "B", "C", "D"], reconciler.Complete().Select(entry => entry.Name));
    }

    [Fact]
    public void MissingQuantityCanBeCopiedFromAdjacentFrame()
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("Trash", uint.MaxValue, 250)]);
        reconciler.ProcessFrame([new("Trash", 23, 250)]);
        Assert.Equal(23u, Assert.Single(reconciler.Complete()).Count);
    }

    [Theory]
    [InlineData("Dawn Crystal")]
    [InlineData("Fortunate Golden Pig King")]
    public void NativeUnitCountItemsUseOneWhenQuantityIsMissing(string name)
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new(name, uint.MaxValue, 250)]);
        reconciler.ProcessFrame([]);
        Assert.Equal(1u, Assert.Single(reconciler.Complete()).Count);
    }

    [Fact]
    public void UnresolvedFinalQuantityIsNotEmitted()
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("Trash", uint.MaxValue, 250)]);
        Assert.Empty(reconciler.Complete());
    }

    [Fact]
    public void ResetClearsPendingAndPreviouslyProcessedFrames()
    {
        CompanionFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("Old", 1, 250)]);
        reconciler.Reset();
        Assert.Empty(reconciler.Complete());
        reconciler.ProcessFrame([new("New", 2, 250)]);
        Assert.Equal("New", Assert.Single(reconciler.Complete()).Name);
    }

    [Fact]
    public void RejectsNullFrameAndNullEntry()
    {
        CompanionFrameReconciler reconciler = new();
        Assert.Throws<ArgumentNullException>(() => reconciler.ProcessFrame(null!));
        Assert.Throws<ArgumentException>(() => reconciler.ProcessFrame([null!]));
    }
}
