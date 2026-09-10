using System.Collections.Concurrent;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class CaptureHudEligibilityTests
{
    private static readonly Rectangle CaptureRegion = new(0, 0, 2, 2);

    [Fact]
    public async Task QueuedBackgroundFramesStayIneligibleWhenTheGameReturnsBeforeConsumption()
    {
        var visible = 0;
        var captures = 0;
        var queueFilled = Completion();
        var releaseConsumer = Completion();
        var observed = new ConcurrentQueue<CapturedFrameMetadata>();
        var foregroundWhileConsumed = new ConcurrentQueue<bool>();
        await using var session = new PassiveCaptureSession(_ =>
        {
            if (Interlocked.Increment(ref captures) == 3) queueFilled.TrySetResult();
            return new Bitmap(2, 2);
        }, frameInterval: TimeSpan.FromMilliseconds(5), maximumQueuedFrames: 2);
        session.StartCompanion(CaptureRegion, async (_, metadata, _) =>
        {
            await releaseConsumer.Task.WaitAsync(TimeSpan.FromSeconds(5));
            foregroundWhileConsumed.Enqueue(Volatile.Read(ref visible) == 1);
            observed.Enqueue(metadata);
        }, () => Volatile.Read(ref visible) == 1);

        try
        {
            await queueFilled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = session.StopAsync();
            Volatile.Write(ref visible, 1);
            releaseConsumer.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { releaseConsumer.TrySetResult(); }

        Assert.Equal(3, captures);
        Assert.Equal([1L, 2L, 3L], observed.Select(frame => frame.Sequence));
        Assert.All(foregroundWhileConsumed, visible => Assert.True(visible));
        Assert.All(observed, frame => Assert.False(frame.CanObserveHud));
    }

    [Fact]
    public async Task ForegroundLostDuringAcquisitionMakesThatFrameIneligible()
    {
        var visible = 1;
        var checkCount = 0;
        var processed = new TaskCompletionSource<CapturedFrameMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        CaptureSessionStoppedEventArgs? stopped = null;
        await using var session = new PassiveCaptureSession(_ =>
        {
            Volatile.Write(ref visible, 0);
            return new Bitmap(2, 2);
        }, frameInterval: TimeSpan.FromHours(1));
        session.Stopped += (_, args) => stopped = args;
        session.StartCompanion(CaptureRegion, (_, metadata, _) =>
        {
            processed.TrySetResult(metadata);
            return Task.CompletedTask;
        }, () =>
        {
            Interlocked.Increment(ref checkCount);
            return Volatile.Read(ref visible) == 1;
        });

        var metadata = await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(metadata.CanObserveHud);
        Assert.Equal(2, checkCount);
        Assert.NotNull(stopped);
        Assert.Null(stopped.Error);
    }

    [Fact]
    public async Task MissingVisibilityCallbackDefaultsToIneligibleWithoutAffectingCapture()
    {
        var processed = new TaskCompletionSource<CapturedFrameMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        CaptureSessionStoppedEventArgs? stopped = null;
        await using var session = new PassiveCaptureSession(_ => new Bitmap(2, 2),
            frameInterval: TimeSpan.FromHours(1));
        session.Stopped += (_, args) => stopped = args;
        session.StartCompanion(CaptureRegion, (frame, metadata, _) =>
        {
            Assert.Equal(2, frame.Width);
            processed.TrySetResult(metadata);
            return Task.CompletedTask;
        });

        var metadata = await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, metadata.Sequence);
        Assert.False(metadata.CanObserveHud);
        Assert.NotNull(stopped);
        Assert.Null(stopped.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibilityExceptionsBeforeOrAfterAcquisitionNeverStopFrameDelivery(bool failAfterCapture)
    {
        var checks = 0;
        var captures = 0;
        var receivedThree = Completion();
        var observed = new ConcurrentQueue<CapturedFrameMetadata>();
        CaptureSessionStoppedEventArgs? stopped = null;
        await using var session = new PassiveCaptureSession(_ =>
        {
            Interlocked.Increment(ref captures);
            return new Bitmap(2, 2);
        }, frameInterval: TimeSpan.FromMilliseconds(5));
        session.Stopped += (_, args) => stopped = args;
        session.StartCompanion(CaptureRegion, (_, metadata, _) =>
        {
            observed.Enqueue(metadata);
            if (observed.Count >= 3) receivedThree.TrySetResult();
            return Task.CompletedTask;
        }, () =>
        {
            var check = Interlocked.Increment(ref checks);
            if (failAfterCapture && check % 2 == 1) return true;
            throw new InvalidOperationException("Synthetic foreground inspection failure");
        });

        await receivedThree.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(observed.Count >= 3);
        Assert.Equal(captures, observed.Count);
        Assert.All(observed, frame => Assert.False(frame.CanObserveHud));
        Assert.NotNull(stopped);
        Assert.Null(stopped.Error);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task AForegroundFrameIsEligibleWhenBothAcquisitionChecksSucceed()
    {
        var processed = new TaskCompletionSource<CapturedFrameMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new PassiveCaptureSession(_ => new Bitmap(2, 2),
            frameInterval: TimeSpan.FromHours(1));
        session.StartCompanion(CaptureRegion, (_, metadata, _) =>
        {
            processed.TrySetResult(metadata);
            return Task.CompletedTask;
        }, () => true);

        var metadata = await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(metadata.CanObserveHud);
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
