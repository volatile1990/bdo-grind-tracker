using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class CapturePreviewLifetimeTests
{
    [Fact]
    public async Task FailedWindowStopDisposesTheCapturedBitmapBeforeReportingTheError()
    {
        var source = new PreviewSource { DisposeError = new InvalidOperationException("Stop failed") };
        using var capture = WindowCapture(source);
        await using var session = new PassiveCaptureSession(capture);

        var error = Assert.Throws<InvalidOperationException>(() => session.CapturePreview(Rectangle.Empty, default));

        Assert.Same(source.DisposeError, error);
        Assert.Equal(1, source.Captures);
        Assert.Equal(1, source.Disposals);
        var bitmap = Assert.IsType<Bitmap>(source.Bitmap);
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.False(session.IsRunning);
        Assert.False(session.HasPendingAnalysis);
    }

    [Fact]
    public async Task SuccessfulWindowStopLeavesTheReturnedBitmapOwnedByTheCaller()
    {
        var source = new PreviewSource();
        using var capture = WindowCapture(source);
        await using var session = new PassiveCaptureSession(capture);

        using var bitmap = session.CapturePreview(new Rectangle(999, 999, 400, 300), default);

        Assert.Same(source.Bitmap, bitmap);
        Assert.Equal(new Size(16, 10), bitmap.Size);
        Assert.Equal(1, source.Captures);
        Assert.Equal(1, source.Disposals);
        Assert.Equal(Color.CornflowerBlue.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        bitmap.SetPixel(0, 0, Color.Magenta);
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.False(session.IsRunning);
        Assert.False(session.HasPendingAnalysis);
    }

    [Fact]
    public async Task CaptureFailureStillStopsAndDisposesTheWindowSource()
    {
        var source = new PreviewSource { CaptureError = new InvalidOperationException("Capture failed") };
        using var capture = WindowCapture(source);
        await using var session = new PassiveCaptureSession(capture);

        var error = Assert.Throws<InvalidOperationException>(() => session.CapturePreview(Rectangle.Empty, default));

        Assert.Same(source.CaptureError, error);
        Assert.Equal(1, source.Captures);
        Assert.Equal(1, source.Disposals);
        Assert.Null(source.Bitmap);
        Assert.False(session.IsRunning);
        Assert.False(session.HasPendingAnalysis);
    }

    [Fact]
    public async Task RunningGuardCannotRebindCaptureOrDisposeTheLiveWindowSource()
    {
        var source = new PreviewSource();
        var selections = 0;
        using var capture = WindowCapture(source, () => selections++);
        await using var session = new PassiveCaptureSession(capture, TimeSpan.FromHours(1));
        var analyzing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StartCompanion(session.ResolveCaptureRegion(Rectangle.Empty), async (_, _, _) =>
        {
            analyzing.TrySetResult();
            await release.Task;
        });
        try
        {
            await analyzing.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var error = Assert.Throws<InvalidOperationException>(() => session.CapturePreview(Rectangle.Empty, default));

            Assert.Contains("pausieren", error.Message);
            Assert.Equal(1, selections);
            Assert.Equal(1, source.Captures);
            Assert.Equal(0, source.Disposals);
            var bitmap = Assert.IsType<Bitmap>(source.Bitmap);
            Assert.Equal(Color.CornflowerBlue.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.True(session.IsRunning);
        }
        finally
        {
            release.TrySetResult();
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, source.Disposals);
    }

    private static PassiveWindowCapture WindowCapture(PreviewSource source, Action? onSelect = null) => new(
        () =>
        {
            onSelect?.Invoke();
            return new WindowCaptureTarget(123, 456, Size.Empty);
        },
        _ =>
        {
            var bounds = new Rectangle(-100, 250, 16, 10);
            return new WindowCaptureGeometry(bounds, bounds, bounds, false);
        }, (_, _) => source);

    private sealed class PreviewSource : IWindowFrameSource
    {
        internal Exception? CaptureError { get; init; }
        internal Exception? DisposeError { get; init; }
        internal Bitmap? Bitmap { get; private set; }
        internal int Captures { get; private set; }
        internal int Disposals { get; private set; }

        public CapturedDesktopBitmap Capture(Func<WindowCaptureGeometry> readGeometry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captures++;
            if (CaptureError is { } error) throw error;
            var geometry = readGeometry();
            Bitmap = new Bitmap(geometry.ClientBounds.Width, geometry.ClientBounds.Height);
            Bitmap.SetPixel(0, 0, Color.CornflowerBlue);
            return new(Bitmap, geometry.IsHdr);
        }

        public void Dispose()
        {
            Disposals++;
            if (DisposeError is { } error) throw error;
        }
    }
}
