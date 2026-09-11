using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class SessionRestorePrimitivesTests
{
    [Fact]
    public void RestoredClockStaysPausedAndIdleExclusionCannotRemoveSavedTime()
    {
        var time = new RestoreTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(20));
        clock.RestorePaused(TimeSpan.FromMinutes(5));
        time.Advance(TimeSpan.FromHours(2));
        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(5), clock.Elapsed);

        clock.Start();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(310), clock.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(5), clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromMinutes(4)));
        clock.Pause(TimeSpan.FromMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(5), clock.Elapsed);

        clock.Reset();
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public void InvalidClockRestorePreservesTheRunningClock()
    {
        var time = new RestoreTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.RestorePaused(TimeSpan.FromTicks(-1)));
        Assert.True(clock.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(20), clock.Elapsed);
    }

    [Fact]
    public void NewProjectionsAddToRestoredTotalsAndRetractOnlyTheirOwnDrops()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(50, 9, ("Old run", 90)));
        var stored = new Dictionary<string, long> { ["Helmet"] = 100, ["Dust"] = 0 };
        aggregate.Restore(new(stored, 100, 25), ["Dust"]);
        stored["Helmet"] = 999;
        Assert.Null(aggregate.LatestArrivalAt);

        Assert.Equal((false, false), aggregate.ApplyProjection(Projection(0, 0)));
        Assert.True(aggregate.ApplyProjection(Projection(1, 2, ("Helmet", 8), ("Dust", 1))).HasNewArrival);
        Assert.Equal(109, aggregate.TotalQuantity);
        Assert.Equal(27, aggregate.ConfirmedEventCount);
        Assert.Equal(108, aggregate.Totals["Helmet"]);
        Assert.False(aggregate.ApplyProjection(Projection(2, 1, ("Dust", 1))).HasNewArrival);
        Assert.Equal(101, aggregate.TotalQuantity);
        Assert.Equal(26, aggregate.ConfirmedEventCount);
        Assert.False(aggregate.ApplyProjection(Projection(3, 0)).HasNewArrival);
        Assert.Equal(100, aggregate.TotalQuantity);
        Assert.Equal(25, aggregate.ConfirmedEventCount);
        Assert.Equal(0, aggregate.Totals["Dust"]);
        Assert.DoesNotContain("Old run", aggregate.Totals.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualCorrectionsAcrossRestoreRemainIndependentOfNewProjections(bool editBeforeFirstProjection)
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Restore(Seed(), ["Dust"]);
        if (editBeforeFirstProjection) aggregate.AdjustQuantity("Helmet", 90, 100);
        aggregate.ApplyProjection(Projection(0, 1, ("Helmet", 4)));
        if (!editBeforeFirstProjection) aggregate.AdjustQuantity("Helmet", 94, 104);
        Assert.Equal(94, aggregate.Totals["Helmet"]);

        aggregate.ApplyProjection(Projection(1, 0));
        Assert.Equal(90, aggregate.Totals["Helmet"]);
        aggregate.AdjustQuantity("Helmet", 0, 90);
        aggregate.ApplyProjection(Projection(2, 1, ("Helmet", 4)));
        Assert.Equal(4, aggregate.Totals["Helmet"]);
        Assert.Equal(0, aggregate.Totals["Dust"]);
    }

    [Fact]
    public void LegacyEventsAndQuantityRevisionsContinueFromRestoredTotals()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Restore(Seed(), ["Dust"]);
        var drop = new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Helmet", 4)
        {
            TotalDropQuantity = 4,
        };
        aggregate.Apply(drop);
        aggregate.Apply(drop);
        aggregate.Apply(drop with { Quantity = -2, Revision = 1, TotalDropQuantity = 2 });
        Assert.Equal(102, aggregate.TotalQuantity);
        Assert.Equal(26, aggregate.ConfirmedEventCount);
        aggregate.AdjustQuantity("Dust", 3, 0);
        aggregate.Apply(new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Dust", 1));
        Assert.Equal(4, aggregate.Totals["Dust"]);
        Assert.Equal(27, aggregate.ConfirmedEventCount);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("mismatch")]
    [InlineData("duplicates")]
    [InlineData("manual-missing")]
    [InlineData("event-count")]
    [InlineData("overflow")]
    public void MalformedLootRestoreDoesNotReplaceCurrentTotalsOrConsumeProjectionRevision(string problem)
    {
        var aggregate = new LootSessionAggregate();
        aggregate.ApplyProjection(Projection(8, 1, ("Helmet", 4)));
        LootSessionSnapshot snapshot = problem switch
        {
            "negative" => new LootSessionSnapshot(new Dictionary<string, long> { ["Helmet"] = -1 }, -1, 1),
            "mismatch" => Seed() with { TotalQuantity = 101 },
            "duplicates" => new(new Dictionary<string, long> { ["Helmet"] = 1, ["helmet"] = 2 }, 3, 1),
            "event-count" => Seed() with { ConfirmedEventCount = -1 },
            "overflow" => new(new Dictionary<string, long> { ["Helmet"] = long.MaxValue, ["Dust"] = 1 }, 0, 1),
            _ => Seed(),
        };
        var exception = Record.Exception(() => aggregate.Restore(snapshot,
            problem == "manual-missing" ? ["Unknown"] : []));
        Assert.True(exception is ArgumentException or OverflowException);
        Assert.Equal(4, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
        Assert.False(aggregate.ApplyProjection(Projection(8, 0)).TotalsChanged);
        aggregate.ApplyProjection(Projection(9, 2, ("Helmet", 8)));
        Assert.Equal(8, aggregate.TotalQuantity);
    }

    [Fact]
    public void RestoredBaselineOverflowRejectsNewProjectionAtomically()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Restore(new(new Dictionary<string, long> { ["Helmet"] = long.MaxValue }, long.MaxValue, 1), []);
        Assert.Throws<OverflowException>(() => aggregate.ApplyProjection(Projection(0, 1, ("Helmet", 1))));
        Assert.Equal(long.MaxValue, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
        Assert.False(aggregate.ApplyProjection(Projection(0, 0)).TotalsChanged);
    }

    [Fact]
    public void MailboxRestoresWithoutActivityAndPublishesSeedPlusNewCapture()
    {
        using var mailbox = new FrameUiMailbox();
        mailbox.Publish(Frame(Projection(99, 1, ("Old run", 5))));
        mailbox.Restore(Seed(), ["Dust"]);
        Assert.Null(mailbox.TakeLatest());
        var callbacks = new List<(long Helmet, bool Activity)>();
        Assert.False(mailbox.Publish(Frame(Projection(0, 0)),
            onPublished: (totals, activity) => callbacks.Add((totals["Helmet"], activity))));
        using (var baseline = mailbox.TakeLatest())
        {
            Assert.Equal(100, baseline!.Totals!.TotalQuantity);
            Assert.Equal(25, baseline.Totals.ConfirmedEventCount);
        }
        Assert.True(mailbox.Publish(Frame(Projection(1, 1, ("Helmet", 4))),
            onPublished: (totals, activity) => callbacks.Add((totals["Helmet"], activity))));
        using var resumed = mailbox.TakeLatest();
        Assert.Equal(new[] { (100L, false), (104L, true) }, callbacks);
        Assert.Equal(104, resumed!.Totals!.TotalQuantity);
        Assert.Equal(26, resumed.Totals.ConfirmedEventCount);
    }

    [Fact]
    public void MalformedMailboxRestoreKeepsThePendingUiSnapshot()
    {
        using var mailbox = new FrameUiMailbox();
        var frame = Frame(Projection(1, 1, ("Helmet", 4)));
        mailbox.Publish(frame);
        Assert.Throws<ArgumentException>(() => mailbox.Restore(Seed() with { TotalQuantity = 1 }, []));
        using var update = mailbox.TakeLatest();
        Assert.Same(frame, update!.Analysis);
        Assert.Equal(4, update.Totals!.TotalQuantity);
    }

    [Fact]
    public void ReadingMailboxSnapshotCopiesTotalsWithoutConsumingPendingUiData()
    {
        using var mailbox = new FrameUiMailbox();
        mailbox.Restore(Seed(), ["Dust"]);
        mailbox.Publish(Frame(Projection(0, 1, ("Helmet", 4))));
        var checkpoint = mailbox.ReadSnapshot(snapshot => snapshot);
        mailbox.Publish(Frame(Projection(1, 2, ("Helmet", 8))));
        using var update = mailbox.TakeLatest();

        Assert.Equal(104, checkpoint.TotalQuantity);
        Assert.Equal(104, checkpoint.Totals["Helmet"]);
        Assert.Equal(26, checkpoint.ConfirmedEventCount);
        Assert.Equal(108, update!.Totals!.TotalQuantity);
        Assert.Equal(27, update.Totals.ConfirmedEventCount);
    }

    [Fact]
    public void ResetAndRepeatedRestoreDoNotAccumulateDuplicateBaselines()
    {
        var aggregate = new LootSessionAggregate();
        aggregate.Restore(Seed(), ["Dust"]);
        aggregate.ApplyProjection(Projection(0, 1, ("Helmet", 4)));
        aggregate.Restore(Seed(), ["Dust"]);
        aggregate.ApplyProjection(Projection(0, 1, ("Helmet", 4)));
        Assert.Equal(104, aggregate.TotalQuantity);
        aggregate.Reset();
        aggregate.ApplyProjection(Projection(0, 1, ("Helmet", 4)));
        Assert.Equal(4, aggregate.TotalQuantity);
        Assert.Equal(1, aggregate.ConfirmedEventCount);
    }

    private static LootSessionSnapshot Seed() =>
        new(new Dictionary<string, long> { ["Helmet"] = 100, ["Dust"] = 0 }, 100, 25);

    private static LootTotalsProjection Projection(long revision, int drops, params (string Name, long Quantity)[] totals) =>
        new(revision, totals.ToDictionary(pair => pair.Name, pair => pair.Quantity), drops,
            drops == 0 ? null : DateTimeOffset.UnixEpoch);

    private static FrameAnalysisResult Frame(LootTotalsProjection projection) =>
        new([], [], 1, "restore-test", 0, 0, 0, 0, null) { LootProjection = projection };

    private sealed class RestoreTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
