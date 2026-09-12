using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.UI;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class AgrisFrameDetectorTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("inactive-gray.png", AgrisStatus.Inactive)]
    [InlineData("inactive-gray-ring.png", AgrisStatus.Inactive)]
    [InlineData("inactive-gray-ring-rotated.png", AgrisStatus.Inactive)]
    [InlineData("active-gold-ring.png", AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", AgrisStatus.Active)]
    [InlineData("active-gold-ring-hdr-20260912.png", AgrisStatus.Active)]
    [InlineData("active-gold-ring-hdr-20260912-rotated.png", AgrisStatus.Active)]
    [InlineData("active-gold-ring-hdr-20260912-rotated-2.png", AgrisStatus.Active)]
    public void RecognizesActualUserFrames(string name, AgrisStatus status)
    {
        using var frame = Load(name);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(new AgrisReading(status), detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("inactive-gray.png", .6, AgrisStatus.Inactive)]
    [InlineData("inactive-gray-ring.png", 1.49, AgrisStatus.Inactive)]
    [InlineData("inactive-gray-ring-rotated.png", 2, AgrisStatus.Inactive)]
    [InlineData("active-gold-ring.png", .6, AgrisStatus.Active)]
    [InlineData("active-gold-ring.png", 1.49, AgrisStatus.Active)]
    [InlineData("active-gold-ring.png", 2, AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", .6, AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", 1.49, AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", 2, AgrisStatus.Active)]
    public void FindsScaledRelocatedHud(string name, double scale, AgrisStatus status)
    {
        using var original = Load(name);
        using var frame = Place(original, 480, 360, 171, 101, scale);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(new AgrisReading(status), detector.Analyze(frame, CancellationToken.None));
    }

    public static IEnumerable<object[]> LiveHdrScales()
    {
        foreach (var name in new[] { "active-gold-ring-hdr-20260912.png", "active-gold-ring-hdr-20260912-rotated.png", "active-gold-ring-hdr-20260912-rotated-2.png" })
        foreach (var scale in new[] { .6, .75, 1, 1.25, 1.49, 2 })
            yield return [name, scale];
    }

    [Theory]
    [MemberData(nameof(LiveHdrScales))]
    public void HdrHighlightsRemainActiveAcrossHudScales(string name, double scale)
    {
        using var original = Load(name);
        using var frame = Place(original, 480, 360, 171, 101, scale);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(AgrisStatus.Active, detector.Analyze(frame, CancellationToken.None).Status);
    }

    [Fact]
    public void GoldenGlyphWithoutOuterRingIsInactive()
    {
        using var frame = Load("active-gold-ring.png");
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
            if (Math.Pow(x - 39, 2) + Math.Pow(y - 34, 2) >= 23 * 23)
                frame.SetPixel(x, y, Color.FromArgb(47, 47, 47));
        using var detector = new AgrisFrameDetector();
        Assert.Equal(new AgrisReading(AgrisStatus.Inactive), detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("active-gold-ring-hdr-20260912.png")]
    [InlineData("active-gold-ring-hdr-20260912-rotated.png")]
    [InlineData("active-gold-ring-hdr-20260912-rotated-2.png")]
    public void HdrGoldenGlyphStillRequiresOuterRing(string name)
    {
        using var frame = Load(name);
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
            if (Math.Pow(x - 35.5, 2) + Math.Pow(y - 35.5, 2) >= 23 * 23)
                frame.SetPixel(x, y, Color.FromArgb(85, 85, 85));
        using var detector = new AgrisFrameDetector();
        Assert.Equal(AgrisStatus.Inactive, detector.Analyze(frame, CancellationToken.None).Status);
    }

    [Theory]
    [InlineData("active-gold-ring-hdr-20260912.png", false)]
    [InlineData("active-gold-ring-hdr-20260912-rotated.png", false)]
    [InlineData("active-gold-ring-hdr-20260912-rotated-2.png", false)]
    [InlineData("active-gold-ring-hdr-20260912.png", true)]
    [InlineData("active-gold-ring-hdr-20260912-rotated.png", true)]
    [InlineData("active-gold-ring-hdr-20260912-rotated-2.png", true)]
    public void HdrRingWithNeutralGlyphIsInactive(string name, bool addColorNoise)
    {
        using var frame = Load(name);
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            if (Math.Pow(x - 35.5, 2) + Math.Pow(y - 35.5, 2) >= 23 * 23) continue;
            var color = frame.GetPixel(x, y);
            var gray = (int)Math.Round(.299 * color.R + .587 * color.G + .114 * color.B);
            frame.SetPixel(x, y, Color.FromArgb(Math.Min(255, gray + (addColorNoise ? 2 : 0)),
                Math.Min(255, gray + (addColorNoise ? 1 : 0)), gray));
        }
        using var detector = new AgrisFrameDetector();
        Assert.Equal(AgrisStatus.Inactive, detector.Analyze(frame, CancellationToken.None).Status);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(210)]
    [InlineData(300)]
    public void ActiveRingCanRotateIndependentlyOfGlyph(int degrees)
    {
        using var original = Load("active-gold-ring.png");
        using var frame = RotateRing(original, degrees, 39, 34);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(new AgrisReading(AgrisStatus.Active), detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(210)]
    [InlineData(300)]
    public void GrayGlyphRemainsInactiveAtEveryRingAngle(int degrees)
    {
        using var original = Load("inactive-gray-ring.png");
        using var frame = RotateRing(original, degrees, 28, 31);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(new AgrisReading(AgrisStatus.Inactive), detector.Analyze(frame, CancellationToken.None));
    }

    private static Bitmap RotateRing(Bitmap original, int degrees, int centerX, int centerY)
    {
        var frame = (Bitmap)original.Clone();
        var angle = degrees * Math.PI / 180;
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var dx = x - (double)centerX;
            var dy = y - (double)centerY;
            if (dx * dx + dy * dy < 23 * 23) continue;
            var sx = (int)Math.Round(centerX + dx * Math.Cos(angle) - dy * Math.Sin(angle));
            var sy = (int)Math.Round(centerY + dx * Math.Sin(angle) + dy * Math.Cos(angle));
            frame.SetPixel(x, y, sx >= 0 && sx < original.Width && sy >= 0 && sy < original.Height
                ? original.GetPixel(sx, sy) : Color.FromArgb(47, 47, 47));
        }
        return frame;
    }

    [Fact]
    public void MissingOrSimilarGoldShapesAreUnknown()
    {
        using var frame = new Bitmap(500, 220);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(AgrisReading.Unknown, detector.Analyze(frame, CancellationToken.None));
        using (var graphics = Graphics.FromImage(frame))
        using (var gold = new Pen(Color.Gold, 4))
        {
            graphics.Clear(Color.FromArgb(45, 45, 45));
            graphics.DrawEllipse(gold, 60, 60, 55, 55);
            graphics.FillRectangle(Brushes.Wheat, 70, 85, 35, 10);
            graphics.FillEllipse(Brushes.Wheat, 82, 73, 9, 15);
            graphics.DrawString("Agris aktiv", SystemFonts.DefaultFont, Brushes.Gold, 210, 75);
        }
        Assert.Equal(AgrisReading.Unknown, detector.Analyze(frame, CancellationToken.None));
    }

    [Fact]
    public void MultipleVisibleGaugesAreUnknown()
    {
        using var active = Load("active-gold-ring.png");
        using var inactive = Load("inactive-gray-ring.png");
        using var frame = new Bitmap(500, 220);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.DrawImageUnscaled(active, 30, 30);
            graphics.DrawImageUnscaled(inactive, 300, 110);
        }
        using var detector = new AgrisFrameDetector();
        Assert.Equal(AgrisReading.Unknown, detector.Analyze(frame, CancellationToken.None));
    }

    [Fact]
    public void CachedGaugeIsReacquiredAfterMoving()
    {
        using var original = Load("active-gold-ring.png");
        using var first = Place(original, 1920, 1080, 143, 151);
        using var moved = Place(original, 1920, 1080, 1651, 843);
        using var empty = new Bitmap(1920, 1080);
        using var detector = new AgrisFrameDetector();
        var timer = Stopwatch.StartNew();
        Assert.Equal(AgrisStatus.Active, detector.Analyze(first, CancellationToken.None).Status);
        output.WriteLine($"Initial full-frame search: {timer.ElapsedMilliseconds} ms");
        timer.Restart();
        Assert.Equal(AgrisStatus.Active, detector.Analyze(first, CancellationToken.None).Status);
        output.WriteLine($"Cached ROI: {timer.ElapsedMilliseconds} ms");
        Assert.Equal(AgrisStatus.Active, detector.Analyze(moved, CancellationToken.None).Status);
        Assert.Equal(AgrisReading.Unknown, detector.Analyze(empty, CancellationToken.None));
    }

    [Fact]
    public void FindsHudOn4KMonitor()
    {
        using var original = Load("active-gold-ring.png");
        using var frame = Place(original, 3840, 2160, 3271, 1741, 1.49);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(AgrisStatus.Active, detector.Analyze(frame, CancellationToken.None).Status);
    }

    public static IEnumerable<object[]> MonitorScales()
    {
        foreach (var name in new[] { "active-gold-ring.png", "active-gold-ring-user-20260912.png", "active-gold-ring-hdr-20260912.png" })
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1440), (3840, 2160) })
        foreach (var scale in new[] { .75, 1, 1.25, 1.5, 2 })
            yield return [name, width, height, scale];
    }

    [Theory]
    [MemberData(nameof(MonitorScales))]
    public void ResolvesEveryRequestedMonitorAndScale(string name, int width, int height, double scale)
    {
        using var original = Load(name);
        using var frame = Place(original, width, height, width - 233, height - 177, scale);
        using var detector = new AgrisFrameDetector();
        Assert.Equal(AgrisStatus.Active, detector.Analyze(frame, CancellationToken.None).Status);
    }

    [Theory]
    [InlineData("active-gold-ring.png", 1f, AgrisStatus.Active)]
    [InlineData("active-gold-ring.png", 2.5f, AgrisStatus.Active)]
    [InlineData("active-gold-ring.png", 5f, AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", 1f, AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", 2.5f, AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", 5f, AgrisStatus.Active)]
    [InlineData("active-gold-ring-user-20260912.png", 7.5f, AgrisStatus.Active)]
    [InlineData("inactive-gray-ring.png", 1f, AgrisStatus.Inactive)]
    [InlineData("inactive-gray-ring.png", 2.5f, AgrisStatus.Inactive)]
    [InlineData("inactive-gray-ring.png", 5f, AgrisStatus.Inactive)]
    [InlineData("inactive-gray-ring.png", 7.5f, AgrisStatus.Inactive)]
    public void ToneMappedHdrPreservesColorAndRingDecision(string name, float whiteLevel, AgrisStatus expected)
    {
        using var original = Load(name);
        using var frame = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < original.Height; y++)
        for (var x = 0; x < original.Width; x++)
        {
            var pixel = original.GetPixel(x, y);
            frame.SetPixel(x, y, Color.FromArgb(Convert(pixel.R), Convert(pixel.G), Convert(pixel.B)));
        }
        using var detector = new AgrisFrameDetector();
        Assert.Equal(expected, detector.Analyze(frame, CancellationToken.None).Status);
        byte Convert(byte value)
        {
            var encoded = value / 255f;
            var linear = encoded <= .04045f ? encoded / 12.92f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
            return DesktopPixelConverter.MapLinearChannel(linear * whiteLevel);
        }
    }

    [Fact]
    public void CancellationStopsBeforeReadingPixels()
    {
        using var frame = new Bitmap(80, 80);
        using var detector = new AgrisFrameDetector();
        Assert.Throws<OperationCanceledException>(() => detector.Analyze(frame, new CancellationToken(true)));
    }

    private static Bitmap Load(string name) => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "agris", name));
    private static Bitmap Place(Bitmap original, int width, int height, int x, int y, double scale = 1)
    {
        var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.FromArgb(51, 61, 42));
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(original, new Rectangle(x, y, (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale)));
        return frame;
    }
}
