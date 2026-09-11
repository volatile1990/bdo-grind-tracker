using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimePolicyProjectionTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AlternativeBirthIdentitiesCannotAccumulateIntoThreeSpotVotes()
    {
        var spot = new AutomaticLootSpotLock();
        for (var alternative = 0; alternative < 8; alternative++)
        {
            LifetimePolicyProjection.Apply(Snapshot(2, 4), spot, null, DropQuantityCatalog.GetBounds);
            Assert.Null(spot.Spot);
        }
        LifetimePolicyProjection.Apply(Snapshot(3, 4), spot, null, DropQuantityCatalog.GetBounds);
        Assert.Equal(LootSpotCatalog.MagaiaId, spot.Spot?.Id);
    }

    [Fact]
    public void QuantityCorrectionReplacesTheAmountPriorAndRetractionRemovesItsSamples()
    {
        var spot = new AutomaticLootSpotLock();
        var anomalies = new TrashQuantityAnomalyDetector();
        var first = Snapshot(8, 4);
        LifetimePolicyProjection.Apply(first, spot, anomalies, DropQuantityCatalog.GetBounds);
        var row = new LootObservation(LootSource.Normal, 0, Helmet + " x400", Helmet, 400, 1, 1, null, null);
        Assert.Equal(4, anomalies.Assess(spot.Spot!.Id, row)!.TypicalQuantity);
        var correction = first with
        {
            PolicyDrops = first.PolicyDrops.Select(drop => drop with { Quantity = 40 }).ToArray(),
        };
        LifetimePolicyProjection.Apply(correction, spot, anomalies, DropQuantityCatalog.GetBounds);
        Assert.Equal(40, anomalies.Assess(spot.Spot.Id, row)!.TypicalQuantity);
        LifetimePolicyProjection.Apply(correction with { PolicyDrops = [] }, spot, anomalies, DropQuantityCatalog.GetBounds);
        Assert.Equal("catalog", anomalies.Assess(spot.Spot.Id, row)!.Basis);
    }

    private static LifetimeSnapshot Snapshot(int count, int quantity) => new(1, 1, Start, 1350,
        new Dictionary<string, long> { [Helmet] = (long)count * quantity }, new Dictionary<string, long>(), [], count, Start, [])
        {
            PolicyDrops = Enumerable.Range(0, count).Select(index => new LifetimeObservedDrop(
                Guid.NewGuid(), Helmet, quantity, Start.AddMilliseconds(index * 200))).ToArray(),
        };
}
