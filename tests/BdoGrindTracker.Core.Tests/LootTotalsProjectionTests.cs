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

    [Fact]
    public void OptionalCorrectionEvidenceMustBeNonnegativeAndSurvivesJsonRoundTrip()
    {
        var original = new LootTotalsProjection(2, new Dictionary<string, long> { ["Trash"] = 131 }, 2,
            DateTimeOffset.UnixEpoch) { QuantityCorrectionRevision = 1 };
        original.Validate();
        var restored = System.Text.Json.JsonSerializer.Deserialize<LootTotalsProjection>(
            System.Text.Json.JsonSerializer.Serialize(original))!;
        Assert.Equal(1L, restored.QuantityCorrectionRevision);
        restored.Validate();
        Assert.Throws<ArgumentException>(() => (original with { QuantityCorrectionRevision = -1 }).Validate());
        (original with { QuantityCorrectionRevision = 0 }).Validate();
        (original with { QuantityCorrectionRevision = null }).Validate();
        Assert.Null(new LootTotalsProjection(0, new Dictionary<string, long>(), 0, null).QuantityCorrectionRevision);
    }
}
