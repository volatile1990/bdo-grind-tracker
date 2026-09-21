using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticGrindMonitorTests
{
    [Fact]
    public async Task BackgroundGameResetsVisualBaselineWithoutCaptureOrOcr()
    {
        using var fixture = new Fixture();
        fixture.Capture.Foreground = false;

        Assert.Null(await fixture.Monitor.CheckAsync("de", CancellationToken.None));

        Assert.Equal(0, fixture.Capture.Captures);
        Assert.Equal(0, fixture.Visual.Observations);
        Assert.Equal(1, fixture.Visual.Resets);
        Assert.Equal(1, fixture.Capture.Suspensions);
        Assert.Empty(fixture.Analyzers);
    }

    [Fact]
    public async Task ChangedWindowSizeReportsCalibrationMismatchWithoutStartingOcr()
    {
        using var fixture = new Fixture();
        fixture.Capture.FrameSize = new Size(3, 2);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Monitor.CheckAsync("de", CancellationToken.None));

        Assert.Contains("Spielfenstergröße", error.Message);
        Assert.Empty(fixture.Analyzers);
        Assert.Equal(0, fixture.Visual.Observations);
        Assert.All(fixture.Capture.Bitmaps, AssertDisposed);
    }

    [Fact]
    public async Task NegativeVisualSampleUsesNoAnalyzerAndSamplingIsLimitedToOncePerSecond()
    {
        using var fixture = new Fixture();
        fixture.Visual.Candidate = false;

        Assert.Null(await fixture.Monitor.CheckAsync("de", CancellationToken.None));
        Assert.Null(await fixture.Monitor.CheckAsync("de", CancellationToken.None));
        fixture.Time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Null(await fixture.Monitor.CheckAsync("de", CancellationToken.None));
        Assert.Equal(1, fixture.Capture.Captures);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(await fixture.Monitor.CheckAsync("de", CancellationToken.None));

        Assert.Equal(2, fixture.Capture.Captures);
        Assert.Empty(fixture.Analyzers);
        Assert.Empty(fixture.Capture.BurstChanges);
        Assert.All(fixture.Capture.Bitmaps, AssertDisposed);
    }

    [Fact]
    public async Task BurstReturnsOnFirstNewTrashArrivalAndKeepsItsTimestampAndHdrReplayMetadata()
    {
        using var fixture = new Fixture();
        fixture.Behavior = (call, _, at, _) => call == 1
            ? Task.FromResult(Result(Projection(5, 1, at.AddMilliseconds(-50))))
            : throw new InvalidOperationException("A second new arrival must not be required.");

        using var detection = await fixture.Monitor.CheckAsync("de", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(detection);
        Assert.Equal(1, fixture.Capture.Captures);
        var analyzer = Assert.Single(fixture.Analyzers);
        Assert.Equal(1, analyzer.Calls);
        Assert.Equal("de", analyzer.Language);
        Assert.Equal(new Size(2, 2), analyzer.ValidatedSize);
        Assert.False(analyzer.UseHdrOcr);
        Assert.True(analyzer.IsToneMapped);
        Assert.True(analyzer.Disposed);
        Assert.Equal(new[] { true, false }, fixture.Capture.BurstChanges);
        Assert.False(fixture.Monitor.IsConfirming);
        Assert.Single(detection.Frames);
        Assert.Equal(detection.Frames[^1].Metadata.CapturedAtUtc.AddMilliseconds(-50), detection.DetectedDropAt);
        Assert.All(detection.Frames, frame =>
        {
            Assert.True(frame.Metadata.IsHdr);
            Assert.True(frame.Metadata.IsToneMapped);
            Assert.True(frame.Metadata.CanObserveHud);
            frame.Bitmap.GetPixel(0, 0);
        });
        Assert.Equal(fixture.Capture.Bitmaps, detection.Frames.Select(frame => frame.Bitmap));
    }

    [Fact]
    public async Task ChangedVisualRowCanBeRecognizedLaterWithoutASecondArrival()
    {
        using var fixture = new Fixture();
        var initialAt = fixture.Time.GetUtcNow();
        fixture.Behavior = (call, _, _, _) => Task.FromResult(Result(
            Projection(call == 1 ? 0 : 5, call == 1 ? 0 : 1, initialAt.AddMilliseconds(-50))));

        using var detection = await fixture.Monitor.CheckAsync("en", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(detection);
        Assert.Equal(2, Assert.Single(fixture.Analyzers).Calls);
        Assert.Equal(initialAt.AddMilliseconds(-50), detection.DetectedDropAt);
        Assert.True(detection.DetectedDropAt < detection.Frames[^1].Metadata.CapturedAtUtc);
    }

    [Fact]
    public async Task CorrectedTrashAndAnotherArrivalCannotTriggerUntilACleanMonsterDrop()
    {
        using var fixture = new Fixture();
        var initialAt = fixture.Time.GetUtcNow();
        fixture.Behavior = (call, _, at, _) => Task.FromResult(Result(call switch
        {
            1 => Projection(0, 0, initialAt) with { QuantityCorrectionRevision = 0 },
            2 or 3 => Projection(20, 5, initialAt) with { QuantityCorrectionRevision = 1 },
            _ => Projection(25, 6, at) with { QuantityCorrectionRevision = 1 },
        }));

        using var detection = await fixture.Monitor.CheckAsync("en", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(detection);
        Assert.Equal(4, Assert.Single(fixture.Analyzers).Calls);
        Assert.Equal(detection.Frames[^1].Metadata.CapturedAtUtc, detection.DetectedDropAt);
        AssertDisposed(fixture.Capture.Bitmaps[0]);
    }

    [Fact]
    public void DetachTransfersDetectedArrivalWithReplayOwnership()
    {
        var at = DateTimeOffset.UnixEpoch.AddSeconds(7);
        using var original = new AutoStartDetection { DetectedDropAt = at };
        var bitmap = new Bitmap(2, 2);
        original.Add(new CapturedDesktopBitmap(bitmap, IsHdr: false, IsToneMapped: false), at.AddSeconds(1));

        using var detached = original.Detach();

        Assert.Equal(at, detached.DetectedDropAt);
        Assert.Same(bitmap, Assert.Single(detached.Frames).Bitmap);
        Assert.Null(original.DetectedDropAt);
        Assert.Empty(original.Frames);
    }

    [Theory]
    [InlineData("Silver")]
    [InlineData("Black Stone")]
    [InlineData("[Event] Mysterious Ore")]
    public async Task NonMonsterDropsCannotConfirmABurst(string item)
    {
        using var fixture = new Fixture();
        fixture.Behavior = (call, _, at, _) => Task.FromResult(Result(Projection(call, call, at, item)));

        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.True(fixture.Time.GetTimestamp() >= AutomaticGrindMonitor.BurstDuration.Ticks);
        Assert.True(Assert.Single(fixture.Analyzers).Disposed);
        Assert.False(fixture.Monitor.IsConfirming);
        Assert.All(fixture.Capture.Bitmaps, AssertDisposed);
    }

    [Fact]
    public async Task FalseCandidateCooldownPreventsRepeatedOcrBursts()
    {
        using var fixture = new Fixture();
        fixture.Behavior = (_, _, _, _) =>
        {
            fixture.Time.Advance(AutomaticGrindMonitor.BurstDuration);
            return Task.FromResult(Result());
        };

        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
        Assert.Single(fixture.Analyzers);
        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
        fixture.Time.Advance(AutomaticGrindMonitor.BurstCooldown - TimeSpan.FromSeconds(1));
        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
        Assert.Single(fixture.Analyzers);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));

        Assert.Equal(2, fixture.Analyzers.Count);
        Assert.Equal(4, fixture.Capture.Captures);
        Assert.All(fixture.Analyzers, analyzer => Assert.True(analyzer.Disposed));
    }

    [Fact]
    public async Task IgnoredOcrCancellationRetainsInputsAndPreventsAnotherProbeUntilCleanupFinishes()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<FrameAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Behavior = (_, _, _, _) => { entered.TrySetResult(); return release.Task; };
        var check = fixture.Monitor.CheckAsync("en", cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await check);

            Assert.False(fixture.Monitor.PendingAnalysis.IsCompleted);
            Assert.False(Assert.Single(fixture.Analyzers).Disposed);
            Assert.Single(fixture.Capture.Bitmaps).GetPixel(0, 0);
            fixture.Time.Advance(TimeSpan.FromMinutes(1));
            Assert.Null(await fixture.Monitor.CheckAsync("en", CancellationToken.None));
            Assert.Equal(1, fixture.Capture.Captures);
            Assert.Single(fixture.Analyzers);

            fixture.Monitor.Dispose();
            Assert.True(fixture.Capture.Disposed);
            Assert.False(fixture.Analyzers[0].Disposed);
            fixture.Capture.Bitmaps[0].GetPixel(0, 0);
        }
        finally
        {
            release.TrySetResult(Result());
            await fixture.Monitor.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(fixture.Analyzers[0].Disposed);
        AssertDisposed(fixture.Capture.Bitmaps[0]);
    }

    private static void AssertDisposed(Bitmap bitmap) =>
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));

    private static FrameAnalysisResult Result(LootTotalsProjection? projection = null) =>
        new([], [], 1, "test", 0, 0, 0, 0, null) { LootProjection = projection };

    private static LootTotalsProjection Projection(long quantity, int drops, DateTimeOffset at,
        string item = "Chilled Soul Piece") => new(drops, new Dictionary<string, long> { [item] = quantity }, drops, at);

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeCapture Capture = new();
        internal readonly FakeVisual Visual = new();
        internal readonly AdvancingClock Time = new();
        internal readonly List<FakeAnalyzer> Analyzers = [];
        internal Func<int, Bitmap, DateTimeOffset, CancellationToken, Task<FrameAnalysisResult>> Behavior =
            (_, _, _, _) => Task.FromResult(Result());
        internal readonly AutomaticGrindMonitor Monitor;

        internal Fixture()
        {
            Monitor = new AutomaticGrindMonitor(Capture, Visual,
                new CompanionCalibration("", "", "", 1, 1, 2, 2, 1,
                    CompanionFontType.StrongSword, 0, false),
                _ =>
                {
                    var analyzer = new FakeAnalyzer((call, frame, at, token) => Behavior(call, frame, at, token));
                    Analyzers.Add(analyzer);
                    return analyzer;
                }, Time);
        }

        public void Dispose() => Monitor.Dispose();
    }

    private sealed class FakeCapture : IGrindStandbyCapture
    {
        internal bool Foreground = true;
        internal bool Disposed;
        internal int Captures;
        internal int Suspensions;
        internal readonly List<Bitmap> Bitmaps = [];
        internal Size FrameSize = new(2, 2);
        internal readonly List<bool> BurstChanges = [];
        public bool IsGameForeground => Foreground;
        public CapturedDesktopBitmap Capture(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Disposed, this);
            Captures++;
            var bitmap = new Bitmap(FrameSize.Width, FrameSize.Height);
            Bitmaps.Add(bitmap);
            return new CapturedDesktopBitmap(bitmap, IsHdr: true, IsToneMapped: true);
        }
        public void SetBurst(bool enabled) => BurstChanges.Add(enabled);
        public void Suspend() => Suspensions++;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeVisual : IGrindStartVisualDetector
    {
        internal bool Candidate = true;
        internal int Observations;
        internal int Resets;
        public bool Observe(Bitmap frame, CompanionCalibration calibration) { Observations++; return Candidate; }
        public void Reset() => Resets++;
    }

    private sealed class FakeAnalyzer(
        Func<int, Bitmap, DateTimeOffset, CancellationToken, Task<FrameAnalysisResult>> analyze) : ILootFrameAnalyzer
    {
        internal int Calls;
        internal bool Disposed;
        internal string? Language;
        internal Size? ValidatedSize;
        internal bool UseHdrOcr;
        internal bool IsToneMapped;
        public bool IsAvailable => true;
        public string Status => "Ready";
        public void ConfigureGameLanguage(string language) => Language = language;
        public void ValidateCaptureSetup(Size frameSize) => ValidatedSize = frameSize;
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset at, CancellationToken token) =>
            analyze(++Calls, frame, at, token);
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset at, bool isHdr,
            bool isToneMapped, CancellationToken token)
        {
            UseHdrOcr = isHdr;
            IsToneMapped = isToneMapped;
            return AnalyzeAsync(frame, at, token);
        }
        public void Reset() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            Advance(dueTime);
            return System.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }
}
