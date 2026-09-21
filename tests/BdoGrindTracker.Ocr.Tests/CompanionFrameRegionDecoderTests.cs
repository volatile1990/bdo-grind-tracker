using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionFrameRegionDecoderTests
{
    [Theory]
    [InlineData(PixelFormat.Format24bppRgb)]
    [InlineData(PixelFormat.Format32bppRgb)]
    [InlineData(PixelFormat.Format32bppArgb)]
    [InlineData(PixelFormat.Format32bppPArgb)]
    public void RegionMatchesFullDecodeIncludingPaddingAlphaAndEdges(PixelFormat format)
    {
        using var bitmap = new Bitmap(37, 23, format);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, Color.FromArgb(60 + (x * 3 + y * 7) % 195,
                (x * 11 + y) % 256, (x + y * 13) % 256, (x * 5 + y * 17) % 256));
        AssertRegions(bitmap);
    }

    [Theory]
    [InlineData(PixelFormat.Format24bppRgb, 3)]
    [InlineData(PixelFormat.Format32bppArgb, 4)]
    public void NegativeStrideRegionsHaveTheSameOrientationAsFullDecode(PixelFormat format, int channels)
    {
        const int width = 37, height = 23;
        var stride = (width * channels + 3) & ~3;
        var bytes = Enumerable.Range(0, stride * height).Select(i => (byte)(i * 31 % 256)).ToArray();
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            using var bitmap = new Bitmap(width, height, -stride, format,
                IntPtr.Add(memory, stride * (height - 1)));
            AssertRegions(bitmap);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(8, 0, 3, 1)]
    [InlineData(0, 8, 1, 3)]
    [InlineData(int.MaxValue, 0, int.MaxValue, 1)]
    public void InvalidRegionsFailWithoutLockingBitmap(int x, int y, int width, int height)
    {
        using var bitmap = new Bitmap(10, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CompanionFrameDecoder.Decode(bitmap, new Rectangle(x, y, width, height)));
        using var valid = CompanionFrameDecoder.Decode(bitmap, new Rectangle(0, 0, 1, 1));
        Assert.Equal(1, valid.Width);
    }

    [Fact]
    public void RegionOwnsPixelsAfterSourceIsChangedAndDisposed()
    {
        var bitmap = new Bitmap(8, 8);
        bitmap.SetPixel(3, 4, Color.Red);
        using var decoded = CompanionFrameDecoder.Decode(bitmap, new Rectangle(3, 4, 1, 1));
        bitmap.SetPixel(3, 4, Color.Blue);
        bitmap.Dispose();
        Assert.Equal(new Vec3b(0, 0, 255), decoded.At<Vec3b>(0, 0));
    }

    private static void AssertRegions(Bitmap bitmap)
    {
        using var full = CompanionFrameDecoder.Decode(bitmap);
        foreach (var region in new[] { new Rectangle(0, 0, 37, 23), new Rectangle(3, 4, 17, 9),
                     new Rectangle(36, 22, 1, 1), new Rectangle(0, 3, 5, 20), new Rectangle(12, 0, 25, 4) })
        {
            using var expected = new Mat(full, new Rect(region.X, region.Y, region.Width, region.Height));
            using var actual = CompanionFrameDecoder.Decode(bitmap, region);
            Assert.Equal(expected.Size(), actual.Size());
            Assert.Equal(MatType.CV_8UC3, actual.Type());
            Assert.Equal(0, Cv2.Norm(expected, actual, NormTypes.INF));
        }
    }
}
