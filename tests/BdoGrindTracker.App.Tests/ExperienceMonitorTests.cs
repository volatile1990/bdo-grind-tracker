using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class ExperienceMonitorTests
{
    [Fact]
    public async Task SamplesOnlyOncePerMinuteAndExpiresOldPercentages()
    {
        var reader = new Reader(() => new(61, .579m));
        using var monitor = new ExperienceMonitor(reader);
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        monitor.Observe(frame, now); await monitor.CurrentAnalysis;
        for (var seconds = 1; seconds < 60; seconds++) monitor.Observe(frame, now.AddSeconds(seconds));
        Assert.Equal(1, reader.Calls);
        Assert.Equal(new ExperienceState(61, .579m, now), monitor.Snapshot(now.AddSeconds(59)));
        monitor.Observe(frame, now.AddSeconds(60)); await monitor.CurrentAnalysis;
        Assert.Equal(2, reader.Calls);
        Assert.True(monitor.Snapshot(now.AddSeconds(209)).IsKnown);
        Assert.Equal(ExperienceState.Unknown, monitor.Snapshot(now.AddSeconds(210)));
        Assert.Equal(ExperienceState.Unknown, monitor.Snapshot(now));
    }

    [Fact]
    public async Task UnreadableInvalidAndFailedSamplesInvalidateStateAndBreakObservationGeneration()
    {
        ExperienceReading? next = new(61, .579m);
        var fail = false;
        using var monitor = new ExperienceMonitor(new Reader(() => fail ? throw new InvalidOperationException() : next));
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        monitor.Observe(frame, now); await monitor.CurrentAnalysis;
        Assert.True(monitor.Snapshot(now, out var generation).IsKnown);
        foreach (var reading in new ExperienceReading?[] { null, new(61, 100m), new(0, .579m), new(61, -.1m) })
        {
            next = reading;
            now = now.AddMinutes(1);
            monitor.Observe(frame, now); await monitor.CurrentAnalysis;
            Assert.Equal(ExperienceState.Unknown, monitor.Snapshot(now, out var nextGeneration));
            Assert.True(nextGeneration > generation);
            generation = nextGeneration;
        }
        fail = true;
        now = now.AddMinutes(1);
        monitor.Observe(frame, now); await monitor.CurrentAnalysis;
        Assert.Equal(ExperienceState.Unknown, monitor.Snapshot(now, out var failureGeneration));
        Assert.True(failureGeneration > generation);
    }

    [Fact]
    public async Task SlowOcrIsSingleFlightAndResetRejectsAResultFromThePreviousSession()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reader = new Reader(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new(61, .579m); });
        using var monitor = new ExperienceMonitor(reader);
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        try
        {
            monitor.Observe(frame, now);
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            monitor.Observe(frame, now.AddMinutes(1));
            Assert.Equal(1, reader.Calls);
            monitor.Reset();
        }
        finally { release.Set(); await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.Equal(ExperienceState.Unknown, monitor.Snapshot(now));
        monitor.Observe(frame, now.AddSeconds(1)); await monitor.CurrentAnalysis;
        Assert.Equal(2, reader.Calls);
        Assert.True(monitor.Snapshot(now.AddSeconds(1)).IsKnown);
    }

    [Fact]
    public async Task WorkerOwnsItsFrameAndDisposesTheReaderOnlyAfterFinishing()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reader = new Reader(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new(61, .579m); });
        using var monitor = new ExperienceMonitor(reader);
        using (var frame = new Bitmap(2, 2)) monitor.Observe(frame, DateTimeOffset.UtcNow);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            monitor.Dispose();
            Assert.Equal(0, reader.Disposals);
        }
        finally { release.Set(); await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.Equal(1, reader.Disposals);
        Assert.Equal(ExperienceState.Unknown, monitor.Snapshot(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SamplingIntervalMustBePositive()
    {
        using var reader = new Reader(() => null);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperienceMonitor(reader, TimeSpan.Zero));
    }

    private sealed class Reader(Func<ExperienceReading?> read) : IExperienceFrameReader
    {
        public int Calls, Disposals;
        public ExperienceReading? Read(Bitmap frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var result = read();
            Assert.Equal(2, frame.Width);
            return result;
        }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}
