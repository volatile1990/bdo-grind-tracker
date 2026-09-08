using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// Prepares the first Windows OCR read of a normal loot row after HDR capture has
/// been converted to SDR. Tone mapping changes the text/background brightness
/// relationship, so the original SDR mask cannot be reused for these pixels.
/// </summary>
public static class ToneMappedNormalRowProcessor
{
    private const int NormalizedHeight = 100;
    private const int NameLeft = 10;
    private const int TextTop = 25;
    private const int TextBottom = 70;

    public static CompanionNormalRowResult Process(Mat sourceBand, int y)
    {
        ArgumentNullException.ThrowIfNull(sourceBand);
        if (sourceBand.Empty() || sourceBand.Type() != MatType.CV_8UC3)
            throw new ArgumentException("Tone-mapped normal rows require a non-empty BGR image.", nameof(sourceBand));
        if (CompanionNormalRowProcessor.CalculateNormalizedWidth(sourceBand.Width, sourceBand.Height) <= NameLeft)
            throw new ArgumentException("The row must extend beyond the name's left margin.", nameof(sourceBand));

        using var gray = CompanionNormalRowProcessor.ConvertToGray(sourceBand);
        var averageLuma = Cv2.Mean(gray).Val0;
        using var hsv = CompanionNormalRowProcessor.ConvertToHsv(sourceBand);
        using var foreground = new Mat();
        // Captured white lettering remains below 240 after tone mapping. A
        // 230 cutoff retains its strokes while rejecting pale neutral scenery
        // admitted by Companion's original SDR range (approximately 160–250).
        Cv2.InRange(hsv, new Scalar(0, 0, 230), new Scalar(180, 12, 255), foreground);
        using var normalized = CompanionNormalRowProcessor.ResizeToHeight(foreground, NormalizedHeight);
        // Retain the normalized Y origin for the existing first-word geometry gate.
        Cv2.Rectangle(normalized, new Rect(0, 0, normalized.Width, TextTop), Scalar.Black, -1);
        Cv2.Rectangle(normalized, new Rect(0, TextBottom, normalized.Width, NormalizedHeight - TextBottom), Scalar.Black, -1);
        var nameWidth = normalized.Width - NameLeft;
        var nameScale = CompanionNormalRowProcessor.CalculateNameScale(nameWidth);
        if (CompanionNormalRowProcessor.IsBlank(normalized))
            return new(y, true, averageLuma, nameWidth, CompanionNormalRowProcessor.MissingQuantity,
                0, 0, nameScale, null);

        using var nameCrop = new Mat(normalized, new Rect(NameLeft, 0, nameWidth, NormalizedHeight));
        using var scaled = new Mat();
        Cv2.Resize(nameCrop, scaled, new OpenCvSharp.Size(), nameScale, nameScale, InterpolationFlags.Cubic);
        var nameImage = CompanionNormalRowProcessor.CreateOwnedMat(destination =>
            Cv2.CopyMakeBorder(scaled, destination, 0, 0, 0, 5, BorderTypes.Constant, Scalar.Black));
        // A template match inside the item name must not shorten this crop or
        // fabricate quantity 1. Windows OCR reads the complete name and suffix;
        // a genuinely unreadable amount keeps the existing missing sentinel.
        return new(y, false, averageLuma, nameWidth, CompanionNormalRowProcessor.MissingQuantity,
            0, 0, nameScale, nameImage);
    }
}
