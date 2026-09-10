using System.Collections.Concurrent;
using System.Threading.Channels;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class PassiveCaptureTimingTests
{
    private static readonly Rectangle Region = new(0, 0, 20, 20);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task LiveDefaultIncludesAcquisitionCostInTwoHundredMillisecondDeadline()
    {
        var clock = new ManualCaptureClock();
        var observed = new TaskCompletionSource<CapturedFrameMetadata>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new PassiveCaptureSession(_ =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(60));
            return new Bitmap(2, 2);
        }, clock);
        session.StartCompanion(Region, (_, metadata, _) =>
        {
            observed.TrySetResult(metadata);
            return Task.CompletedTask;
        });

        var timer = await clock.NextTimerAsync();
        var metadata = await observed.Task.WaitAsync(TestTimeout);
        await session.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(TimeSpan.FromMilliseconds(200), session.FrameInterval);
        Assert.Equal(4, session.MaximumQueuedFrames);
        Assert.Equal(TimeSpan.FromMilliseconds(140), timer.DueTime);
        Assert.Equal(session.FrameInterval, metadata.TargetFrameInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(60), metadata.CaptureDuration);
        Assert.Null(metadata.CaptureInterval);
        Assert.Equal(TimeSpan.Zero, metadata.QueueDelay);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task CaptureTimeAndQueueDelayUseMonotonicClockDespiteWallClockChange(int wallClockShiftDays)
    {
        var clock = new ManualCaptureClock();
        var observed = new ConcurrentQueue<CapturedFrameMetadata>();
        var firstFrame = NewCompletionSource();
        var releaseFirstFrame = NewCompletionSource();
        await using var session = new PassiveCaptureSession(_ =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(60));
            return new Bitmap(2, 2);
        }, clock);
        session.StartCompanion(Region, async (_, metadata, _) =>
        {
            observed.Enqueue(metadata);
            if (metadata.Sequence == 1)
            {
                firstFrame.TrySetResult();
                await releaseFirstFrame.Task.WaitAsync(TestTimeout);
            }
        });

        var firstTimer = await clock.NextTimerAsync();
        await firstFrame.Task.WaitAsync(TestTimeout);
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddDays(wallClockShiftDays);
        firstTimer.Fire();
        await clock.NextTimerAsync(); // The second frame has been enqueued at 260 ms.
        clock.Advance(TimeSpan.FromMilliseconds(250));
        var stopping = session.StopAsync();
        releaseFirstFrame.TrySetResult();
        await stopping.WaitAsync(TestTimeout);

        var frames = observed.ToArray();
        Assert.Equal([1L, 2L], frames.Select(frame => frame.Sequence));
        Assert.Equal(DateTimeOffset.UnixEpoch, frames[0].CapturedAtUtc);
        Assert.Equal(TimeSpan.FromMilliseconds(200), frames[1].CapturedAtUtc - frames[0].CapturedAtUtc);
        Assert.Equal(TimeSpan.FromMilliseconds(200), frames[1].CaptureInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(250), frames[1].QueueDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(60), frames[1].CaptureDuration);
        Assert.Equal(TimeSpan.Zero, frames[1].BackpressureDuration);
    }

    [Fact]
    public async Task ResumePreservesRealPauseGapWhenWallClockMovesBackward()
    {
        var initialUtc = new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);
        var clock = new ManualCaptureClock { UtcNow = initialUtc };
        var firstFrame = new TaskCompletionSource<CapturedFrameMetadata>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resumedFrame = new TaskCompletionSource<CapturedFrameMetadata>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new PassiveCaptureSession(_ =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(60));
            return new Bitmap(2, 2);
        }, clock);
        session.StartCompanion(Region, (_, metadata, _) =>
        {
            firstFrame.TrySetResult(metadata);
            return Task.CompletedTask;
        });
        await clock.NextTimerAsync();
        var beforePause = await firstFrame.Task.WaitAsync(TestTimeout);
        await session.StopAsync().WaitAsync(TestTimeout);

        clock.UtcNow = initialUtc.AddDays(-1);
        clock.Advance(TimeSpan.FromSeconds(30));
        session.StartCompanion(Region, (_, metadata, _) =>
        {
            resumedFrame.TrySetResult(metadata);
            return Task.CompletedTask;
        });
        await clock.NextTimerAsync();
        var afterPause = await resumedFrame.Task.WaitAsync(TestTimeout);
        await session.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(initialUtc, beforePause.CapturedAtUtc);
        Assert.True(afterPause.CapturedAtUtc > beforePause.CapturedAtUtc);
        Assert.Equal(TimeSpan.FromMilliseconds(30_060), afterPause.CapturedAtUtc - beforePause.CapturedAtUtc);
        Assert.Equal(1, afterPause.Sequence);
        Assert.Null(afterPause.CaptureInterval);
    }

    [Fact]
    public async Task FullQueueReportsBackpressureAndLongerActualCadenceWithoutDroppingFrames()
    {
        var clock = new ManualCaptureClock();
        var observed = new ConcurrentQueue<CapturedFrameMetadata>();
        var firstFrame = NewCompletionSource();
        var releaseFirstFrame = NewCompletionSource();
        var releaseSecondFrame = NewCompletionSource();
        await using var session = new PassiveCaptureSession(_ =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(20));
            return new Bitmap(2, 2);
        }, clock, maximumQueuedFrames: 1);
        session.StartCompanion(Region, async (_, metadata, _) =>
        {
            observed.Enqueue(metadata);
            if (metadata.Sequence == 1)
            {
                firstFrame.TrySetResult();
                await releaseFirstFrame.Task.WaitAsync(TestTimeout);
            }
            if (metadata.Sequence == 2)
                await releaseSecondFrame.Task.WaitAsync(TestTimeout);
        });

        var firstTimer = await clock.NextTimerAsync();
        await firstFrame.Task.WaitAsync(TestTimeout);
        firstTimer.Fire();
        var secondTimer = await clock.NextTimerAsync();
        var waitingForCapacity = clock.ObserveTimestampReadAt(TimeSpan.FromMilliseconds(400));
        secondTimer.Fire();
        await waitingForCapacity.WaitAsync(TestTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        releaseFirstFrame.TrySetResult();
        await clock.NextTimerAsync(); // Third acquisition completed after capacity became free.
        var stopping = session.StopAsync();
        releaseSecondFrame.TrySetResult();
        await stopping.WaitAsync(TestTimeout);

        var frames = observed.ToArray();
        Assert.Equal([1L, 2L, 3L], frames.Select(frame => frame.Sequence));
        Assert.Equal(TimeSpan.FromMilliseconds(500), frames[2].BackpressureDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(700), frames[2].CaptureInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(200), frames[2].TargetFrameInterval);
        Assert.Equal(1, session.MaximumQueuedFrames);
    }

    [Fact]
    public async Task ExplicitCadenceIsReportedInsteadOfHistoricalCompanionConstant()
    {
        var clock = new ManualCaptureClock();
        var observed = new TaskCompletionSource<CapturedFrameMetadata>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var interval = TimeSpan.FromMilliseconds(320);
        await using var session = new PassiveCaptureSession(_ => new Bitmap(2, 2), clock, interval);
        session.StartCompanion(Region, (_, metadata, _) =>
        {
            observed.TrySetResult(metadata);
            return Task.CompletedTask;
        });

        var timer = await clock.NextTimerAsync();
        var metadata = await observed.Task.WaitAsync(TestTimeout);
        await session.StopAsync().WaitAsync(TestTimeout);

        Assert.Equal(interval, session.FrameInterval);
        Assert.Equal(interval, metadata.TargetFrameInterval);
        Assert.Equal(interval, timer.DueTime);
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ManualCaptureClock : TimeProvider
    {
        private readonly Channel<ManualCaptureTimer> _timers = Channel.CreateUnbounded<ManualCaptureTimer>();
        private long _timestamp;
        private long _observedTimestamp = -1;
        private TaskCompletionSource? _timestampObserved;

        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp()
        {
            var timestamp = Interlocked.Read(ref _timestamp);
            if (timestamp == Interlocked.Read(ref _observedTimestamp))
                _timestampObserved?.TrySetResult();
            return timestamp;
        }

        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);

        public Task ObserveTimestampReadAt(TimeSpan timestamp)
        {
            _timestampObserved = NewCompletionSource();
            Interlocked.Exchange(ref _observedTimestamp, timestamp.Ticks);
            return _timestampObserved.Task;
        }

        public async Task<ManualCaptureTimer> NextTimerAsync() =>
            await _timers.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualCaptureTimer(this, callback, state, dueTime);
            _timers.Writer.TryWrite(timer);
            return timer;
        }

        public sealed class ManualCaptureTimer(
            ManualCaptureClock clock, TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private int _disposed;
            public TimeSpan DueTime { get; } = dueTime;

            public void Fire()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                clock.Advance(DueTime);
                callback(state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
