using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>Additional views of an original normal-loot band, used only for recovery.</summary>
public enum NormalLootRecoveryVariant
{
    Grayscale,
    AdaptiveThreshold,
}

/// <summary>Owns both independent prepared images; disposing never affects the source band.</summary>
public sealed class NormalLootRecoveryImages : IDisposable
{
    internal NormalLootRecoveryImages(
        Mat nameImage,
        Mat? quantityImage,
        int recognizedTextWidth,
        float nameScale)
    {
        NameImage = nameImage;
        QuantityImage = quantityImage;
        RecognizedTextWidth = recognizedTextWidth;
        NameScale = nameScale;
    }

    public Mat NameImage { get; }

    public Mat? QuantityImage { get; }

    public int RecognizedTextWidth { get; }

    public float NameScale { get; }

    public void Dispose()
    {
        NameImage.Dispose();
        QuantityImage?.Dispose();
    }
}

/// <summary>
/// Additive alternatives made from original BGR pixels, without Companion's HSV
/// mask, fixed brightness cutoff, or blank-row gate. This never changes the
/// baseline preparation and does not recognize, invent, or validate quantities.
/// </summary>
public static class NormalLootRecoveryPreprocessor
{
    private const int NormalizedHeight = 100;
    private const int NameLeft = 10;
    private const int QuantityLeft = 120;
    private const int QuantityTop = 30;
    private const int BackgroundSeparation = 18;
    private const int MinimumComponentHeight = 4;

    public static NormalLootRecoveryImages Prepare(Mat sourceBand, NormalLootRecoveryVariant variant)
    {
        ArgumentNullException.ThrowIfNull(sourceBand);
        if (sourceBand.Empty())
        {
            throw new ArgumentException("The original source band must not be empty.", nameof(sourceBand));
        }

        if (sourceBand.Type() != MatType.CV_8UC3)
        {
            throw new ArgumentException("Recovery consumes an 8-bit three-channel BGR band.", nameof(sourceBand));
        }

        if (!Enum.IsDefined(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }

        if (CompanionNormalRowProcessor.CalculateNormalizedWidth(sourceBand.Width, sourceBand.Height) <= NameLeft)
        {
            throw new ArgumentException("The normalized band must extend beyond its 10-pixel left margin.", nameof(sourceBand));
        }

        using var originalGray = CompanionNormalRowProcessor.ConvertToGray(sourceBand);
        using var normalizedGray = CompanionNormalRowProcessor.ResizeToHeight(originalGray, NormalizedHeight);
        using var localForeground = CreateLocalForeground(normalizedGray);
        var prepared = variant == NormalLootRecoveryVariant.Grayscale ? normalizedGray : localForeground;

        Mat? nameImage = null;
        Mat? quantityImage = null;
        try
        {
            var nameWidth = normalizedGray.Width - NameLeft;
            var nameScale = CompanionNormalRowProcessor.CalculateNameScale(nameWidth);
            using (var nameCrop = new Mat(prepared, new Rect(NameLeft, 0, nameWidth, NormalizedHeight)))
            using (var scaled = Scale(nameCrop, nameScale, variant))
            {
                // Retain the full normalized Y origin and Companion's scale so
                // existing first-word-Y parsing keeps exactly the same geometry.
                nameImage = CompanionNormalRowProcessor.CreateOwnedMat(destination =>
                    Cv2.CopyMakeBorder(scaled, destination, 0, 0, 0, 5, BorderTypes.Constant, Scalar.Black));
            }

            var quantityBounds = FindQuantityBounds(localForeground);
            if (quantityBounds is { } bounds)
            {
                using var quantityCrop = new Mat(prepared, bounds);
                var quantityScale = Math.Clamp(48d / bounds.Height, 1d, 3d);
                using var scaled = Scale(quantityCrop, quantityScale, variant);
                quantityImage = CompanionNormalRowProcessor.CreateOwnedMat(destination =>
                    Cv2.CopyMakeBorder(scaled, destination, 8, 8, 8, 8, BorderTypes.Constant, Scalar.Black));
            }

            return new NormalLootRecoveryImages(nameImage, quantityImage, nameWidth, nameScale);
        }
        catch
        {
            nameImage?.Dispose();
            quantityImage?.Dispose();
            throw;
        }
    }

    private static Mat CreateLocalForeground(Mat gray)
    {
        // OpenCV applies source > localMean - C for Binary. Negative C requires
        // a small positive local contrast, keeping uniform/slowly varying dark
        // backgrounds black instead of turning the entire surface white.
        // This preserves dim text below the baseline's fixed 140/170 threshold.
        return CompanionNormalRowProcessor.CreateOwnedMat(destination =>
            Cv2.AdaptiveThreshold(
                gray, destination, 255, AdaptiveThresholdTypes.GaussianC,
                ThresholdTypes.Binary, 31, -4));
    }

    private static Rect? FindQuantityBounds(Mat foreground)
    {
        if (foreground.Width <= QuantityLeft)
        {
            return null;
        }

        using var quantityRegion = new Mat(foreground,
            new Rect(QuantityLeft, QuantityTop, foreground.Width - QuantityLeft, NormalizedHeight - QuantityTop));
        Cv2.FindContours(quantityRegion, out OpenCvSharp.Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var components = contours.Select(contour => Cv2.BoundingRect(contour))
            // Ignore isolated pixel specks, but retain thin strokes (including 1).
            .Where(bounds => bounds.Height >= MinimumComponentHeight)
            .OrderBy(bounds => bounds.Left)
            .ToArray();
        if (components.Length == 0)
        {
            return null;
        }

        // Group by horizontal background gaps, not by collecting every digit-like
        // mark across the row. This prevents a name suffix or a distant number
        // from being silently concatenated with the actual rightmost quantity.
        // Eighteen normalized blank pixels form a separator; ordinary adjacent
        // glyphs stay together. No character classification happens here: a word
        // at the right edge remains a word for the caller's digits-only parser.
        var rightmost = components[0];
        var precedingRight = 0;
        foreach (var component in components.Skip(1))
        {
            if (component.Left - rightmost.Right >= BackgroundSeparation)
            {
                precedingRight = rightmost.Right;
                rightmost = component;
            }
            else
            {
                rightmost = Union(rightmost, component);
            }
        }

        // The x=120 cut may bisect the item name. Only use a group with a visible
        // background separator on its left; the full name view remains available
        // even when a separate quantity cannot be isolated safely.
        if (rightmost.Left - precedingRight < BackgroundSeparation)
        {
            return null;
        }

        return new Rect(QuantityLeft + rightmost.Left, QuantityTop + rightmost.Top,
            rightmost.Width, rightmost.Height);
    }

    private static Rect Union(Rect first, Rect second)
    {
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        return new Rect(left, top, Math.Max(first.Right, second.Right) - left,
            Math.Max(first.Bottom, second.Bottom) - top);
    }

    private static Mat Scale(Mat source, double scale, NormalLootRecoveryVariant variant) =>
        CompanionNormalRowProcessor.CreateOwnedMat(destination =>
            Cv2.Resize(source, destination, new OpenCvSharp.Size(), scale, scale,
                variant == NormalLootRecoveryVariant.AdaptiveThreshold
                    ? InterpolationFlags.Nearest
                    : InterpolationFlags.Cubic));
}
