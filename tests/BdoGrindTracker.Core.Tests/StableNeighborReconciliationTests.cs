using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class StableNeighborReconciliationTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Stone = "Black Stone";
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-10T15:13:16Z");

    private static CompanionRecognizedEntry H(int slot) => Row(Helmet, 4, slot);
    private static CompanionRecognizedEntry S(int slot) => Row(Stone, 1, slot);
    private static CompanionRecognizedEntry Row(string name, uint count, int slot) =>
        new(name, count, (int)Math.Round(373 - slot * 74.6))
        { Slot = slot, QuantityBounds = new(1, 1000) };

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(7)]
    public void RecordedFrames1240Through1247KeepStoneIdWhenAnotherRowsTagWraps(int emptyPrefix)
    {
        // Original recognized normal rows/timestamps from the user's
        // loot-20260910-150334-c7555f5bbf8745829d035edd9248fbe1 recording.
        var frames = new (string Time, CompanionRecognizedEntry[] Rows)[]
        {
            ("2026-09-10T15:13:16.3016093Z", [H(0), H(1), H(2)]),
            ("2026-09-10T15:13:16.7688103Z", [H(0), H(1), H(2)]),
            ("2026-09-10T15:13:17.2386632Z", [H(0), S(1), H(2)]),
            ("2026-09-10T15:13:17.7073706Z", [H(0), S(1), H(2)]),
            ("2026-09-10T15:13:18.17009Z", [H(0), H(1), H(2), Row(Stone, uint.MaxValue, 3)]),
            ("2026-09-10T15:13:18.6369094Z", [H(0), H(1), H(2), H(3)]),
            ("2026-09-10T15:13:19.1147462Z", [H(0), H(1), H(2), H(3)]),
            ("2026-09-10T15:13:19.5788108Z", [H(0), H(1)]),
        };
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        var events = new List<CompanionRecognizedEntry>();
        var traces = new List<NormalLootReconciliationTrace>();
        void Add(IReadOnlyList<CompanionRecognizedEntry> rows, DateTimeOffset at)
        {
            events.AddRange(counter.ProcessFrame(rows, at));
            traces.AddRange(counter.LastTrace);
        }
        for (var i = 0; i < emptyPrefix; i++) Add([], Start.AddSeconds(-emptyPrefix + i));
        foreach (var frame in frames) Add(frame.Rows, DateTimeOffset.Parse(frame.Time));
        events.AddRange(counter.Complete());
        traces.AddRange(counter.LastTrace);

        Assert.Equal(1u, Assert.Single(events, entry => entry.Name == Stone).Count);
        // Do not suppress the ambiguous Helmet renewals along with the Stone.
        Assert.Equal(36, events.Where(entry => entry.Name == Helmet).Sum(entry => (int)entry.Count));
        var before = traces.Single(trace => trace.CaptureIndex == emptyPrefix + 3);
        var after = traces.Single(trace => trace.CaptureIndex == emptyPrefix + 4);
        var oldStone = Assert.Single(before.Rows, row => row.ItemName == Stone);
        var keptStone = Assert.Single(after.Rows, row => row.ItemName == Stone);
        Assert.Equal(3, after.GeometricOverlap);
        Assert.Equal(1, after.ConfirmedOverlap);
        Assert.Equal(oldStone.TrackId, keptStone.TrackId);
        Assert.Equal(oldStone.TrackId, keptStone.MatchedPreviousTrackId);
        Assert.Equal(1, keptStone.MatchedPreviousSlot);
        Assert.Equal("verified-stable-neighbor", keptStone.AlignmentReason);
        Assert.Equal("matched-existing", keptStone.Outcome);
        Assert.Equal(0, keptStone.QuantityDelta);
        Assert.Contains(after.Rows, row => row.ItemName == Helmet && row.Outcome == "counted-new");
    }

    [Fact]
    public void GenuineLeadingHelmetsAndAnotherStoneAreStillNewDrops()
    {
        var result = Run(
            [H(0), S(1), H(2)],
            [H(0), H(1), S(2), H(3)],
            [S(0), H(1), H(2), S(3), H(4)]);
        Assert.Equal(12, result.Events.Where(entry => entry.Name == Helmet).Sum(entry => (int)entry.Count));
        Assert.Equal(2, result.Events.Where(entry => entry.Name == Stone).Sum(entry => (int)entry.Count));
        Assert.Equal(2, result.Events.Where(entry => entry.Name == Stone).Select(entry => entry.EventId).Distinct().Count());
        Assert.DoesNotContain(result.Traces.SelectMany(trace => trace.Rows),
            row => row.AlignmentReason == "verified-stable-neighbor");
    }

    [Fact]
    public void AllIdenticalRowsStillRenewAtTheOriginalCyclicBoundary()
    {
        var result = Run([H(0)], [H(0)], [H(0)], [H(0)]);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(8, result.Events.Sum(entry => (int)entry.Count));
        Assert.DoesNotContain(result.Traces.SelectMany(trace => trace.Rows),
            row => row.AlignmentReason == "verified-stable-neighbor");
    }

    [Fact]
    public void AStonesOwnTagRenewalIsNotOverridden()
    {
        var result = Run([H(0), H(1), H(2)], [H(0), H(1), H(2)],
            [H(0), S(1), H(2)], [H(0), S(1), H(2)],
            [H(0), S(1), H(2)], [H(0), S(1), H(2)]);
        Assert.Equal(2, result.Events.Count(entry => entry.Name == Stone));
        Assert.Equal("counted-new", Assert.Single(result.Traces[5].Rows, row => row.ItemName == Stone).Outcome);
    }

    [Theory]
    [InlineData(2u, 2)]
    [InlineData(uint.MaxValue, 1)]
    public void ClampedOrRecoveredQuantitiesDoNotProveAnUnchangedPanel(uint lastRead, int originalStoneDrops)
    {
        // Both values become four internally, but neither is a repeated exact read.
        var result = Run([H(0), H(1), H(2)], [H(0), H(1), H(2)],
            [H(0), S(1), H(2)],
            [Row(Helmet, lastRead, 0) with { QuantityBounds = new(4, 1000) }, S(1), H(2)]);
        // The pre-existing estimated-quantity path may already retain the Stone;
        // neither uncertainty case is evidence for the new neighbor rescue.
        Assert.Equal(originalStoneDrops, result.Events.Count(entry => entry.Name == Stone));
        Assert.DoesNotContain(result.Traces.SelectMany(trace => trace.Rows),
            row => row.AlignmentReason == "verified-stable-neighbor");
    }

    [Fact]
    public void TwoRowsOfTheSameItemAreNotAnUnambiguousNeighbor()
    {
        var result = Run([H(0), H(1), H(2)], [H(0), H(1), H(2)],
            [H(0), S(1), H(2), S(3), H(4)], [H(0), S(1), H(2), S(3), H(4)]);
        Assert.Equal(3, result.Events.Count(entry => entry.Name == Stone));
        Assert.DoesNotContain(result.Traces.SelectMany(trace => trace.Rows),
            row => row.AlignmentReason == "verified-stable-neighbor");
    }

    [Fact]
    public void AChangedNativePositionDoesNotProveAStableNeighbor()
    {
        var moved = new CompanionRecognizedEntry(Stone, 1, S(1).Y + 1)
            { Slot = 1, QuantityBounds = new(1, 1000) };
        var result = Run([H(0), H(1), H(2)], [H(0), H(1), H(2)],
            [H(0), S(1), H(2)], [H(0), moved, H(2)]);
        Assert.Equal(2, result.Events.Count(entry => entry.Name == Stone));
    }

    [Theory]
    [InlineData(false, true, 0.47)] // Legacy mode.
    [InlineData(true, false, 0.47)] // Missing timestamps.
    [InlineData(true, true, 1.001)] // Capture gap.
    [InlineData(true, true, 0)] // Repeated timestamp.
    [InlineData(true, true, -0.47)] // Out-of-order timestamp.
    public void InsufficientCaptureContinuityDoesNotRetainTheNeighbor(bool tracked, bool timestamped, double step)
    {
        var counter = new CompanionFrameReconciler(null, tracked);
        CompanionRecognizedEntry[][] frames =
            [[H(0), H(1), H(2)], [H(0), H(1), H(2)], [H(0), S(1), H(2)], [H(0), S(1), H(2)]];
        for (var i = 0; i < frames.Length; i++)
            counter.ProcessFrame(frames[i], timestamped ? Start.AddSeconds(i * step) : null);
        var events = counter.Complete();
        Assert.Equal(2, events.Count(entry => entry.Name == Stone));
        Assert.DoesNotContain(counter.LastTrace.SelectMany(trace => trace.Rows),
            row => row.AlignmentReason == "verified-stable-neighbor");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewSessionOrEmptyPanelDoesNotCarryTheOldNeighbor(bool reset)
    {
        var counter = new CompanionFrameReconciler(null, true);
        counter.ProcessFrame([H(0), S(1), H(2)], Start);
        var firstStone = Assert.Single(counter.Complete(), entry => entry.Name == Stone);
        if (reset) counter.Reset();
        else counter.ProcessFrame([], Start.AddSeconds(.47));
        counter.ProcessFrame([H(0), S(1), H(2)], Start.AddSeconds(.94));
        var nextStone = Assert.Single(counter.Complete(), entry => entry.Name == Stone);
        Assert.NotEqual(firstStone.EventId, nextStone.EventId);
    }

    private static (List<CompanionRecognizedEntry> Events, List<NormalLootReconciliationTrace> Traces)
        Run(params CompanionRecognizedEntry[][] frames)
    {
        var counter = new CompanionFrameReconciler(null, true);
        var events = new List<CompanionRecognizedEntry>();
        var traces = new List<NormalLootReconciliationTrace>();
        for (var i = 0; i < frames.Length; i++)
        {
            events.AddRange(counter.ProcessFrame(frames[i], Start.AddSeconds(i * .47)));
            traces.AddRange(counter.LastTrace);
        }
        events.AddRange(counter.Complete());
        traces.AddRange(counter.LastTrace);
        return (events, traces);
    }
}
