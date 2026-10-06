using System.Drawing.Imaging;

namespace BdoGrindTracker.App.Capture;

/// <summary>Converts the actual DXGI texture layout into an owned OCR bitmap.</summary>
internal static unsafe class DesktopPixelConverter
{
    internal const uint Bgra8 = 87;
    internal const uint Rgba16Float = 10;
    private static readonly Lazy<byte[]> HalfToSdr = new(CreateHalfLookup);

    internal static Bitmap CopyBitmap(nint source, uint rowPitch, int width, int height, uint format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (source == 0) throw new ArgumentNullException(nameof(source));
        var bytesPerPixel = format switch
        {
            Bgra8 => 4,
            Rgba16Float => 8,
            _ => throw new NotSupportedException($"Nicht unterstütztes DXGI-Bildformat: {format}."),
        };
        var rowBytes = checked(width * bytesPerPixel);
        if (rowPitch < rowBytes) throw new ArgumentException("Die DXGI-Zeilenbreite ist zu klein.", nameof(rowPitch));
        var lookup = format == Rgba16Float ? HalfToSdr.Value : null;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb);
            try
            {
                if (lookup is not null)
                    CopyHdrPixels(source, rowPitch, data.Scan0, data.Stride, width, height, lookup);
                else
                {
                    for (var y = 0; y < height; y++)
                    {
                        var src = (byte*)source + checked((nuint)y * rowPitch);
                        var dst = (byte*)data.Scan0 + checked((nint)y * data.Stride);
                        Buffer.MemoryCopy(src, dst, Math.Abs(data.Stride), rowBytes);
                    }
                }
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    // Keep a managed reference/fallback and a narrow seam for parity tests and local benchmarks.
    internal static bool CopyHdrPixels(nint source, uint rowPitch, nint destination, int destinationStride,
        int width, int height, byte[] lookup, bool useNative = true)
    {
        if (useNative && NativeDesktopPixelConverter.TryConvert(source, rowPitch, destination,
                destinationStride, width, height, lookup))
            return true;
        CopyHdrPixelsManaged(source, rowPitch, destination, destinationStride, width, height, lookup);
        return false;
    }

    internal static void CopyHdrPixelsManaged(nint source, uint rowPitch, nint destination,
        int destinationStride, int width, int height, byte[] lookup)
    {
        for (var y = 0; y < height; y++)
        {
            var rgba = (ushort*)((byte*)source + checked((nuint)y * rowPitch));
            var dst = (byte*)destination + checked((nint)y * destinationStride);
            for (var x = 0; x < width; x++)
            {
                dst[x * 4] = lookup[rgba[x * 4 + 2]];
                dst[x * 4 + 1] = lookup[rgba[x * 4 + 1]];
                dst[x * 4 + 2] = lookup[rgba[x * 4]];
                dst[x * 4 + 3] = 255;
            }
        }
    }

    // Fixed, scene-independent exposure: changing brightness elsewhere on the
    // monitor cannot change the same loot glyph between frames. Reinhard keeps
    // positive scRGB values above 1 distinct before the final 8-bit quantization.
    // This is an OCR representation, not a calibrated HDR screenshot for display.
    internal static byte MapLinearChannel(float value)
    {
        if (float.IsNaN(value) || value <= 0) return 0;
        var linear = float.IsPositiveInfinity(value) ? 1f : value / (1f + value);
        var encoded = linear <= .0031308f ? 12.92f * linear
            : 1.055f * MathF.Pow(linear, 1f / 2.4f) - .055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }

    internal static byte[] CreateHalfLookup()
    {
        var lookup = new byte[65536];
        for (var bits = 0; bits < lookup.Length; bits++)
            lookup[bits] = MapLinearChannel((float)BitConverter.UInt16BitsToHalf((ushort)bits));
        return lookup;
    }
}
