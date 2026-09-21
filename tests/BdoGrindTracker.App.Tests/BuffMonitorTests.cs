using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffMonitorTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static BuffFrameReading Reading() => new([new("harmony-draught", TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(1))]);

    [Fact]
    public async Task SamplingIsThrottledAndFailuresBreakContinuity()
    {
        var reader = new Reader();
        using var monitor = new BuffMonitor(reader);
        using var frame = new Bitmap(10, 10);
        monitor.Observe(frame, Epoch);
        await monitor.CurrentAnalysis;
        Assert.True(monitor.Snapshot(Epoch, out var generation).IsKnown);
        monitor.Observe(frame, Epoch.AddSeconds(9));
        Assert.Equal(1, reader.Calls);
        reader.Next = null;
        monitor.Observe(frame, Epoch.AddSeconds(10));
        await monitor.CurrentAnalysis;
        Assert.False(monitor.Snapshot(Epoch.AddSeconds(10), out var failedGeneration).IsKnown);
        Assert.True(failedGeneration > generation);
        Assert.Contains("unbekannt", monitor.LastDiagnostic);
        reader.Next = Reading();
        monitor.Observe(frame, Epoch.AddSeconds(20));
        await monitor.CurrentAnalysis;
        Assert.True(monitor.Snapshot(Epoch.AddSeconds(20), out var recoveredGeneration).IsKnown);
        Assert.Equal(failedGeneration, recoveredGeneration);
    }

    [Fact]
    public async Task ExpiredSnapshotAndResetInvalidateContinuityOnce()
    {
        using var monitor = new BuffMonitor(new Reader());
        using var frame = new Bitmap(10, 10);
        monitor.Observe(frame, Epoch);
        await monitor.CurrentAnalysis;
        monitor.Snapshot(Epoch, out var original);
        Assert.False(monitor.Snapshot(Epoch.AddSeconds(30), out var expired).IsKnown);
        Assert.True(expired > original);
        Assert.Contains("veraltet", monitor.LastDiagnostic);
        monitor.Snapshot(Epoch.AddSeconds(31), out var stillExpired);
        Assert.Equal(expired, stillExpired);
        monitor.Reset();
        monitor.Snapshot(Epoch.AddSeconds(31), out var reset);
        Assert.True(reset > expired);
    }

    [Fact]
    public async Task ReaderDiagnosticsArePreservedAndEmptyReadingsCannotEstablishContinuity()
    {
        var reader = new Reader { Next = null, LastDiagnostic = "Restzeit für Harmony nicht lesbar." };
        using var monitor = new BuffMonitor(reader);
        using var frame = new Bitmap(10, 10);
        monitor.Observe(frame, Epoch);
        await monitor.CurrentAnalysis;
        Assert.Equal(reader.LastDiagnostic, monitor.LastDiagnostic);
        Assert.False(monitor.Snapshot(Epoch, out var unknown).IsKnown);

        reader.Next = new([]);
        reader.LastDiagnostic = null;
        monitor.Observe(frame, Epoch.AddSeconds(10));
        await monitor.CurrentAnalysis;
        Assert.False(monitor.Snapshot(Epoch.AddSeconds(10), out var empty).IsKnown);
        Assert.True(empty > unknown);
    }

    [Fact]
    public async Task ResetDiscardsLateReadAndConcurrentFramesDoNotQueue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader { Block = () => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); } };
        using var monitor = new BuffMonitor(reader, TimeSpan.FromMilliseconds(1));
        using var frame = new Bitmap(10, 10);
        monitor.Observe(frame, Epoch);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        monitor.Observe(frame, Epoch.AddSeconds(10));
        Assert.Equal(1, reader.Calls);
        monitor.Reset();
        release.SetResult();
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(monitor.Snapshot(Epoch.AddSeconds(10)).IsKnown);
        reader.Block = null;
        monitor.Observe(frame, Epoch.AddSeconds(10));
        await monitor.CurrentAnalysis;
        Assert.True(monitor.Snapshot(Epoch.AddSeconds(10)).IsKnown);
    }

    [Fact]
    public async Task DisposalWaitsForOwnedWorkerBeforeDisposingReader()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader { Block = () => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); } };
        var monitor = new BuffMonitor(reader);
        using var frame = new Bitmap(10, 10);
        monitor.Observe(frame, Epoch);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        monitor.Dispose();
        Assert.False(reader.Disposed);
        release.SetResult();
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(reader.Disposed);
        Assert.False(monitor.Snapshot(Epoch).IsKnown);
    }

    private sealed class Reader : IBuffFrameReader
    {
        internal BuffFrameReading? Next { get; set; } = Reading();
        internal Action? Block { get; set; }
        internal int Calls { get; private set; }
        internal bool Disposed { get; private set; }
        public string? LastDiagnostic { get; set; }
        public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken)
        { Calls++; Block?.Invoke(); return Next; }
        public void Dispose() => Disposed = true;
    }
}
