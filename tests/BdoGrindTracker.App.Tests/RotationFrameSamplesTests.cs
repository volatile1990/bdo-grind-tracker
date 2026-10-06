using BdoGrindTracker.App.Analysis;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationFrameSamplesTests
{
    private static readonly Rectangle Region = new(4, 5, 16, 12);

    [Fact]
    public void OnlyTheSameCaptureRegionAndLanguageSharePixelsAndText()
    {
        using var frame = new Bitmap(40, 30);
        frame.SetPixel(Region.X, Region.Y, Color.CornflowerBlue);
        using var capture = new RotationFrameSamples(frame);
        using var nextCapture = new RotationFrameSamples(frame);
        var first = capture.Acquire(Region, "en-US");
        var same = capture.Acquire(Region, "en-US");
        var anotherLanguage = capture.Acquire(Region, "de-DE");
        var anotherRegion = capture.Acquire(Region with { X = 5 }, "en-US");
        var next = nextCapture.Acquire(Region, "en-US");
        try
        {
            Assert.Same(first, same);
            Assert.NotSame(first, anotherLanguage);
            Assert.NotSame(first, anotherRegion);
            Assert.NotSame(first, next);
            Assert.Equal("first", first.Read((pixels, _) =>
            {
                Assert.Equal(Region.Width, pixels.Width);
                Assert.Equal(Region.Height, pixels.Height);
                var color = frame.GetPixel(Region.X, Region.Y);
                Assert.Equal(new Vec3b(color.B, color.G, color.R), pixels.At<Vec3b>(0, 0));
                return "first";
            }));
            Assert.Equal("first", same.Read((_, _) => throw new InvalidOperationException("Duplicate read")));
            Assert.Equal("next", next.Read((_, _) => "next"));
            Assert.Equal("German", anotherLanguage.Read((_, _) => "German"));
        }
        finally
        {
            first.Release();
            same.Release();
            anotherLanguage.Release();
            anotherRegion.Release();
            next.Release();
        }
    }

    [Fact]
    public async Task ASlowNativeReadDoesNotDelayAnotherEngineAndPixelsSurviveCaptureDisposal()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var frame = new Bitmap(40, 30);
        using var capture = new RotationFrameSamples(frame);
        var first = capture.Acquire(Region, "en-US");
        var second = capture.Acquire(Region, "en-US");
        capture.Dispose();
        frame.Dispose();
        Bitmap? retained = null;
        first.WithPixels(pixels => retained = pixels);
        nint sharedData = 0;
        var reads = 0;
        string Read(Mat pixels, CancellationToken token)
        {
            Interlocked.Increment(ref reads);
            sharedData = pixels.Data;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5), token));
            pixels.At<Vec3b>(0, 0);
            return "raw\fprepared";
        }
        var pendingFirst = Task.Run(() => first.Read(Read));
        Task<string>? pendingSecond = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            pendingSecond = Task.Run(() => second.Read((pixels, _) =>
            {
                Interlocked.Increment(ref reads);
                Assert.Equal(sharedData, pixels.Data);
                return "raw\fprepared";
            }));
            Assert.Equal("raw\fprepared", await pendingSecond.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(pendingFirst.IsCompleted);
            Assert.Equal(Region.Size, retained!.Size);
            Assert.Equal(2, Volatile.Read(ref reads));
        }
        finally { release.Set(); }
        try
        {
            Assert.Equal("raw\fprepared", await pendingFirst.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("raw\fprepared", await pendingSecond!.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, Volatile.Read(ref reads));
            Assert.Equal("raw\fprepared", second.Read((_, _) => throw new InvalidOperationException("Completed read was lost")));
            first.Release();
            retained!.GetPixel(0, 0);
        }
        finally { second.Release(); }
        Assert.ThrowsAny<Exception>(() => retained!.GetPixel(0, 0));
    }

    [Fact]
    public async Task WaitingReaderCancellationDoesNotCancelOrReleaseTheActiveReadersPixels()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        using var frame = new Bitmap(40, 30);
        using var capture = new RotationFrameSamples(frame);
        var active = capture.Acquire(Region, "en-US");
        var waiting = capture.Acquire(Region, "en-US");
        capture.Dispose();
        Bitmap? retained = null;
        active.WithPixels(pixels => retained = pixels);
        var pending = Task.Run(() => active.Read((pixels, _) =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            pixels.At<Vec3b>(0, 0);
            return "completed";
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            var cancelled = Task.Run(() => waiting.Read((_, _) => throw new InvalidOperationException("Waiting read ran"),
                cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(5)));
            waiting.Release();
            retained!.GetPixel(0, 0);
        }
        finally { release.Set(); }
        try { Assert.Equal("completed", await pending.WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { active.Release(); }
        Assert.ThrowsAny<Exception>(() => retained!.GetPixel(0, 0));
    }

    [Fact]
    public void CancelledAndFailedResultsStayRetryableAndAnEmptySuccessfulResultIsCached()
    {
        using var cancellation = new CancellationTokenSource();
        using var frame = new Bitmap(40, 30);
        using var capture = new RotationFrameSamples(frame);
        var sample = capture.Acquire(Region, "en-US");
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => sample.Read((_, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
                return "cancelled";
            }, cancellation.Token));
            Assert.Throws<InvalidOperationException>(() => sample.Read((_, _) => throw new InvalidOperationException("Native failure")));
            Assert.Equal("", sample.Read((_, _) => ""));
            Assert.Equal("", sample.Read((_, _) => throw new InvalidOperationException("Successful empty read was lost")));
            Assert.ThrowsAny<OperationCanceledException>(() => sample.Read((_, _) => "ignored", cancellation.Token));
        }
        finally { sample.Release(); }
    }

    [Fact]
    public async Task DiagnosticsAndRecognitionDoNotUseTheSameGdiBitmapConcurrently()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var frame = new Bitmap(40, 30);
        using var capture = new RotationFrameSamples(frame);
        var sample = capture.Acquire(Region, "en-US");
        var inspecting = 0;
        var inspection = Task.Run(() => sample.WithPixels(pixels =>
        {
            Interlocked.Exchange(ref inspecting, 1);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            pixels.GetPixel(0, 0);
            Interlocked.Exchange(ref inspecting, 0);
        }));
        Task<string>? read = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            read = Task.Run(() => sample.Read((pixels, _) =>
            {
                Assert.Equal(0, Volatile.Read(ref inspecting));
                pixels.At<Vec3b>(0, 0);
                return "done";
            }));
        }
        finally { release.Set(); }
        try
        {
            await inspection.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("done", await read!.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { sample.Release(); }
    }
}
