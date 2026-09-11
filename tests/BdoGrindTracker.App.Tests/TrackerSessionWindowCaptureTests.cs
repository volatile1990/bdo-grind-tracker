using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task WindowCaptureUsesClientCalibrationAndCanResumeAfterMovingToAnotherMonitor()
    {
        var geometry = new WindowCaptureGeometry(new Rectangle(-1000, 100, 8, 6),
            new Rectangle(-1000, 100, 8, 6), new Rectangle(-1000, 100, 8, 6), false);
        var sources = new List<ServiceWindowSource>();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validations = new List<Size>();
        var foregroundChecks = 0;
        using var window = new PassiveWindowCapture(() => new(123, 456, Size.Empty), _ => geometry,
            (_, _) => { var source = new ServiceWindowSource(); sources.Add(source); return source; });
        var capture = new PassiveCaptureSession(window, TimeSpan.FromDays(1));
        var analyzer = new SyntheticAnalyzer
        {
            ValidateSetup = size => validations.Add(size),
            Analyze = () => { observed.TrySetResult(); return Task.FromResult(Analysis()); },
        };
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer, suppliedCapture: capture,
            lootScrollVisible: _ => { foregroundChecks++; return false; });
        Assert.True(fixture.Service.CapturesGameWindow);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(new Size(8, 6), Assert.Single(validations));
        Assert.True(Assert.Single(sources).Disposed);

        geometry = new(new Rectangle(2200, 100, 8, 6), new Rectangle(2200, 100, 8, 6),
            new Rectangle(2200, 100, 8, 6), false);
        observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(2, validations.Count);
        Assert.All(validations, size => Assert.Equal(new Size(8, 6), size));
        Assert.All(sources, source => Assert.True(source.Disposed));
        Assert.Equal(0, foregroundChecks);
    }

    private sealed class ServiceWindowSource : IWindowFrameSource
    {
        public bool Disposed { get; private set; }
        public CapturedDesktopBitmap Capture(Func<WindowCaptureGeometry> geometry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(new Bitmap(geometry().ClientBounds.Width, geometry().ClientBounds.Height), false, false);
        }
        public void Dispose() => Disposed = true;
    }
}
