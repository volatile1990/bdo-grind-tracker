using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

// These tests preserve the measured 0.5.1 contract, including its frame-tag
// behavior. They do not claim that a synthetic frame sequence is ground truth.
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
