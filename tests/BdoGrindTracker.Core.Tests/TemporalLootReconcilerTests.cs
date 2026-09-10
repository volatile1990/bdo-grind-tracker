namespace BdoGrindTracker.Core.Tests;

public sealed class TemporalLootReconcilerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static CompanionRecognizedEntry Row(string name = "Trash", uint quantity = 5, int slot = 0) =>
        new(name, quantity, 250 - slot * 50) { Slot = slot, NameConfidence = .99 };

    [Theory]
    [InlineData(200)]
    [InlineData(450)]
    public void StationaryRowNeverRenewsJustBecauseTimePasses(int interval)
    {
        var tracker = new TemporalLootReconciler();
        var events = new List<CompanionRecognizedEntry>();
        for (var at = 0; at <= 12000; at += interval)
            events.AddRange(tracker.ProcessFrame([Row()], Start.AddMilliseconds(at)));
        events.AddRange(tracker.Complete());
        var drop = Assert.Single(events);
        Assert.Equal(5u, drop.Count);
        Assert.Equal(Start, drop.DetectedAt);
        Assert.NotNull(drop.EventId);
        Assert.Empty(tracker.Complete());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public void ThreeIdenticalArrivalsWithVisibleScrollProduceThreeDrops(int interval)
    {
        var events = Simulate(interval, [(0, "Trash", 5), (600, "Trash", 5), (1200, "Trash", 5)]);
        Assert.Equal(15, events.Sum(Delta));
        Assert.Equal(3, events.Where(e => e.Revision == 0).Select(e => e.EventId).Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(600)]
    public void OneLargeOcrOutlierDoesNotSettleAsTheDropAmount(int glitchAt)
    {
        var tracker = new TemporalLootReconciler();
        var events = new List<CompanionRecognizedEntry>();
        for (var at = 0; at <= 1200; at += 200)
            events.AddRange(tracker.ProcessFrame([Row(quantity: at == glitchAt ? 500u : 5u)], Start.AddMilliseconds(at)));
        events.AddRange(tracker.Complete());
        Assert.Equal(5, events.Sum(Delta));
        Assert.All(events, e => Assert.Equal(5, e.TotalDropQuantity));
    }

    [Fact]
    public void LaterQuantityMajorityRevisesThePublishedIdDownward()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row(quantity: 50)], Start);
        var first = Assert.Single(tracker.ProcessFrame([Row(quantity: 50)], Start.AddMilliseconds(200)));
        var changes = new List<CompanionRecognizedEntry>();
        for (var at = 400; at <= 1200; at += 200)
            changes.AddRange(tracker.ProcessFrame([Row()], Start.AddMilliseconds(at)));
        changes.AddRange(tracker.Complete());
        var revision = Assert.Single(changes);
        Assert.Equal(first.EventId, revision.EventId);
        Assert.Equal(1, revision.Revision);
        Assert.Equal(-45, revision.QuantityDelta);
        Assert.Equal(5, revision.TotalDropQuantity);
        Assert.Equal(first.DetectedAt, revision.DetectedAt);
    }

    [Fact]
    public void PublishedQuantityDoesNotOscillateOnTiedVotes()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row(quantity: 8)], Start);
        var first = Assert.Single(tracker.ProcessFrame([Row(quantity: 8)], Start.AddMilliseconds(200)));
        Assert.Empty(tracker.ProcessFrame([Row(quantity: 5)], Start.AddMilliseconds(400)));
        Assert.Empty(tracker.ProcessFrame([Row(quantity: 5)], Start.AddMilliseconds(600)));
        Assert.Empty(tracker.Complete());
        Assert.Equal(8, first.TotalDropQuantity);
    }

    [Fact]
    public void MissingQuantityCanFlushAsMinimumThenReviseWithMeasuredEvidence()
    {
        var tracker = new TemporalLootReconciler();
        var unknown = Row(quantity: uint.MaxValue) with { QuantityBounds = new(4, 100) };
        Assert.Empty(tracker.ProcessFrame([unknown], Start));
        var estimate = Assert.Single(tracker.Complete());
        Assert.Equal(4u, estimate.Count);
        Assert.True(estimate.IsMinimumQuantityEstimate);
        var changes = tracker.ProcessFrame([Row(quantity: 6) with { QuantityBounds = new(4, 100) }], Start.AddMilliseconds(200))
            .Concat(tracker.ProcessFrame([Row(quantity: 6) with { QuantityBounds = new(4, 100) }], Start.AddMilliseconds(400)))
            .Concat(tracker.Complete()).ToArray();
        var correction = Assert.Single(changes);
        Assert.Equal(estimate.EventId, correction.EventId);
        Assert.Equal(2, correction.QuantityDelta);
        Assert.Equal(6, correction.TotalDropQuantity);
    }

    [Fact]
    public void UnknownWithoutVerifiedMinimumDoesNotInventQuantity()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row(quantity: uint.MaxValue)], Start);
        Assert.Empty(tracker.Complete());
        tracker.ProcessFrame([], Start.AddMilliseconds(200));
        tracker.ProcessFrame([], Start.AddMilliseconds(400));
        Assert.Empty(tracker.ProcessFrame([], Start.AddSeconds(2)));
        Assert.Empty(tracker.Complete());
    }

    [Fact]
    public void BoundsApplyBeforeVotingAndFixedUnitIsSafe()
    {
        var tracker = new TemporalLootReconciler();
        var row = Row(quantity: 500) with { QuantityBounds = new(1, 1) };
        tracker.ProcessFrame([row], Start);
        var drop = Assert.Single(tracker.ProcessFrame([row], Start.AddMilliseconds(200)));
        Assert.Equal(1u, drop.Count);
    }

    [Fact]
    public void FixedUnitUnknownIsMeasuredPolicyUnitWithoutWaitingForFlush()
    {
        var tracker = new TemporalLootReconciler();
        var row = Row(quantity: uint.MaxValue) with { QuantityBounds = new(1, 1) };
        tracker.ProcessFrame([row], Start);
        var drop = Assert.Single(tracker.ProcessFrame([row], Start.AddMilliseconds(200)));
        Assert.Equal(1u, drop.Count);
        Assert.False(drop.IsMinimumQuantityEstimate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlyConfirmedSpotBoundsReevaluatePreviousVotes(bool alreadyPublished)
    {
        var tracker = new TemporalLootReconciler();
        var events = new List<CompanionRecognizedEntry>();
        var row = Row(quantity: 5) with { QuantityBounds = new(1, 5) };
        events.AddRange(tracker.ProcessFrame([row], Start));
        events.AddRange(tracker.ProcessFrame([row], Start.AddMilliseconds(alreadyPublished ? 200 : 100)));
        events.AddRange(tracker.ProcessFrame([Row(quantity: 1) with { QuantityBounds = new(1, 1) }],
            Start.AddMilliseconds(400)));
        events.AddRange(tracker.Complete());
        Assert.Equal(1, events.Sum(Delta));
        Assert.Equal(1u, events[^1].QuantityBounds!.Maximum);
        Assert.Equal(1u, events[^1].Count);
        if (alreadyPublished)
        {
            Assert.Equal(2, events.Count);
            Assert.Equal(events[0].EventId, events[1].EventId);
            Assert.Equal(-4, events[1].QuantityDelta);
        }
        else Assert.Single(events);
    }

    [Fact]
    public void BlankFrameDoesNotDuplicateRecoveredRows()
    {
        var tracker = new TemporalLootReconciler();
        var events = new List<CompanionRecognizedEntry>();
        events.AddRange(tracker.ProcessFrame([Row()], Start));
        events.AddRange(tracker.ProcessFrame([Row()], Start.AddMilliseconds(200)));
        events.AddRange(tracker.ProcessFrame([], Start.AddMilliseconds(400)));
        events.AddRange(tracker.ProcessFrame([Row()], Start.AddMilliseconds(600)));
        events.AddRange(tracker.Complete());
        Assert.Equal(5, events.Sum(Delta));
        Assert.Single(events);
    }

    [Fact]
    public void ObservedAbsenceAllowsANewIdenticalPickup()
    {
        var events = Simulate(200, [(0, "Trash", 5), (2400, "Trash", 5)]);
        Assert.Equal(10, events.Sum(Delta));
        Assert.Equal(2, events.Count(e => e.Revision == 0));
    }

    [Fact]
    public void CaptureGapDoesNotRenewTheSameStillVisibleRow()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row()], Start);
        var first = Assert.Single(tracker.Complete());
        Assert.Empty(tracker.ProcessFrame([Row()], Start.AddMinutes(2)));
        Assert.Empty(tracker.Complete());
        Assert.Equal(5u, first.Count);
    }

    [Fact]
    public void InteriorOcrHoleKeepsItsSlotAndCountsOnlyTheRecoveredItem()
    {
        var tracker = new TemporalLootReconciler();
        var events = new List<CompanionRecognizedEntry>();
        events.AddRange(tracker.ProcessFrame([Row("A", 2, 0), Row("C", 7, 2)], Start));
        events.AddRange(tracker.ProcessFrame([Row("A", 2, 0), Row("C", 7, 2)], Start.AddMilliseconds(200)));
        events.AddRange(tracker.ProcessFrame([Row("A", 2, 0), Row("B", 3, 1), Row("C", 7, 2)], Start.AddMilliseconds(400)));
        events.AddRange(tracker.ProcessFrame([Row("A", 2, 0), Row("B", 3, 1), Row("C", 7, 2)], Start.AddMilliseconds(600)));
        events.AddRange(tracker.Complete());
        Assert.Equal(3, events.Count);
        Assert.Equal(12, events.Sum(Delta));
    }

    [Fact]
    public void AlignmentOnlyFramesCannotCreateOrReviseLoot()
    {
        var tracker = new TemporalLootReconciler();
        var anchor = Row(quantity: 500) with { IsAlignmentAnchor = true, AlignmentPreviousSlot = 0 };
        Assert.Empty(tracker.ProcessFrame([anchor], Start));
        Assert.Empty(tracker.Complete());
        tracker.ProcessFrame([Row()], Start.AddMilliseconds(200));
        var first = Assert.Single(tracker.Complete());
        Assert.Empty(tracker.ProcessFrame([anchor], Start.AddMilliseconds(400)));
        Assert.Empty(tracker.Complete());
        Assert.Equal(5u, first.Count);
    }

    [Fact]
    public void AlignmentOnlyScrollMovesTheOldIdentityWithoutBookingAnArrival()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row()], Start);
        var first = Assert.Single(tracker.Complete());
        Assert.Empty(tracker.ProcessFrame([Row(slot: 1) with
            { IsAlignmentAnchor = true, AlignmentPreviousSlot = 0 }], Start.AddMilliseconds(200)));
        Assert.Empty(tracker.Complete());
        tracker.ProcessFrame([Row("New", 2), Row(slot: 1)], Start.AddMilliseconds(400));
        var added = tracker.ProcessFrame([Row("New", 2), Row(slot: 1)], Start.AddMilliseconds(600))
            .Concat(tracker.Complete()).ToArray();
        Assert.Equal("New", Assert.Single(added).Name);
        Assert.NotEqual(first.EventId, added[0].EventId);
    }

    [Fact]
    public void ContradictoryAlignmentAnchorsDegradeWithoutThrowingOrAddingLoot()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row("A", 2), Row("B", 3, 1)], Start);
        Assert.Equal(2, tracker.Complete().Count);
        Assert.Empty(tracker.ProcessFrame([
            Row("A", 200, 0) with { IsAlignmentAnchor = true, AlignmentPreviousSlot = 1 },
            Row("B", 300, 1) with { IsAlignmentAnchor = true, AlignmentPreviousSlot = 0 },
        ], Start.AddMilliseconds(200)));
        Assert.Empty(tracker.Complete());
    }

    [Theory]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(600)]
    public void SustainedMixedFeedCountsEveryObservedArrival(int spacing)
    {
        var births = Enumerable.Range(0, 80).Select(index => (index * spacing,
            index % 3 == 0 ? "Stone" : "Trash", index % 3 == 0 ? 1u : 5u)).ToArray();
        var events = Simulate(200, births);
        Assert.Equal(births.Sum(row => row.Item3), events.Sum(row => (long)Delta(row)));
        Assert.Equal(80, events.Count(row => row.Revision == 0));
    }

    [Fact]
    public void OneVisibleReadCanBeSettledAtSessionEnd()
    {
        var tracker = new TemporalLootReconciler();
        Assert.Empty(tracker.ProcessFrame([Row("Rare in feed", 1)], Start));
        var drop = Assert.Single(tracker.Complete());
        Assert.Equal(1u, drop.Count);
        Assert.Equal(Start, drop.DetectedAt);
    }

    [Fact]
    public void StaleTimestampCannotChangeStateOrVotes()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row()], Start);
        Assert.Empty(tracker.ProcessFrame([Row(quantity: 500)], Start));
        Assert.Empty(tracker.ProcessFrame([Row(quantity: 500)], Start.AddSeconds(-1)));
        Assert.Equal(5u, Assert.Single(tracker.Complete()).Count);
    }

    [Fact]
    public void ResetBeginsANewCounterAndFlushIsIdempotent()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row()], Start);
        var first = Assert.Single(tracker.Complete());
        tracker.Reset();
        Assert.Equal(0, tracker.CaptureIndex);
        Assert.Empty(tracker.LastTrace);
        tracker.ProcessFrame([Row()], Start.AddSeconds(1));
        var second = Assert.Single(tracker.Complete());
        Assert.NotEqual(first.EventId, second.EventId);
        Assert.Empty(tracker.Complete());
    }

    [Fact]
    public void InvalidSlotsFailBeforeMutatingTheCounter()
    {
        var tracker = new TemporalLootReconciler();
        Assert.Throws<ArgumentException>(() => tracker.ProcessFrame([Row(), Row()], Start));
        Assert.Throws<ArgumentException>(() => tracker.ProcessFrame([Row() with { Slot = 6 }], Start));
        Assert.Equal(0, tracker.CaptureIndex);
        Assert.Empty(tracker.Complete());
    }

    private static List<CompanionRecognizedEntry> Simulate(int interval, (int At, string Name, uint Amount)[] births)
    {
        var tracker = new TemporalLootReconciler();
        var events = new List<CompanionRecognizedEntry>();
        for (var at = 0; at <= births.Max(drop => drop.At) + 3200; at += interval)
        {
            var rows = births.Where(drop => drop.At <= at && at - drop.At < 1300)
                .OrderByDescending(drop => drop.At).Take(6)
                .Select((drop, slot) => Row(drop.Name, drop.Amount, slot)).ToArray();
            events.AddRange(tracker.ProcessFrame(rows, Start.AddMilliseconds(at)));
        }
        events.AddRange(tracker.Complete());
        return events;
    }
    private static int Delta(CompanionRecognizedEntry entry) => entry.QuantityDelta ?? checked((int)entry.Count);
}
