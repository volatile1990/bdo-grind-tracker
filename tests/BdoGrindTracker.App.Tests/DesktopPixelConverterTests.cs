using System.Runtime.InteropServices;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class DesktopPixelConverterTests
{
    [Fact]
    public void HdrHighlightsRemainDistinctAboveTheLegacyWhiteLimit()
    {
        var values = new[] { 0f, .25f, 1f, 2f, 4f, 6f, 12f };
        var bytes = values.SelectMany(value => new[] { (Half)value, (Half)value, (Half)value, (Half)1 })
            .SelectMany(value => BitConverter.GetBytes(BitConverter.HalfToUInt16Bits(value))).ToArray();
        WithPixels(bytes, pointer =>
        {
            using var image = DesktopPixelConverter.CopyBitmap(pointer, (uint)bytes.Length, values.Length, 1,
                DesktopPixelConverter.Rgba16Float);
            var levels = Enumerable.Range(0, values.Length).Select(x => image.GetPixel(x, 0).R).ToArray();
            Assert.Equal(new byte[] { 0, 124, 188, 213, 231, 238, 246 }, levels);
            Assert.All(Enumerable.Range(1, levels.Length - 1), i => Assert.True(levels[i] > levels[i - 1]));
        });
    }

    [Fact]
    public void FloatConversionRespectsChannelOrderAndPaddedRows()
    {
        var bytes = new byte[24];
        Write(0, 4f, 0f, 0f, 0f);
        Write(12, 0f, 0f, 4f, 1f);
        WithPixels(bytes, pointer =>
        {
            using var image = DesktopPixelConverter.CopyBitmap(pointer, 12, 1, 2, DesktopPixelConverter.Rgba16Float);
            Assert.Equal(Color.FromArgb(231, 0, 0).ToArgb(), image.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.FromArgb(0, 0, 231).ToArgb(), image.GetPixel(0, 1).ToArgb());
        });
        void Write(int offset, params float[] channels)
        {
            for (var i = 0; i < channels.Length; i++)
                BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)channels[i])).CopyTo(bytes, offset + i * 2);
        }
    }

    [Fact]
    public void LegacyBgraPixelsStayUnchangedIncludingPaddedRows()
    {
        WithPixels([30, 20, 10, 0, 99, 99, 99, 99, 60, 50, 40, 0, 88, 88, 88, 88], pointer =>
        {
            using var image = DesktopPixelConverter.CopyBitmap(pointer, 8, 1, 2, DesktopPixelConverter.Bgra8);
            Assert.Equal(Color.FromArgb(10, 20, 30).ToArgb(), image.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.FromArgb(40, 50, 60).ToArgb(), image.GetPixel(0, 1).ToArgb());
        });
    }

    [Fact]
    public void EveryFinitePositiveHalfMapsMonotonicallyWithoutInversion()
    {
        byte previous = 0;
        for (var bits = 0; bits <= 0x7bff; bits++)
        {
            var current = DesktopPixelConverter.MapLinearChannel((float)BitConverter.UInt16BitsToHalf((ushort)bits));
            Assert.True(current >= previous);
            previous = current;
        }
        Assert.Equal(0, DesktopPixelConverter.MapLinearChannel(float.NaN));
        Assert.Equal(0, DesktopPixelConverter.MapLinearChannel(float.NegativeInfinity));
        Assert.Equal(0, DesktopPixelConverter.MapLinearChannel(-1));
        Assert.Equal(255, DesktopPixelConverter.MapLinearChannel(float.PositiveInfinity));
    }

    [Fact]
    public void InvalidTextureLayoutsFailBeforeReadingPixels()
    {
        WithPixels(new byte[16], pointer =>
        {
            Assert.Throws<NotSupportedException>(() => DesktopPixelConverter.CopyBitmap(pointer, 8, 1, 1, 24));
            Assert.Throws<ArgumentException>(() => DesktopPixelConverter.CopyBitmap(pointer, 4, 1, 1, DesktopPixelConverter.Rgba16Float));
            Assert.Throws<ArgumentOutOfRangeException>(() => DesktopPixelConverter.CopyBitmap(pointer, 8, 0, 1, DesktopPixelConverter.Bgra8));
            Assert.Throws<ArgumentNullException>(() => DesktopPixelConverter.CopyBitmap(0, 8, 1, 1, DesktopPixelConverter.Bgra8));
            Assert.Throws<OverflowException>(() => DesktopPixelConverter.CopyBitmap(pointer, uint.MaxValue, int.MaxValue, 1, DesktopPixelConverter.Rgba16Float));
        });
    }

    private static void WithPixels(byte[] bytes, Action<nint> test)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); test(pointer); }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}
