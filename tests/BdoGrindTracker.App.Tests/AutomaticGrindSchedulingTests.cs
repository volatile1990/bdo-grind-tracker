using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

[Collection("Timing-sensitive integration")]
public sealed class AutomaticGrindSchedulingTests
{
    [Fact]
    public async Task SampleAcquiresPriorityBeforeCaptureAndReleasesItAfterCompletion()
    {
        using var fixture = new Fixture();

        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));

        Assert.Equal(1, fixture.Acquired);
        Assert.Equal(1, fixture.Released);
        AssertDisposed(Assert.Single(fixture.Capture.Bitmaps));
        fixture.Monitor.Dispose();
        Assert.Equal(1, fixture.Released);
    }

    [Fact]
    public async Task BackgroundAndTooFrequentChecksDoNotAcquirePriority()
    {
        using var fixture = new Fixture();
        fixture.Capture.Foreground = false;
        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
        Assert.Equal(0, fixture.Acquired);

        fixture.Capture.Foreground = true;
        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
        Assert.Equal(1, fixture.Acquired);
        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
        Assert.Equal(1, fixture.Acquired);
        Assert.Single(fixture.Capture.Bitmaps);
    }

    [Fact]
    public async Task ConfirmationKeepsPriorityUntilTheAnalyzerAndSampleCleanupFinish()
    {
        using var fixture = new Fixture();
        fixture.Visual.Candidate = true;
        fixture.Analyze = (_, at, _) => Task.FromResult(new FrameAnalysisResult([], [], 1, "test", 0, 0, 0, 0, null)
        {
            LootProjection = new LootTotalsProjection(1,
                new Dictionary<string, long> { ["Chilled Soul Piece"] = 5 }, 1, at)
        });

        using var detection = await fixture.Monitor.CheckAsync("en", CancellationToken.None);

        Assert.NotNull(detection);
        Assert.True(Assert.Single(fixture.Analyzers).Disposed);
        Assert.Equal(1, fixture.Acquired);
        Assert.Equal(1, fixture.Released);
        Assert.Single(detection.Frames).Bitmap.GetPixel(0, 0);
    }

    [Fact]
    public async Task FailedAcquisitionReleasesPriority()
    {
        using var fixture = new Fixture();
        fixture.Capture.Failure = new IOException("Capture failed");

        await Assert.ThrowsAsync<IOException>(() => fixture.Monitor.CheckAsync("en", CancellationToken.None));

        Assert.Equal(1, fixture.Acquired);
        Assert.Equal(1, fixture.Released);
        Assert.True(fixture.Monitor.PendingAnalysis.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedBannerRetainsPriorityUntilPendingNativeInputsAreReleased(bool cancelProbe)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(2560, 1440, (_, _) =>
        {
            entered.Set();
            release.Wait();
            return "";
        }, cancelProbe ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1));
        var check = fixture.Monitor.CheckAsync("en", cancellation.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            if (cancelProbe)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.WaitAsync(TimeSpan.FromSeconds(3)));
            }
            else
            {
                await Assert.ThrowsAsync<TimeoutException>(() => check.WaitAsync(TimeSpan.FromSeconds(3)));
            }

            Assert.False(fixture.Monitor.PendingAnalysis.IsCompleted);
            Assert.Equal(1, fixture.Acquired);
            Assert.Equal(0, fixture.Released);
            Assert.Single(fixture.Capture.Bitmaps).GetPixel(0, 0);
            fixture.Time.Advance(TimeSpan.FromSeconds(15));
            Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
            Assert.Equal(1, fixture.Acquired);
            fixture.Monitor.Dispose();
            Assert.Equal(0, fixture.Released);
        }
        finally
        {
            release.Set();
            await fixture.Monitor.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
        }

        await fixture.PriorityReleased.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, fixture.Released);
        AssertDisposed(Assert.Single(fixture.Capture.Bitmaps));
    }

    private static void AssertDisposed(Bitmap bitmap) =>
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeCapture Capture = new();
        internal readonly FakeVisual Visual = new();
        internal readonly ManualClock Time = new();
        internal readonly List<FakeAnalyzer> Analyzers = [];
        internal readonly TaskCompletionSource PriorityReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Acquired, Released;
        internal Func<Bitmap, DateTimeOffset, CancellationToken, Task<FrameAnalysisResult>> Analyze =
            (_, _, _) => Task.FromResult(new FrameAnalysisResult([], [], 1, "test", 0, 0, 0, 0, null));
        internal readonly AutomaticGrindMonitor Monitor;

        internal Fixture(int width = 2, int height = 2,
            Func<Bitmap, CancellationToken, string>? bannerReader = null, TimeSpan? bannerTimeout = null)
        {
            Capture.FrameSize = new(width, height);
            Capture.BeforeCapture = AssertPriorityHeld;
            Monitor = new AutomaticGrindMonitor(Capture, Visual,
                new CompanionCalibration("", "", "", 1, 1, width, height, 1,
                    CompanionFontType.StrongSword, 0, false),
                _ =>
                {
                    AssertPriorityHeld();
                    var analyzer = new FakeAnalyzer((frame, at, token) =>
                    {
                        AssertPriorityHeld();
                        return Analyze(frame, at, token);
                    }, AssertPriorityHeld);
                    Analyzers.Add(analyzer);
                    return analyzer;
                }, Time, new RotationStartWatcher(_ => "", bannerReader), bannerTimeout)
            {
                PreserveCapturePriority = () =>
                {
                    Interlocked.Increment(ref Acquired);
                    return new Lease(() =>
                    {
                        Interlocked.Increment(ref Released);
                        PriorityReleased.TrySetResult();
                    });
                }
            };
        }

        private void AssertPriorityHeld() => Assert.Equal(1, Volatile.Read(ref Acquired) - Volatile.Read(ref Released));
        public void Dispose() => Monitor.Dispose();
    }

    private sealed class FakeCapture : IGrindStandbyCapture
    {
        internal bool Foreground = true;
        internal Size FrameSize;
        internal Action BeforeCapture = () => { };
        internal Exception? Failure;
        internal readonly List<Bitmap> Bitmaps = [];
        public bool IsGameForeground => Foreground;
        public CapturedDesktopBitmap Capture(CancellationToken cancellationToken)
        {
            BeforeCapture();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is { } error) throw error;
            var bitmap = new Bitmap(FrameSize.Width, FrameSize.Height);
            Bitmaps.Add(bitmap);
            return new(bitmap, false);
        }
        public void SetBurst(bool enabled) { }
        public void Suspend() { }
        public void Dispose() { }
    }

    private sealed class FakeVisual : IGrindStartVisualDetector
    {
        internal bool Candidate;
        public bool Observe(Bitmap frame, CompanionCalibration calibration) => Candidate;
        public void Reset() { }
    }

    private sealed class FakeAnalyzer(
        Func<Bitmap, DateTimeOffset, CancellationToken, Task<FrameAnalysisResult>> analyze,
        Action beforeDispose) : ILootFrameAnalyzer
    {
        internal bool Disposed;
        public bool IsAvailable => true;
        public string Status => "Ready";
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset at, CancellationToken token) =>
            analyze(frame, at, token);
        public void Reset() { }
        public void Dispose() { beforeDispose(); Disposed = true; }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
