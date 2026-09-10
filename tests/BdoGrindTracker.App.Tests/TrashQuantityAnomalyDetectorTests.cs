using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class TrashQuantityAnomalyDetectorTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Spot = LootSpotCatalog.MagaiaId;

    [Theory]
    [InlineData(43)]
    [InlineData(45)]
    public void CountedRegularDropsFlagAnExtraDigitEvenWithCorrectCatalogMinimumTwo(int amount)
    {
        var detector = Learned(4);
        var anomaly = detector.Assess(Spot, Row(amount));
        Assert.NotNull(anomaly);
        Assert.Equal(new TrashQuantityAnomaly(amount, 4, "history", 8), anomaly);
        Assert.True(anomaly.IsPlausibleCorrection(4, new(2, 1000)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void OrdinaryBuffChangesAreNotLargeSpikes(int amount)
    {
        Assert.Null(Learned(4).Assess(Spot, Row(amount)));
        Assert.Null(Learned(2).Assess(Spot, Row(amount)));
        Assert.Null(new TrashQuantityAnomalyDetector().Assess(Spot, Row(amount)));
    }

    [Theory]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(56)]
    [InlineData(60)]
    [InlineData(64)]
    [InlineData(104)]
    [InlineData(84)]
    [InlineData(50)]
    [InlineData(338)]
    public void VerifiedLargeDropsAreOnlyReviewedAndRemainValidWhenConfirmed(int amount)
    {
        var original = Row(amount);
        var anomaly = Learned(4).Assess(Spot, original);
        Assert.NotNull(anomaly);
        Assert.True(anomaly.IsPlausibleCorrection(amount, original.QuantityBounds));
        Assert.Equal(amount, original.Quantity);
    }

    [Fact]
    public void CatalogFallbackIsConservativeUntilEnoughDistinctDropsExist()
    {
        var detector = new TrashQuantityAnomalyDetector();
        detector.ObserveCountedDrops(Spot, Enumerable.Range(0, 7).Select(_ => Drop(4)).ToArray());
        Assert.Equal(new TrashQuantityAnomaly(43, 2, "catalog", 0), detector.Assess(Spot, Row(43)));
        Assert.Null(detector.Assess(Spot, Row(15)));
        detector.ObserveCountedDrops(Spot, [Drop(4)]);
        Assert.Equal("history", detector.Assess(Spot, Row(43))!.Basis);
    }

    [Fact]
    public void DuplicateIdsRevisionsAndEstimatesCannotEstablishARegularAmount()
    {
        var detector = new TrashQuantityAnomalyDetector();
        var repeated = Drop(4);
        detector.ObserveCountedDrops(Spot, Enumerable.Repeat(repeated, 20).ToArray());
        detector.ObserveCountedDrops(Spot,
        [
            Drop(4) with { Revision = 1 }, Drop(4) with { IsMinimumQuantityEstimate = true },
            Drop(4) with { QuantityDelta = 0 }, Drop(4) with { QuantityDelta = -2 },
            Drop(4) with { EventId = null }, Drop(4) with { EventId = Guid.Empty },
            Drop(4) with { IsPlaceholder = true }, Drop(4) with { IsAlignmentAnchor = true },
            Drop(0), Drop(uint.MaxValue),
        ]);
        Assert.Equal("catalog", detector.Assess(Spot, Row(43))!.Basis);
    }

    [Fact]
    public void MissingExplicitDeltaStillLearnsANewConcreteCounterEvent()
    {
        var detector = new TrashQuantityAnomalyDetector();
        detector.ObserveCountedDrops(Spot, Enumerable.Range(0, 8).Select(_ => Drop(4) with { QuantityDelta = null }).ToArray());
        Assert.Equal("history", detector.Assess(Spot, Row(43))!.Basis);
    }

    [Fact]
    public void CorrectedDropTotalsReplaceInitialOcrMistakesWithoutGrowingHistory()
    {
        var detector = new TrashQuantityAnomalyDetector();
        var drops = Enumerable.Range(0, 8).Select(_ => Drop(43)).ToArray();
        detector.ObserveCountedDrops(Spot, drops);
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 4, 1)).ToArray());

        Assert.Equal(new TrashQuantityAnomaly(43, 4, "history", 8), detector.Assess(Spot, Row(43)));
    }

    [Fact]
    public void DuplicateAndOlderRevisionsCannotUndoANewerCorrection()
    {
        var detector = new TrashQuantityAnomalyDetector();
        var drops = Enumerable.Range(0, 8).Select(_ => Drop(43)).ToArray();
        detector.ObserveCountedDrops(Spot, drops);
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 4, 2)).ToArray());
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 8, 1)).ToArray());
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 8, 2)).ToArray());
        detector.ObserveCountedDrops(Spot, drops);
        Assert.Equal(new TrashQuantityAnomaly(43, 4, "history", 8), detector.Assess(Spot, Row(43)));

        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 8, 3)).ToArray());
        Assert.Equal(new TrashQuantityAnomaly(43, 8, "history", 8), detector.Assess(Spot, Row(43)));
    }

    [Fact]
    public void RevisionsOfUnknownIdsCannotEstablishOrExpandHistory()
    {
        var detector = Learned(4);
        detector.ObserveCountedDrops(Spot, Enumerable.Range(0, 20).Select(_ => Revision(Drop(8), 8, 1)).ToArray());
        Assert.Equal(new TrashQuantityAnomaly(43, 4, "history", 8), detector.Assess(Spot, Row(43)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(1001)]
    public void MissingNonpositiveAndOutOfBoundsRevisionTotalsAreNotLearned(int? quantity)
    {
        var detector = new TrashQuantityAnomalyDetector();
        var drops = Enumerable.Range(0, 8).Select(_ => Drop(4)).ToArray();
        detector.ObserveCountedDrops(Spot, drops);
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, quantity, 2)).ToArray());
        Assert.Equal(new TrashQuantityAnomaly(43, 4, "history", 8), detector.Assess(Spot, Row(43)));
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 8, 1)).ToArray());
        Assert.Equal(new TrashQuantityAnomaly(43, 8, "history", 8), detector.Assess(Spot, Row(43)));
    }

    [Fact]
    public void EstimatedOrPlaceholderRevisionsDoNotRewriteConcreteHistory()
    {
        var detector = new TrashQuantityAnomalyDetector();
        var drops = Enumerable.Range(0, 8).Select(_ => Drop(4)).ToArray();
        detector.ObserveCountedDrops(Spot, drops);
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 8, 1) with { IsMinimumQuantityEstimate = true }).ToArray());
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 8, 1) with { IsPlaceholder = true }).ToArray());
        detector.ObserveCountedDrops(Spot, drops.Select(drop => Revision(drop, 8, 1) with { IsAlignmentAnchor = true }).ToArray());
        Assert.Equal(new TrashQuantityAnomaly(43, 4, "history", 8), detector.Assess(Spot, Row(43)));
    }

    [Fact]
    public void RevisionPreservesSampleAgeAndEvictedIdsCannotReenterThroughCorrections()
    {
        var detector = new TrashQuantityAnomalyDetector();
        var drops = Enumerable.Range(0, 64).Select(i => Drop(i < 32 ? 4u : 8u)).ToArray();
        detector.ObserveCountedDrops(Spot, drops);
        detector.ObserveCountedDrops(Spot, [Revision(drops[0], 8, 1)]);
        Assert.Equal(new TrashQuantityAnomaly(43, 8, "history", 64), detector.Assess(Spot, Row(43)));

        detector.ObserveCountedDrops(Spot, [Drop(4)]);
        Assert.Equal("catalog", detector.Assess(Spot, Row(43))!.Basis);
        detector.ObserveCountedDrops(Spot, [Revision(drops[0], 8, 2)]);
        Assert.Equal("catalog", detector.Assess(Spot, Row(43))!.Basis);
        detector.ObserveCountedDrops(Spot, [Revision(drops[1], 8, 1)]);
        Assert.Equal(new TrashQuantityAnomaly(43, 8, "history", 64), detector.Assess(Spot, Row(43)));
    }

    [Fact]
    public void HistoryIsBoundedAndAdaptsWhenRegularDropsChange()
    {
        var detector = Learned(4);
        detector.ObserveCountedDrops(Spot, Enumerable.Range(0, 70).Select(_ => Drop(2)).ToArray());
        Assert.Equal(new TrashQuantityAnomaly(43, 2, "history", 64), detector.Assess(Spot, Row(43)));
    }

    [Fact]
    public void SplitOrDiverseHistoryUsesCatalogRatherThanAnUnstableMode()
    {
        var detector = new TrashQuantityAnomalyDetector();
        detector.ObserveCountedDrops(Spot, Enumerable.Range(0, 8).Select(i => Drop(i % 2 == 0 ? 2u : 4u)).ToArray());
        Assert.Equal("catalog", detector.Assess(Spot, Row(43))!.Basis);
    }

    [Fact]
    public void SpotChangeAndNewSessionCannotReuseOldDropHistory()
    {
        var detector = Learned(4);
        detector.ObserveCountedDrops(LootSpotCatalog.HermesiaId, []);
        Assert.Null(detector.Assess(LootSpotCatalog.HermesiaId, Row(43)));
        Assert.Equal("catalog", detector.Assess(Spot, Row(43))!.Basis);
        detector = Learned(4);
        detector.Reset();
        Assert.Equal("catalog", detector.Assess(Spot, Row(43))!.Basis);
    }

    [Fact]
    public void NonTrashRareRejectedAndMissingRowsDoNotTriggerReview()
    {
        var detector = Learned(4);
        Assert.Null(detector.Assess(Spot, Row(43) with { ItemName = "Black Stone" }));
        Assert.Null(detector.Assess(Spot, Row(43) with { Source = LootSource.Rare }));
        Assert.Null(detector.Assess(Spot, Row(43) with { RejectionReason = "ocr-geometry" }));
        Assert.Null(detector.Assess(Spot, Row(43) with { Quantity = null }));
        Assert.Null(detector.Assess(Spot, Row(0)));
        Assert.Null(detector.Assess(Spot, Row(-1)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(4, true)]
    [InlineData(19, true)]
    [InlineData(20, false)]
    [InlineData(45, false)]
    [InlineData(1001, false)]
    [InlineData(43, true)]
    public void AChangedAmountMustBeWithinBoundsAndBelowTheHistorySpikeThreshold(int amount, bool expected)
    {
        var anomaly = new TrashQuantityAnomaly(43, 4, "history", 8);
        Assert.Equal(expected, anomaly.IsPlausibleCorrection(amount, new(2, 1000)));
    }

    [Fact]
    public void CatalogPlausibilityHasItsOwnConservativeThresholdAndKeepsAnUnchangedRawValue()
    {
        var anomaly = new TrashQuantityAnomaly(43, 2, "catalog", 0);
        Assert.True(anomaly.IsPlausibleCorrection(15, new(2, 1000)));
        Assert.False(anomaly.IsPlausibleCorrection(16, new(2, 1000)));
        Assert.True(anomaly.IsPlausibleCorrection(43, new(2, 20)));
    }

    private static TrashQuantityAnomalyDetector Learned(uint quantity)
    {
        var detector = new TrashQuantityAnomalyDetector();
        detector.ObserveCountedDrops(Spot, Enumerable.Range(0, 8).Select(_ => Drop(quantity)).ToArray());
        return detector;
    }

    private static CompanionRecognizedEntry Drop(uint quantity) =>
        new(Helmet, quantity) { EventId = Guid.NewGuid(), QuantityDelta = quantity <= int.MaxValue ? (int)quantity : null };

    private static CompanionRecognizedEntry Revision(CompanionRecognizedEntry drop, int? total, int revision) =>
        new(Helmet, 0)
        {
            EventId = drop.EventId, Revision = revision, TotalDropQuantity = total,
            QuantityDelta = total is { } quantity ? quantity - (int)drop.Count : 0,
            QuantityBounds = new(2, 1000),
        };

    private static LootObservation Row(int quantity) =>
        new(LootSource.Normal, 0, Helmet + " x " + quantity, Helmet, quantity, 1, 0, null, null)
        { NativeY = 373, QuantityBounds = new(2, 1000) };
}
