using System.Diagnostics;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class FrameUiMailboxTests(ITestOutputHelper output)
{
    [Fact]
    public void ProjectionIsAuthoritativeAndAuditDeltasAreNotAppliedAgain()
    {
        using var mailbox = new FrameUiMailbox();
        var frame = ProjectedFrame(1, 1, "Helmet", 4) with
        {
            NewEvents = [new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Helmet", 4),
                new(Guid.Empty, DateTimeOffset.UnixEpoch, "Audit retraction", 0)],
        };
        Assert.True(mailbox.Publish(frame));
        using var update = mailbox.TakeLatest();
        Assert.Equal(4, update!.Totals!.TotalQuantity);
        Assert.Equal(1, update.Totals.ConfirmedEventCount);
        Assert.Equal("Helmet", Assert.Single(update.Totals.Totals).Key);
    }

    [Fact]
    public void EqualSumProjectionReplacementRefreshesUiAndUploadWithoutActivity()
    {
        using var mailbox = new FrameUiMailbox();
        mailbox.Publish(ProjectedFrame(1, 1, "Helmet", 4));
        using var first = mailbox.TakeLatest();
        Dictionary<string, long>? published = null;
        bool? activity = null;
        Assert.False(mailbox.Publish(ProjectedFrame(2, 1, "Dust", 4), onPublished: (totals, hasArrival) =>
        {
            published = new(totals);
            activity = hasArrival;
        }));
        using var update = mailbox.TakeLatest();
        Assert.NotNull(update!.Totals);
        Assert.Equal(4, update.Totals.TotalQuantity);
        Assert.Equal("Dust", Assert.Single(update.Totals.Totals).Key);
        Assert.Equal(4, published!["Dust"]);
        Assert.False(activity);
        Assert.Equal("Helmet", Assert.Single(first!.Totals!.Totals).Key);
    }

    [Fact]
    public void ProjectionRetractionAndCountRecoveryDoNotRestartActivityClock()
    {
        using var mailbox = new FrameUiMailbox();
        var start = ProjectedFrame(1, 2, "Helmet", 8);
        Assert.True(mailbox.Publish(start));
        using var first = mailbox.TakeLatest();
        Assert.False(mailbox.Publish(ProjectedFrame(2, 1, "Helmet", 4)));
        using var retracted = mailbox.TakeLatest();
        Assert.Equal(1, retracted!.Totals!.ConfirmedEventCount);
        Assert.False(mailbox.Publish(ProjectedFrame(3, 2, "Helmet", 8)));
        using var restored = mailbox.TakeLatest();
        Assert.Equal(2, restored!.Totals!.ConfirmedEventCount);
        Assert.True(mailbox.Publish(start with
        {
            LootProjection = new(4, start.LootProjection!.Totals, 2,
                DateTimeOffset.UnixEpoch.AddSeconds(1)),
        }));
        using var arrivalOnly = mailbox.TakeLatest();
        Assert.Null(arrivalOnly!.Totals);
    }

    [Fact]
    public void DuplicateStaleAndUnchangedProjectionsDoNotRefreshTotals()
    {
        using var mailbox = new FrameUiMailbox();
        var frame = ProjectedFrame(2, 1, "Helmet", 4);
        mailbox.Publish(frame);
        using var first = mailbox.TakeLatest();
        Assert.False(mailbox.Publish(frame));
        using var duplicate = mailbox.TakeLatest();
        Assert.Null(duplicate!.Totals);
        Assert.False(mailbox.Publish(ProjectedFrame(1, 5, "Dust", 50)));
        using var stale = mailbox.TakeLatest();
        Assert.Null(stale!.Totals);
        Assert.False(mailbox.Publish(ProjectedFrame(3, 1, "Helmet", 4)));
        using var unchanged = mailbox.TakeLatest();
        Assert.Null(unchanged!.Totals);
    }

    [Fact]
    public void CoalescedProjectionKeepsFinalSnapshotAndManualCorrection()
    {
        using var mailbox = new FrameUiMailbox();
        mailbox.Publish(ProjectedFrame(1, 1, "Helmet", 4));
        mailbox.AdjustQuantity("Helmet", 10, 4, _ => { });
        mailbox.Publish(ProjectedFrame(2, 2, "Helmet", 8));
        var last = ProjectedFrame(3, 1, "Dust", 4);
        mailbox.Publish(last);
        using var update = mailbox.TakeLatest();
        Assert.Same(last, update!.Analysis);
        Assert.Equal(10, update.Totals!.TotalQuantity);
        Assert.Equal(6, update.Totals.Totals["Helmet"]);
        Assert.Equal(4, update.Totals.Totals["Dust"]);
        Assert.Equal(1, update.Totals.ConfirmedEventCount);
        Assert.Null(mailbox.TakeLatest());
    }

    [Fact]
    public void InvalidProjectionPreservesPendingUiSnapshotAndCanBeRetried()
    {
        using var mailbox = new FrameUiMailbox();
        var first = ProjectedFrame(1, 1, "Helmet", 4);
        mailbox.Publish(first);
        Assert.Throws<ArgumentException>(() => mailbox.Publish(Frame() with
        {
            LootProjection = new(2, new Dictionary<string, long> { ["Dust"] = 4, ["dust"] = 4 },
                2, DateTimeOffset.UnixEpoch),
        }));
        using var update = mailbox.TakeLatest();
        Assert.Same(first, update!.Analysis);
        Assert.Equal(4, update.Totals!.TotalQuantity);
        mailbox.Publish(ProjectedFrame(2, 2, "Dust", 8));
        using var corrected = mailbox.TakeLatest();
        Assert.Equal(8, corrected!.Totals!.TotalQuantity);
    }

    [Fact]
    public void ResetAllowsProjectionRevisionAndArrivalTimeToStartAgain()
    {
        using var mailbox = new FrameUiMailbox();
        var frame = ProjectedFrame(0, 1, "Helmet", 4);
        mailbox.Publish(frame);
        mailbox.AdjustQuantity("Helmet", 10, 4, _ => { });
        mailbox.Reset();
        Assert.True(mailbox.Publish(frame));
        using var update = mailbox.TakeLatest();
        Assert.Equal(4, update!.Totals!.TotalQuantity);
    }

    private static FrameAnalysisResult ProjectedFrame(long revision, int drops, string item, long quantity) =>
        Frame() with { LootProjection = new LootTotalsProjection(revision,
            new Dictionary<string, long> { [item] = quantity }, drops, DateTimeOffset.UnixEpoch) };

    [Fact]
    public void AStalledUiCannotDropEventsOrQueueFrameUpdates()
    {
        using var mailbox = new FrameUiMailbox();
        const int frames = 100_000;
        var started = Stopwatch.GetTimestamp();
        FrameAnalysisResult? lastFrame = null;
        for (var index = 0; index < frames; index++)
        {
            lastFrame = Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Trash", 25));
            mailbox.Publish(lastFrame);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        using var update = mailbox.TakeLatest();
        Assert.NotNull(update);
        Assert.Same(lastFrame, update.Analysis);
        Assert.Single(update.Analysis.NewEvents);
        Assert.NotNull(update.Totals);
        Assert.Equal(frames, update.Totals.ConfirmedEventCount);
        Assert.Equal(frames * 25L, update.Totals.TotalQuantity);
        Assert.Single(update.Totals.Totals);
        Assert.Null(mailbox.TakeLatest());
        output.WriteLine($"Published {frames:N0} frames without a UI consumer in {elapsed.TotalMilliseconds:N0} ms; one visual update retained.");
    }

    [Fact]
    public void DuplicateEventsDoNotCauseCountOrTotalsRefresh()
    {
        using var mailbox = new FrameUiMailbox();
        var frame = Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Trash", 25));
        mailbox.Publish(frame);
        using var first = mailbox.TakeLatest();
        Assert.NotNull(first?.Totals);

        mailbox.Publish(frame);
        using var repeated = mailbox.TakeLatest();
        Assert.NotNull(repeated);
        Assert.Null(repeated.Totals);
    }

    [Fact]
    public void ACorrectionOnlyFrameRefreshesTotalsWithoutIncrementingEventCount()
    {
        using var mailbox = new FrameUiMailbox();
        mailbox.Publish(Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "BON Origin Shard", 1)));
        using var first = mailbox.TakeLatest();
        Assert.Equal(1, first!.Totals!.TotalQuantity);

        var correction = Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "BON Origin Shard", -1));
        mailbox.Publish(correction);
        using var corrected = mailbox.TakeLatest();

        Assert.NotNull(corrected!.Totals);
        Assert.Equal(0, corrected.Totals.TotalQuantity);
        Assert.Equal(1, corrected.Totals.ConfirmedEventCount);
        Assert.Empty(corrected.Totals.Totals);

        mailbox.Publish(correction);
        using var repeated = mailbox.TakeLatest();
        Assert.Null(repeated!.Totals);
    }

    [Fact]
    public void TakenTotalsRemainImmutableWhileCaptureContinues()
    {
        using var mailbox = new FrameUiMailbox();
        mailbox.Publish(Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Trash", 25)));
        using var first = mailbox.TakeLatest();
        mailbox.Publish(Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Trash", 30)));
        using var second = mailbox.TakeLatest();

        Assert.Equal(25, first!.Totals!.Totals["Trash"]);
        Assert.Equal(55, second!.Totals!.Totals["Trash"]);
    }

    [Fact]
    public void ReplacedThumbnailIsDisposedAndLatestTransfersToUi()
    {
        using var mailbox = new FrameUiMailbox();
        using var first = new Bitmap(2, 2);
        using var second = new Bitmap(2, 2);
        var snapshot = default(LiveDetectionDebugSnapshot);
        mailbox.Publish(Frame(), snapshot, first);
        mailbox.Publish(Frame(), snapshot, second);
        Assert.Throws<ArgumentException>(() => first.GetPixel(0, 0));

        using var update = mailbox.TakeLatest();
        Assert.Same(second, update!.Thumbnail);
        mailbox.Reset();
        Assert.Equal(Color.FromArgb(0), second.GetPixel(0, 0));
    }

    [Fact]
    public void ResetClearsPendingTotalsAndDisposesRetainedPreview()
    {
        using var mailbox = new FrameUiMailbox();
        using var thumbnail = new Bitmap(2, 2);
        var frame = Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Trash", 25));
        mailbox.Publish(frame, default(LiveDetectionDebugSnapshot), thumbnail);
        mailbox.Reset();

        Assert.Null(mailbox.TakeLatest());
        Assert.Throws<ArgumentException>(() => thumbnail.GetPixel(0, 0));
        mailbox.Publish(frame);
        using var update = mailbox.TakeLatest();
        Assert.Equal(1, update!.Totals!.ConfirmedEventCount);
    }

    private static FrameAnalysisResult Frame(params LootEventView[] events) =>
        new(events, [], 1, "test", 0, 0, 0, 0, null);

    [Fact]
    public void OnlyNewPositiveOutputsReportActivityForAutomaticPause()
    {
        using var mailbox = new FrameUiMailbox();
        var drop = Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Trash", 25));
        Assert.False(mailbox.Publish(Frame()));
        Assert.True(mailbox.Publish(drop));
        Assert.False(mailbox.Publish(drop));
        Assert.False(mailbox.Publish(Frame(new LootEventView(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Trash", -1))));
        mailbox.Reset();
        Assert.True(mailbox.Publish(drop));
        mailbox.Dispose();
        Assert.False(mailbox.Publish(drop));
    }
}
