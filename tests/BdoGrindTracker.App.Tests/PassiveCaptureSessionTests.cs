using System.Collections.Concurrent;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class PassiveCaptureSessionTests
{
    [Fact]
    public void CompanionCadenceIsExactlyFourHundredFiftyMilliseconds()
    {
        Assert.Equal(
            TimeSpan.FromTicks(4_500_000),
            PassiveCaptureSession.CompanionFrameInterval);
    }

    private static readonly Rectangle FourKRegion = new(0, 0, 3840, 2160);

    [Fact]
    public async Task BlockedAnalysisDoesNotCaptureAdditionalFramesAfterCadenceElapses()
    {
        var callbackEntered = NewCompletionSource();
        var releaseCallback = NewCompletionSource();
        var captureCount = 0;

        await using var session = new PassiveCaptureSession(
            _ =>
            {
                Interlocked.Increment(ref captureCount);
                return new Bitmap(2, 2);
            },
            frameInterval: TimeSpan.FromMilliseconds(20));

        session.StartCompanion(
            FourKRegion,
            async (_, _, cancellationToken) =>
            {
                callbackEntered.TrySetResult();
                await releaseCallback.Task.WaitAsync(cancellationToken);
            });

        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(125);

        Assert.Equal(1, Volatile.Read(ref captureCount));

        releaseCallback.TrySetResult();
        await session.StopAsync();
    }

    [Fact]
    public async Task CompletedAnalysisWaitsOnlyForRemainingFrameDeadline()
    {
        var timeProvider = new RecordingTimeProvider();
        var delayObserved = new TaskCompletionSource<TimeSpan>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        timeProvider.TimerCreated += dueTime => delayObserved.TrySetResult(dueTime);

        await using var session = new PassiveCaptureSession(
            _ => new Bitmap(2, 2),
            timeProvider,
            PassiveCaptureSession.CompanionFrameInterval);

        session.StartCompanion(
            FourKRegion,
            (_, _, _) =>
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(125));
                return Task.CompletedTask;
            });

        var dueTime = await delayObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.StopAsync();

        Assert.Equal(TimeSpan.FromMilliseconds(325), dueTime);
    }

    [Fact]
    public async Task AnalysisPastFrameDeadlineStartsNextCaptureWithoutDelay()
    {
        var timeProvider = new RecordingTimeProvider();
        var secondCallbackEntered = NewCompletionSource();
        var captureCount = 0;

        await using var session = new PassiveCaptureSession(
            _ =>
            {
                Interlocked.Increment(ref captureCount);
                return new Bitmap(2, 2);
            },
            timeProvider,
            PassiveCaptureSession.CompanionFrameInterval);

        session.StartCompanion(
            FourKRegion,
            async (_, metadata, cancellationToken) =>
            {
                if (metadata.Sequence == 1)
                {
                    timeProvider.Advance(TimeSpan.FromMilliseconds(500));
                    return;
                }

                secondCallbackEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        await secondCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, Volatile.Read(ref captureCount));
        Assert.Equal(0, timeProvider.TimerCreationCount);

        await session.StopAsync();
    }

    [Fact]
    public async Task ConsumerReceivesIncreasingSequenceAndMonotonicCaptureTime()
    {
        var frozenNow = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var metadata = new ConcurrentQueue<CapturedFrameMetadata>();
        var enoughFrames = NewCompletionSource();

        await using var session = new PassiveCaptureSession(
            _ => new Bitmap(2, 2),
            new FrozenTimeProvider(frozenNow),
            TimeSpan.FromMilliseconds(5));

        session.StartCompanion(
            new Rectangle(0, 0, 320, 180),
            (_, frameMetadata, _) =>
            {
                metadata.Enqueue(frameMetadata);
                if (metadata.Count >= 5)
                {
                    enoughFrames.TrySetResult();
                }

                return Task.CompletedTask;
            });

        await enoughFrames.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await session.StopAsync();

        var observed = metadata.Take(5).ToArray();
        Assert.Equal([1L, 2L, 3L, 4L, 5L], observed.Select(frame => frame.Sequence));
        Assert.All(
            observed.Zip(observed.Skip(1)),
            pair => Assert.True(pair.First.CapturedAtUtc < pair.Second.CapturedAtUtc));
        Assert.Equal(frozenNow, observed[0].CapturedAtUtc);
    }

    [Fact]
    public async Task CompanionHdrStateTravelsWithTheCapturedFrame()
    {
        var observed = new TaskCompletionSource<CapturedFrameMetadata>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new PassiveCaptureSession(
            _ => new CapturedDesktopBitmap(new Bitmap(2, 2), IsHdr: true),
            frameInterval: TimeSpan.FromMilliseconds(5));

        session.StartCompanion(
            new Rectangle(0, 0, 320, 180),
            (_, metadata, _) =>
            {
                observed.TrySetResult(metadata);
                return Task.CompletedTask;
            });

        var metadata = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.StopAsync();

        Assert.True(metadata.IsHdr);
    }

    [Fact]
    public async Task StopCancelsCurrentAnalysisDisposesFrameAndRaisesStoppedOnce()
    {
        var createdFrames = new ConcurrentQueue<Bitmap>();
        var callbackEntered = NewCompletionSource();
        var captureCount = 0;
        var stoppedCount = 0;
        CaptureSessionStoppedEventArgs? stopped = null;

        await using var session = new PassiveCaptureSession(_ =>
        {
            var bitmap = new Bitmap(2, 2);
            createdFrames.Enqueue(bitmap);
            Interlocked.Increment(ref captureCount);
            return bitmap;
        });
        session.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref stoppedCount);
            stopped = args;
        };

        session.StartCompanion(
            FourKRegion,
            async (_, _, cancellationToken) =>
            {
                callbackEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(150);
        Assert.Equal(1, Volatile.Read(ref captureCount));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(session.IsRunning);
        Assert.Equal(1, Volatile.Read(ref stoppedCount));
        Assert.NotNull(stopped);
        Assert.Null(stopped.Error);
        Assert.All(
            createdFrames,
            bitmap => Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0)));
    }

    [Fact]
    public async Task AnalysisFailureStopsSessionAndIsReportedExactlyOnce()
    {
        var stoppedSource = new TaskCompletionSource<CaptureSessionStoppedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedCount = 0;
        var captureCount = 0;

        await using var session = new PassiveCaptureSession(_ =>
        {
            Interlocked.Increment(ref captureCount);
            return new Bitmap(2, 2);
        });
        session.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref stoppedCount);
            stoppedSource.TrySetResult(args);
        };

        session.StartCompanion(
            new Rectangle(0, 0, 320, 180),
            (_, _, _) => Task.FromException(new InvalidOperationException("analysis failed")));

        var stopped = await stoppedSource.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var capturesAfterStop = Volatile.Read(ref captureCount);
        await Task.Delay(150);

        Assert.IsType<InvalidOperationException>(stopped.Error);
        Assert.Equal("analysis failed", stopped.Error.Message);
        Assert.Equal(1, Volatile.Read(ref stoppedCount));
        Assert.Equal(capturesAfterStop, Volatile.Read(ref captureCount));
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task CaptureFailureStopsSessionAndIsReportedExactlyOnce()
    {
        var createdFrames = new ConcurrentQueue<Bitmap>();
        var stoppedSource = new TaskCompletionSource<CaptureSessionStoppedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedCount = 0;
        var captureCount = 0;

        await using var session = new PassiveCaptureSession(_ =>
        {
            if (Interlocked.Increment(ref captureCount) == 3)
            {
                throw new InvalidOperationException("capture failed");
            }

            var bitmap = new Bitmap(2, 2);
            createdFrames.Enqueue(bitmap);
            return bitmap;
        });
        session.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref stoppedCount);
            stoppedSource.TrySetResult(args);
        };

        session.StartCompanion(
            new Rectangle(0, 0, 320, 180),
            (_, _, _) => Task.CompletedTask);

        var stopped = await stoppedSource.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(stopped.Error);
        Assert.Equal("capture failed", stopped.Error.Message);
        Assert.Equal(1, Volatile.Read(ref stoppedCount));
        Assert.False(session.IsRunning);
        Assert.All(
            createdFrames,
            bitmap => Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0)));
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingTimeProvider : TimeProvider
    {
        private long _timestamp;
        private int _timerCreationCount;

        public event Action<TimeSpan>? TimerCreated;

        public int TimerCreationCount => Volatile.Read(ref _timerCreationCount);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, duration.Ticks);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            Interlocked.Increment(ref _timerCreationCount);
            TimerCreated?.Invoke(dueTime);
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
