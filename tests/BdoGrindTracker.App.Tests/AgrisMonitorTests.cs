using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class AgrisMonitorTests
{
    [Fact]
    public async Task SamplesAtFiveSecondIntervalsAndExpiresUnobservedHud()
    {
        var detector = new Detector(() => new(AgrisStatus.Active));
        using var monitor = new AgrisMonitor(detector);
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        monitor.Observe(frame, now);
        await monitor.CurrentAnalysis;
        monitor.Observe(frame, now.AddSeconds(4));
        Assert.Equal(1, detector.Calls);
        monitor.Observe(frame, now.AddSeconds(5));
        await monitor.CurrentAnalysis;
        Assert.Equal(2, detector.Calls);
        Assert.Equal(AgrisStatus.Active, monitor.Snapshot(now.AddSeconds(6)).Status);
        Assert.Equal(AgrisState.Unknown, monitor.Snapshot(now.AddSeconds(20)));
        Assert.Equal(AgrisState.Unknown, monitor.Snapshot(now));
    }

    [Fact]
    public async Task UnknownAndFailedReadingsClearThePreviousState()
    {
        var status = AgrisStatus.Active;
        var fail = false;
        using var monitor = new AgrisMonitor(new Detector(() => fail
            ? throw new InvalidOperationException("Optional image failure") : new(status)));
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        monitor.Observe(frame, now); await monitor.CurrentAnalysis;
        Assert.Equal(AgrisStatus.Active, monitor.Snapshot(now).Status);
        status = AgrisStatus.Unknown;
        monitor.Observe(frame, now.AddSeconds(5)); await monitor.CurrentAnalysis;
        Assert.Equal(AgrisState.Unknown, monitor.Snapshot(now.AddSeconds(5)));
        status = AgrisStatus.Active;
        monitor.Observe(frame, now.AddSeconds(10)); await monitor.CurrentAnalysis;
        fail = true;
        monitor.Observe(frame, now.AddSeconds(15)); await monitor.CurrentAnalysis;
        Assert.Equal(AgrisState.Unknown, monitor.Snapshot(now.AddSeconds(15)));
    }

    [Fact]
    public async Task ResetRejectsAnOldWorkerAndAllowsFreshObservation()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var detector = new Detector(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new(AgrisStatus.Active); });
        using var monitor = new AgrisMonitor(detector);
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        try
        {
            monitor.Observe(frame, now);
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            monitor.Observe(frame, now.AddSeconds(5));
            Assert.Equal(1, detector.Calls);
            monitor.Reset();
            Assert.Equal(AgrisState.Unknown, monitor.Snapshot(now));
        }
        finally { release.Set(); await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.Equal(AgrisState.Unknown, monitor.Snapshot(now));
        monitor.Observe(frame, now.AddSeconds(1)); await monitor.CurrentAnalysis;
        Assert.Equal(AgrisStatus.Active, monitor.Snapshot(now.AddSeconds(1)).Status);
        Assert.Equal(2, detector.Calls);
    }

    [Fact]
    public async Task SlowRecognitionOwnsACopyAndDisposesOnlyAfterTheWorkerFinishes()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var detector = new Detector(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new(AgrisStatus.Active); });
        using var monitor = new AgrisMonitor(detector);
        using (var frame = new Bitmap(2, 2)) monitor.Observe(frame, DateTimeOffset.UtcNow);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(monitor.CurrentAnalysis.IsCompleted);
            monitor.Dispose();
            Assert.Equal(0, detector.Disposals);
        }
        finally { release.Set(); await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.Equal(1, detector.Disposals);
        Assert.Equal(AgrisState.Unknown, monitor.Snapshot(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InvalidSamplingIntervalIsRejected()
    {
        using var detector = new Detector(() => AgrisReading.Unknown);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgrisMonitor(detector, TimeSpan.Zero));
    }

    private sealed class Detector(Func<AgrisReading> read) : IAgrisFrameDetector
    {
        public int Calls, Disposals;
        public AgrisReading Analyze(Bitmap frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var result = read();
            Assert.Equal(2, frame.Width);
            return result;
        }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}
