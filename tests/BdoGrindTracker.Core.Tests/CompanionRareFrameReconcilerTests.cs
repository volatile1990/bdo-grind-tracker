using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class CompanionRareFrameReconcilerTests
{
    [Fact]
    public void RepeatedRareObservationKeepsOneNativeEpisode()
    {
        CompanionRareFrameReconciler reconciler = new();
        List<CompanionRareCountDelta> result = [];
        for (int frame = 0; frame < 20; frame++)
        {
            result.AddRange(reconciler.ProcessFrame([new("BON Origin Shard", 1)]));
        }
        result.AddRange(reconciler.Complete());
        Assert.Equal(new CompanionRareCountDelta("BON Origin Shard", 1), Assert.Single(result));
        Assert.Equal(20ul, reconciler.FrameIndex);
        Assert.Equal(20u, reconciler.Support["BON Origin Shard"]);
    }

    [Fact]
    public void RareEpisodeCanRestartAfterTwelveEmptyFrames()
    {
        CompanionRareFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("BON Origin Shard", 1)]);
        Assert.Single(reconciler.Complete());
        for (int frame = 0; frame < 12; frame++)
        {
            Assert.Empty(reconciler.ProcessFrame([]));
        }
        reconciler.ProcessFrame([new("BON Origin Shard", 1)]);
        Assert.Equal(new CompanionRareCountDelta("BON Origin Shard", 1), Assert.Single(reconciler.Complete()));
    }

    [Fact]
    public void LongerSuffixAliasReplacesOnePreviouslyCountedUnit()
    {
        CompanionLootLedger ledger = new();
        CompanionRareFrameReconciler reconciler = new(sharedLedger: ledger);
        reconciler.ProcessFrame([new("Crystal of Origin", 1)]);
        Assert.Single(reconciler.Complete());
        reconciler.ProcessFrame([new("Silent Crystal of Origin", 1)]);
        Assert.Equal(
            [new CompanionRareCountDelta("Crystal of Origin", -1), new("Silent Crystal of Origin", 1)],
            reconciler.Complete());
        Assert.False(ledger.Totals.ContainsKey("Crystal of Origin"));
        Assert.Equal(1, ledger.Totals["Silent Crystal of Origin"]);
    }

    [Fact]
    public void ShorterSuffixAliasDoesNotReplaceLongerObservation()
    {
        CompanionRareFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("Silent Crystal of Origin", 1)]);
        Assert.Single(reconciler.Complete());
        reconciler.ProcessFrame([new("Crystal of Origin", 1)]);
        Assert.Empty(reconciler.Complete());
    }

    [Fact]
    public void SuppressedObservationUpdatesSupportWithoutCounting()
    {
        CompanionRareFrameReconciler reconciler = new();
        reconciler.ProcessFrame([new("BON Origin Shard", 1, suppressCounting: true)]);
        Assert.Empty(reconciler.Complete());
        Assert.Equal(1u, reconciler.Support["BON Origin Shard"]);
        Assert.Equal(1ul, reconciler.LastSeen["BON Origin Shard"]);
    }

    [Fact]
    public void ResetDoesNotClearSharedLedger()
    {
        CompanionLootLedger ledger = new();
        ledger.Add("Trash", 50);
        CompanionRareFrameReconciler reconciler = new(sharedLedger: ledger);
        reconciler.ProcessFrame([new("BON Origin Shard", 1)]);
        reconciler.Complete();
        reconciler.Reset();
        Assert.Equal(50, ledger.Totals["Trash"]);
        Assert.Equal(1, ledger.Totals["BON Origin Shard"]);
        Assert.Empty(reconciler.Support);
        Assert.Empty(reconciler.LastSeen);
        Assert.Equal(0ul, reconciler.FrameIndex);
        Assert.Empty(reconciler.Complete());
    }
}
