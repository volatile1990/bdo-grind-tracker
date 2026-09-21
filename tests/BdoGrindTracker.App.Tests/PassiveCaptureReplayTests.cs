using System.Collections.Concurrent;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

[Collection("Timing-sensitive integration")]
public sealed class PassiveCaptureReplayTests
{
    [Fact]
    public async Task ReplayTransfersOwnershipAndIsAnalyzedBeforeLiveFramesInOriginalOrder()
    {
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var replay = new AutoStartDetection();
        var first = Frame(Color.Red);
        var second = Frame(Color.Blue);
        var live = Frame(Color.Green);
        replay.Add(new CapturedDesktopBitmap(first, true, true), start);
        replay.Add(new CapturedDesktopBitmap(second, false), start.AddMilliseconds(200));
        var observed = new ConcurrentQueue<(Color Color, CapturedFrameMetadata Metadata)>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var capture = new PassiveCaptureSession(_ => live, frameInterval: TimeSpan.FromDays(1));

        capture.StartCompanion(new Rectangle(0, 0, 2, 2), (bitmap, metadata, _) =>
        {
            observed.Enqueue((bitmap.GetPixel(0, 0), metadata));
            if (observed.Count == 3) received.TrySetResult();
            return Task.CompletedTask;
        }, takeInitialFrames: replay.TakeFrames);

        Assert.Empty(replay.Frames);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await capture.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var frames = observed.ToArray();
        Assert.Equal(new[] { Color.Red.ToArgb(), Color.Blue.ToArgb(), Color.Green.ToArgb() },
            frames.Select(frame => frame.Color.ToArgb()));
        Assert.Equal(start, frames[0].Metadata.CapturedAtUtc);
        Assert.Equal(start.AddMilliseconds(200), frames[1].Metadata.CapturedAtUtc);
        Assert.True(frames[0].Metadata.IsHdr);
        Assert.True(frames[0].Metadata.IsToneMapped);
        Assert.True(frames[0].Metadata.CanObserveHud);
        Assert.All(new[] { first, second, live }, AssertDisposed);
    }

    [Fact]
    public async Task StuckReplayUsesWatchdogRetainsActiveFrameAndDisposesQueuedAndUnqueuedFrames()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var liveCaptures = 0;
        using var replay = new AutoStartDetection();
        var initial = new[] { Frame(Color.Red), Frame(Color.Blue), Frame(Color.Green) };
        foreach (var bitmap in initial) replay.Add(new CapturedDesktopBitmap(bitmap, false), DateTimeOffset.UtcNow);
        await using var capture = new PassiveCaptureSession(_ =>
        {
            Interlocked.Increment(ref liveCaptures);
            return new Bitmap(2, 2);
        }, frameInterval: TimeSpan.FromDays(1), maximumQueuedFrames: 1,
            analysisTimeout: TimeSpan.FromMilliseconds(250));
        capture.Stopped += (_, args) => stopped.TrySetResult(args.Error);
        capture.StartCompanion(new Rectangle(0, 0, 2, 2), async (bitmap, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            bitmap.GetPixel(0, 0);
        }, takeInitialFrames: replay.TakeFrames);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<TimeoutException>(await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await capture.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(capture.HasPendingAnalysis);
            Assert.True(capture.AnalysisFailed);
            Assert.Equal(0, liveCaptures);
            initial[0].GetPixel(0, 0);
            AssertDisposed(initial[1]);
            AssertDisposed(initial[2]);
            var transfers = 0;
            Assert.Throws<InvalidOperationException>(() => capture.StartCompanion(new Rectangle(0, 0, 2, 2),
                (_, _, _) => Task.CompletedTask, takeInitialFrames: () => { transfers++; return []; }));
            Assert.Equal(0, transfers);
        }
        finally
        {
            release.TrySetResult();
            await capture.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.All(initial, AssertDisposed);
    }

    [Fact]
    public void ReplayEvictsOldFramesAndTransferredFramesOutliveBufferDisposal()
    {
        using var replay = new AutoStartDetection();
        var frames = Enumerable.Range(0, 4).Select(_ => new Bitmap(2, 2)).ToArray();
        foreach (var bitmap in frames) replay.Add(new CapturedDesktopBitmap(bitmap, false), DateTimeOffset.UtcNow);

        Assert.Equal(3, replay.Frames.Count);
        AssertDisposed(frames[0]);
        var transferred = replay.TakeFrames();
        replay.Dispose();

        Assert.Empty(replay.Frames);
        Assert.Equal(frames.Skip(1), transferred.Select(frame => frame.Bitmap));
        foreach (var frame in transferred)
        {
            frame.Bitmap.GetPixel(0, 0);
            frame.Bitmap.Dispose();
        }
    }

    private static Bitmap Frame(Color color)
    {
        var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, color);
        return bitmap;
    }

    private static void AssertDisposed(Bitmap bitmap) =>
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
}
