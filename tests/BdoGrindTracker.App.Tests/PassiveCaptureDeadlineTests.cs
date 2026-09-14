using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

[Collection("Timing-sensitive integration")]
public sealed class PassiveCaptureDeadlineTests
{
    [Fact]
    public async Task SynchronousStuckCallbackAndSlowCancellationCannotBlockStopOrReleaseItsBitmap()
    {
        using var releaseWorker = new ManualResetEventSlim();
        using var releaseCancellation = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Bitmap? bitmap = null;
        await using var capture = new PassiveCaptureSession(_ => bitmap = new Bitmap(2, 2),
            frameInterval: TimeSpan.FromDays(1), analysisTimeout: TimeSpan.FromMilliseconds(500));
        capture.StartCompanion(new(0, 0, 2, 2), (frame, _, token) =>
        {
            using var registration = token.Register(() =>
            {
                cancellationEntered.TrySetResult();
                releaseCancellation.Wait(TimeSpan.FromSeconds(5));
            });
            entered.TrySetResult();
            releaseWorker.Wait(TimeSpan.FromSeconds(5));
            frame.GetPixel(0, 0);
            return Task.CompletedTask;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await capture.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(capture.HasPendingAnalysis);
            Assert.True(capture.AnalysisFailed);
            bitmap!.GetPixel(0, 0);
            Assert.Throws<InvalidOperationException>(() => capture.StartCompanion(new(0, 0, 2, 2),
                (_, _, _) => Task.CompletedTask));
        }
        finally { releaseCancellation.Set(); releaseWorker.Set(); }
        await capture.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Throws<ArgumentException>(() => bitmap!.GetPixel(0, 0));
    }
}
