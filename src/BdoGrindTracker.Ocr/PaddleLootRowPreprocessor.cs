using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

public static class PaddleLootRowPreprocessor
{
    /// <summary>Crop the calibrated normal row's text band, without a brightness mask.
    /// Its left edge already excludes the icon. All offsets follow BDO's UI scale.</summary>
    public static Mat Prepare(Mat originalBand, double uiScale, bool rare, bool grayscale,
        IReadOnlyList<CompanionOcrWord>? rareWords = null)
    {
        ArgumentNullException.ThrowIfNull(originalBand);
        if (originalBand.Empty() || originalBand.Type() != MatType.CV_8UC3)
            throw new ArgumentException("Paddle preparation requires a BGR row.", nameof(originalBand));
        if (!double.IsFinite(uiScale) || uiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(uiScale));
        // Preserve the rare announcement's full width, including family and enhancement
        // prefixes. Its decorative frame is not part of the text line fed to Paddle.
        var left = rare ? 0 : Math.Clamp((int)Math.Round(5 * uiScale), 0, originalBand.Width - 1);
        var top = rare ? 0 : Math.Clamp((int)Math.Round(10 * uiScale), 0, originalBand.Height - 1);
        var bottom = rare ? originalBand.Height : Math.Clamp((int)Math.Round(38 * uiScale), top + 1, originalBand.Height);
        if (rare && TryGetRareTextBounds(originalBand, rareWords, out var lineTop, out var lineBottom))
        {
            top = lineTop;
            bottom = lineBottom;
        }
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

    private static bool TryGetRareTextBounds(Mat band, IReadOnlyList<CompanionOcrWord>? words,
        out int top, out int bottom)
    {
        top = 0;
        bottom = band.Height;
        if (words is null || words.Count == 0) return false;
        // Rare primary and recovery images share this normalization. Geometry is
        // only a current-frame location hint; Paddle must still read the pixels.
        var normalizedWidth = CompanionNormalRowProcessor.CalculateNormalizedWidth(band.Width, band.Height);
        var minimumY = double.PositiveInfinity;
        var maximumY = double.NegativeInfinity;
        var overlapTop = double.NegativeInfinity;
        var overlapBottom = double.PositiveInfinity;
        foreach (var word in words)
        {
            if (word is null) return false;
            var bounds = word.Geometry;
            if (bounds.Status != CompanionOcrGeometryStatus.Success ||
                !float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) ||
                !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) ||
                bounds.X < 0 || bounds.Y < 0 || bounds.Width <= 0 || bounds.Height <= 0 ||
                (double)bounds.X + bounds.Width > normalizedWidth ||
                (double)bounds.Y + bounds.Height > RareLootRecoveryPreprocessor.NormalizedHeight)
                return false;
            minimumY = Math.Min(minimumY, bounds.Y);
            maximumY = Math.Max(maximumY, (double)bounds.Y + bounds.Height);
            overlapTop = Math.Max(overlapTop, bounds.Y);
            overlapBottom = Math.Min(overlapBottom, (double)bounds.Y + bounds.Height);
        }
        // Do not select one line from mixed UI text or discard another prefix.
        // All boxes must intersect a common horizontal text line.
        if (overlapTop >= overlapBottom) return false;
        var padding = Math.Max(2, (maximumY - minimumY) * .25);
        var scale = band.Height / (double)RareLootRecoveryPreprocessor.NormalizedHeight;
        top = Math.Clamp((int)Math.Floor((minimumY - padding) * scale), 0, band.Height - 1);
        bottom = Math.Clamp((int)Math.Ceiling((maximumY + padding) * scale), top + 1, band.Height);
        return true;
    }
}
