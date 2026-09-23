namespace BdoGrindTracker.Core.Tests;

public sealed class LifetimeRarePaddleVerificationTests
{
    private const string Ring = "Twilight of the End - Ring";
    private const string Earring = "Apeiron Earring";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static LifetimeLootReconciler Tracker(LootSource source = LootSource.Rare) =>
        new(row => new(row.ItemName!, row.Quantity, row.NameConfidence), null, false,
            source: source, slotCount: 1);

    private static LootObservation Row(string name = Ring, int quantity = 1) =>
        new(LootSource.Rare, 0, name + " x " + quantity, name, quantity, 1, 1, null, null);

    private static LootObservation Unconfirmed() => Row(Earring, 99) with
    {
        RejectionReason = LootObservation.RarePaddleUnconfirmedReason,
    };

    [Fact]
    public void LongVerificationFailureKeepsTheSameArrivalWithoutItemOrQuantityVotes()
    {
        var tracker = Tracker();
        var first = tracker.ProcessObservations([Row()], Start);
        var identity = Assert.Single(first.ObservedDrops).EventId;
        for (var frame = 1; frame < 5; frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));

        // A true blank starts an absence interval, but a visible unconfirmed row
        // interrupts it even if the rejected OCR suggests a different item/amount.
        tracker.ProcessObservations([], Start.AddSeconds(1));
        for (var frame = 6; frame < 36; frame++)
        {
            var unconfirmed = tracker.ProcessObservations([Unconfirmed()], Start.AddMilliseconds(frame * 200));
            Assert.Equal(1, unconfirmed.Totals.GetValueOrDefault(Ring));
            Assert.False(unconfirmed.Totals.ContainsKey(Earring));
            Assert.Equal(identity, Assert.Single(unconfirmed.ObservedDrops).EventId);
        }
        for (var frame = 36; frame < 41; frame++)
        {
            var resumed = tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));
            Assert.Equal(1, resumed.Totals.GetValueOrDefault(Ring));
            Assert.Equal(1, resumed.SupportedDropCount);
            Assert.Equal(identity, Assert.Single(resumed.ObservedDrops).EventId);
        }
    }

    [Fact]
    public void UnconfirmedReadingsCannotCreateTheFirstArrivalEvenWhenTheParserRecognizesThem()
    {
        var tracker = Tracker();
        for (var frame = 0; frame < 20; frame++)
        {
            var unconfirmed = tracker.ProcessObservations([Unconfirmed()], Start.AddMilliseconds(frame * 200));
            Assert.Empty(unconfirmed.Totals);
            Assert.Equal(0, unconfirmed.SupportedDropCount);
        }
        Assert.Empty(tracker.Complete(Start.AddSeconds(4)).Totals);

        var confirmed = tracker.ProcessObservations([Row()], Start.AddSeconds(5));
        Assert.Equal(1, confirmed.Totals.GetValueOrDefault(Ring));
        Assert.False(confirmed.Totals.ContainsKey(Earring));
        Assert.Equal(1, confirmed.SupportedDropCount);
    }

    [Fact]
    public void ATrueBlankAfterVerificationFailuresStillAllowsAnotherIdenticalDrop()
    {
        var tracker = Tracker();
        for (var frame = 0; frame < 5; frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));
        for (var frame = 5; frame < 20; frame++)
            tracker.ProcessObservations([Unconfirmed()], Start.AddMilliseconds(frame * 200));
        for (var frame = 20; frame < 30; frame++)
            tracker.ProcessObservations([], Start.AddMilliseconds(frame * 200));

        LifetimeSnapshot? repeated = null;
        for (var frame = 30; frame < 35; frame++)
            repeated = tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));

        Assert.Equal(2, repeated!.Totals.GetValueOrDefault(Ring));
        Assert.Equal(2, repeated.SupportedDropCount);
        Assert.False(repeated.Totals.ContainsKey(Earring));
    }

    [Fact]
    public void RareVerificationReasonDoesNotAlterNormalSourceParsing()
    {
        var tracker = Tracker(LootSource.Normal);
        var normal = Unconfirmed() with { Source = LootSource.Normal };

        var result = tracker.ProcessObservations([normal], Start);

        Assert.Equal(99, result.Totals.GetValueOrDefault(Earring));
        Assert.Equal(1, result.SupportedDropCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void RawReinterpretationCannotReplaceAVerifiedRareIdentityOrBorrowAnotherItemsAmount(int? quantity)
    {
        var tracker = new LifetimeLootReconciler(_ => new(Earring, 99, 1), null, false,
            source: LootSource.Rare, slotCount: 1);
        for (var frame = 0; frame < 20; frame++)
            tracker.ProcessObservations([Row() with { Quantity = quantity }], Start.AddMilliseconds(frame * 200));

        var completed = tracker.Complete(Start.AddSeconds(4));
        Assert.False(completed.Totals.ContainsKey(Earring));
        Assert.Equal(quantity ?? 0, completed.Totals.GetValueOrDefault(Ring));
    }
}
