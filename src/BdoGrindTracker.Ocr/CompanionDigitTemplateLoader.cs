using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>The three UI-font families used by BDO Companion's digit-template loader.</summary>
public enum CompanionUiFontType : byte
{
    StrongSword = 0,
    DejaVu = 1,
    CabinDroid = 2,
}

/// <summary>An encoded digit source from one of Companion's three 0.png-9.png resource tables.</summary>
public sealed record CompanionEncodedDigitTemplateSource(
    CompanionUiFontType FontType,
    int Digit,
    ReadOnlyMemory<byte> ImageBytes);

/// <summary>
/// Owns the decoded and prepared matrices returned by <see cref="CompanionDigitTemplateLoader"/>.
/// A <see cref="CompanionQuantityRecognizer"/> clones these matrices, so this set may be disposed
/// immediately after constructing the recognizer.
/// </summary>
public sealed class CompanionDigitTemplateSet : IDisposable
{
    private readonly CompanionQuantityTemplate[] _templates;
    private bool _disposed;

    internal CompanionDigitTemplateSet(CompanionQuantityTemplate[] templates)
    {
        _templates = templates;
    }

    public IReadOnlyList<CompanionQuantityTemplate> Templates
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _templates;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var template in _templates)
        {
            template.Image.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// Owns the two independently built template sets used by BDO Companion's configured dual-set
/// loader call site.
/// </summary>
public sealed class CompanionDigitTemplateSetPair : IDisposable
{
    private bool _disposed;

    internal CompanionDigitTemplateSetPair(
        CompanionDigitTemplateSet first,
        CompanionDigitTemplateSet second)
    {
        First = first;
        Second = second;
    }

    public CompanionDigitTemplateSet First { get; }

    public CompanionDigitTemplateSet Second { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        First.Dispose();
        Second.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Prepares digit images with BDO Companion's statically verified template-loader operations.
/// Companion has an independent ten-image resource table for every UI-font profile.
/// </summary>
public sealed class CompanionDigitTemplateLoader
{
    private const float NormalInitialThreshold = 0.7f;
    private const float DigitOneConfiguredAdjustment = 0.02f;
    private const float DigitEightConfiguredAdjustment = -0.05f;

    /// <summary>
    /// Maps the value captured from GameOption.txt's UIFontType setting to Companion's internal
    /// font family. A missing setting is represented by <see langword="null"/>.
    /// </summary>
    public static CompanionUiFontType MapGameOptionUiFontType(int? uiFontType)
    {
        return uiFontType switch
        {
            0 => CompanionUiFontType.DejaVu,
            2 => CompanionUiFontType.StrongSword,
            null => CompanionUiFontType.CabinDroid,
            _ => CompanionUiFontType.StrongSword,
        };
    }

    /// <summary>
    /// Loads one set with the primitive Companion loader: digits are attempted from 0 through 9,
    /// absent digits are skipped, and every present digit receives the supplied threshold.
    /// </summary>
    public CompanionDigitTemplateSet Load(
        IEnumerable<CompanionEncodedDigitTemplateSource> sources,
        CompanionUiFontType fontType,
        float targetHeight,
        float threshold)
    {
        ValidateFontType(fontType);
        ValidateTargetHeight(targetHeight);
        ValidateThreshold(threshold);

        var selectedSources = SelectSources(sources, fontType);
        return LoadSelected(
            selectedSources,
            targetHeight,
            _ => threshold);
    }

    /// <summary>
    /// Loads the single template set used by Companion's normal loot-quantity worker. Template
    /// height is exactly 24 pixels; UI scale affects only its per-digit thresholds.
    /// </summary>
    public CompanionDigitTemplateSet LoadNormalQuantity(
        IEnumerable<CompanionEncodedDigitTemplateSource> sources,
        CompanionUiFontType fontType,
        float uiScale)
    {
        ValidateFontType(fontType);
        ValidateUiScale(uiScale);

        var selectedSources = SelectSources(sources, fontType);
        return LoadSelected(
            selectedSources,
            targetHeight: 24f,
            digit => GetNormalQuantityThreshold(fontType, uiScale, digit));
    }

    /// <summary>
    /// Loads the two sets from Companion's second verified loader call site. This recipe is kept
    /// separate from <see cref="LoadNormalQuantity"/> because it is not the normal trash path.
    /// </summary>
    public CompanionDigitTemplateSetPair LoadConfiguredPair(
        IEnumerable<CompanionEncodedDigitTemplateSource> sources,
        CompanionUiFontType fontType,
        bool customHp,
        float uiScale)
    {
        ValidateFontType(fontType);
        ValidateUiScale(uiScale);

        var selectedSources = SelectSources(sources, fontType);

        float firstBaseHeight;
        float firstBaseThreshold;
        float secondBaseHeight;
        float secondBaseThreshold;

        if (customHp)
        {
            firstBaseHeight = 27f;
            firstBaseThreshold = 0.8f;
            secondBaseHeight = 10f;
            secondBaseThreshold = 0.7f;
        }
        else
        {
            firstBaseHeight = fontType == CompanionUiFontType.StrongSword ? 22f : 23f;
            firstBaseThreshold = 0.72f;
            secondBaseHeight = 13f;
            secondBaseThreshold = fontType == CompanionUiFontType.StrongSword ? 0.76f : 0.68f;
        }

        CompanionDigitTemplateSet? first = null;
        try
        {
            first = LoadSelected(
                selectedSources,
                firstBaseHeight * uiScale,
                digit => ApplyConfiguredDigitAdjustment(firstBaseThreshold, digit));
            var second = LoadSelected(
                selectedSources,
                secondBaseHeight * uiScale,
                digit => ApplyConfiguredDigitAdjustment(secondBaseThreshold, digit));
            return new CompanionDigitTemplateSetPair(first, second);
        }
        catch
        {
            first?.Dispose();
            throw;
        }
    }

    /// <summary>Returns the exact threshold assigned by the normal loot-quantity worker.</summary>
    public static float GetNormalQuantityThreshold(
        CompanionUiFontType fontType,
        float uiScale,
        int digit)
    {
        ValidateFontType(fontType);
        ValidateUiScale(uiScale);
        ValidateDigit(digit);

        return digit switch
        {
            0 => fontType == CompanionUiFontType.StrongSword ? 0.62f : 0.61f,
            1 => fontType == CompanionUiFontType.StrongSword && uiScale < 1f
                ? 0.75f
                : 0.77f,
            2 => fontType == CompanionUiFontType.CabinDroid ? 0.738f : 0.75f,
            3 => fontType switch
            {
                CompanionUiFontType.DejaVu => 0.74f,
                CompanionUiFontType.CabinDroid => 0.705f,
                _ => uiScale == 1f ? 0.74f : 0.7f,
            },
            4 => fontType switch
            {
                CompanionUiFontType.StrongSword => 0.74f,
                CompanionUiFontType.DejaVu => 0.6f,
                _ => 0.63f,
            },
            5 => 0.74f,
            6 => fontType == CompanionUiFontType.StrongSword ? 0.7f : 0.69f,
            7 => 0.787f,
            8 => fontType switch
            {
                CompanionUiFontType.StrongSword => uiScale == 1f ? 0.72f : 0.64f,
                CompanionUiFontType.DejaVu => uiScale < 1f ? 0.7f : 0.75f,
                _ => 0.72f,
            },
            9 => fontType == CompanionUiFontType.StrongSword ? 0.76f : 0.745f,
            _ => NormalInitialThreshold,
        };
    }

    private static CompanionDigitTemplateSet LoadSelected(
        IReadOnlyDictionary<int, ReadOnlyMemory<byte>> selectedSources,
        float targetHeight,
        Func<int, float> thresholdSelector)
    {
        ValidateTargetHeight(targetHeight);

        var loaded = new List<CompanionQuantityTemplate>(capacity: 10);
        try
        {
            for (var digit = 0; digit <= 9; digit++)
            {
                if (!selectedSources.TryGetValue(digit, out var encoded))
                {
                    continue;
                }

                var threshold = thresholdSelector(digit);
                ValidateThreshold(threshold);
                var image = PrepareImage(encoded, targetHeight);
                loaded.Add(new CompanionQuantityTemplate(digit, image, threshold));
            }

            return new CompanionDigitTemplateSet(loaded.ToArray());
        }
        catch
        {
            foreach (var template in loaded)
            {
                template.Image.Dispose();
            }

            throw;
        }
    }

    private static Mat PrepareImage(ReadOnlyMemory<byte> encoded, float targetHeight)
    {
        if (encoded.IsEmpty)
        {
            throw new ArgumentException("Encoded digit template bytes must not be empty.", nameof(encoded));
        }

        using var decoded = Cv2.ImDecode(encoded.Span, ImreadModes.Grayscale);
        if (decoded.Empty())
        {
            throw new ArgumentException(
                "Encoded digit template bytes do not contain a supported image.",
                nameof(encoded));
        }

        using var blurred = new Mat();
        Cv2.GaussianBlur(
            decoded,
            blurred,
            new OpenCvSharp.Size(3, 3),
            sigmaX: 0,
            sigmaY: 0,
            borderType: BorderTypes.Default);

        var targetRows = RoundLikeC(targetHeight);
        var targetColumns = RoundLikeC(((float)blurred.Cols / blurred.Rows) * targetHeight);
        if (targetRows <= 0 || targetColumns <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetHeight),
                targetHeight,
                "The requested height must produce positive template dimensions.");
        }

        if (blurred.Rows == targetRows && blurred.Cols == targetColumns)
        {
            return blurred.Clone();
        }

        var resized = new Mat();
        var interpolation = blurred.Rows < targetRows
            ? InterpolationFlags.Cubic
            : InterpolationFlags.Area;
        Cv2.Resize(
            blurred,
            resized,
            new OpenCvSharp.Size(targetColumns, targetRows),
            fx: 0,
            fy: 0,
            interpolation: interpolation);
        return resized;
    }

    private static Dictionary<int, ReadOnlyMemory<byte>> SelectSources(
        IEnumerable<CompanionEncodedDigitTemplateSource> sources,
        CompanionUiFontType fontType)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var selected = new Dictionary<int, ReadOnlyMemory<byte>>();
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ValidateFontType(source.FontType);
            ValidateDigit(source.Digit);

            if (source.FontType != fontType)
            {
                continue;
            }

            if (!selected.TryAdd(source.Digit, source.ImageBytes))
            {
                throw new ArgumentException(
                    $"More than one template was supplied for digit {source.Digit} and font {fontType}.",
                    nameof(sources));
            }
        }

        return selected;
    }

    private static float ApplyConfiguredDigitAdjustment(float threshold, int digit)
    {
        return digit switch
        {
            1 => threshold + DigitOneConfiguredAdjustment,
            8 => threshold + DigitEightConfiguredAdjustment,
            _ => threshold,
        };
    }

    private static int RoundLikeC(float value)
    {
        return checked((int)MathF.Round(value, MidpointRounding.AwayFromZero));
    }

    private static void ValidateFontType(CompanionUiFontType fontType)
    {
        if (fontType is < CompanionUiFontType.StrongSword or > CompanionUiFontType.CabinDroid)
        {
            throw new ArgumentOutOfRangeException(nameof(fontType), fontType, "Unknown UI font type.");
        }
    }

    private static void ValidateDigit(int digit)
    {
        if ((uint)digit > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(digit), digit, "A template digit must be between 0 and 9.");
        }
    }

    private static void ValidateTargetHeight(float targetHeight)
    {
        if (!float.IsFinite(targetHeight) || targetHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetHeight),
                targetHeight,
                "Template height must be finite and positive.");
        }
    }

    private static void ValidateUiScale(float uiScale)
    {
        if (!float.IsFinite(uiScale) || uiScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uiScale),
                uiScale,
                "UI scale must be finite and positive.");
        }
    }

    private static void ValidateThreshold(float threshold)
    {
        if (!float.IsFinite(threshold) || threshold <= 0f || threshold > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                threshold,
                "Template threshold must be finite and in the interval (0, 1].");
        }
    }
}
