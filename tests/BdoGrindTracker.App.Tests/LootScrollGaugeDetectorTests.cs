using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.UI;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class LootScrollGaugeDetectorTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("inactive.png", LootScrollStatus.Inactive, null)]
    [InlineData("inactive-2024.png", LootScrollStatus.Inactive, null)]
    [InlineData("inactive-user-20260910.png", LootScrollStatus.Inactive, null)]
    [InlineData("inactive-expanded-user-20260910.png", LootScrollStatus.Inactive, null)]
    [InlineData("inactive-zero-user-20260910.png", LootScrollStatus.Inactive, null)]
    [InlineData("active-1.png", LootScrollStatus.Active, 1)]
    [InlineData("active-1-collapsed.png", LootScrollStatus.Active, 1)]
    [InlineData("active-2.png", LootScrollStatus.Active, 2)]
    [InlineData("active-2-user-20260910.png", LootScrollStatus.Active, 2)]
    public void RecognizesOriginalGaugeFrames(string name, LootScrollStatus status, int? level)
    {
        using var frame = Load(name);
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(new LootScrollReading(status, level), detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("inactive.png", 0.5, LootScrollStatus.Inactive, null)]
    [InlineData("inactive.png", 1.13, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-user-20260910.png", 0.5, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-user-20260910.png", 1.13, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-expanded-user-20260910.png", 0.5, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-expanded-user-20260910.png", 1.13, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-zero-user-20260910.png", 0.99, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-zero-user-20260910.png", 1.01, LootScrollStatus.Inactive, null)]
    [InlineData("active-1-collapsed.png", 0.5, LootScrollStatus.Active, 1)]
    [InlineData("active-1-collapsed.png", 1.5, LootScrollStatus.Active, 1)]
    [InlineData("active-2.png", 2.0, LootScrollStatus.Active, 2)]
    [InlineData("active-2.png", 0.77, LootScrollStatus.Active, 2)]
    public void RecognizesScaledAndRelocatedGauge(string name, double scale, LootScrollStatus status, int? level)
    {
        using var original = Load(name);
        using var frame = new Bitmap((int)Math.Ceiling(original.Width * scale) + 93,
            (int)Math.Ceiling(original.Height * scale) + 77, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(57, 71, 49));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle(43, 31,
                (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale)));
        }
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(new LootScrollReading(status, level), detector.Analyze(frame, CancellationToken.None));
    }

    [Fact]
    public void MissingGaugeAndLootScrollTextDoNotMeanInactiveOrActive()
    {
        using var frame = new Bitmap(800, 220);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(34, 38, 42));
            using var font = new Font("Segoe UI", 17);
            graphics.DrawString("Loot Scroll aktiv · Level 2\nActivate Lv. 1 · Lv. 1 active\nSchriftrolle: Beute deaktiviert", font, Brushes.Gold, 12, 12);
        }
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(LootScrollReading.Unknown, detector.Analyze(frame, CancellationToken.None));
    }

    [Fact]
    public void PartiallyClippedSymbolIsUnknown()
    {
        using var original = Load("active-1-collapsed.png");
        using var frame = original.Clone(new Rectangle(0, 0, 90, original.Height), PixelFormat.Format24bppRgb);
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(LootScrollReading.Unknown, detector.Analyze(frame, CancellationToken.None));
    }

    [Fact]
    public void ConflictingVisibleGaugesAreUnknown()
    {
        using var active = Load("active-2.png");
        using var inactive = Load("inactive.png");
        using var frame = new Bitmap(800, 500);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.DrawImageUnscaled(active, 0, 0);
            graphics.DrawImageUnscaled(inactive, 0, 250);
        }
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(LootScrollReading.Unknown, detector.Analyze(frame, CancellationToken.None));
    }

    [Fact]
    public void CancelledSearchStopsBeforeReadingPixels()
    {
        using var frame = new Bitmap(80, 80);
        using var detector = new LootScrollGaugeDetector();
        Assert.Throws<OperationCanceledException>(() => detector.Analyze(frame, new CancellationToken(true)));
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public void FullMonitorWithoutGaugeIsUnknown(int width, int height)
    {
        using var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var detector = new LootScrollGaugeDetector();
        var timer = Stopwatch.StartNew();
        Assert.Equal(LootScrollReading.Unknown, detector.Analyze(frame, CancellationToken.None));
        output.WriteLine($"{width}×{height} absent gauge: {timer.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void SmallGaugeOn4KMonitorSurvivesReducedSearch(double scale)
    {
        using var original = Load("active-1-collapsed.png");
        using var frame = new Bitmap(3840, 2160, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(37, 49, 61));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle(3107, 1781,
                (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale)));
        }
        using var detector = new LootScrollGaugeDetector();
        var timer = Stopwatch.StartNew();
        Assert.Equal(new LootScrollReading(LootScrollStatus.Active, 1), detector.Analyze(frame, CancellationToken.None));
        output.WriteLine($"3840×2160 gauge at scale {scale}: {timer.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("inactive-user-20260910.png", 1.0)]
    [InlineData("inactive-user-20260910.png", 0.5)]
    [InlineData("inactive-expanded-user-20260910.png", 1.0)]
    [InlineData("inactive-expanded-user-20260910.png", 0.5)]
    public void ActualInactiveGaugeSurvives4KSearch(string name, double scale)
    {
        using var original = Load(name);
        using var frame = new Bitmap(3840, 2160, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(37, 49, 61));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle(3107, 1781,
                (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale)));
        }
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(new LootScrollReading(LootScrollStatus.Inactive), detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("inactive-user-20260910.png", 2.5f, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-user-20260910.png", 5f, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-expanded-user-20260910.png", 2.5f, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-expanded-user-20260910.png", 5f, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-zero-user-20260910.png", 2.5f, LootScrollStatus.Inactive, null)]
    [InlineData("inactive-zero-user-20260910.png", 5f, LootScrollStatus.Inactive, null)]
    [InlineData("active-1-collapsed.png", 2.5f, LootScrollStatus.Active, 1)]
    [InlineData("active-1-collapsed.png", 5f, LootScrollStatus.Active, 1)]
    [InlineData("active-2.png", 2.5f, LootScrollStatus.Active, 2)]
    [InlineData("active-2.png", 5f, LootScrollStatus.Active, 2)]
    public void HdrCaptureBrightnessPreservesSymbolStatus(string name, float whiteLevel,
        LootScrollStatus status, int? level)
    {
        using var original = Load(name);
        using var frame = ToneMapHdr(original, whiteLevel);
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(new LootScrollReading(status, level), detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("inactive-user-20260910.png", 189, 122)]
    [InlineData("inactive-expanded-user-20260910.png", 167, 133)]
    [InlineData("active-2-user-20260910.png", 170, 99)]
    public void ReportsGaugeBoundsInOriginalFrameCoordinates(string name, int centerX, int centerY)
    {
        using var frame = Load(name);
        using var detector = new LootScrollGaugeDetector();
        var match = Assert.IsType<LootScrollGaugeMatch>(detector.FindGauge(frame, CancellationToken.None));
        Assert.InRange(match.Bounds.Width, 80, 86);
        Assert.InRange(Math.Abs(match.Bounds.X + match.Bounds.Width / 2 - centerX), 0, 2);
        Assert.InRange(Math.Abs(match.Bounds.Y + match.Bounds.Height / 2 - centerY), 0, 2);
    }

    [Theory]
    [InlineData("active-1-collapsed.png", 0.25)]
    [InlineData("active-1-collapsed.png", 0.5)]
    [InlineData("active-2.png", 0.25)]
    [InlineData("active-2.png", 0.5)]
    public void DimmedActiveSymbolNeverBecomesAnInactiveWarning(string name, float brightness)
    {
        using var original = Load(name);
        using var frame = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(new ColorMatrix
            {
                Matrix00 = brightness, Matrix11 = brightness, Matrix22 = brightness,
                Matrix33 = 1, Matrix44 = 1
            });
            graphics.DrawImage(original, new Rectangle(0, 0, frame.Width, frame.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
        }
        using var detector = new LootScrollGaugeDetector();
        Assert.NotEqual(LootScrollStatus.Inactive, detector.Analyze(frame, CancellationToken.None).Status);
    }

    [Fact]
    public void SimilarGoldHudShapesWithoutTheActualSymbolAreUnknown()
    {
        using var frame = new Bitmap(480, 180, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        using (var rim = new Pen(Color.Goldenrod, 3))
        {
            graphics.Clear(Color.FromArgb(44, 39, 30));
            for (var index = 0; index < 6; index++)
            {
                var x = 20 + index * 75;
                graphics.FillEllipse(Brushes.SaddleBrown, x, 30, 52, 52);
                graphics.DrawEllipse(rim, x, 30, 52, 52);
                graphics.FillEllipse(Brushes.Wheat, x + 12, 50, 28, 25);
                graphics.FillRectangle(Brushes.Wheat, x + 18, 43, 15, 8);
                graphics.DrawLine(Pens.Gold, x + 48, 72, x + 48, 84);
                graphics.DrawLine(Pens.Gold, x + 42, 78, x + 54, 78);
            }
        }
        using var detector = new LootScrollGaugeDetector();
        Assert.Equal(LootScrollReading.Unknown, detector.Analyze(frame, CancellationToken.None));
    }

    private static Bitmap Load(string name) => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "loot-scroll", name));

    internal static Bitmap ToneMapHdr(Bitmap original, float whiteLevel)
    {
        var bytes = new byte[original.Width * original.Height * 8];
        for (var y = 0; y < original.Height; y++)
        for (var x = 0; x < original.Width; x++)
        {
            var pixel = original.GetPixel(x, y);
            Write(pixel.R, 0);
            Write(pixel.G, 1);
            Write(pixel.B, 2);
            void Write(byte value, int channel)
            {
                var encoded = value / 255f;
                var linear = encoded <= .04045f ? encoded / 12.92f
                    : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
                var bits = BitConverter.HalfToUInt16Bits((Half)(linear * whiteLevel));
                var offset = (y * original.Width + x) * 8 + channel * 2;
                bytes[offset] = (byte)bits;
                bytes[offset + 1] = (byte)(bits >> 8);
            }
        }
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return DesktopPixelConverter.CopyBitmap(pointer, (uint)original.Width * 8,
                original.Width, original.Height, DesktopPixelConverter.Rgba16Float);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}
