using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// Prepared full-width row produced by BDO Companion 0.7.4's mode-one worker.
/// The instance owns <see cref="NameImage"/> and must be disposed.
/// </summary>
public sealed class CompanionRareRowResult : IDisposable
{
    internal CompanionRareRowResult(
        int y,
        bool isBlank,
        double averageLuma,
        int recognizedTextWidth,
        Mat? nameImage)
    {
        Y = y;
        IsBlank = isBlank;
        AverageLuma = averageLuma;
        RecognizedTextWidth = recognizedTextWidth;
        NameImage = nameImage;
    }

    public int Y { get; }

    public bool IsBlank { get; }

    public double AverageLuma { get; }

    /// <summary>Full normalized row width passed to Companion's text contract.</summary>
    public int RecognizedTextWidth { get; }

    /// <summary>Mode one never performs quantity-template recognition.</summary>
    public int TemplateQuantity => CompanionNormalRowProcessor.MissingQuantity;

    public int LeftmostQuantityX => 0;

    public float QuantityScore => 0f;

    /// <summary>Mode one forces the pre-OCR image scale to one.</summary>
    public float NameScale => 1f;

    public Mat? NameImage { get; }

    public void Dispose() => NameImage?.Dispose();
}

/// <summary>
/// Exact image preparation performed by BDO Companion 0.7.4's mode-one rare/full-row
/// worker. It intentionally has no quantity recognizer and no fallback processing.
/// </summary>
public sealed class CompanionRareRowProcessor
{
    private const int NormalizedHeight = 100;
    private const float ForcedNameScale = 1f;

    public CompanionRareRowResult Process(
        Mat sourceBand,
        int y,
        float uiScale,
        int fontType,
        bool isHdr = false)
    {
        ArgumentNullException.ThrowIfNull(sourceBand);
        if (sourceBand.Empty())
        {
            throw new ArgumentException("The source band must not be empty.", nameof(sourceBand));
        }

        if (sourceBand.Type() != MatType.CV_8UC3)
        {
            throw new ArgumentException(
                "Companion's mode-one worker consumes an 8-bit three-channel BGR band.",
                nameof(sourceBand));
        }

        if (!float.IsFinite(uiScale) || uiScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(uiScale));
        }

        if (fontType < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontType));
        }

        // The producer creates HSV first (OpenCV conversion code 40), then gray
        // (conversion code 6), and queues both matrices for the shared worker.
        using var hsv = CompanionNormalRowProcessor.ConvertToHsv(sourceBand);
        using var gray = CompanionNormalRowProcessor.ConvertToGray(sourceBand);
        var averageLuma = Cv2.Mean(gray).Val0;

        // Unlike mode zero, mode one has no average-luma < 25 early return. Both
        // modes still share the later inverse-threshold 0.993 blank test.
        // Keep the pre-OR HSV mask: mode one's font>1 branch later resizes this
        // exact matrix, whereas mode zero builds a separate bright-gray mask.
        using var textMask = new Mat();
        CreateInitialTextMask(hsv, isHdr, textMask);
        if (Cv2.Mean(textMask).Val0 / 255d > 0.15d)
        {
            // The mode-one worker replaces masks covering more than 15 percent
            // of the row with its narrower neutral-white range.
            Cv2.InRange(
                hsv,
                new Scalar(0, 0, 180),
                new Scalar(45, 60, 255),
                textMask);
        }
        using var blackMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(4, 4, 4), blackMask);
        using var combinedMask = new Mat();
        Cv2.BitwiseOr(textMask, blackMask, combinedMask);

        using var maskedGray = new Mat();
        Cv2.BitwiseAnd(gray, gray, maskedGray, combinedMask);
        using var binary = new Mat();
        Cv2.Threshold(
            maskedGray,
            binary,
            uiScale == 1f ? 170 : 140,
            255,
            ThresholdTypes.Binary);

        if (CompanionNormalRowProcessor.IsBlank(binary))
        {
            return CreateBlank(y, averageLuma, binary.Width, binary.Height);
        }

        using var normalized = CompanionNormalRowProcessor.ResizeToHeight(
            binary,
            NormalizedHeight);
        using var blurred = new Mat();
        Cv2.GaussianBlur(
            normalized,
            blurred,
            new OpenCvSharp.Size(3, 3),
            0,
            0,
            BorderTypes.Reflect101);

        using var fontSpecific = CreateFontSpecificSource(
            fontType,
            textMask,
            normalized,
            blurred);
        using var nameCrop = new Mat(
            fontSpecific,
            new Rect(0, 0, normalized.Width, normalized.Height));
        using var scaledName = new Mat();
        Cv2.Resize(
            nameCrop,
            scaledName,
            new OpenCvSharp.Size(),
            ForcedNameScale,
            ForcedNameScale,
            InterpolationFlags.Cubic);
        var borderedName = CompanionNormalRowProcessor.CreateOwnedMat(destination =>
            Cv2.CopyMakeBorder(
                scaledName,
                destination,
                0,
                0,
                0,
                5,
                BorderTypes.Constant,
                Scalar.Black));

        return new CompanionRareRowResult(
            y,
            isBlank: false,
            averageLuma,
            normalized.Width,
            borderedName);
    }

    private static CompanionRareRowResult CreateBlank(
        int y,
        double averageLuma,
        int width,
        int height)
    {
        return new CompanionRareRowResult(
            y,
            isBlank: true,
            averageLuma,
            Math.Max(CompanionNormalRowProcessor.CalculateNormalizedWidth(width, height), 0),
            nameImage: null);
    }

    internal static void CreateInitialTextMask(Mat hsv, bool isHdr, Mat destination)
    {
        var lower = isHdr
            ? new Scalar(0, 0, 160)
            : new Scalar(10, 35, 140);
        var upper = isHdr
            ? new Scalar(45, 130, 255)
            : new Scalar(30, 110, 255);
        Cv2.InRange(hsv, lower, upper, destination);
    }

    private static Mat CreateFontSpecificSource(
        int fontType,
        Mat textMask,
        Mat normalized,
        Mat blurred)
    {
        if (fontType == 0)
        {
            return blurred.Clone();
        }

        if (fontType == 1)
        {
            return normalized.Clone();
        }

        return CompanionNormalRowProcessor.ResizeToHeight(textMask, NormalizedHeight);
    }
}
