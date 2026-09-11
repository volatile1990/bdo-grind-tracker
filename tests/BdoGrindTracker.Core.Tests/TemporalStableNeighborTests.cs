using System.Globalization;

namespace BdoGrindTracker.Core.Tests;

public sealed class TemporalStableNeighborTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Stone = "Black Stone";
    private const double HelmetConfidence = 0.9565217383205891;

    [Fact]
    public void RecordedFrames1240Through1247KeepOneStoneIdentityAcrossTheNeighborChange()
    {
        // Exact accepted normal observations from frames 1240..1247 of
        // loot-20260910-150334-c7555f5bbf8745829d035edd9248fbe1, preserved in
        // artifacts/loot-neighbor-fix-qa/recorded-bounds/observations.jsonl.
        // Include the recorded timing, bounds, confidence and unresolved quantity.
        // This tests the verified Stone continuity, not a guessed Helmet total.
        var frames = new (string At, CompanionRecognizedEntry[] Rows)[]
        {
            ("2026-09-10T15:13:16.3016093Z", [H(0), H(1, "Elion Follower(s Helmet x 4"), H(2)]),
            ("2026-09-10T15:13:16.7688103Z", [H(0), H(1, "Elion Follower(s Helmet x 4"), H(2, "Elion Follower's Helmet x 4", 1)]),
            ("2026-09-10T15:13:17.2386632Z", [H(0), S(1), H(2)]),
            ("2026-09-10T15:13:17.7073706Z", [H(0), S(1), H(2, "Elion Follower's Helmet x 4", 1)]),
            ("2026-09-10T15:13:18.17009Z", [H(0), H(1, "Elion Follower(s Helmet x 4"), H(2, "Elion Follower' Helmet x 4"), S(3, uint.MaxValue)]),
            ("2026-09-10T15:13:18.6369094Z", [H(0), H(1, "Elion Follower(s Helmet x 4"), H(2), H(3, "Elion Followerrs Helmet x 4")]),
            ("2026-09-10T15:13:19.1147462Z", [H(0), H(1, "Elion Follower(s Helmet x 4"), H(2, "Elion Follower's Helmet x 4", 1), H(3, "Elion Followers Helmet x 4")]),
            ("2026-09-10T15:13:19.5788108Z", [H(0), H(1, "Elion Follower's Helmet x 4", 1)]),
        };
        var tracker = new TemporalLootReconciler();
        var events = new List<CompanionRecognizedEntry>();
        var traces = new List<NormalLootReconciliationTrace>();
        foreach (var frame in frames)
        {
            events.AddRange(tracker.ProcessFrame(frame.Rows,
                DateTimeOffset.Parse(frame.At, CultureInfo.InvariantCulture)));
            traces.AddRange(tracker.LastTrace);
        }
        events.AddRange(tracker.Complete());

        var stone = Assert.Single(events, entry => entry.Name == Stone);
        Assert.Equal(1u, stone.Count);
        Assert.Equal(1, stone.QuantityDelta);
        Assert.Equal(0, stone.Revision);
        Assert.NotNull(stone.EventId);
        Assert.False(stone.IsMinimumQuantityEstimate);
        Assert.Equal(DateTimeOffset.Parse(frames[2].At, CultureInfo.InvariantCulture), stone.DetectedAt);
        var first = StoneAt(traces, 3); // Source frame 1242.
        var repeated = StoneAt(traces, 4); // Same Stone; neighboring OCR spelling changes.
        var scrolled = StoneAt(traces, 5); // Source frame 1244, quantity unreadable.
        // The first observation may still favor another provisional explanation.
        // Repeated evidence must settle the measured Stone and retain its identity.
        Assert.Equal(stone.EventId, repeated.TrackId);
        Assert.Equal(repeated.TrackId, scrolled.TrackId);
        Assert.Equal(1, first.Slot);
        Assert.Equal(1, repeated.Slot);
        Assert.Equal(3, scrolled.Slot);
        Assert.Empty(tracker.Complete());
    }

    [Fact]
    public void ANewLeadingStoneWithThePreviousStoneStillVisibleGetsItsOwnIdentity()
    {
        var tracker = new TemporalLootReconciler();
        var at = new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero);
        var events = new List<CompanionRecognizedEntry>();
        events.AddRange(tracker.ProcessFrame([H(0), S(1), H(2)], at));
        events.AddRange(tracker.ProcessFrame([H(0), S(1), H(2)], at.AddMilliseconds(200)));
        var firstStone = Assert.Single(events, entry => entry.Name == Stone);
        events.AddRange(tracker.ProcessFrame([S(0), H(1), S(2), H(3)], at.AddMilliseconds(400)));
        events.AddRange(tracker.ProcessFrame([S(0), H(1), S(2), H(3)], at.AddMilliseconds(600)));
        var oldStone = Assert.Single(tracker.LastTrace.SelectMany(trace => trace.Rows),
            row => row.ItemName == Stone && row.Slot == 2);
        events.AddRange(tracker.Complete());

        var stones = events.Where(entry => entry.Name == Stone).ToArray();
        Assert.Equal(2, stones.Length);
        Assert.All(stones, stone =>
        {
            Assert.Equal(1u, stone.Count);
            Assert.Equal(0, stone.Revision);
            Assert.NotNull(stone.EventId);
        });
        Assert.NotEqual(stones[0].EventId, stones[1].EventId);
        Assert.Equal(firstStone.EventId, oldStone.TrackId);
    }

    [Fact]
    public void OneStoneNameGlitchOnAPublishedHelmetDoesNotCreateAnotherDrop()
    {
        var tracker = new TemporalLootReconciler();
        var at = new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero);
        var events = new List<CompanionRecognizedEntry>();
        events.AddRange(tracker.ProcessFrame([H(0)], at));
        events.AddRange(tracker.ProcessFrame([H(0)], at.AddMilliseconds(200)));
        var original = Assert.Single(events);
        events.AddRange(tracker.ProcessFrame([S(0)], at.AddMilliseconds(400)));
        events.AddRange(tracker.ProcessFrame([H(0)], at.AddMilliseconds(600)));
        events.AddRange(tracker.ProcessFrame([H(0)], at.AddMilliseconds(800)));
        var recovered = Assert.Single(tracker.LastTrace.SelectMany(trace => trace.Rows),
            row => row.ItemName == Helmet && row.Slot == 0);
        events.AddRange(tracker.Complete());

        var only = Assert.Single(events);
        Assert.Equal(Helmet, only.Name);
        Assert.Equal(4u, only.Count);
        Assert.Equal(original.EventId, recovered.TrackId);
    }

    private static NormalLootRowTrace StoneAt(IEnumerable<NormalLootReconciliationTrace> traces, long capture) =>
        Assert.Single(traces.Where(trace => trace.CaptureIndex == capture).SelectMany(trace => trace.Rows),
            row => row.ItemName == Stone);

    private static CompanionRecognizedEntry H(int slot, string rawText = "Elion Followerts Helmet x 4",
        double confidence = HelmetConfidence) => new(Helmet, 4, NativeY(slot))
    {
        Slot = slot, RawText = rawText, NameConfidence = confidence, QuantityBounds = new(4, 1000),
    };

    private static CompanionRecognizedEntry S(int slot, uint quantity = 1) => new(Stone, quantity, NativeY(slot))
    {
        Slot = slot, RawText = quantity == uint.MaxValue ? "Black Stone" : "Black Stone x I",
        NameConfidence = 1, QuantityBounds = new(1, 50),
    };

    private static int NativeY(int slot) => slot switch
    {
        0 => 373, 1 => 298, 2 => 224, 3 => 149, _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}
