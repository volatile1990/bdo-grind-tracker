using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// Prepared normal-loot row produced by the statically verified BDO Companion 0.7.4
/// image pipeline. The instance owns <see cref="NameImage"/> and must be disposed.
/// </summary>
public sealed class CompanionNormalRowResult : IDisposable
{
    internal CompanionNormalRowResult(
        int y,
        bool isBlank,
        double averageLuma,
        int recognizedTextWidth,
        int templateQuantity,
        int leftmostQuantityX,
        float quantityScore,
        float nameScale,
        Mat? nameImage,
        float normalizedNameTop = 0)
    {
        Y = y;
        IsBlank = isBlank;
        AverageLuma = averageLuma;
        RecognizedTextWidth = recognizedTextWidth;
        TemplateQuantity = templateQuantity;
        LeftmostQuantityX = leftmostQuantityX;
        QuantityScore = quantityScore;
        NameScale = nameScale;
        NameImage = nameImage;
        NormalizedNameTop = normalizedNameTop;
    }

    public int Y { get; }

    public bool IsBlank { get; }

    public double AverageLuma { get; }

    /// <summary>Width of Companion's pre-scale name rectangle (the logged <c>rw</c>).</summary>
    public int RecognizedTextWidth { get; }

    /// <summary><c>-1</c> is Companion's normal-path missing-quantity sentinel.</summary>
    public int TemplateQuantity { get; }

    public int LeftmostQuantityX { get; }

    public float QuantityScore { get; }

    public float NameScale { get; }

    /// <summary>Y origin of the name crop in the 100-pixel normalized row, before scaling.</summary>
    public float NormalizedNameTop { get; }

    public Mat? NameImage { get; }

    public void Dispose() => NameImage?.Dispose();
}

/// <summary>
/// Exact normal-loot row preparation used by BDO Companion 0.7.4. It contains no
/// adaptive thresholds, learned priors, alternate variants, or fallback heuristics.
/// </summary>
public sealed class CompanionNormalRowProcessor
{
    public const int MissingQuantity = -1;

    private const int NormalizedHeight = 100;
    private const int QuantityRegionLeft = 120;
    private const int QuantityRegionTop = 30;
    private const int QuantityRegionHeight = 70;
    private const int QuantityToNameGap = 45;
    private const float BlankPixelRatio = 0.993f;

    private readonly CompanionQuantityRecognizer? _quantityRecognizer;

    public CompanionNormalRowProcessor(CompanionQuantityRecognizer? quantityRecognizer)
    {
        _quantityRecognizer = quantityRecognizer;
    }

    public CompanionNormalRowResult Process(
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
                "Companion's normal worker consumes an 8-bit three-channel BGR band.",
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

        using var hsv = ConvertToHsv(sourceBand);
        using var gray = ConvertToGray(sourceBand);
        var averageLuma = Cv2.Mean(gray).Val0;

        // Companion returns immediately only for a genuinely dark normal band. The
        // later 0.993 test remains authoritative for translucent but empty HUD rows.
        if (averageLuma < 25d)
        {
            return CreateBlank(y, averageLuma, sourceBand.Width, sourceBand.Height);
        }

        using var textMask = new Mat();
        CreateTextMask(hsv, averageLuma, uiScale, fontType, isHdr, textMask);
        using var blackMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(4, 4, 4), blackMask);
        Cv2.BitwiseOr(textMask, blackMask, textMask);

        using var maskedGray = new Mat();
        Cv2.BitwiseAnd(gray, gray, maskedGray, textMask);
        using var binary = new Mat();
        Cv2.Threshold(
            maskedGray,
            binary,
            uiScale == 1f ? 170 : 140,
            255,
            ThresholdTypes.Binary);

        if (IsBlank(binary))
        {
            return CreateBlank(y, averageLuma, binary.Width, binary.Height);
        }

        using var normalized = ResizeToHeight(binary, NormalizedHeight);
        using var blurred = new Mat();
        Cv2.GaussianBlur(
            normalized,
            blurred,
            new OpenCvSharp.Size(3, 3),
            0,
            0,
            BorderTypes.Reflect101);

        var quantity = RecognizeQuantity(blurred);
        var leftmostQuantityX = ResolveLeftmostQuantityX(quantity);
        var templateQuantity = quantity?.Quantity ?? MissingQuantity;
        var quantityScore = quantity?.AverageScore ?? 0f;

        var nameWidth = ResolveNameWidth(
            normalized.Width,
            quantity is not null,
            leftmostQuantityX);

        var nameTop = nameWidth < 250 ? 10 : 0;
        var nameHeight = nameWidth < 250 ? 80 : 100;
        var nameBounds = new Rect(10, nameTop, nameWidth, nameHeight);

        using var fontSpecific = CreateFontSpecificSource(
            fontType,
            gray,
            hsv,
            normalized,
            blurred);
        using var nameCrop = new Mat(fontSpecific, nameBounds);
        var nameScale = CalculateNameScale(nameWidth);
        using var scaledName = new Mat();
        Cv2.Resize(
            nameCrop,
            scaledName,
            new OpenCvSharp.Size(),
            nameScale,
            nameScale,
            InterpolationFlags.Cubic);
        var borderedName = CreateOwnedMat(destination =>
            Cv2.CopyMakeBorder(
                scaledName,
                destination,
                0,
                0,
                0,
                5,
                BorderTypes.Constant,
                Scalar.Black));

        return new CompanionNormalRowResult(
            y,
            isBlank: false,
            averageLuma,
            nameWidth,
            templateQuantity,
            leftmostQuantityX,
            quantityScore,
            nameScale,
            borderedName,
            normalizedNameTop: nameTop);
    }

    internal static int CalculateMinimumValue(double averageLuma) => averageLuma switch
    {
        < 60f => 160,
        < 80f => 170,
        < 110f => 171,
        < 115f => 172,
        < 120f => 175,
        < 125f => 176,
        < 130f => 178,
        < 135f => 180,
        < 145f => 181,
        < 155f => 182,
        < 165f => 183,
        _ => 185,
    };

    internal static void CreateTextMask(
        Mat hsv,
        double averageLuma,
        float uiScale,
        int fontType,
        bool isHdr,
        Mat destination)
    {
        if (isHdr)
        {
            // BDO Companion 0.7.4 receives this flag from
            // IDXGIOutput6::GetDesc1. Its HDR branch selects only the brightest
            // pixels; it is not a relaxed form of the SDR upper-value cutoff.
            var thresholdLuma = uiScale < 1f ? averageLuma - 5d : averageLuma;
            var brightBand = thresholdLuma < 190d;
            Cv2.InRange(
                hsv,
                new Scalar(0, 0, brightBand ? 250 : 254),
                new Scalar(
                    brightBand ? 158 : 20,
                    brightBand ? 23 : 8,
                    255),
                destination);
            return;
        }

        var adjustedLuma = uiScale < 1f ? averageLuma - 5d : averageLuma;
        var minimumValue = CalculateMinimumValue(adjustedLuma);
        var maximumHue = uiScale == 1f ? 158 : 165;
        var maximumSaturation = uiScale == 1f ? 23 : 30;
        if (uiScale > 1f && fontType != 0)
        {
            minimumValue -= 5;
        }
        else if (uiScale < 1f && fontType != 0)
        {
            minimumValue -= 20;
            maximumSaturation = 45;
        }

        Cv2.InRange(
            hsv,
            new Scalar(0, 0, minimumValue),
            new Scalar(maximumHue, maximumSaturation, 250),
            destination);
    }

    internal static float CalculateNameScale(int nameWidth) => nameWidth switch
    {
        < 120 => 2f,
        < 350 => 1.75f,
        _ => 1.5f,
    };

    internal static bool IsBlank(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty())
        {
            throw new ArgumentException("The image must not be empty.", nameof(image));
        }

        using var gray = image.Channels() > 1 ? ConvertToGray(image) : null;
        var source = gray ?? image;
        using var inverse = new Mat();
        Cv2.Threshold(source, inverse, 10, 255, ThresholdTypes.BinaryInv);
        // FUN_1401717c0 performs both conversions and the division with scalar
        // single-precision instructions (CVTSI2SS/DIVSS/UCOMISS). Keeping this
        // as float is observable at the 0.993 boundary.
        var nonZeroRatio = (float)Cv2.CountNonZero(inverse) /
            checked(source.Rows * source.Cols);
        return nonZeroRatio >= BlankPixelRatio;
    }

    internal static int ResolveLeftmostQuantityX(CompanionQuantityRecognition? quantity)
    {
        if (quantity is null || quantity.IsSpecialOne)
        {
            // Companion's digit-one shortcut runs before it translates regular
            // template coordinates by (+120,+30), and writes final X=0.
            return 0;
        }

        return checked(quantity.X + QuantityRegionLeft);
    }

    internal static int ResolveNameWidth(
        int normalizedWidth,
        bool hasQuantity,
        int leftmostQuantityX)
    {
        var nameWidth = hasQuantity
            ? RoundAwayFromZero(leftmostQuantityX - QuantityToNameGap)
            : normalizedWidth - 10;
        return nameWidth < 0 ? normalizedWidth - 10 : nameWidth;
    }

    private CompanionQuantityRecognition? RecognizeQuantity(Mat blurred)
    {
        if (_quantityRecognizer is null || blurred.Width <= QuantityRegionLeft)
        {
            return null;
        }

        using var quantityRegion = new Mat(
            blurred,
            new Rect(
                QuantityRegionLeft,
                QuantityRegionTop,
                blurred.Width - QuantityRegionLeft,
                QuantityRegionHeight));
        return _quantityRecognizer.Recognize(quantityRegion);
    }

    private static CompanionNormalRowResult CreateBlank(
        int y,
        double averageLuma,
        int width,
        int height)
    {
        var normalizedWidth = CalculateNormalizedWidth(width, height);
        return new CompanionNormalRowResult(
            y,
            isBlank: true,
            averageLuma,
            Math.Max(normalizedWidth - 10, 0),
            MissingQuantity,
            0,
            0,
            1f,
            null);
    }

    private static Mat CreateFontSpecificSource(
        int fontType,
        Mat gray,
        Mat hsv,
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

        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 200), new Scalar(158, 23, 255), mask);
        using var filtered = new Mat();
        Cv2.BitwiseAnd(gray, gray, filtered, mask);
        return ResizeToHeight(filtered, NormalizedHeight);
    }

    internal static int CalculateNormalizedWidth(int width, int height)
    {
        return height <= 0
            ? width
            : checked((int)Math.Round(
                width * (NormalizedHeight / (double)height),
                MidpointRounding.ToEven));
    }

    internal static Mat ResizeToHeight(Mat source, int height)
    {
        var scale = height / (double)source.Rows;
        return CreateOwnedMat(destination =>
            Cv2.Resize(
                source,
                destination,
                new OpenCvSharp.Size(),
                scale,
                scale,
                InterpolationFlags.Cubic));
    }

    internal static Mat ConvertToGray(Mat source)
    {
        if (source.Type() == MatType.CV_8UC1)
        {
            return source.Clone();
        }

        switch (source.Type().Channels)
        {
            case 3:
                return CreateOwnedMat(destination =>
                    Cv2.CvtColor(source, destination, ColorConversionCodes.BGR2GRAY));
            case 4:
                return CreateOwnedMat(destination =>
                    Cv2.CvtColor(source, destination, ColorConversionCodes.BGRA2GRAY));
            default:
                throw new ArgumentException(
                    "Companion row input must be an 8-bit one-, three-, or four-channel image.",
                    nameof(source));
        }
    }

    internal static Mat ConvertToHsv(Mat source)
    {
        using var bgr = source.Type().Channels switch
        {
            1 => ConvertGrayToBgr(source),
            3 => source.Clone(),
            4 => ConvertBgraToBgr(source),
            _ => throw new ArgumentException(
                "Companion row input must be an 8-bit one-, three-, or four-channel image.",
                nameof(source)),
        };
        return CreateOwnedMat(destination =>
            Cv2.CvtColor(bgr, destination, ColorConversionCodes.BGR2HSV));
    }

    private static Mat ConvertGrayToBgr(Mat source)
    {
        return CreateOwnedMat(destination =>
            Cv2.CvtColor(source, destination, ColorConversionCodes.GRAY2BGR));
    }

    private static Mat ConvertBgraToBgr(Mat source)
    {
        return CreateOwnedMat(destination =>
            Cv2.CvtColor(source, destination, ColorConversionCodes.BGRA2BGR));
    }

    internal static Mat CreateOwnedMat(Action<Mat> populate)
    {
        var result = new Mat();
        try
        {
            populate(result);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static int RoundAwayFromZero(float value) =>
        checked((int)MathF.Round(value, MidpointRounding.AwayFromZero));
}
