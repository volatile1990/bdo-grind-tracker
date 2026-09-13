using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>Recovery views of the complete calibrated special-drop announcement.</summary>
public static class RareLootRecoveryPreprocessor
{
    internal const int NormalizedHeight = 100;

    public static NormalLootRecoveryImages Prepare(Mat sourceBand, NormalLootRecoveryVariant variant)
    {
        ArgumentNullException.ThrowIfNull(sourceBand);
        if (sourceBand.Empty() || sourceBand.Type() != MatType.CV_8UC3)
            throw new ArgumentException("Special-drop recovery requires a non-empty BGR band.", nameof(sourceBand));
        if (!Enum.IsDefined(variant)) throw new ArgumentOutOfRangeException(nameof(variant));
        if (CompanionNormalRowProcessor.CalculateNormalizedWidth(sourceBand.Width, sourceBand.Height) <= 0)
            throw new ArgumentException("The normalized special-drop band must have a positive width.", nameof(sourceBand));

        using var gray = CompanionNormalRowProcessor.ConvertToGray(sourceBand);
        using var normalized = CompanionNormalRowProcessor.ResizeToHeight(gray, NormalizedHeight);
        using var foreground = variant == NormalLootRecoveryVariant.AdaptiveThreshold
            ? NormalLootRecoveryPreprocessor.CreateLocalForeground(normalized) : normalized.Clone();
        var image = CompanionNormalRowProcessor.CreateOwnedMat(destination =>
            Cv2.CopyMakeBorder(foreground, destination, 0, 0, 0, 5, BorderTypes.Constant, Scalar.Black));
        // The announcement has neither the normal row's left inset nor its
        // quantity location. Read any amount from the complete textual suffix.
        return new(image, null, normalized.Width, 1f);
    }

    public static bool PassesGeometryGate(CompanionOcrWordGeometry word, int recognizedTextWidth) =>
        word.Status == CompanionOcrGeometryStatus.Success && recognizedTextWidth > 0 &&
        float.IsFinite(word.X) && float.IsFinite(word.Y) &&
        float.IsFinite(word.Width) && float.IsFinite(word.Height) &&
        word.X >= 0 && word.Y >= 0 && word.Width > 0 && word.Height > 0 &&
        word.X < recognizedTextWidth && word.X + word.Width <= recognizedTextWidth + 5d &&
        word.Y + word.Height <= NormalizedHeight;
}
