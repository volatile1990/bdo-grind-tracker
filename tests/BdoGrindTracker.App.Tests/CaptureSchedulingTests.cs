using System.Collections.Concurrent;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

[Collection("Timing-sensitive integration")]
public sealed class CaptureSchedulingTests
{
    [Fact]
    public async Task PriorityIsPreservedBeforeCaptureAndUntilEveryQueuedFrameIsProcessed()
    {
        var acquired = 0;
        var released = 0;
        var captured = 0;
        var observed = new ConcurrentQueue<long>();
        var queueFilled = Completion();
        var releaseAnalysis = Completion();
        await using var session = new PassiveCaptureSession(_ =>
        {
            Assert.Equal(1, Volatile.Read(ref acquired));
            Assert.Equal(0, Volatile.Read(ref released));
            if (Interlocked.Increment(ref captured) == 3) queueFilled.TrySetResult();
            return new Bitmap(2, 2);
        }, frameInterval: TimeSpan.FromMilliseconds(10), maximumQueuedFrames: 2)
        {
            PreserveCapturePriority = () =>
            {
                Interlocked.Increment(ref acquired);
                return new Lease(() => Interlocked.Increment(ref released));
            }
        };
        session.StartCompanion(new(0, 0, 2, 2), async (_, metadata, _) =>
        {
            Assert.Equal(0, Volatile.Read(ref released));
            observed.Enqueue(metadata.Sequence);
            await releaseAnalysis.Task;
        });
        Assert.Equal(1, Volatile.Read(ref acquired));
        try
        {
            await queueFilled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var stopped = session.StopAsync();
            Assert.False(stopped.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref released));
            releaseAnalysis.TrySetResult();
            await stopped.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { releaseAnalysis.TrySetResult(); }
        Assert.Equal(1, Volatile.Read(ref released));
        Assert.Equal([1L, 2L, 3L], observed);
    }

    [Fact]
    public async Task WatchdogRetainsPriorityUntilAbandonedNativeAnalysisActuallyFinishes()
    {
        var releaseAnalysis = Completion();
        var priorityReleased = Completion();
        var stopped = Completion();
        var entered = Completion();
        var releaseCount = 0;
        await using var session = new PassiveCaptureSession(_ => new Bitmap(2, 2),
            frameInterval: TimeSpan.FromMilliseconds(10), analysisTimeout: TimeSpan.FromMilliseconds(100))
        {
            PreserveCapturePriority = () => new Lease(() =>
            {
                Interlocked.Increment(ref releaseCount);
                priorityReleased.TrySetResult();
            })
        };
        session.Stopped += (_, args) =>
        {
            Assert.IsType<TimeoutException>(args.Error);
            stopped.TrySetResult();
        };
        session.StartCompanion(new(0, 0, 2, 2), async (_, _, _) =>
        {
            entered.TrySetResult();
            await releaseAnalysis.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(session.HasPendingAnalysis);
            Assert.False(priorityReleased.Task.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref releaseCount));
        }
        finally { releaseAnalysis.TrySetResult(); }
        await session.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
        await priorityReleased.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, Volatile.Read(ref releaseCount));
    }

    [Fact]
    public async Task CaptureFailureReleasesItsPriorityLease()
    {
        var released = 0;
        var failed = Completion();
        await using var session = new PassiveCaptureSession(
            (Func<Rectangle, Bitmap>)(_ => throw new IOException("Capture failed")))
        {
            PreserveCapturePriority = () => new Lease(() => Interlocked.Increment(ref released))
        };
        session.Stopped += (_, args) =>
        {
            Assert.IsType<IOException>(args.Error);
            failed.TrySetResult();
        };
        session.StartCompanion(new(0, 0, 2, 2), (_, _, _) => Task.CompletedTask);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, Volatile.Read(ref released));
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
