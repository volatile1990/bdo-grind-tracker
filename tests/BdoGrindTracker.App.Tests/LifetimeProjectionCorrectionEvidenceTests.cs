using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeProjectionCorrectionEvidenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(130, 1)]
    [InlineData(3, 125)]
    [InlineData(3, 97)]
    public void OldQuantityRevisionRemainsVisibleAlongsideNewDropsEvenWithNoNetChange(int revised, int added)
    {
        var composer = new LifetimeLootProjectionComposer();
        var old = Drop(1, 100);
        var first = composer.Combine(Snapshot(old), Snapshot(), Start);
        var second = composer.Combine(Snapshot(old with { Quantity = revised }, Drop(2, added)), Snapshot(), Start.AddSeconds(1));
        var repeated = composer.Combine(Snapshot(old with { Quantity = revised }, Drop(2, added)), Snapshot(), Start.AddSeconds(2));

        Assert.Equal(0L, first.Projection.QuantityCorrectionRevision);
        Assert.Equal(1L, second.Projection.QuantityCorrectionRevision);
        Assert.Equal(revised + added, second.Projection.Totals["Trash"]);
        Assert.Equal(2, second.Projection.ConfirmedDropCount);
        Assert.Equal(second.Projection.QuantityCorrectionRevision, repeated.Projection.QuantityCorrectionRevision);
        Assert.Equal(second.Projection.Revision, repeated.Projection.Revision);
        Assert.Empty(repeated.Events);
    }

    [Fact]
    public void CorrectionsIncreaseProjectionRevisionWhenTotalsCountAndArrivalAreUnchanged()
    {
        var composer = new LifetimeLootProjectionComposer();
        var firstDrop = Drop(1, 50);
        var secondDrop = Drop(2, 50);
        var first = composer.Combine(Snapshot(firstDrop, secondDrop), Snapshot(), Start);
        var revised = composer.Combine(Snapshot(firstDrop with { Quantity = 40 }, secondDrop with { Quantity = 60 }),
            Snapshot(), Start.AddSeconds(1));

        Assert.Equal(first.Projection.Totals, revised.Projection.Totals);
        Assert.Equal(first.Projection.ConfirmedDropCount, revised.Projection.ConfirmedDropCount);
        Assert.Equal(first.Projection.LatestArrivalAt, revised.Projection.LatestArrivalAt);
        Assert.Equal(1L, revised.Projection.QuantityCorrectionRevision);
        Assert.True(revised.Projection.Revision > first.Projection.Revision);
        Assert.Empty(revised.Events);
    }

    [Fact]
    public void NewDropsAndUnchangedEmptySourcesRetainZeroCorrectionVersion()
    {
        var composer = new LifetimeLootProjectionComposer();
        var empty = composer.Combine(Snapshot(), Snapshot(), Start);
        var first = composer.Combine(Snapshot(Drop(1, 100)), Snapshot(), Start.AddSeconds(1));
        var second = composer.Combine(Snapshot(Drop(1, 100), Drop(2, 31)), Snapshot(), Start.AddSeconds(2));
        var special = composer.Combine(Snapshot(Drop(1, 100), Drop(2, 31)), Snapshot(Drop(3, 1)), Start.AddSeconds(3));

        Assert.Equal(0, empty.Projection.Revision);
        Assert.All(new[] { empty.Projection, first.Projection, second.Projection, special.Projection },
            projection => Assert.Equal(0L, projection.QuantityCorrectionRevision));
    }

    [Fact]
    public void SettlingAndPolicyWindowEvictionsDoNotInventCorrections()
    {
        var composer = new LifetimeLootProjectionComposer();
        var drops = Enumerable.Range(1, 65).Select(id => Drop(id, 10)).ToArray();
        var initial = Snapshot(drops[..64]);
        composer.Combine(initial, Snapshot(), Start);
        var rolled = Snapshot(drops) with { ObservedDrops = [drops[^1]], PolicyDrops = drops[1..] };

        var added = composer.Combine(rolled, Snapshot(), Start.AddSeconds(1));
        var settled = composer.Combine(rolled with { ObservedDrops = [] }, Snapshot(), Start.AddSeconds(2));

        Assert.Equal(0L, added.Projection.QuantityCorrectionRevision);
        Assert.Equal(0L, settled.Projection.QuantityCorrectionRevision);
        Assert.Equal(650, settled.Projection.Totals["Trash"]);
        Assert.Equal(added.Projection.Revision, settled.Projection.Revision);
    }

    [Fact]
    public void RetractionInsidePolicyWindowIsDetectedAlongsideAReplacementOfEqualQuantity()
    {
        var composer = new LifetimeLootProjectionComposer();
        var old = new[] { Drop(1, 10), Drop(2, 20), Drop(3, 30) };
        composer.Combine(Snapshot(old), Snapshot(), Start);
        var next = composer.Combine(Snapshot(old[0], old[2], Drop(4, 20)), Snapshot(), Start.AddSeconds(1));

        Assert.Equal(60, next.Projection.Totals["Trash"]);
        Assert.Equal(3, next.Projection.ConfirmedDropCount);
        Assert.Equal(1L, next.Projection.QuantityCorrectionRevision);
    }

    [Fact]
    public void RenamingAKnownIdentityIsCorrectionEvidence()
    {
        var composer = new LifetimeLootProjectionComposer();
        var old = Drop(1, 10);
        composer.Combine(Snapshot(old), Snapshot(), Start);
        var next = composer.Combine(Snapshot(old with { Name = "Rare" }, Drop(2, 1)), Snapshot(), Start.AddSeconds(1));

        Assert.Equal(1L, next.Projection.QuantityCorrectionRevision);
    }

    [Fact]
    public void MissingIdentityEvidenceRemainsUnknownUntilReset()
    {
        var composer = new LifetimeLootProjectionComposer();
        var known = Snapshot(Drop(1, 10));
        var unknown = composer.Combine(known with { PolicyDrops = [], ObservedDrops = [] }, Snapshot(), Start);
        var resumed = composer.Combine(Snapshot(Drop(1, 10), Drop(2, 5)), Snapshot(), Start.AddSeconds(1));
        Assert.Null(unknown.Projection.QuantityCorrectionRevision);
        Assert.Null(resumed.Projection.QuantityCorrectionRevision);

        composer.Reset();
        Assert.Equal(0L, composer.Combine(known, Snapshot(), Start).Projection.QuantityCorrectionRevision);
    }

    [Fact]
    public void UnexplainedTotalsCannotBeCertifiedAsPureAdditions()
    {
        var composer = new LifetimeLootProjectionComposer();
        composer.Combine(Snapshot(Drop(1, 10)), Snapshot(), Start);
        var next = Snapshot(Drop(1, 10), Drop(2, 5)) with { Totals = new Dictionary<string, long> { ["Trash"] = 20 } };

        Assert.Null(composer.Combine(next, Snapshot(), Start.AddSeconds(1)).Projection.QuantityCorrectionRevision);
    }

    private static LifetimeObservedDrop Drop(int id, int quantity) =>
        new(new Guid(id, 0, 0, new byte[8]), "Trash", quantity, Start.AddMilliseconds(id));

    private static LifetimeSnapshot Snapshot(params LifetimeObservedDrop[] drops) =>
        new(1, 1, Start, 1350, drops.GroupBy(drop => drop.Name).ToDictionary(group => group.Key,
                group => group.Sum(drop => (long)drop.Quantity)), new Dictionary<string, long>(), [], drops.Length,
            drops.Length == 0 ? null : drops.Max(drop => drop.DetectedAt), drops)
        { PolicyDrops = drops };
}
