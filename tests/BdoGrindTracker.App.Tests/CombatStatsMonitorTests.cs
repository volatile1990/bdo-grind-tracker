using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class CombatStatsMonitorTests
{
    [Fact]
    public async Task ConfirmsTwoSparseSamplesThenExpiresStaleStats()
    {
        var reader = new Reader(() => new(2374, 826, CombatStatsCategory.Edania));
        using var monitor = new CombatStatsMonitor(reader);
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        monitor.Observe(frame, now); await monitor.CurrentAnalysis;
        Assert.False(monitor.Snapshot(now).IsKnown);
        for (var second = 1; second < 5; second++) monitor.Observe(frame, now.AddSeconds(second));
        Assert.Equal(1, reader.Calls);
        monitor.Observe(frame, now.AddSeconds(5)); await monitor.CurrentAnalysis;
        Assert.Equal(new CombatStatsState(2374, 826, CombatStatsCategory.Edania, now.AddSeconds(5)), monitor.Snapshot(now.AddSeconds(5)));
        Assert.True(monitor.Snapshot(now.AddSeconds(19)).IsKnown);
        Assert.False(monitor.Snapshot(now.AddSeconds(20)).IsKnown);
        Assert.False(monitor.Snapshot(now).IsKnown);
    }

    [Fact]
    public async Task CategoryAndStatChangesRequireFreshConfirmationAndHideTheOldValues()
    {
        CombatStatsReading? next = new(2374, 826, CombatStatsCategory.Edania);
        using var monitor = new CombatStatsMonitor(new Reader(() => next));
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        async Task Sample(int second) { monitor.Observe(frame, now.AddSeconds(second)); await monitor.CurrentAnalysis; }
        await Sample(0); await Sample(5);
        Assert.True(monitor.Snapshot(now.AddSeconds(5)).IsKnown);
        next = next with { Category = CombatStatsCategory.General };
        await Sample(10);
        Assert.False(monitor.Snapshot(now.AddSeconds(10)).IsKnown);
        await Sample(15);
        Assert.Equal(CombatStatsCategory.General, monitor.Snapshot(now.AddSeconds(15)).Category);
        next = next with { Ap = 2100 };
        await Sample(20);
        Assert.False(monitor.Snapshot(now.AddSeconds(20)).IsKnown);
        await Sample(25);
        Assert.Equal(2100, monitor.Snapshot(now.AddSeconds(25)).Ap);
    }

    [Fact]
    public async Task MissingOrFailedReadClearsConfirmationAndCannotBridgeALongGap()
    {
        CombatStatsReading? next = new(2374, 826, CombatStatsCategory.Edania);
        var fail = false;
        using var monitor = new CombatStatsMonitor(new Reader(() => fail ? throw new IOException() : next));
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        async Task Sample(int second) { monitor.Observe(frame, now.AddSeconds(second)); await monitor.CurrentAnalysis; }
        await Sample(0); await Sample(20);
        Assert.False(monitor.Snapshot(now.AddSeconds(20)).IsKnown);
        await Sample(25);
        Assert.True(monitor.Snapshot(now.AddSeconds(25)).IsKnown);
        next = null; await Sample(30);
        Assert.False(monitor.Snapshot(now.AddSeconds(30)).IsKnown);
        next = new(2374, 826, CombatStatsCategory.Edania); await Sample(35);
        Assert.False(monitor.Snapshot(now.AddSeconds(35)).IsKnown);
        fail = true; await Sample(40); fail = false; await Sample(45);
        Assert.False(monitor.Snapshot(now.AddSeconds(45)).IsKnown);
    }

    [Fact]
    public async Task ResetCancelsSingleFlightWorkerAndRejectsOldResult()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reader = new Reader(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new(2374, 826, CombatStatsCategory.Edania); });
        using var monitor = new CombatStatsMonitor(reader);
        using var frame = new Bitmap(2, 2);
        var now = DateTimeOffset.UtcNow;
        try
        {
            monitor.Observe(frame, now);
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            monitor.Observe(frame, now.AddSeconds(5));
            Assert.Equal(1, reader.Calls);
            monitor.Reset();
        }
        finally { release.Set(); await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.False(monitor.Snapshot(now).IsKnown);
        monitor.Observe(frame, now.AddSeconds(1)); await monitor.CurrentAnalysis;
        Assert.False(monitor.Snapshot(now.AddSeconds(1)).IsKnown);
        monitor.Observe(frame, now.AddSeconds(6)); await monitor.CurrentAnalysis;
        Assert.True(monitor.Snapshot(now.AddSeconds(6)).IsKnown);
    }

    [Fact]
    public async Task DisposeWaitsForWorkerBeforeDisposingReaderAndNeverPublishesLateResult()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reader = new Reader(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new(2374, 826, CombatStatsCategory.Edania); });
        using var monitor = new CombatStatsMonitor(reader);
        using (var frame = new Bitmap(2, 2)) monitor.Observe(frame, DateTimeOffset.UtcNow);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            monitor.Dispose();
            Assert.Equal(0, reader.Disposals);
        }
        finally { release.Set(); await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.Equal(1, reader.Disposals);
        Assert.False(monitor.Snapshot(DateTimeOffset.UtcNow).IsKnown);
    }

    private sealed class Reader(Func<CombatStatsReading?> read) : ICombatStatsFrameReader
    {
        public int Calls, Disposals;
        public CombatStatsReading? Read(Bitmap frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Assert.Equal(2, frame.Width);
            return read();
        }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}
