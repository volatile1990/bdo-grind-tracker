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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QueueCapacityMustBePositive(int maximumQueuedFrames)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PassiveCaptureSession(
            _ => new Bitmap(2, 2), maximumQueuedFrames: maximumQueuedFrames));
    }

    [Fact]
    public async Task BlockedAnalysisContinuesCaptureUntilBoundedQueueIsFull()
    {
        var callbackEntered = NewCompletionSource();
        var releaseCallback = NewCompletionSource();
        var queueFilled = NewCompletionSource();
        var captureCount = 0;
        var callbackCount = 0;
        var activeCallbacks = 0;
        var concurrentCallbacks = false;
        var observed = new ConcurrentQueue<long>();

        await using var session = new PassiveCaptureSession(
            _ =>
            {
                if (Interlocked.Increment(ref captureCount) == 3) queueFilled.TrySetResult();
                return new Bitmap(2, 2);
            },
            frameInterval: TimeSpan.FromMilliseconds(20),
            maximumQueuedFrames: 2);

        session.StartCompanion(
            FourKRegion,
            async (_, metadata, cancellationToken) =>
            {
                if (Interlocked.Increment(ref activeCallbacks) != 1) concurrentCallbacks = true;
                Interlocked.Increment(ref callbackCount);
                observed.Enqueue(metadata.Sequence);
                callbackEntered.TrySetResult();
                await releaseCallback.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                Interlocked.Decrement(ref activeCallbacks);
            });

        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queueFilled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(125);
        var capturesWhileBlocked = Volatile.Read(ref captureCount);
        var callbacksWhileBlocked = Volatile.Read(ref callbackCount);

        var stopping = session.StopAsync();
        releaseCallback.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, capturesWhileBlocked);
        Assert.Equal(1, callbacksWhileBlocked);
        Assert.False(concurrentCallbacks);
        Assert.Equal([1L, 2L, 3L], observed);
    }

    [Fact]
    public async Task CaptureWaitsOnlyForRemainingAcquisitionDeadline()
    {
        var timeProvider = new RecordingTimeProvider { MetadataElapsed = TimeSpan.FromMilliseconds(125) };
        var delayObserved = new TaskCompletionSource<TimeSpan>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        timeProvider.TimerCreated += dueTime => delayObserved.TrySetResult(dueTime);

        await using var session = new PassiveCaptureSession(
            _ => new Bitmap(2, 2),
            timeProvider,
            PassiveCaptureSession.CompanionFrameInterval);

        session.StartCompanion(
            FourKRegion,
            (_, _, _) => Task.CompletedTask);

        var dueTime = await delayObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.StopAsync();

        Assert.Equal(TimeSpan.FromMilliseconds(325), dueTime);
    }

    [Fact]
    public async Task CaptureDeadlineDoesNotWaitForAnalysisCompletion()
    {
        var timeProvider = new RecordingTimeProvider();
        var callbackEntered = NewCompletionSource();
        var releaseCallback = NewCompletionSource();
        var delayObserved = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        timeProvider.TimerCreated += dueTime => delayObserved.TrySetResult(dueTime);

        await using var session = new PassiveCaptureSession(
            _ => new Bitmap(2, 2),
            timeProvider,
            PassiveCaptureSession.CompanionFrameInterval);

        session.StartCompanion(
            FourKRegion,
            async (_, _, cancellationToken) =>
            {
                callbackEntered.TrySetResult();
                await releaseCallback.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            });

        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var dueTime = await delayObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopping = session.StopAsync();
        releaseCallback.TrySetResult();
        await stopping;

        Assert.Equal(PassiveCaptureSession.CompanionFrameInterval, dueTime);
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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public async Task CaptureRepresentationSelectsOcrThresholdsWithoutLosingPhysicalHdrState(
        bool hdr, bool toneMapped, bool hdrOcr)
    {
        var observed = new TaskCompletionSource<CapturedFrameMetadata>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new PassiveCaptureSession(
            _ => new CapturedDesktopBitmap(new Bitmap(2, 2), IsHdr: hdr, IsToneMapped: toneMapped),
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

        Assert.Equal(hdr, metadata.IsHdr);
        Assert.Equal(toneMapped, metadata.IsToneMapped);
        Assert.Equal(hdrOcr, metadata.UseHdrOcr);
    }

    [Fact]
    public async Task StopDrainsCurrentAndQueuedFramesWithoutCancellingAnalysisThenDisposesAllFrames()
    {
        var createdFrames = new ConcurrentQueue<Bitmap>();
        var callbackEntered = NewCompletionSource();
        var releaseCallback = NewCompletionSource();
        var queueFilled = NewCompletionSource();
        var observed = new ConcurrentQueue<long>();
        var processingTokenCancelled = false;
        var captureCount = 0;
        var stoppedCount = 0;
        CaptureSessionStoppedEventArgs? stopped = null;

        await using var session = new PassiveCaptureSession(_ =>
        {
            var bitmap = new Bitmap(2, 2);
            createdFrames.Enqueue(bitmap);
            if (Interlocked.Increment(ref captureCount) == 3) queueFilled.TrySetResult();
            return bitmap;
        }, frameInterval: TimeSpan.FromMilliseconds(5), maximumQueuedFrames: 2);
        session.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref stoppedCount);
            stopped = args;
        };

        session.StartCompanion(
            FourKRegion,
            async (frame, metadata, cancellationToken) =>
            {
                callbackEntered.TrySetResult();
                await releaseCallback.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                processingTokenCancelled |= cancellationToken.IsCancellationRequested;
                frame.GetPixel(0, 0);
                observed.Enqueue(metadata.Sequence);
            });

        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queueFilled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopping = session.StopAsync();
        var stopWaitedForAnalysis = !stopping.IsCompleted;
        releaseCallback.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(stopWaitedForAnalysis);
        Assert.False(processingTokenCancelled);
        Assert.Equal([1L, 2L, 3L], observed);
        Assert.Equal(3, Volatile.Read(ref captureCount));
        Assert.False(session.IsRunning);
        Assert.Equal(1, Volatile.Read(ref stoppedCount));
        Assert.NotNull(stopped);
        Assert.Null(stopped.Error);
        Assert.All(
            createdFrames,
            bitmap => Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0)));
    }

    [Fact]
    public async Task StopDuringAcquisitionStillProcessesSuccessfullyAcquiredFrame()
    {
        var captureEntered = NewCompletionSource();
        using var releaseCapture = new ManualResetEventSlim();
        Bitmap? capturedBitmap = null;
        var processed = new List<long>();
        await using var session = new PassiveCaptureSession(_ =>
        {
            captureEntered.TrySetResult();
            if (!releaseCapture.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return capturedBitmap = new Bitmap(2, 2);
        });
        session.StartCompanion(FourKRegion, (bitmap, metadata, cancellationToken) =>
        {
            Assert.False(cancellationToken.IsCancellationRequested);
            bitmap.GetPixel(0, 0);
            processed.Add(metadata.Sequence);
            return Task.CompletedTask;
        });

        await captureEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopping = session.StopAsync();
        releaseCapture.Set();
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([1L], processed);
        Assert.NotNull(capturedBitmap);
        Assert.Throws<ArgumentException>(() => capturedBitmap.GetPixel(0, 0));
    }

    [Fact]
    public async Task RestartCannotDeliverOldFramesToNewCallback()
    {
        var queueFilled = NewCompletionSource();
        var releaseAnalysis = NewCompletionSource();
        var newCallbackEntered = NewCompletionSource();
        var releaseNewCallback = NewCompletionSource();
        var captureCount = 0;
        var oldSequences = new ConcurrentQueue<long>();
        var newSequences = new ConcurrentQueue<long>();
        await using var session = new PassiveCaptureSession(_ =>
        {
            if (Interlocked.Increment(ref captureCount) == 3) queueFilled.TrySetResult();
            return new Bitmap(2, 2);
        }, frameInterval: TimeSpan.FromMilliseconds(5), maximumQueuedFrames: 2);
        session.StartCompanion(FourKRegion, async (_, metadata, _) =>
        {
            await releaseAnalysis.Task.WaitAsync(TimeSpan.FromSeconds(5));
            oldSequences.Enqueue(metadata.Sequence);
        });
        await queueFilled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstStop = session.StopAsync();
        releaseAnalysis.TrySetResult();
        await firstStop.WaitAsync(TimeSpan.FromSeconds(2));

        session.StartCompanion(FourKRegion, async (_, metadata, _) =>
        {
            newSequences.Enqueue(metadata.Sequence);
            newCallbackEntered.TrySetResult();
            await releaseNewCallback.Task.WaitAsync(TimeSpan.FromSeconds(5));
        });
        await newCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondStop = session.StopAsync();
        releaseNewCallback.TrySetResult();
        await secondStop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([1L, 2L, 3L], oldSequences);
        Assert.NotEmpty(newSequences);
        Assert.Equal(1, newSequences.First());
        Assert.Equal(captureCount, oldSequences.Count + newSequences.Count);
    }

    [Fact]
    public async Task AnalysisFailureStopsCaptureDisposesQueuedFramesAndIsReportedExactlyOnce()
    {
        var stoppedSource = new TaskCompletionSource<CaptureSessionStoppedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedCount = 0;
        var captureCount = 0;
        var callbackCount = 0;
        var createdFrames = new ConcurrentQueue<Bitmap>();
        var queueFilled = NewCompletionSource();
        var failAnalysis = NewCompletionSource();

        await using var session = new PassiveCaptureSession(_ =>
        {
            var bitmap = new Bitmap(2, 2);
            createdFrames.Enqueue(bitmap);
            if (Interlocked.Increment(ref captureCount) == 3) queueFilled.TrySetResult();
            return bitmap;
        }, frameInterval: TimeSpan.FromMilliseconds(5), maximumQueuedFrames: 2);
        session.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref stoppedCount);
            stoppedSource.TrySetResult(args);
        };

        session.StartCompanion(
            new Rectangle(0, 0, 320, 180),
            async (_, _, _) =>
            {
                Interlocked.Increment(ref callbackCount);
                await failAnalysis.Task.WaitAsync(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("analysis failed");
            });

        await queueFilled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        failAnalysis.TrySetResult();
        var stopped = await stoppedSource.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.StopAsync();
        var capturesAfterStop = Volatile.Read(ref captureCount);
        await Task.Delay(150);

        Assert.IsType<InvalidOperationException>(stopped.Error);
        Assert.Equal("analysis failed", stopped.Error.Message);
        Assert.Equal(1, Volatile.Read(ref stoppedCount));
        Assert.Equal(capturesAfterStop, Volatile.Read(ref captureCount));
        Assert.Equal(1, callbackCount);
        Assert.False(session.IsRunning);
        Assert.All(createdFrames, bitmap => Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0)));
    }

    [Fact]
    public async Task CaptureFailureDrainsPreviouslyCapturedFramesBeforeReportingFailureExactlyOnce()
    {
        var createdFrames = new ConcurrentQueue<Bitmap>();
        var stoppedSource = new TaskCompletionSource<CaptureSessionStoppedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedCount = 0;
        var captureCount = 0;
        var observed = new ConcurrentQueue<long>();
        var captureFailed = NewCompletionSource();
        var releaseAnalysis = NewCompletionSource();

        await using var session = new PassiveCaptureSession(_ =>
        {
            if (Interlocked.Increment(ref captureCount) == 3)
            {
                captureFailed.TrySetResult();
                throw new InvalidOperationException("capture failed");
            }

            var bitmap = new Bitmap(2, 2);
            createdFrames.Enqueue(bitmap);
            return bitmap;
        }, frameInterval: TimeSpan.FromMilliseconds(5));
        session.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref stoppedCount);
            stoppedSource.TrySetResult(args);
        };

        session.StartCompanion(
            new Rectangle(0, 0, 320, 180),
            async (_, metadata, _) =>
            {
                await releaseAnalysis.Task.WaitAsync(TimeSpan.FromSeconds(5));
                observed.Enqueue(metadata.Sequence);
            });

        await captureFailed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var reportedBeforeDrain = stoppedSource.Task.IsCompleted;
        releaseAnalysis.TrySetResult();
        var stopped = await stoppedSource.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.StopAsync();

        Assert.False(reportedBeforeDrain);
        Assert.Equal([1L, 2L], observed);
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
        public TimeSpan MetadataElapsed { get; init; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public override DateTimeOffset GetUtcNow()
        {
            Advance(MetadataElapsed);
            return DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        }

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
