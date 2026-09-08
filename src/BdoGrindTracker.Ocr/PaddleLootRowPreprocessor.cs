using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

public static class PaddleLootRowPreprocessor
{
    /// <summary>Crop the calibrated normal row's text band, without a brightness mask.
    /// Its left edge already excludes the icon. All offsets follow BDO's UI scale.</summary>
    public static Mat Prepare(Mat originalBand, double uiScale, bool rare, bool grayscale)
    {
        ArgumentNullException.ThrowIfNull(originalBand);
        if (originalBand.Empty() || originalBand.Type() != MatType.CV_8UC3)
            throw new ArgumentException("Paddle preparation requires a BGR row.", nameof(originalBand));
        if (!double.IsFinite(uiScale) || uiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(uiScale));
        // The rare announcement has a different layout: retain its complete, already calibrated band.
        var left = rare ? 0 : Math.Clamp((int)Math.Round(5 * uiScale), 0, originalBand.Width - 1);
        var top = rare ? 0 : Math.Clamp((int)Math.Round(10 * uiScale), 0, originalBand.Height - 1);
        var bottom = rare ? originalBand.Height : Math.Clamp((int)Math.Round(38 * uiScale), top + 1, originalBand.Height);
        using var crop = new Mat(originalBand, new Rect(left, top, originalBand.Width - left, bottom - top));
        var result = new Mat();
        try
        {
            if (grayscale) Cv2.CvtColor(crop, result, ColorConversionCodes.BGR2GRAY);
            else crop.CopyTo(result);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
