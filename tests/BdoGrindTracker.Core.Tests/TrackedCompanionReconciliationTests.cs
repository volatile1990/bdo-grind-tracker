using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class TrackedCompanionReconciliationTests
{
    private static CompanionRecognizedEntry Row(string name, uint quantity, int slot = 0, double scale = 1) =>
        new(name, quantity, (int)Math.Round((250 - slot * 50) * scale))
        { Slot = slot, QuantityBounds = new(4, 1000) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlignmentAnchorCannotEmitEvenAcrossFrameTagRenewals(bool tracked)
    {
        var counter = new CompanionFrameReconciler(null, tracked);
        var events = new List<CompanionRecognizedEntry>();
        for (var i = 0; i < 14; i++)
            events.AddRange(counter.ProcessFrame([Row("Helmet", 6) with { IsAlignmentAnchor = true }]));
        events.AddRange(counter.Complete());
        Assert.Empty(events);
        if (tracked) Assert.All(counter.LastTrace.SelectMany(t => t.Rows), row =>
        {
            Assert.Equal("alignment-anchor", row.Outcome);
            Assert.Equal(0, row.QuantityDelta);
        });
    }

    [Fact]
    public void AlignmentAnchorCannotReviseAnAlreadyBookedEstimate()
    {
        var counter = new CompanionFrameReconciler(null, true);
        counter.ProcessFrame([Row("Helmet", uint.MaxValue)]);
        Assert.Equal(4u, Assert.Single(counter.Complete()).Count);
        counter.ProcessFrame([Row("Helmet", 6) with { IsAlignmentAnchor = true }]);
        Assert.Empty(counter.Complete());
        Assert.Equal("alignment-anchor", Assert.Single(Assert.Single(counter.LastTrace).Rows).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryReadAfterAnAnchorAtTagWrapRetainsTheAlreadyCountedId(bool flushBeforeAnchor)
    {
        var counter = new CompanionFrameReconciler(null, true);
        var events = new List<CompanionRecognizedEntry>();
        for (var i = 0; i < 3; i++) events.AddRange(counter.ProcessFrame([Row("Helmet", 6)]));
        if (flushBeforeAnchor) events.AddRange(counter.Complete());
        events.AddRange(counter.ProcessFrame([Row("Helmet", 6) with { IsAlignmentAnchor = true, AlignmentPreviousSlot = 0 }]));
        events.AddRange(counter.ProcessFrame([Row("Helmet", 6)]));
        events.AddRange(counter.Complete());
        Assert.Equal(6u, Assert.Single(events).Count);
        var anchor = Assert.Single(counter.LastTrace.SelectMany(t => t.Rows), r => r.Outcome == "alignment-anchor");
        Assert.Equal(events[0].EventId, anchor.TrackId);
        Assert.Equal(events[0].EventId, anchor.MatchedPreviousTrackId);
        Assert.Equal("verified-older-row", anchor.AlignmentReason);
    }

    [Theory]
    [InlineData(.75, 0)]
    [InlineData(1, 7)]
    [InlineData(1.49, 8)]
    [InlineData(2, 9)]
    public void OlderAnchorRestoresNewSixWithoutCountingOldRows(double scale, int emptyPrefix)
    {
        var counter = new CompanionFrameReconciler(null, true);
        var events = new List<CompanionRecognizedEntry>();
        void Frame(params CompanionRecognizedEntry[] rows) => events.AddRange(counter.ProcessFrame(rows));
        for (var i = 0; i < emptyPrefix; i++) Frame();
        CompanionRecognizedEntry Helmet(uint count, int slot) => Row("Helmet", count, slot, scale);
        CompanionRecognizedEntry Shard(int slot) => Row("Shard", 1, slot, scale) with { QuantityBounds = new(1, 1) };
        Frame(Helmet(6, 0), Shard(1));
        Frame(Helmet(6, 0), Helmet(6, 1), Shard(2));
        Frame(Helmet(4, 0), Helmet(6, 1), Helmet(6, 2), Helmet(6, 3) with { IsAlignmentAnchor = true, AlignmentPreviousSlot = 1 });
        Frame(Helmet(4, 0), Helmet(6, 1));
        Frame();
        events.AddRange(counter.Complete());
        Assert.Equal(22, events.Where(e => e.Name == "Helmet").Sum(e => (int)e.Count));
        Assert.Equal(5, events.Select(e => e.EventId).Distinct().Count());
        Assert.Equal(1u, Assert.Single(events, e => e.Name == "Shard").Count);
        Assert.Empty(counter.Complete());
    }

    [Fact]
    public void TraceExplainsCyclicRenewalAndLinksTheOriginalCapture()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        var start = DateTimeOffset.Parse("2026-09-09T12:00:00Z");
        for (var i = 0; i < 4; i++) counter.ProcessFrame([Row("Helmet", 6)], start.AddSeconds(i));
        var events = counter.Complete();
        Assert.Equal(2, events.Count);
        Assert.Equal(4, counter.LastTrace.Count);
        var trace = counter.LastTrace[3];
        Assert.Equal(4, trace.CaptureIndex);
        Assert.Equal(3, trace.PreviousCaptureIndex);
        Assert.Equal(start.AddSeconds(3), trace.CapturedAt);
        Assert.Equal(1, trace.GeometricOverlap);
        Assert.Equal(0, trace.ConfirmedOverlap);
        Assert.Contains(trace.OverlapAttempts, attempt => !attempt.Accepted && attempt.Reason == "frame-tag-progression");
        var row = Assert.Single(trace.Rows);
        Assert.Equal("overlap-rejected-by-frame-tags", row.AlignmentReason);
        Assert.Equal("counted-new", row.Outcome);
        Assert.Equal(events[0].EventId, row.CandidatePreviousTrackId);
        Assert.Null(row.MatchedPreviousTrackId);
        Assert.Equal(events[1].EventId, row.TrackId);
        Assert.Equal(6, row.QuantityDelta);
        Assert.Equal(0, row.Slot);
        Assert.Equal(250, row.NativeY);
        Assert.Empty(counter.Complete());
        Assert.Empty(counter.LastTrace);
        Assert.Equal("counted-new", row.Outcome);
    }

    [Fact]
    public void TraceKeepsOriginalIndicesAcrossBatchTrimmingAndResetsWithCounter()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        for (var i = 0; i < 10; i++) counter.ProcessFrame([Row("Helmet", 6)]);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), counter.LastTrace.Select(trace => trace.CaptureIndex));
        counter.ProcessFrame([Row("Helmet", 6)]);
        Assert.Empty(counter.LastTrace);
        counter.Complete();
        var trace = Assert.Single(counter.LastTrace);
        Assert.Equal(11, trace.CaptureIndex);
        Assert.Equal(10, trace.PreviousCaptureIndex);
        counter.Reset();
        Assert.Equal(0, counter.CaptureIndex);
        Assert.Empty(counter.LastTrace);
        counter.ProcessFrame([Row("Helmet", 6)]);
        counter.Complete();
        trace = Assert.Single(counter.LastTrace);
        Assert.Equal(1, trace.CaptureIndex);
        Assert.Null(trace.PreviousCaptureIndex);
    }

    [Fact]
    public void LateReadRevisesAnAlreadyBookedMinimumUsingTheSameDropId()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("Helmet", uint.MaxValue)]);
        var first = Assert.Single(counter.Complete());
        Assert.Equal(4u, first.Count);
        Assert.True(first.IsMinimumQuantityEstimate);
        Assert.NotNull(first.EventId);
        var initialTrace = Assert.Single(Assert.Single(counter.LastTrace).Rows);
        Assert.Null(initialTrace.InputQuantity);
        Assert.True(initialTrace.EstimatedQuantity);
        Assert.Equal(4, initialTrace.QuantityDelta);
        counter.ProcessFrame([Row("Helmet", 6)]);
        var correction = Assert.Single(counter.Complete());
        Assert.Equal(first.EventId, correction.EventId);
        Assert.Equal(1, correction.Revision);
        Assert.Equal(2, correction.QuantityDelta);
        Assert.Equal(6, correction.TotalDropQuantity);
        var revisionTrace = Assert.Single(Assert.Single(counter.LastTrace).Rows);
        Assert.Equal("quantity-revised", revisionTrace.Outcome);
        Assert.Equal(first.EventId, revisionTrace.MatchedPreviousTrackId);
        Assert.Equal(first.EventId, revisionTrace.TrackId);
        Assert.Equal(1, revisionTrace.Revision);
        Assert.Equal(2, revisionTrace.QuantityDelta);
        Assert.Equal(0, initialTrace.Revision);
        counter.ProcessFrame([Row("Helmet", 6)]);
        Assert.Empty(counter.Complete());
        Assert.Empty(counter.Complete());
    }

    [Theory]
    [InlineData(.75)]
    [InlineData(1)]
    [InlineData(1.49)]
    [InlineData(2)]
    public void InteriorUnreadRowDoesNotCompressOrRecountItsNeighbors(double scale)
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 4, 0, scale), Row("C", 6, 2, scale)]);
        var initial = counter.Complete();
        var gap = Assert.Single(Assert.Single(counter.LastTrace).Rows, row => row.Placeholder);
        Assert.Null(gap.NativeY);
        Assert.Equal(1, gap.Slot);
        Assert.Equal("unresolved-placeholder", gap.Outcome);
        counter.ProcessFrame([Row("A", 4, 0, scale), Row("B", 8, 1, scale), Row("C", 6, 2, scale)]);
        var added = Assert.Single(counter.Complete());
        Assert.Equal("B", added.Name);
        Assert.Equal(8u, added.Count);
        Assert.Equal(3, initial.Append(added).Select(row => row.EventId).Distinct().Count());
    }

    [Fact]
    public void QuantityRecoveryAtLegacyTagWrapStillRevisesTheExistingDrop()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        for (var i = 0; i < 3; i++) counter.ProcessFrame([Row("A", uint.MaxValue)]);
        var initial = Assert.Single(counter.Complete());
        counter.ProcessFrame([Row("A", 6)]);
        var correction = Assert.Single(counter.Complete());
        Assert.Equal(initial.EventId, correction.EventId);
        Assert.Equal(2, correction.QuantityDelta);
    }

    [Fact]
    public void ReappearingDifferentItemCannotReuseAnIdCarriedThroughAnUnreadSlot()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 4, 0), Row("B", 4, 1), Row("C", 4, 2)]);
        var initial = counter.Complete();
        counter.ProcessFrame([Row("A", 4, 0), Row("C", 4, 2)]);
        Assert.Empty(counter.Complete());
        counter.ProcessFrame([Row("A", 4, 0), Row("D", 4, 1), Row("C", 4, 2)]);
        var added = Assert.Single(counter.Complete());
        Assert.Equal("D", added.Name);
        Assert.DoesNotContain(initial, row => row.EventId == added.EventId);
    }

    [Fact]
    public void NewRowsAndLaterQuantityReadingAreReconciledInOneOrderedMovement()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", uint.MaxValue, 0), Row("B", 4, 1)]);
        var initial = counter.Complete();
        counter.ProcessFrame([Row("C", 4, 0), Row("A", 6, 1), Row("B", 4, 2)]);
        var result = counter.Complete();
        Assert.Equal(2, result.Count);
        Assert.Equal("C", result[0].Name);
        Assert.Equal(initial.Single(row => row.Name == "A").EventId, result[1].EventId);
        Assert.Equal(2, result[1].QuantityDelta);
    }

    [Fact]
    public void QuantityBoundsAndFixedUnitsStillApplyPerDrop()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 1, 0), Row("B", 5000, 1),
            Row("Rare", 999, 2) with { QuantityBounds = new(1, 1) }]);
        var result = counter.Complete();
        Assert.Equal([4u, 1000u, 1u], result.Select(row => row.Count));
    }

    [Fact]
    public void ResetAndVisiblyClearedPanelStartNewIds()
    {
        var counter = new CompanionFrameReconciler(null, trackRows: true);
        counter.ProcessFrame([Row("A", 4)]);
        var first = Assert.Single(counter.Complete());
        counter.ProcessFrame([]);
        counter.ProcessFrame([Row("A", 4)]);
        var second = Assert.Single(counter.Complete());
        counter.Reset();
        counter.ProcessFrame([Row("A", 4)]);
        var third = Assert.Single(counter.Complete());
        Assert.Equal(3, new[] { first.EventId, second.EventId, third.EventId }.Distinct().Count());
    }
}
