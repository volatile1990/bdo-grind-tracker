using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.Core.Tests;

public sealed class BuffSnapshotCacheTests
{
    private static readonly BuffDefinition Definition = new("test", "Test", null, TimeSpan.FromMinutes(30));

    [Fact]
    public void SnapshotIsReusedBetweenObservationsAndFrozenAcrossRefreshAndBreak()
    {
        var ledger = new BuffLedger([Definition]);
        var at = DateTimeOffset.UnixEpoch;
        ledger.Apply([new("test", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(1))], at, _ => null);
        var confirmed = ledger.Apply([new("test", TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(1))], at.AddSeconds(10), _ => null);
        Assert.Empty(confirmed.Consumptions);
        Assert.Single(confirmed.Active);
        Assert.Same(confirmed, ledger.Snapshot);
        Assert.Same(confirmed, ledger.Apply([], at, _ => null));

        var renewed = ledger.Apply([new("test", TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1))], at.AddSeconds(20), _ => null);
        Assert.NotSame(confirmed, renewed);
        Assert.Single(renewed.Consumptions);
        Assert.Empty(confirmed.Consumptions);
        ledger.BreakContinuity("test");
        Assert.Empty(ledger.Snapshot.Active);
        Assert.Single(renewed.Active);
        Assert.Single(ledger.Snapshot.Consumptions);
    }

    [Fact]
    public void PriceResolverCannotCacheAPartiallyAppliedSnapshot()
    {
        var ledger = new BuffLedger([Definition]);
        var at = DateTimeOffset.UnixEpoch;
        ledger.Apply([new("test", TimeSpan.FromMinutes(20), TimeSpan.Zero)], at, _ => null);
        var result = ledger.Apply([new("test", TimeSpan.FromMinutes(21), TimeSpan.Zero)], at.AddSeconds(10), _ =>
        {
            Assert.Empty(ledger.Snapshot.Consumptions);
            return null;
        });
        Assert.Single(result.Consumptions);
        Assert.Same(result, ledger.Snapshot);
    }

    [Fact]
    public void RestoreAndResetInvalidateSnapshotsWithoutMutatingPreviouslyPublishedData()
    {
        var ledger = new BuffLedger([Definition]);
        var empty = ledger.Snapshot;
        ledger.Apply([new("test", TimeSpan.FromMinutes(20), TimeSpan.Zero)], DateTimeOffset.UnixEpoch, _ => null);
        var populated = ledger.Apply([new("test", TimeSpan.FromMinutes(21), TimeSpan.Zero)], DateTimeOffset.UnixEpoch.AddSeconds(10), _ => null);
        ledger.Reset();
        Assert.Empty(ledger.Snapshot.Consumptions);
        Assert.Single(populated.Consumptions);
        ledger.Restore(populated);
        Assert.Single(ledger.Snapshot.Consumptions);
        Assert.Empty(ledger.Snapshot.Active);
        Assert.Empty(empty.Consumptions);
        var list = Assert.IsAssignableFrom<IList<BuffConsumption>>(ledger.Snapshot.Consumptions);
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }
}
