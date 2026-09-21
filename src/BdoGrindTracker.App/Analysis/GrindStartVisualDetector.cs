using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

internal interface IGrindStartVisualDetector
{
    bool Observe(Bitmap frame, CompanionCalibration calibration);
    void Reset();
}

/// <summary>
/// A cheap wake-up hint, never loot evidence. Only the calibrated text line in
/// the newest normal-loot row is read; recognizing the item still requires OCR.
/// </summary>
internal sealed class GrindStartVisualDetector : IGrindStartVisualDetector
{
    private Rectangle? _previousBounds;
    private float _previousScale;
    private GrindStartVisualEvidence? _previous;

    public bool Observe(Bitmap frame, CompanionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(calibration);
        if (!TryGetTextBounds(frame.Size, calibration, out var bounds))
        {
            Reset();
            return false;
        }

        var width = Math.Min(360, (int)Math.Round(bounds.Width / calibration.UiScale));
        const int height = 24;
        var pixels = SampleGrayscale(frame, bounds, width, height);
        var current = GrindStartVisualEvidence.Create(pixels, width, height);
        var previous = _previousBounds == bounds && _previousScale == calibration.UiScale ? _previous : null;
        _previous = current;
        _previousBounds = bounds;
        _previousScale = calibration.UiScale;
        // The first image is a baseline: an already visible log is not a new drop.
        return previous is not null && current.IsNewComparedTo(previous);
    }

    public void Reset()
    {
        _previousBounds = null;
        _previous = null;
    }

    internal static bool TryGetTextBounds(Size frameSize, CompanionCalibration calibration, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (frameSize.Width != calibration.ScreenWidth || frameSize.Height != calibration.ScreenHeight ||
            !float.IsFinite(calibration.UiScale) || calibration.UiScale <= 0)
            return false;
        try
        {
            var slots = CompanionNormalLootGeometry.CalculateSlotCrops(calibration);
            if (slots.Count == 0) return false;
            var row = slots[0];
            var left = checked((int)Math.Round(10 * calibration.UiScale));
            var top = checked((int)Math.Round(12 * calibration.UiScale));
            var width = Math.Min(checked((int)Math.Round(360 * calibration.UiScale)), row.Width - left);
            var height = checked((int)Math.Round(24 * calibration.UiScale));
            if (width < 20 || height < 8 || top + height > row.Height) return false;
            bounds = new Rectangle(checked(row.X + left), checked(row.Y + top), width, height);
            return new Rectangle(Point.Empty, frameSize).Contains(bounds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static byte[] SampleGrayscale(Bitmap frame, Rectangle bounds, int width, int height)
    {
        // Lock only this tiny region, without decoding/copying the full frame or
        // waking OpenCV. Normalize sampling so the work is bounded at every UI scale.
        var data = frame.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[width * height];
            var row = new byte[bounds.Width * 4];
            for (var y = 0; y < height; y++)
            {
                var sourceY = Math.Min(bounds.Height - 1, (int)((y + .5) * bounds.Height / height));
                Marshal.Copy(IntPtr.Add(data.Scan0, sourceY * data.Stride), row, 0, row.Length);
                for (var x = 0; x < width; x++)
                {
                    var sourceX = Math.Min(bounds.Width - 1, (int)((x + .5) * bounds.Width / width)) * 4;
                    pixels[y * width + x] = (byte)((29 * row[sourceX] + 150 * row[sourceX + 1] +
                        77 * row[sourceX + 2]) >> 8);
                }
            }
            return pixels;
        }
        finally
        {
            frame.UnlockBits(data);
        }
    }
}
