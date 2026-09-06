using System.Runtime.InteropServices;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BdoGrindTracker.Ocr;

public enum CompanionOcrGeometryStatus
{
    Missing,
    Success,
    Error,
}

public readonly record struct CompanionOcrWordGeometry(
    CompanionOcrGeometryStatus Status,
    float X,
    float Y,
    float Width,
    float Height);

public sealed record CompanionOcrResult(
    string Text,
    CompanionOcrWordGeometry FirstWord);

/// <summary>
/// Thin Windows.Media.Ocr adapter matching Companion's OCR-result access pattern:
/// complete <see cref="OcrResult.Text"/> plus geometry from only the first word of the
/// first line.
/// </summary>
public sealed class CompanionWindowsOcrRecognizer
{
    private const int InvalidArgumentHResult = unchecked((int)0x80070057);

    private readonly OcrEngine _engine;

    private CompanionWindowsOcrRecognizer(OcrEngine engine)
    {
        _engine = engine;
        LanguageTag = engine.RecognizerLanguage.LanguageTag;
    }

    public string BackendName => "windows-media-ocr";

    public string LanguageTag { get; }

    public static CompanionWindowsOcrRecognizer? TryCreate(
        string preferredLanguageTag = "en-US",
        bool throwIfUnavailable = false)
    {
        Exception? initializationError = null;
        OcrEngine? engine = null;

        try
        {
            var preferred = CreatePreferredLanguage(preferredLanguageTag);
            if (OcrEngine.IsLanguageSupported(preferred))
            {
                engine = OcrEngine.TryCreateFromLanguage(preferred);
            }
        }
        catch (ArgumentException exception) when (
            exception.ParamName == nameof(preferredLanguageTag))
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableRuntimeFailure(exception))
        {
            initializationError = exception;
        }

        if (engine is null)
        {
            try
            {
                engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
            catch (Exception exception) when (IsRecoverableRuntimeFailure(exception))
            {
                initializationError ??= exception;
            }
        }

        if (engine is not null)
        {
            return new CompanionWindowsOcrRecognizer(engine);
        }

        if (!throwIfUnavailable)
        {
            return null;
        }

        string languages;
        try
        {
            languages = string.Join(
                ", ",
                OcrEngine.AvailableRecognizerLanguages.Select(
                    static language => language.LanguageTag));
        }
        catch (Exception exception) when (IsRecoverableRuntimeFailure(exception))
        {
            languages = string.Empty;
            initializationError ??= exception;
        }

        var message = languages.Length == 0
            ? "Windows OCR has no installed recognition language."
            : $"Windows OCR could not create an engine. Installed languages: {languages}.";
        throw new InvalidOperationException(message, initializationError);
    }

    public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!SupportsImage(image))
        {
            throw new ArgumentException(
                $"Windows OCR requires a non-empty 8-bit one-, three-, or four-channel " +
                $"image no larger than {OcrEngine.MaxImageDimension} pixels per side.",
                nameof(image));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var convertedGray = ConvertToGray8IfNeeded(image);
        var gray = convertedGray ?? image;
        var pixels = CopyPixels(gray);
        var buffer = CryptographicBuffer.CreateFromByteArray(pixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Gray8,
            gray.Width,
            gray.Height,
            BitmapAlphaMode.Ignore);
        var result = _engine
            .RecognizeAsync(bitmap)
            .AsTask(cancellationToken)
            .GetAwaiter()
            .GetResult();

        return new CompanionOcrResult(result.Text, ReadFirstWordGeometry(result));
    }

    internal static bool PassesNormalGeometryGate(
        CompanionOcrWordGeometry geometry,
        float uiScale)
    {
        if (geometry.Status == CompanionOcrGeometryStatus.Error)
        {
            return true;
        }

        if (geometry.Status != CompanionOcrGeometryStatus.Success || geometry.X > 200f)
        {
            return false;
        }

        var minimumY = uiScale <= 1f ? 26f : 19f;
        var maximumY = uiScale <= 1f ? 70f : 77f;
        return geometry.Y >= minimumY && geometry.Y <= maximumY;
    }

    /// <summary>
    /// Applies Companion's mode-dependent first-word gate. Mode one deliberately
    /// bypasses every first-word geometry check; mode zero uses the bounds above.
    /// </summary>
    internal static bool PassesGeometryGate(
        CompanionOcrWordGeometry geometry,
        float uiScale,
        bool rareDropMode) =>
        rareDropMode || PassesNormalGeometryGate(geometry, uiScale);

    internal static bool SupportsImage(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return !image.Empty() &&
            image.Depth() == MatType.CV_8U &&
            image.Channels() is 1 or 3 or 4 &&
            image.Width <= OcrEngine.MaxImageDimension &&
            image.Height <= OcrEngine.MaxImageDimension;
    }

    internal static bool IsRecoverableRuntimeFailure(Exception exception) =>
        exception is COMException or
            ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            UnauthorizedAccessException or
            DllNotFoundException or
            FileNotFoundException or
            BadImageFormatException;

    private static CompanionOcrWordGeometry ReadFirstWordGeometry(OcrResult result)
    {
        if (result.Lines.Count == 0 || result.Lines[0].Words.Count == 0)
        {
            return new CompanionOcrWordGeometry(
                CompanionOcrGeometryStatus.Missing,
                0,
                0,
                0,
                0);
        }

        try
        {
            var bounds = result.Lines[0].Words[0].BoundingRect;
            return new CompanionOcrWordGeometry(
                CompanionOcrGeometryStatus.Success,
                (float)bounds.X,
                (float)bounds.Y,
                (float)bounds.Width,
                (float)bounds.Height);
        }
        catch (Exception exception) when (IsRecoverableRuntimeFailure(exception))
        {
            // Companion's tagged HRESULT-error branch deliberately bypasses the normal
            // X/Y reject while retaining OcrResult.Text.
            return new CompanionOcrWordGeometry(
                CompanionOcrGeometryStatus.Error,
                0,
                0,
                0,
                0);
        }
    }

    private static Language CreatePreferredLanguage(string preferredLanguageTag)
    {
        try
        {
            return new Language(preferredLanguageTag);
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is COMException { HResult: InvalidArgumentHResult })
        {
            throw new ArgumentException(
                "The preferred Windows OCR language must be a valid BCP-47 tag.",
                nameof(preferredLanguageTag),
                exception);
        }
    }

    private static Mat? ConvertToGray8IfNeeded(Mat image)
    {
        if (image.Type() == MatType.CV_8UC1)
        {
            return null;
        }

        var result = new Mat();
        switch (image.Channels())
        {
            case 3:
                Cv2.CvtColor(image, result, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                Cv2.CvtColor(image, result, ColorConversionCodes.BGRA2GRAY);
                break;
            default:
                result.Dispose();
                throw new ArgumentException(
                    "The image must have one, three, or four channels.",
                    nameof(image));
        }

        return result;
    }

    private static byte[] CopyPixels(Mat image)
    {
        var width = image.Width;
        var height = image.Height;
        var length = checked(width * height);
        var pixels = new byte[length];
        if (image.IsContinuous() && image.Step() == width)
        {
            Marshal.Copy(image.Data, pixels, 0, length);
            return pixels;
        }

        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(image.Ptr(row), pixels, row * width, width);
        }

        return pixels;
    }
}
