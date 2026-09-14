using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationFlushTests
{
    [Fact]
    public async Task FinalBufferedFramesConfirmAfkEndBeforePause()
    {
        using var frames = new RotationFlushFrames(DateTimeOffset.UnixEpoch);
        using var monitor = new RotationMonitor(_ => frames.Profile);
        monitor.Snapshot(frames.Epoch, LootSpotCatalog.HermesiaId);
        await frames.FeedThroughAsync(71);
        Assert.Empty(monitor.ExportSession()); // Regular probes ran at 69, next due at 72.

        await monitor.FlushAsync();
        monitor.Interrupt();
        Assert.Equal(70, Assert.Single(monitor.ExportSession()).Run.Duration);
        Assert.False(monitor.Snapshot(frames.Epoch.AddSeconds(71), LootSpotCatalog.HermesiaId).Synchronized);
    }

    [Fact]
    public async Task PauseWaitsForAnInFlightAfkEndConfirmation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var frames = new RotationFlushFrames(DateTimeOffset.UnixEpoch, code =>
        {
            if (code != 8) return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Test did not release OCR.");
        });
        using var monitor = new RotationMonitor(_ => frames.Profile);
        monitor.Snapshot(frames.Epoch, LootSpotCatalog.HermesiaId);
        try
        {
            await frames.FeedThroughAsync(69);
            for (var time = 69.5; time <= 72; time += .5) frames.Observe(time);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var flush = monitor.FlushAsync();
            Assert.False(flush.IsCompleted);
            // A UI refresh while stopping must not expire the pending run.
            Assert.True(monitor.Snapshot(frames.Epoch.AddSeconds(80), LootSpotCatalog.HermesiaId).Synchronized);
            release.Set();
            await flush.WaitAsync(TimeSpan.FromSeconds(30));
            monitor.Interrupt();
            Assert.Equal(70, Assert.Single(monitor.ExportSession()).Run.Duration);
        }
        finally { release.Set(); await frames.Profile.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30)); }
    }

    [Fact]
    public async Task FlushTimeoutAllowsPauseAndDiscardsLateResultsSafely()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var frames = new RotationFlushFrames(DateTimeOffset.UnixEpoch, code =>
        {
            if (code != 8) return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Test did not release OCR.");
        });
        using var monitor = new RotationMonitor(_ => frames.Profile);
        monitor.Snapshot(frames.Epoch, LootSpotCatalog.HermesiaId);
        try
        {
            await frames.FeedThroughAsync(69);
            for (var time = 69.5; time <= 72; time += .5) frames.Observe(time);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await monitor.FlushAsync(TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(30));
            monitor.Interrupt();
            Assert.False(frames.Profile.PendingAnalysis.IsCompleted);
            release.Set();
            await frames.Profile.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Empty(monitor.ExportSession());
            Assert.False(monitor.Snapshot(frames.Epoch.AddSeconds(72), LootSpotCatalog.HermesiaId).Synchronized);
        }
        finally { release.Set(); await frames.Profile.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30)); }
    }
}

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task PausePersistsRotationConfirmedByFinalBufferedFrames()
    {
        var clock = new HudCaptureClock();
        var capture = new PassiveCaptureSession(_ => new Bitmap(2, 2), clock, TimeSpan.FromDays(1));
        await using var fixture = new Fixture(autoUpload: false, suppliedCapture: capture);
        using var frames = new RotationFlushFrames(clock.UtcNow);
        var monitor = new RotationMonitor(_ => frames.Profile);
        SetField(fixture.Service, "_rotationMonitor", monitor);
        fixture.Begin();
        await frames.FeedThroughAsync(71);
        clock.UtcNow = frames.Epoch.AddSeconds(71);
        Assert.Empty(monitor.ExportSession());

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.False(fixture.Service.State.IsRunning);
        var saved = Assert.Single(Assert.Single(fixture.HistoryStore.Load()).Rotations);
        Assert.Equal(LootSpotCatalog.HermesiaId, saved.SpotId);
        Assert.Equal(70, saved.Run.Duration);
    }
}

internal sealed class RotationFlushFrames : IDisposable
{
    private static readonly string[] Messages = ["", "porters gather to offer", "who dares interferes with our work",
        "f father", "authority over two mines transferred", "quarry management authority confirmed",
        "patrol descends", "begins absorbing nearby black crystals", "work in the mine is suspended"];
    private readonly Bitmap _frame = new(320, 200);
    internal DateTimeOffset Epoch { get; }
    internal HermesiaRotationMonitor Profile { get; }

    internal RotationFlushFrames(DateTimeOffset epoch, Action<byte>? beforeRead = null)
    {
        Epoch = epoch;
        Profile = new(recognize: bitmap =>
        {
            var code = bitmap.GetPixel(0, 0).R;
            beforeRead?.Invoke(code);
            return Messages[code];
        });
    }

    internal void Observe(double seconds)
    {
        var eventIndex = (int)(seconds / 10);
        var code = seconds % 10 < 6.5 && eventIndex < 8 ? eventIndex + 1 : 0;
        // Encode the synthetic OCR result in the first pixel of the real crop.
        _frame.SetPixel(80, 108, Color.FromArgb(code, 0, 0));
        Profile.Observe(_frame, Epoch.AddSeconds(seconds));
    }

    internal async Task FeedThroughAsync(double seconds)
    {
        for (var time = 0d; time <= seconds; time += .5)
        {
            Observe(time);
            await Profile.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    public void Dispose() { Profile.Dispose(); _frame.Dispose(); }
}
