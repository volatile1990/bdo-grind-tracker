using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Size = OpenCvSharp.Size;

namespace BdoGrindTracker.Ocr;

/// <summary>A complete personal-item message, with coordinates relative to the original chat crop.</summary>
public sealed record PrivateItemChatLine(string ItemName, int Quantity, int Y, string RawText)
{
    public Rect Bounds { get; init; }
}

/// <summary>
/// Reads complete chat lines without the transient loot panel's color and geometry gates.
/// Parsing a line does not establish that it is new; callers must reconcile scrolling history.
/// </summary>
public sealed class PrivateItemChatOcrReader
{
    private const int MaximumLines = 128;
    private readonly OcrEngine _engine;

    private PrivateItemChatOcrReader(OcrEngine engine)
    {
        _engine = engine;
        LanguageTag = engine.RecognizerLanguage.LanguageTag;
    }

    public string LanguageTag { get; }

    public static PrivateItemChatOcrReader? TryCreate(string preferredLanguageTag = "en-US")
    {
        try
        {
            var preferred = new Language(preferredLanguageTag);
            var engine = OcrEngine.IsLanguageSupported(preferred)
                ? OcrEngine.TryCreateFromLanguage(preferred)
                : null;
            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            return engine is null ? null : new PrivateItemChatOcrReader(engine);
        }
        catch (Exception exception) when (CompanionWindowsOcrRecognizer.IsRecoverableRuntimeFailure(exception))
        {
            return null;
        }
    }

    public IReadOnlyList<PrivateItemChatLine> Read(
        Mat croppedChat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(croppedChat);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CompanionWindowsOcrRecognizer.SupportsImage(croppedChat))
        {
            throw new ArgumentException(
                $"Chat OCR requires a non-empty 8-bit one-, three-, or four-channel image " +
                $"no larger than {OcrEngine.MaxImageDimension} pixels per side.",
                nameof(croppedChat));
        }

        using var gray = new Mat();
        switch (croppedChat.Channels())
        {
            case 1:
                croppedChat.CopyTo(gray);
                break;
            case 3:
                Cv2.CvtColor(croppedChat, gray, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                Cv2.CvtColor(croppedChat, gray, ColorConversionCodes.BGRA2GRAY);
                break;
        }

        var scale = Math.Min(
            1.5,
            OcrEngine.MaxImageDimension / (double)Math.Max(gray.Width, gray.Height));
        using var enlarged = new Mat();
        Cv2.Resize(gray, enlarged, new Size(
            Math.Max(1, (int)Math.Floor(gray.Width * scale)),
            Math.Max(1, (int)Math.Floor(gray.Height * scale))),
            interpolation: InterpolationFlags.Cubic);

        var lines = ReadLines(enlarged, croppedChat.Size(), cancellationToken);
        if (lines.Count != 0)
        {
            return lines;
        }

        // A second pass helps white-on-dark text. Never merge two interpretations of a row.
        cancellationToken.ThrowIfCancellationRequested();
        Cv2.BitwiseNot(enlarged, enlarged);
        return ReadLines(enlarged, croppedChat.Size(), cancellationToken);
    }

    private IReadOnlyList<PrivateItemChatLine> ReadLines(
        Mat gray,
        Size originalSize,
        CancellationToken cancellationToken)
    {
        var width = gray.Width;
        var height = gray.Height;
        var pixels = new byte[checked(width * height)];
        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(gray.Ptr(row), pixels, row * width, width);
        }

        var buffer = CryptographicBuffer.CreateFromByteArray(pixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Gray8,
            gray.Width,
            gray.Height,
            BitmapAlphaMode.Ignore);
        var result = _engine.RecognizeAsync(bitmap)
            .AsTask(cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();

        var scaleX = originalSize.Width / (double)gray.Width;
        var scaleY = originalSize.Height / (double)gray.Height;
        var lines = new List<PrivateItemChatLine>();
        foreach (var line in result.Lines.Take(MaximumLines))
        {
            if (line.Words.Count == 0 ||
                !PrivateItemChatParser.TryParse(line.Text, out var itemName, out var quantity))
            {
                continue;
            }

            var words = line.Words.Select(static word => word.BoundingRect).ToArray();
            var left = Math.Clamp((int)Math.Floor(words.Min(static word => word.X) * scaleX), 0, originalSize.Width);
            var top = Math.Clamp((int)Math.Floor(words.Min(static word => word.Y) * scaleY), 0, originalSize.Height);
            var right = Math.Clamp((int)Math.Ceiling(words.Max(static word => word.Right) * scaleX), left, originalSize.Width);
            var bottom = Math.Clamp((int)Math.Ceiling(words.Max(static word => word.Bottom) * scaleY), top, originalSize.Height);
            lines.Add(new PrivateItemChatLine(itemName, quantity, top, line.Text)
            {
                Bounds = new Rect(left, top, right - left, bottom - top),
            });
        }

        return lines.OrderBy(static line => line.Y).ToArray();
    }
}

/// <summary>
/// Accepts only complete English personal-loot payloads. It deliberately does not repair
/// ambiguous digits, infer a missing quantity, or interpret arbitrary player chat.
/// </summary>
public static partial class PrivateItemChatParser
{
    public static bool TryParse(string? text, out string itemName, out int quantity)
    {
        itemName = string.Empty;
        quantity = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 512 || text.Contains('\n') || text.Contains('\r'))
        {
            return false;
        }

        var match = MessagePattern().Match(text);
        if (!match.Success ||
            !int.TryParse(match.Groups["quantity"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedQuantity) ||
            parsedQuantity <= 0)
        {
            return false;
        }

        var parsedName = WhitespacePattern().Replace(match.Groups["item"].Value.Trim(), " ");
        if (parsedName.Length == 0)
        {
            return false;
        }

        itemName = parsedName;
        quantity = parsedQuantity;
        return true;
    }

    // OCR sometimes reads the item icon as a short token such as "t'". Ignore only
    // that bounded token between the complete system prefix and the actual bracket.
    [GeneratedRegex(
        @"\A\s*(?:(?:\[System\]|System)\s+)?You\s+have\s+obtained\s*(?:[^\[\]\s]{1,3}\s*)?\[(?<item>[^\[\]\r\n]{1,160})\]\s*[xX×]\s*(?<quantity>[0-9]{1,10})\s*\.?\s*(?:\((?:[01][0-9]|2[0-3]):[0-5][0-9](?::[0-5][0-9])?\))?\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 50)]
    private static partial Regex MessagePattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 50)]
    private static partial Regex WhitespacePattern();
}
