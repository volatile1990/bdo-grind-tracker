using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindStandbyCaptureTests
{
    [Fact]
    public void BackgroundGameDoesNotLocateWindowOrCreateNativeCapture()
    {
        using var fixture = new Fixture();
        fixture.Foreground.IsForeground = false;

        Assert.False(fixture.Capture.IsGameForeground);
        Assert.Throws<InvalidOperationException>(() => fixture.Capture.Capture(CancellationToken.None));

        Assert.Equal(0, fixture.Selections);
        Assert.Empty(fixture.Sources);
    }

    [Fact]
    public void BackgroundCheckReleasesBurstEvenBeforeForegroundNotificationArrives()
    {
        using var fixture = new Fixture();
        fixture.Capture.SetBurst(true);
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }
        fixture.Foreground.IsForeground = false;

        Assert.Throws<InvalidOperationException>(() => fixture.Capture.Capture(CancellationToken.None));

        Assert.True(Assert.Single(fixture.Sources).Disposed);
        Assert.Equal(1, fixture.Selections);
    }

    [Fact]
    public async Task NativeForegroundHookCanBeRegisteredAndReleasedOnItsMessageThread()
    {
        await Task.Run(() =>
        {
            using var monitor = new NativeGameForegroundMonitor();
            monitor.Dispose();
            Assert.False(monitor.IsGameForeground);
            monitor.Dispose();
        }).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EveryStandbySampleReleasesNativeCaptureAndPreservesPixelMetadata()
    {
        using var fixture = new Fixture();

        var captured = fixture.Capture.Capture(CancellationToken.None);
        using (captured.Bitmap)
        {
            Assert.True(captured.IsHdr);
            Assert.True(captured.IsToneMapped);
            Assert.Equal(12345L, captured.AcquiredAtTimestamp);
            Assert.Equal(new Size(4, 3), captured.Bitmap.Size);
            Assert.True(Assert.Single(fixture.Sources).Disposed);
        }
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }

        Assert.Equal(2, fixture.Selections);
        Assert.Equal(2, fixture.Sources.Count);
        Assert.All(fixture.Sources, source => Assert.True(source.Disposed));
    }

    [Fact]
    public void ConfirmationBurstReusesNativeSourceAndReturningToStandbyReleasesIt()
    {
        using var fixture = new Fixture();
        fixture.Capture.SetBurst(true);

        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }

        Assert.Equal(1, fixture.Selections);
        var source = Assert.Single(fixture.Sources);
        Assert.Equal(2, source.Captures);
        Assert.False(source.Disposed);

        fixture.Capture.SetBurst(false);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void ForegroundNotificationReleasesIdleBurstAndNextSampleRebinds()
    {
        using var fixture = new Fixture();
        fixture.Capture.SetBurst(true);
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }

        fixture.Foreground.SetForeground(false);

        Assert.True(Assert.Single(fixture.Sources).Disposed);
        Assert.False(fixture.Capture.IsGameForeground);
        Assert.Throws<InvalidOperationException>(() => fixture.Capture.Capture(CancellationToken.None));
        Assert.Single(fixture.Sources);

        fixture.Foreground.SetForeground(true);
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }
        Assert.Equal(2, fixture.Selections);
        Assert.All(fixture.Sources, source => Assert.True(source.Disposed));
    }

    [Fact]
    public async Task FocusLossCancelsPendingCaptureWithoutWaitingForAnotherStandbyTick()
    {
        using var fixture = new Fixture();
        using var started = new ManualResetEventSlim();
        fixture.OnCapture = token =>
        {
            started.Set();
            if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Foreground notification did not cancel capture.");
            token.ThrowIfCancellationRequested();
        };
        fixture.Capture.SetBurst(true);
        var pending = Task.Run(() => fixture.Capture.Capture(CancellationToken.None));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        fixture.Foreground.SetForeground(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.True(Assert.Single(fixture.Sources).Disposed);
    }

    [Fact]
    public void FocusChangeDuringReadbackCannotReturnFrameEvenIfSourceIgnoresCancellation()
    {
        using var fixture = new Fixture();
        fixture.OnCapture = _ => fixture.Foreground.IsForeground = false;

        Assert.Throws<InvalidOperationException>(() => fixture.Capture.Capture(CancellationToken.None));

        var source = Assert.Single(fixture.Sources);
        Assert.True(source.Disposed);
        Assert.NotNull(source.LastBitmap);
        Assert.Throws<ArgumentException>(() => _ = source.LastBitmap.Width);
    }

    [Fact]
    public void CaptureFailureReleasesBurstBeforeRetrying()
    {
        using var fixture = new Fixture();
        fixture.Capture.SetBurst(true);
        fixture.OnCapture = _ => throw new InvalidOperationException("Native acquisition failed.");

        Assert.Throws<InvalidOperationException>(() => fixture.Capture.Capture(CancellationToken.None));
        Assert.True(Assert.Single(fixture.Sources).Disposed);

        fixture.OnCapture = null;
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }
        Assert.Equal(2, fixture.Selections);
    }

    [Fact]
    public void SuspendReleasesBurstAndCancelledCallDoesNotCreateSource()
    {
        using var fixture = new Fixture();
        fixture.Capture.SetBurst(true);
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }

        fixture.Capture.Suspend();
        Assert.True(Assert.Single(fixture.Sources).Disposed);
        Assert.Throws<OperationCanceledException>(() => fixture.Capture.Capture(new CancellationToken(true)));
        Assert.Single(fixture.Sources);
    }

    [Fact]
    public void DisposeDetachesForegroundEventsAndReleasesCaptureAndMonitor()
    {
        using var fixture = new Fixture();
        fixture.Capture.SetBurst(true);
        using (fixture.Capture.Capture(CancellationToken.None).Bitmap) { }

        fixture.Capture.Dispose();
        fixture.Capture.Dispose();

        Assert.True(fixture.Foreground.Disposed);
        Assert.Equal(0, fixture.Foreground.Subscribers);
        Assert.True(Assert.Single(fixture.Sources).Disposed);
        Assert.False(fixture.Capture.IsGameForeground);
        Assert.Throws<ObjectDisposedException>(() => fixture.Capture.Capture(CancellationToken.None));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeForegroundMonitor Foreground = new();
        internal readonly List<FakeSource> Sources = [];
        internal readonly GrindStandbyCapture Capture;
        internal int Selections;
        internal Action<CancellationToken>? OnCapture;

        internal Fixture()
        {
            var bounds = new Rectangle(0, 0, 4, 3);
            var window = new PassiveWindowCapture(
                () => { Selections++; return new WindowCaptureTarget(123, 456, Size.Empty); },
                _ => new WindowCaptureGeometry(bounds, bounds, bounds, true),
                (_, _) =>
                {
                    var source = new FakeSource(token => OnCapture?.Invoke(token));
                    Sources.Add(source);
                    return source;
                });
            Capture = new GrindStandbyCapture(window, Foreground);
        }

        public void Dispose() => Capture.Dispose();
    }

    private sealed class FakeForegroundMonitor : IGameForegroundMonitor
    {
        private EventHandler? _changed;
        internal bool IsForeground = true;
        internal bool Disposed;
        internal int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public bool IsGameForeground => IsForeground;
        public event EventHandler? Changed { add => _changed += value; remove => _changed -= value; }
        internal void SetForeground(bool foreground)
        {
            IsForeground = foreground;
            _changed?.Invoke(this, EventArgs.Empty);
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSource(Action<CancellationToken> onCapture) : IWindowFrameSource
    {
        internal bool Disposed;
        internal int Captures;
        internal Bitmap? LastBitmap;
        public CapturedDesktopBitmap Capture(Func<WindowCaptureGeometry> readGeometry, CancellationToken cancellationToken)
        {
            Captures++;
            onCapture(cancellationToken);
            var geometry = readGeometry();
            LastBitmap = new Bitmap(geometry.ClientBounds.Width, geometry.ClientBounds.Height);
            return new CapturedDesktopBitmap(LastBitmap, geometry.IsHdr, IsToneMapped: true)
            { AcquiredAtTimestamp = 12345 };
        }
        public void Dispose() => Disposed = true;
    }
}
