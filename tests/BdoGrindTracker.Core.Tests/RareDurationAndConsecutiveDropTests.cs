namespace BdoGrindTracker.Core.Tests;

public sealed class RareDurationAndConsecutiveDropTests
{
    private const string Necklace = "Twilight of the End - Necklace";
    private const string Earring = "Twilight of the End - Earring";
    private const string Ring = "Twilight of the End - Ring";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static LifetimeLootReconciler Tracker() =>
        new(_ => null, null, false, source: LootSource.Rare, slotCount: 1);

    private static LootObservation Row(string name = Necklace, int? quantity = 1) =>
        new(LootSource.Rare, 0, name + (quantity is null ? "" : " x " + quantity),
            name, quantity, 1, quantity is null ? 0 : 1, null, null);

    private static LootObservation Unreadable() => Row() with
    {
        RawText = "",
        ItemName = null,
        Quantity = null,
        NameConfidence = 0,
        QuantityConfidence = 0,
        RejectionReason = LootObservation.RarePaddleUnconfirmedReason,
    };

    [Theory]
    [InlineData(23)] // The recorded necklace banner spans at least 4.45 seconds.
    [InlineData(300)]
    public void LongNecklaceWithPartialAndFailedReadsKeepsOneArrival(int frames)
    {
        var tracker = Tracker();
        var first = tracker.ProcessObservations([Row()], Start);
        var identity = Assert.Single(first.ObservedDrops).EventId;
        for (var frame = 1; frame < frames; frame++)
        {
            var reading = frame % 7 == 0 ? Unreadable() : Row(quantity: frame % 3 == 0 ? null : 1);
            var current = tracker.ProcessObservations([reading], Start.AddMilliseconds(frame * 200));
            Assert.Equal(1, current.Totals.GetValueOrDefault(Necklace));
            Assert.Equal(1, current.SupportedDropCount);
            Assert.Equal(identity, Assert.Single(current.ObservedDrops).EventId);
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    public void BriefDifferentDropAfterLongNecklaceSurvivesImmediateReplacement(
        int middleFrames, int unreadableFrames)
    {
        var tracker = Tracker();
        var frame = 0;
        for (; frame < 150; frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));
        for (var middle = 0; middle < middleFrames; middle++, frame++)
            tracker.ProcessObservations([Row(Earring)], Start.AddMilliseconds(frame * 200));
        for (var unreadable = 0; unreadable < unreadableFrames; unreadable++, frame++)
            tracker.ProcessObservations([Unreadable()], Start.AddMilliseconds(frame * 200));
        for (var last = 0; last < 23; last++, frame++)
            tracker.ProcessObservations([Row(Ring)], Start.AddMilliseconds(frame * 200));

        var completed = tracker.Complete(Start.AddMilliseconds(frame * 200));
        Assert.Equal(1, completed.Totals.GetValueOrDefault(Necklace));
        Assert.Equal(1, completed.Totals.GetValueOrDefault(Earring));
        Assert.Equal(1, completed.Totals.GetValueOrDefault(Ring));
        Assert.Equal(3, completed.SupportedDropCount);
    }

    [Fact]
    public void SecondIdenticalNecklaceAfterObservedAbsenceStillCountsAfterLongFirstBanner()
    {
        var tracker = Tracker();
        var frame = 0;
        for (; frame < 150; frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));
        for (var unreadable = 0; unreadable < 10; unreadable++, frame++)
            tracker.ProcessObservations([Unreadable()], Start.AddMilliseconds(frame * 200));
        for (var blank = 0; blank < 10; blank++, frame++)
            tracker.ProcessObservations([], Start.AddMilliseconds(frame * 200));
        for (var repeated = 0; repeated < 23; repeated++, frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 200));

        var completed = tracker.Complete(Start.AddMilliseconds(frame * 200));
        Assert.Equal(2, completed.Totals.GetValueOrDefault(Necklace));
        Assert.Equal(2, completed.SupportedDropCount);
    }
}
