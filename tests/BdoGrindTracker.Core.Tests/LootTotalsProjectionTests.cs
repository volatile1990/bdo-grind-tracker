namespace BdoGrindTracker.Core.Tests;

public sealed class LootTotalsProjectionTests
{
    [Fact]
    public void ConstructorCopiesAndFreezesTotals()
    {
        var input = new Dictionary<string, long> { ["Helmet"] = 4 };
        var projection = new LootTotalsProjection(1, input, 1, DateTimeOffset.UnixEpoch);
        input["Helmet"] = 400;
        Assert.Equal(4, projection.Totals["Helmet"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)projection.Totals).Clear());
        projection.Validate();
    }

    [Fact]
    public void InvalidTotalsAndRevisionAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new LootTotalsProjection(-1, new Dictionary<string, long>(), 0, null));
        Assert.Throws<ArgumentException>(() => new LootTotalsProjection(0, new Dictionary<string, long>(), -1, null));
        Assert.Throws<ArgumentException>(() => new LootTotalsProjection(0, new Dictionary<string, long> { [" "] = 1 }, 0, null));
        Assert.Throws<ArgumentException>(() => new LootTotalsProjection(0, new Dictionary<string, long> { ["Helmet"] = -1 }, 0, null));
        Assert.Throws<OverflowException>(() => new LootTotalsProjection(0,
            new Dictionary<string, long> { ["Helmet"] = long.MaxValue, ["Stone"] = 1 }, 0, null));
    }
}
