using System.Drawing.Imaging;
using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>Losslessly copies a captured DXGI bitmap into an owned BGR OpenCV matrix.</summary>
public static class CompanionFrameDecoder
{
    public static Mat Decode(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return Decode(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
    }

    /// <summary>Copies only the requested pixels, retaining the full decoder's color and stride semantics.</summary>
    public static Mat Decode(Bitmap bitmap, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            (long)region.X + region.Width > bitmap.Width || (long)region.Y + region.Height > bitmap.Height)
            throw new ArgumentOutOfRangeException(nameof(region), "The region must be inside the bitmap.");

        Bitmap? converted = null;
        var source = bitmap;
        var pixelFormat = source.PixelFormat;
        if (!TryGetMatLayout(pixelFormat, out var matType, out var conversion))
        {
            converted = bitmap.Clone(region, PixelFormat.Format32bppPArgb);
            source = converted;
            region = new Rectangle(0, 0, converted.Width, converted.Height);
            pixelFormat = source.PixelFormat;
            _ = TryGetMatLayout(pixelFormat, out matType, out conversion);
        }

        BitmapData? bitmapData = null;
        try
        {
            bitmapData = source.LockBits(
                region,
                ImageLockMode.ReadOnly,
                pixelFormat);
            using var wrapped = CreateTopDownView(bitmapData, region.Size, matType);
            if (conversion is not { } colorConversion)
            {
                return wrapped.Clone();
            }

            var result = new Mat();
            try
            {
                Cv2.CvtColor(wrapped, result, colorConversion);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        finally
        {
            if (bitmapData is not null)
            {
                source.UnlockBits(bitmapData);
            }

            converted?.Dispose();
        }
    }

    private static Mat CreateTopDownView(
        BitmapData bitmapData,
        System.Drawing.Size size,
        MatType matType)
    {
        if (bitmapData.Stride >= 0)
        {
            return Mat.FromPixelData(
                size.Height,
                size.Width,
                matType,
                bitmapData.Scan0,
                bitmapData.Stride);
        }

        var physicalFirstRow = IntPtr.Add(
            bitmapData.Scan0,
            bitmapData.Stride * (size.Height - 1));
        using var bottomUp = Mat.FromPixelData(
            size.Height,
            size.Width,
            matType,
            physicalFirstRow,
            -bitmapData.Stride);
        var topDown = new Mat();
        Cv2.Flip(bottomUp, topDown, FlipMode.X);
        return topDown;
    }

    private static bool TryGetMatLayout(
        PixelFormat pixelFormat,
        out MatType matType,
        out ColorConversionCodes? conversion)
    {
        switch (pixelFormat)
        {
            case PixelFormat.Format24bppRgb:
                matType = MatType.CV_8UC3;
                conversion = null;
                return true;
            case PixelFormat.Format32bppArgb:
            case PixelFormat.Format32bppPArgb:
            case PixelFormat.Format32bppRgb:
                matType = MatType.CV_8UC4;
                conversion = ColorConversionCodes.BGRA2BGR;
                return true;
            default:
                matType = default;
                conversion = null;
                return false;
        }
    }
}
