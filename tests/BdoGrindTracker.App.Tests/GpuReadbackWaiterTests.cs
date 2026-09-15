using System.Runtime.InteropServices;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class GpuReadbackWaiterTests
{
    private const int DeviceRemoved = unchecked((int)0x887A0005);
    private const int DeviceHung = unchecked((int)0x887A0006);
    private const int InvalidCall = unchecked((int)0x887A0001);

    [Fact]
    public void ReadyResourceReturnsWithoutFlushingWaitingOrQueryingDevice()
    {
        var attempts = 0;
        var waiter = new GpuReadbackWaiter(wait: (_, _) => Assert.Fail("A ready resource must not wait."));

        waiter.Wait(() => { attempts++; return 0; },
            () => Assert.Fail("A ready resource must not flush."),
            () => throw new InvalidOperationException("A ready resource needs no device-error query."), default);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void BusyResourceFlushesOnceBeforeWaitingAndRetriesUntilReady()
    {
        var clock = new ReadbackClock();
        var events = new List<string>();
        var attempts = 0;
        var waiter = new GpuReadbackWaiter(clock, wait: (delay, _) =>
        {
            Assert.True(delay > TimeSpan.Zero);
            events.Add("wait");
            clock.Advance(delay);
        });

        waiter.Wait(() =>
        {
            events.Add("map");
            return ++attempts < 3 ? GpuReadbackWaiter.WasStillDrawing : 0;
        }, () => events.Add("flush"), () => 0, default);

        Assert.Equal(new[] { "map", "flush", "wait", "map", "wait", "map" }, events);
    }

    [Fact]
    public void AlreadyCanceledRequestNeverTouchesGraphicsResources()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var waiter = new GpuReadbackWaiter(wait: (_, _) => Assert.Fail("Canceled capture must not wait."));

        var error = Assert.Throws<OperationCanceledException>(() => waiter.Wait(
            () => throw new InvalidOperationException("Canceled capture must not map."),
            () => Assert.Fail("Canceled capture must not flush."),
            () => throw new InvalidOperationException("Canceled capture must not query the device."),
            cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void CancellationDuringWaitPreventsAnotherMapAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var waiter = new GpuReadbackWaiter(new ReadbackClock(), wait: (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
        });

        var error = Assert.Throws<OperationCanceledException>(() => waiter.Wait(
            () => { attempts++; return GpuReadbackWaiter.WasStillDrawing; }, () => { }, () => 0,
            cancellation.Token));

        Assert.Equal(1, attempts);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void CancellationInsideSuccessfulMapStillReturnsOwnershipToCaller()
    {
        using var cancellation = new CancellationTokenSource();
        var waiter = new GpuReadbackWaiter(wait: (_, _) => Assert.Fail("Successful map must not wait."));

        // Returning lets the native caller set its mapped flag before checking cancellation,
        // so its finally block can still Unmap the resource acquired in this race.
        waiter.Wait(() => { cancellation.Cancel(); return 0; },
            () => Assert.Fail("Successful map must not flush."), () => 0, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
    }

    [Theory]
    [InlineData(DeviceRemoved, DeviceHung)]
    [InlineData(InvalidCall, 0)]
    [InlineData(DeviceRemoved, DeviceRemoved)]
    public void PermanentFailureIsNotRetriedAndPreservesOriginalHResult(int mapError, int deviceReason)
    {
        var attempts = 0;
        var waiter = new GpuReadbackWaiter(wait: (_, _) => Assert.Fail("Permanent errors must not wait."));

        var error = Assert.Throws<GpuReadbackException>(() => waiter.Wait(
            () => { attempts++; return mapError; },
            () => Assert.Fail("Permanent errors must not flush."), () => deviceReason, default));

        Assert.Equal(1, attempts);
        Assert.IsAssignableFrom<ExternalException>(error);
        Assert.Equal(mapError, error.HResult);
        if (deviceReason < 0 && deviceReason != mapError)
            Assert.Equal(deviceReason, Assert.IsType<COMException>(error.InnerException).HResult);
        else
            Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(DeviceHung)]
    public void BusyResourceTimesOutOnMonotonicDeadlineDespiteWallClockChanges(int deviceReason)
    {
        var clock = new ReadbackClock();
        var timeout = TimeSpan.FromMilliseconds(12);
        var waits = new List<TimeSpan>();
        var flushes = 0;
        var waiter = new GpuReadbackWaiter(clock, timeout, (delay, _) =>
        {
            Assert.True(waits.Count < 10, "Readback must stop at its deadline.");
            Assert.InRange(delay, TimeSpan.FromTicks(1), timeout - clock.Elapsed);
            waits.Add(delay);
            clock.Advance(delay);
            clock.WallClock = clock.WallClock.AddDays(-1);
        });

        var error = Assert.Throws<TimeoutException>(() => waiter.Wait(
            () => GpuReadbackWaiter.WasStillDrawing, () => flushes++, () => deviceReason, default));

        Assert.Equal(timeout, clock.Elapsed);
        Assert.Equal(1, flushes);
        Assert.NotEmpty(waits);
        if (deviceReason < 0)
            Assert.Equal(deviceReason, Assert.IsType<COMException>(error.InnerException).HResult);
        else
            Assert.Null(error.InnerException);
    }

    [Fact]
    public void TimeSpentSubmittingCopyCountsTowardDeadline()
    {
        var clock = new ReadbackClock();
        var timeout = TimeSpan.FromMilliseconds(10);
        var waiter = new GpuReadbackWaiter(clock, timeout,
            (_, _) => Assert.Fail("No waiting budget remains after the slow flush."));

        Assert.Throws<TimeoutException>(() => waiter.Wait(() => GpuReadbackWaiter.WasStillDrawing,
            () => clock.Advance(timeout), () => 0, default));
    }

    private sealed class ReadbackClock : TimeProvider
    {
        // Start away from zero to detect accidentally comparing an absolute timestamp to a duration.
        private long _timestamp = TimeSpan.FromHours(1).Ticks;
        public TimeSpan Elapsed { get; private set; }
        public DateTimeOffset WallClock { get; set; } = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => WallClock;

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
            Elapsed += duration;
        }
    }
}
