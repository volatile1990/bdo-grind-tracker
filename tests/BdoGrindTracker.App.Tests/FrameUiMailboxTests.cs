using System.Diagnostics;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class FrameUiMailboxTests(ITestOutputHelper output)
{
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
