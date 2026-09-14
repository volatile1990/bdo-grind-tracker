using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task RotationUsesCaptureEpochForElapsedTimeAndFreshness(int captureOffsetDays)
    {
        // The epoch retained by capture can differ from the corrected Windows
        // wall clock in either direction, even though capture remains continuous.
        var clock = new HudCaptureClock { UtcNow = DateTimeOffset.UtcNow.AddDays(captureOffsetDays) };
        var capture = new PassiveCaptureSession(_ => new Bitmap(2, 2), clock, TimeSpan.FromDays(1));
        await using var fixture = new Fixture(autoUpload: false, suppliedCapture: capture);
        var firstCapture = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.StartCompanion(new Rectangle(0, 0, 320, 200), (_, metadata, _) =>
        {
            firstCapture.TrySetResult(metadata.CapturedAtUtc);
            return Task.CompletedTask;
        });
        var epoch = await firstCapture.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await capture.StopAsync();
        clock.UtcNow = DateTimeOffset.UtcNow;

        var profile = new HermesiaRotationMonitor(recognize: _ => "Markthanan's patrol descends.");
        var rotation = new RotationMonitor(_ => profile);
        SetField(fixture.Service, "_rotationMonitor", rotation);
        fixture.Begin();
        using var frame = new Bitmap(320, 200);
        rotation.Observe(frame, epoch, LootSpotCatalog.HermesiaId);
        await profile.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
        for (var i = 1; i <= 3; i++) rotation.Observe(frame, epoch.AddSeconds(i), LootSpotCatalog.HermesiaId);
        await profile.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(3));
        fixture.Service.RefreshPendingState();
        Assert.True(fixture.Service.State.Rotation.Synchronized);
        Assert.Equal(3, fixture.Service.State.Rotation.Elapsed);

        clock.Advance(TimeSpan.FromSeconds(5));
        fixture.Service.RefreshPendingState();
        Assert.False(fixture.Service.State.Rotation.Synchronized);
        Assert.Contains("Bildsignal unterbrochen", fixture.Service.State.Rotation.Status);
    }
}
