namespace BdoGrindTracker.Core.Tests;

public sealed class TemporalLootReconcilerRegressionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 19, 40, 0, TimeSpan.Zero);
    private static CompanionRecognizedEntry Row(int slot) =>
        new("Elion Follower's Helmet", 4, 373 - slot * 73) { Slot = slot, NameConfidence = .99 };

    [Fact]
    public void VerifiedFadeReplacementCreatesANewDropWithoutAQuantityChange()
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row(0)], Start);
        var original = Assert.Single(tracker.ProcessFrame([Row(0)], Start.AddMilliseconds(200)));
        var fresh = Row(0) with { AppearanceEvidence = Fade(0) };
        var events = tracker.ProcessFrame([fresh], Start.AddMilliseconds(400)).ToList();
        events.AddRange(tracker.ProcessFrame([Row(0)], Start.AddMilliseconds(600)));
        events.AddRange(tracker.Complete());
        var arrived = Assert.Single(events);
        Assert.Equal(4, arrived.QuantityDelta);
        Assert.NotEqual(original.EventId, arrived.EventId);
        Assert.Equal(Start.AddMilliseconds(400), arrived.DetectedAt);
    }

    [Fact]
    public void ARefreshedUpperSlotMeansOneScrollNotASecondCopyOfEachBrightRow()
    {
        var tracker = new TemporalLootReconciler();
        var rows = Enumerable.Range(0, 3).Select(Row).ToArray();
        tracker.ProcessFrame(rows, Start);
        var originals = tracker.ProcessFrame(rows, Start.AddMilliseconds(200)).Concat(tracker.Complete()).ToArray();
        Assert.Equal(12, originals.Sum(entry => entry.QuantityDelta));
        var fresh = rows.Select(row => row with { AppearanceEvidence = Fade(2) }).ToArray();
        var events = tracker.ProcessFrame(fresh, Start.AddMilliseconds(400)).ToList();
        var moved = Assert.Single(tracker.LastTrace.SelectMany(trace => trace.Rows), row => row.TrackId == originals[0].EventId);
        Assert.Equal(1, moved.Slot);
        events.AddRange(tracker.ProcessFrame(rows, Start.AddMilliseconds(600)));
        events.AddRange(tracker.Complete());
        Assert.Equal(4, Assert.Single(events).QuantityDelta);
    }

    [Fact]
    public void UnsupportedVisualBitsCannotForceAnArrival()
    {
        var tracker = new TemporalLootReconciler();
        Assert.Throws<ArgumentException>(() => tracker.ProcessFrame(
            [Row(0) with { AppearanceEvidence = new(1, []) }], Start));
        Assert.Equal(0, tracker.CaptureIndex);
    }

    private static NormalLootAppearanceEvidence Fade(int slot) => new(1 << slot, [new(slot, .99, .4)]);

    [Theory]
    [InlineData(false, 36)]
    [InlineData(true, 32)]
    public void RecordedFirstBurstUsesTheVerifiedFadeResetWhileLegacyReplayStaysUnchanged(bool legacyMode, int expected)
    {
        // Accepted normal row counts/times from source frames 86..109. Every
        // readable row is Helmet x4. Independent image review identifies nine
        // arrivals (86:+1, 87:+2, 91:+2, 98:+1, 99:+2, 100:+1).
        // In 97 the old glyphs are still visible but too faded for OCR; in 98
        // the bottom glyphs return bright. That pixel evidence supplies the
        // otherwise invisible replacement, not the total 576 from the session.
        (int At, int Rows)[] frames =
        [
            (0,1),(204,3),(408,3),(609,3),(809,3),(1013,5),(1218,5),(1419,3),
            (1627,2),(1828,2),(2028,1),(2230,0),(2431,1),(2635,3),(2841,4),
            (3045,4),(3248,4),(3446,4),(3646,3),(3849,2),(4053,1),(4258,0),(4462,0),(4676,0),
        ];
        var tracker = new TemporalLootReconciler(legacyMode: legacyMode);
        var events = new List<CompanionRecognizedEntry>();
        foreach (var frame in frames)
        {
            var rows = Enumerable.Range(0, frame.Rows).Select(slot => Row(slot) with
            {
                NameConfidence = .9565217383205891,
                QuantityBounds = new(2, 1000),
                AppearanceEvidence = frame.At == 2431 ? Fade(0) : null,
            }).ToArray();
            events.AddRange(tracker.ProcessFrame(rows, Start.AddMilliseconds(frame.At)));
        }
        events.AddRange(tracker.Complete());
        Assert.Equal(expected, events.Sum(entry => entry.QuantityDelta));
        Assert.Equal(expected / 4, events.Select(entry => entry.EventId).Distinct().Count());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SeveralIdenticalArrivalsMoveExistingIdentityAboveAllNewRows(int arrivals)
    {
        var tracker = new TemporalLootReconciler();
        tracker.ProcessFrame([Row(0)], Start);
        var original = Assert.Single(tracker.ProcessFrame([Row(0)], Start.AddMilliseconds(200)));
        var current = Enumerable.Range(0, arrivals + 1).Select(Row).ToArray();
        var events = tracker.ProcessFrame(current, Start.AddMilliseconds(400)).ToList();
        var originalTrace = Assert.Single(tracker.LastTrace.SelectMany(trace => trace.Rows),
            row => row.TrackId == original.EventId);
        Assert.Equal(arrivals, originalTrace.Slot);
        events.AddRange(tracker.ProcessFrame(current, Start.AddMilliseconds(600)));
        events.AddRange(tracker.Complete());
        Assert.DoesNotContain(events, entry => entry.EventId == original.EventId);
        Assert.Equal(arrivals * 4, events.Sum(entry => entry.QuantityDelta ?? (int)entry.Count));
    }
}
