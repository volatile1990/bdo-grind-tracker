namespace BdoGrindTracker.Core.Tests;

public sealed class LifetimeSpecialLootTests
{
    private const string Ring = "Twilight of the End - Ring";
    private const string Earring = "Twilight of the End - Earring";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static LifetimeLootReconciler Tracker(Func<LootObservation, LifetimeParsedReading?>? parser = null) =>
        new(parser ?? (_ => null), null, false, source: LootSource.Rare, slotCount: 1);
    private static LootObservation Row(string name = Ring, int? quantity = 1) =>
        new(LootSource.Rare, 0, name + (quantity is null ? "" : " x " + quantity), name, quantity, 1, 1, null, null);

    [Fact]
    public void DifferentTwilightAccessoriesNeverCompeteThroughTheirFamilyOrGearCategory()
    {
        var tracker = Tracker();
        LifetimeSnapshot? last = null;
        for (var frame = 0; frame < 50; frame++)
            last = tracker.ProcessObservations([Row(frame < 20 ? Earring : Ring)], Start.AddMilliseconds(frame * 450));
        Assert.Equal(1, last!.Totals[Earring]);
        Assert.Equal(1, last.Totals[Ring]);
        Assert.Equal(2, last.SupportedDropCount);
    }

    [Fact]
    public void UnchangedNotificationAndCaptureGapDoNotCreateAdditionalArrivals()
    {
        var tracker = Tracker();
        var first = tracker.ProcessObservations([Row()], Start);
        var identity = Assert.Single(first.ObservedDrops).EventId;
        LifetimeSnapshot last = first;
        for (var frame = 1; frame <= 150; frame++)
            last = tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 450));
        last = tracker.ProcessObservations([Row()], Start.AddHours(1));
        Assert.Equal(1, last.Totals[Ring]);
        Assert.Equal(identity, Assert.Single(last.ObservedDrops).EventId);
    }

    [Fact]
    public void BriefOcrGapKeepsOneArrivalButObservedBlankPhaseAllowsAnIdenticalDrop()
    {
        var tracker = Tracker();
        for (var frame = 0; frame < 5; frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));
        tracker.ProcessObservations([], Start.AddSeconds(1));
        var resumed = tracker.ProcessObservations([Row()], Start.AddMilliseconds(1400));
        Assert.Equal(1, resumed.Totals[Ring]);
        tracker.ProcessObservations([], Start.AddSeconds(2));
        tracker.ProcessObservations([], Start.AddSeconds(3));
        tracker.ProcessObservations([], Start.AddMilliseconds(3600));
        LifetimeSnapshot? repeated = null;
        for (var frame = 0; frame < 5; frame++)
            repeated = tracker.ProcessObservations([Row()], Start.AddMilliseconds(3800 + frame * 200));
        Assert.Equal(2, repeated!.Totals[Ring]);
        Assert.Equal(2, repeated.SupportedDropCount);
    }

    [Fact]
    public void ABlankBeforeACaptureGapCannotProveTheNotificationDisappeared()
    {
        var tracker = Tracker();
        for (var frame = 0; frame < 5; frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));
        tracker.ProcessObservations([], Start.AddSeconds(1));
        Assert.Equal(1, tracker.ProcessObservations([Row()], Start.AddHours(1)).Totals[Ring]);
        tracker.ProcessObservations([], Start.AddHours(1).AddMilliseconds(200));
        tracker.ProcessObservations([], Start.AddHours(2));
        var afterSparseBlanks = tracker.ProcessObservations([Row()], Start.AddHours(2).AddMilliseconds(200));
        Assert.Equal(1, afterSparseBlanks.Totals[Ring]);
        Assert.Equal(1, afterSparseBlanks.SupportedDropCount);
    }

    [Fact]
    public void OneWrongAccessoryReadIsReversibleWithoutReplacingTheOriginalItem()
    {
        var tracker = Tracker();
        LifetimeSnapshot? last = null;
        for (var frame = 0; frame < 20; frame++)
            last = tracker.ProcessObservations([Row(frame == 8 ? Earring : Ring)], Start.AddMilliseconds(frame * 200));
        Assert.Equal(1, last!.Totals[Ring]);
        Assert.False(last.Totals.ContainsKey(Earring));
        Assert.Equal(1, last.SupportedDropCount);
    }

    [Fact]
    public void QuantityOutlierIsCorrectedWithinTheSameArrival()
    {
        var tracker = Tracker();
        var first = tracker.ProcessObservations([Row(quantity: 99)], Start);
        var identity = Assert.Single(first.ObservedDrops).EventId;
        LifetimeSnapshot last = first;
        for (var frame = 1; frame < 10; frame++)
            last = tracker.ProcessObservations([Row(quantity: 1)], Start.AddMilliseconds(frame * 200));
        Assert.Equal(1, last.Totals[Ring]);
        Assert.Equal(identity, Assert.Single(last.ObservedDrops).EventId);
    }

    [Fact]
    public void PartialTextCanMaintainAReadQuantityButCannotInventANewAmount()
    {
        var tracker = Tracker();
        var unknown = Row() with { ItemName = null, Quantity = null, RawText = "Twilight of the End - Ri", RejectionReason = "item-catalog" };
        for (var frame = 0; frame < 5; frame++)
            Assert.Empty(tracker.ProcessObservations([unknown], Start.AddMilliseconds(frame * 200)).Totals);
        tracker.ProcessObservations([Row()], Start.AddSeconds(1));
        LifetimeSnapshot? last = null;
        for (var frame = 0; frame < 100; frame++)
            last = tracker.ProcessObservations([unknown], Start.AddMilliseconds(1200 + frame * 200));
        Assert.Equal(1, last!.Totals[Ring]);
        Assert.Equal(1, last.SupportedDropCount);
    }

    [Fact]
    public void ContextCanReinterpretPendingEvidenceAndExplicitExclusionRemovesIt()
    {
        var excluded = false;
        var tracker = Tracker(_ => excluded ? LifetimeParsedReading.Excluded : new(Ring, 1, 1));
        tracker.ProcessObservations([Row()], Start);
        tracker.ProcessObservations([Row()], Start.AddMilliseconds(200));
        excluded = true;
        Assert.Empty(tracker.Complete(Start.AddMilliseconds(400)).Totals);
    }

    [Fact]
    public void StaleDeliveryAndResetUseTheSameNormalGuards()
    {
        var tracker = Tracker();
        tracker.ProcessObservations([Row()], Start);
        var stale = tracker.ProcessObservations([Row(Earring)], Start);
        Assert.Equal(1, stale.Totals[Ring]);
        Assert.Empty(stale.Deltas);
        Assert.Empty(tracker.Complete(Start).Deltas);
        tracker.Reset();
        Assert.Empty(tracker.Complete(Start).Totals);
        Assert.Equal(1, tracker.ProcessObservations([Row(Earring)], Start).Totals[Earring]);
    }

    [Fact]
    public void SourceAndSlotConfigurationKeepNormalAndSpecialIdentitiesIndependent()
    {
        var special = Tracker();
        Assert.Empty(special.ProcessObservations([Row() with { Source = LootSource.Normal }], Start).Totals);
        Assert.Throws<ArgumentException>(() => special.ProcessObservations([Row() with { Slot = 1 }], Start.AddMilliseconds(200)));
        var normal = new LifetimeLootReconciler(_ => null);
        var normalId = Assert.Single(normal.ProcessObservations([Row() with { Source = LootSource.Normal }], Start).ObservedDrops).EventId;
        var rareId = Assert.Single(special.ProcessObservations([Row()], Start.AddMilliseconds(200)).ObservedDrops).EventId;
        Assert.NotEqual(normalId, rareId);
    }
}
