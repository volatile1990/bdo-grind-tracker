using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionCompletionDrainsCapturedBuffRenewalWithoutCountingDrainTime(bool shutdown)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var monitor = new BuffMonitor(new SessionBuffReader(() =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read < 3) return BuffReading(read == 1 ? 60 : 59);
            entered.TrySetResult();
            release.Wait();
            return BuffReading(1200);
        }), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        try
        {
            BeginBuffSession(fixture);
            var now = DateTimeOffset.UtcNow;
            await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
            await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
            using var frame = new Bitmap(2, 2);
            await fixture.Service.ProcessFrameAsync(frame,
                new CapturedFrameMetadata(3, now.AddSeconds(-1)) { CanObserveHud = true }, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var completion = shutdown ? fixture.Service.ShutdownAsync() : fixture.Service.PauseAsync();
            Assert.False(completion.IsCompleted);
            var frozen = fixture.Clock.Elapsed;
            Assert.False(fixture.Clock.IsRunning);
            fixture.Time.Advance(TimeSpan.FromMinutes(10));
            fixture.Service.RefreshPendingState();
            release.Set();
            await completion.WaitAsync(TimeSpan.FromSeconds(8));

            Assert.Equal(frozen, fixture.Clock.Elapsed);
            Assert.False(fixture.Service.State.IsRunning);
            var buffs = Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs);
            Assert.Empty(buffs.Active);
            Assert.Equal(2, buffs.Consumptions.Count);
            Assert.Single(buffs.Consumptions, item => item.IsSessionStart);
            Assert.Equal(now.AddSeconds(-1), Assert.Single(buffs.Consumptions,
                item => !item.IsSessionStart).ConsumedAt);
            Assert.Equal(2_400_000m, buffs.ConsumedCost);
            Assert.Equal(2000m, buffs.ProratedCost);
            var saved = Assert.IsType<BuffLedgerSnapshot>(Assert.Single(fixture.HistoryStore.Load()).Buffs);
            Assert.Equal(buffs.ConsumedCost, saved.ConsumedCost);
            Assert.Equal(buffs.ProratedCost, saved.ProratedCost);
            Assert.Equal(buffs.Consumptions, new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
                CurrentSessionStore.FileName)).Load()!.Buffs!.Consumptions);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task PauseDoesNotDrainBuffEvidenceAfterVisibilityWasLost()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var visible = true;
        var monitor = new BuffMonitor(new SessionBuffReader(() =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read < 3) return BuffReading(read == 1 ? 60 : 59);
            entered.TrySetResult();
            release.Wait();
            return BuffReading(1200);
        }), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => visible);
        try
        {
            BeginBuffSession(fixture);
            var now = DateTimeOffset.UtcNow;
            await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
            await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
            using var frame = new Bitmap(2, 2);
            await fixture.Service.ProcessFrameAsync(frame,
                new CapturedFrameMetadata(3, now.AddSeconds(-1)) { CanObserveHud = true }, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            visible = false;

            Assert.True((await fixture.Service.PauseAsync().WaitAsync(TimeSpan.FromSeconds(3))).Succeeded);
            release.Set();
            await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Service.RefreshPendingState();

            Assert.True(Assert.Single(fixture.Service.State.Buffs!.Consumptions).IsSessionStart);
            Assert.Empty(fixture.Service.State.Buffs.Active);
            Assert.Equal(1000m, fixture.Service.State.Buffs.ProratedCost);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task UnresponsiveFinalBuffReadCannotBlockPauseOrApplyAfterward()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var monitor = new BuffMonitor(new SessionBuffReader(() =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read < 3) return BuffReading(read == 1 ? 60 : 59);
            entered.TrySetResult();
            release.Wait(); // Deliberately ignore cancellation, as a stuck OCR engine can.
            return BuffReading(1200);
        }), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        try
        {
            BeginBuffSession(fixture);
            var now = DateTimeOffset.UtcNow;
            await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
            await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
            using var frame = new Bitmap(2, 2);
            await fixture.Service.ProcessFrameAsync(frame,
                new CapturedFrameMetadata(3, now.AddSeconds(-1)) { CanObserveHud = true }, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var paused = await fixture.Service.PauseAsync().WaitAsync(BuffMonitor.AnalysisTimeout + TimeSpan.FromSeconds(3));
            Assert.True(paused.Succeeded);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.False(monitor.CurrentAnalysis.IsCompleted);
            release.Set();
            await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Service.RefreshPendingState();

            Assert.True(Assert.Single(fixture.Service.State.Buffs!.Consumptions).IsSessionStart);
            Assert.Empty(fixture.Service.State.Buffs.Active);
            Assert.True(Assert.Single(Assert.Single(fixture.HistoryStore.Load()).Buffs!.Consumptions).IsSessionStart);
        }
        finally { release.Set(); }
    }
}
