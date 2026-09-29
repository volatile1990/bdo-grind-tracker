using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class PassiveWindowCaptureTests
{
    [Theory]
    [InlineData(800, 631, 0, 31)] // DWM bounds omit the invisible resize border.
    [InlineData(816, 639, 8, 31)] // Full Win32 bounds include it.
    [InlineData(800, 600, 0, 0)] // A client-only capture surface.
    public void ClientCropRemovesTitleAndResizeBorders(int width, int height, int x, int y)
    {
        var geometry = new WindowCaptureGeometry(new Rectangle(-1200, 231, 800, 600),
            new Rectangle(-1208, 200, 816, 639), new Rectangle(-1200, 200, 800, 631), false);
        Assert.Equal(new Rectangle(x, y, 800, 600), geometry.ClientCrop(new Size(width, height)));
    }

    [Fact]
    public void MovingBetweenMonitorsDoesNotChangeClientCoordinates()
    {
        var first = Borderless(new Rectangle(-1920, 0, 1920, 1080));
        var moved = Borderless(new Rectangle(250, 140, 1920, 1080));
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), first.ClientCrop(first.ClientBounds.Size));
        Assert.Equal(first.ClientCrop(first.ClientBounds.Size), moved.ClientCrop(moved.ClientBounds.Size));
    }

    [Fact]
    public void MismatchedCaptureSurfaceFailsInsteadOfScalingOrCroppingDesktopPixels()
    {
        var geometry = Borderless(new Rectangle(100, 100, 800, 600));
        Assert.Throws<InvalidOperationException>(() => geometry.ClientCrop(new Size(1920, 1080)));
    }

    [Fact]
    public void ClientOutsideWindowFailsInsteadOfReturningPartialFrame()
    {
        var geometry = new WindowCaptureGeometry(new Rectangle(99, 100, 800, 600),
            new Rectangle(100, 100, 800, 600), new Rectangle(100, 100, 800, 600), false);
        Assert.Throws<InvalidOperationException>(() => geometry.ClientCrop(new Size(800, 600)));
    }

    [Fact]
    public void CaptureBindsWindowOnceAndKeepsItWhenWindowMoves()
    {
        var selections = 0;
        var geometry = Borderless(new Rectangle(100, 100, 800, 600));
        var source = new FakeSource();
        using var capture = new PassiveWindowCapture(() => { selections++; return new WindowCaptureTarget(123, 456, Size.Empty); },
            target => { Assert.Equal((nint)123, target.Handle); return geometry; }, (_, _) => source);
        var region = capture.PrepareCapture();
        Assert.Equal(new Rectangle(0, 0, 800, 600), region);
        geometry = Borderless(new Rectangle(-1000, 200, 800, 600));
        using var bitmap = capture.Capture(region, CancellationToken.None).Bitmap;
        Assert.Equal(1, selections);
        Assert.Equal(new Size(800, 600), bitmap.Size);
        Assert.False(source.Disposed);
        capture.StopCapture();
        Assert.True(source.Disposed);
    }

    [Fact]
    public void MinimumUpdateIntervalChangesReuseTheSourceAndSurviveRebinding()
    {
        var sources = new List<FakeSource>();
        var selections = 0;
        using var capture = new PassiveWindowCapture(
            () => { selections++; return new WindowCaptureTarget(123, 456, Size.Empty); },
            _ => Borderless(new Rectangle(100, 100, 8, 6)), (_, _) =>
            {
                var source = new FakeSource();
                sources.Add(source);
                return source;
            });

        capture.SetMinimumUpdateInterval(GraphicsCaptureRateLimiter.StandbyMinimumInterval);
        capture.SetMinimumUpdateInterval(GraphicsCaptureRateLimiter.StandbyMinimumInterval);
        Assert.Empty(sources);
        Assert.Equal(0, selections);
        var region = capture.PrepareCapture();
        using (capture.Capture(region, CancellationToken.None).Bitmap) { }
        var first = Assert.Single(sources);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(500) }, first.MinimumIntervals);

        capture.SetMinimumUpdateInterval(GraphicsCaptureRateLimiter.LiveMinimumInterval);
        capture.SetMinimumUpdateInterval(GraphicsCaptureRateLimiter.LiveMinimumInterval);
        using (capture.Capture(region, CancellationToken.None).Bitmap) { }
        capture.SetMinimumUpdateInterval(GraphicsCaptureRateLimiter.StandbyMinimumInterval);
        using (capture.Capture(region, CancellationToken.None).Bitmap) { }

        Assert.Single(sources);
        Assert.Equal(1, selections);
        Assert.False(first.Disposed);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(500) }, first.MinimumIntervals);
        Assert.Equal(first.MinimumIntervals, first.CapturedIntervals);

        capture.StopCapture();
        Assert.True(first.Disposed);
        region = capture.PrepareCapture();
        using (capture.Capture(region, CancellationToken.None).Bitmap) { }

        Assert.Equal(2, selections);
        Assert.Equal(2, sources.Count);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(500) }, sources[1].MinimumIntervals);
        Assert.Equal(sources[1].MinimumIntervals, sources[1].CapturedIntervals);
    }

    [Fact]
    public void CaptureUsesTheLiveIntervalByDefault()
    {
        var source = new FakeSource();
        using var capture = new PassiveWindowCapture(() => new(123, 456, Size.Empty),
            _ => Borderless(new Rectangle(100, 100, 8, 6)), (_, _) => source);

        var region = capture.PrepareCapture();
        using (capture.Capture(region, CancellationToken.None).Bitmap) { }
        capture.SetMinimumUpdateInterval(GraphicsCaptureRateLimiter.LiveMinimumInterval);
        using (capture.Capture(region, CancellationToken.None).Bitmap) { }

        Assert.Equal(new[] { TimeSpan.FromMilliseconds(100) }, source.MinimumIntervals);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100) },
            source.CapturedIntervals);
    }

    [Fact]
    public void ResizeFailsBeforeAFrameCanReachAnalyzer()
    {
        var geometry = Borderless(new Rectangle(100, 100, 800, 600));
        var creations = 0;
        using var capture = new PassiveWindowCapture(() => new WindowCaptureTarget(123, 456, Size.Empty),
            _ => geometry, (_, _) => { creations++; return new FakeSource(); });
        var region = capture.PrepareCapture();
        geometry = Borderless(new Rectangle(100, 100, 801, 600));
        Assert.Contains("Größe", Assert.Throws<InvalidOperationException>(() => capture.Capture(region, CancellationToken.None)).Message);
        Assert.Equal(0, creations);
    }

    [Theory]
    [InlineData("minimiert")]
    [InlineData("geschlossen")]
    public void UnavailableBoundWindowCannotSwitchToAnotherWindowOrUseOldFrame(string failure)
    {
        var valid = true;
        var selections = 0;
        var source = new FakeSource();
        using var capture = new PassiveWindowCapture(() => { selections++; return new WindowCaptureTarget(123, 456, Size.Empty); },
            _ => valid ? Borderless(new Rectangle(100, 100, 800, 600)) : throw new InvalidOperationException(failure),
            (_, _) => source);
        var region = capture.PrepareCapture();
        using (capture.Capture(region, CancellationToken.None).Bitmap) { }
        valid = false;
        Assert.Equal(failure, Assert.Throws<InvalidOperationException>(() => capture.Capture(region, CancellationToken.None)).Message);
        Assert.Equal(1, selections);
        Assert.Equal(1, source.Captures);
    }

    [Fact]
    public void FailedRebindCannotResumePreviouslyBoundWindow()
    {
        var selections = 0;
        using var capture = new PassiveWindowCapture(() => ++selections == 1 ? new WindowCaptureTarget(123, 456, Size.Empty)
            : throw new InvalidOperationException("Kein Spielfenster"),
            _ => Borderless(new Rectangle(100, 100, 800, 600)), (_, _) => new FakeSource());
        var region = capture.PrepareCapture();
        Assert.Throws<InvalidOperationException>(() => capture.PrepareCapture());
        Assert.Throws<InvalidOperationException>(() => capture.Capture(region, CancellationToken.None));
    }

    [Fact]
    public async Task WindowFramesAreHudEligibleWithoutForegroundAndStopReleasesCapture()
    {
        var source = new FakeSource();
        using var capture = new PassiveWindowCapture(() => new WindowCaptureTarget(123, 456, Size.Empty),
            _ => Borderless(new Rectangle(100, 100, 8, 6)), (_, _) => source);
        await using var session = new PassiveCaptureSession(capture, TimeSpan.FromMilliseconds(5));
        var region = session.ResolveCaptureRegion(new Rectangle(-1920, 0, 1920, 1080));
        Assert.True(session.UsesWindowCapture);
        Assert.Equal(new Rectangle(0, 0, 8, 6), region);
        var observed = new TaskCompletionSource<CapturedFrameMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StartCompanion(region, (_, metadata, _) => { observed.TrySetResult(metadata); return Task.CompletedTask; }, () => false);
        Assert.True((await observed.Task.WaitAsync(TimeSpan.FromSeconds(5))).CanObserveHud);
        await session.StopAsync();
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task CaptureCleanupFailureStillCompletesQueueAndReportsStop()
    {
        var source = new FakeSource { ThrowOnDispose = true };
        using var capture = new PassiveWindowCapture(() => new WindowCaptureTarget(123, 456, Size.Empty),
            _ => Borderless(new Rectangle(100, 100, 8, 6)), (_, _) => source);
        await using var session = new PassiveCaptureSession(capture, TimeSpan.FromMilliseconds(5));
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Stopped += (_, args) => stopped.TrySetResult(args.Error);
        session.StartCompanion(session.ResolveCaptureRegion(Rectangle.Empty), (_, _, _) => { observed.TrySetResult(); return Task.CompletedTask; });
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Dispose failed", (await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)))?.Message);
    }

    [Fact]
    public async Task NativeFrameTimestampExcludesGpuReadbackTimeWhileCaptureDurationIncludesIt()
    {
        var clock = new CaptureReadbackClock();
        await using var session = new PassiveCaptureSession(_ =>
        {
            clock.Timestamp = TimeSpan.FromMilliseconds(60).Ticks;
            return new CapturedDesktopBitmap(new Bitmap(2, 2), true, true)
            { AcquiredAtTimestamp = TimeSpan.FromMilliseconds(20).Ticks };
        }, clock);
        var observed = new TaskCompletionSource<CapturedFrameMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StartCompanion(new Rectangle(0, 0, 2, 2), (_, metadata, _) => { observed.TrySetResult(metadata); return Task.CompletedTask; });
        var frame = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync();
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(-40), frame.CapturedAtUtc);
        Assert.Equal(TimeSpan.FromMilliseconds(60), frame.CaptureDuration);
    }

    private static WindowCaptureGeometry Borderless(Rectangle bounds) => new(bounds, bounds, bounds, false);

    private sealed class CaptureReadbackClock : TimeProvider
    {
        internal long Timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeSource : IWindowFrameSource
    {
        internal bool Disposed { get; private set; }
        internal bool ThrowOnDispose { get; init; }
        internal int Captures { get; private set; }
        internal List<TimeSpan> MinimumIntervals { get; } = [];
        internal List<TimeSpan> CapturedIntervals { get; } = [];
        public void SetMinimumUpdateInterval(TimeSpan minimumInterval) => MinimumIntervals.Add(minimumInterval);
        public CapturedDesktopBitmap Capture(Func<WindowCaptureGeometry> readGeometry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captures++;
            CapturedIntervals.Add(MinimumIntervals[^1]);
            var geometry = readGeometry();
            return new CapturedDesktopBitmap(new Bitmap(geometry.ClientBounds.Width, geometry.ClientBounds.Height), geometry.IsHdr);
        }
        public void Dispose()
        {
            Disposed = true;
            if (ThrowOnDispose) throw new InvalidOperationException("Dispose failed");
        }
    }
}
