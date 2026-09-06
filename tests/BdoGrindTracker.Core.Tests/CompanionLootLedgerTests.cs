using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class CompanionLootLedgerTests
{
    [Fact]
    public void AggregatesPositiveQuantitiesAndRemovesZeroTotal()
    {
        CompanionLootLedger ledger = new();
        ledger.Add("Item", 1);
        ledger.Add("Item", 2);
        Assert.Equal(3, ledger.Totals["Item"]);
        Assert.True(ledger.TryRemoveOne("Item"));
        Assert.True(ledger.TryRemoveOne("Item"));
        Assert.True(ledger.TryRemoveOne("Item"));
        Assert.False(ledger.TryRemoveOne("Item"));
        Assert.Empty(ledger.Totals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveAdditions(long count)
    {
        CompanionLootLedger ledger = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Add("Item", count));
    }

    [Fact]
    public void OverflowDoesNotOverwritePreviousTotal()
    {
        CompanionLootLedger ledger = new();
        ledger.Add("Item", long.MaxValue);
        Assert.Throws<OverflowException>(() => ledger.Add("Item", 1));
        Assert.Equal(long.MaxValue, ledger.Totals["Item"]);
    }

    [Fact]
    public void ResetClearsAllTotals()
    {
        CompanionLootLedger ledger = new();
        ledger.Add("Item", 2);
        ledger.Reset();
        Assert.Empty(ledger.Totals);
    }
}
