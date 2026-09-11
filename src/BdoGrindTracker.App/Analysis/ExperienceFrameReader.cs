using System.Globalization;
using System.Text.RegularExpressions;
using BdoGrindTracker.Ocr;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

namespace BdoGrindTracker.App.Analysis;

internal sealed record ExperienceReading(int Level, decimal Percent);

internal interface IExperienceFrameReader : IDisposable
{
    ExperienceReading? Read(Bitmap frame, CancellationToken cancellationToken);
}

/// <summary>Reads the large character level and its smaller XP percentage directly beneath it.</summary>
internal sealed partial class ExperienceFrameReader : IExperienceFrameReader
{
    private readonly Func<Mat, CancellationToken, CompanionOcrResult>? _recognize;
    private readonly Func<ExperienceHudConfiguration?>? _readConfiguration;
    private CompanionWindowsOcrRecognizer? _engine;
    private bool _disposed;

    internal ExperienceFrameReader(Func<Mat, CancellationToken, CompanionOcrResult>? recognize = null,
        Func<ExperienceHudConfiguration?>? readConfiguration = null)
    {
        _recognize = recognize;
        _readConfiguration = readConfiguration;
    }

    public ExperienceReading? Read(Bitmap frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        var region = HudRegion(frame.Size);
        if (region.Width < 30 || region.Height < 30) return null;
        if (_recognize is null)
        {
            _engine ??= CompanionWindowsOcrRecognizer.TryCreate();
            if (_engine is null) return null;
        }
        // BDO saves UI scale independently of resolution or Windows DPI. Refresh
        // it for each (minute-spaced) observation, including after profile changes.
        var configuration = _readConfiguration?.Invoke();
        if (configuration is { } hud && hud.ScreenWidth == frame.Width && hud.ScreenHeight == frame.Height
            && double.IsFinite(hud.UiScale) && hud.UiScale is >= .5 and <= 3)
        {
            var calibrated = CalibratedHudRegion(frame.Size, hud.UiScale);
            var calibratedReading = ReadRegion(frame, calibrated, cancellationToken, out var calibratedRejected,
                2.5 / hud.UiScale);
            if (calibratedReading is not null || calibratedRejected) return calibratedReading;
        }
        var reading = ReadRegion(frame, region, cancellationToken, out var rejected);
        // BDO's HUD scale is independent of the display resolution. On a 4K
        // screen the actual level panel can still be only ~130 pixels wide;
        // the wider search then includes enough icons to confuse Windows OCR.
        // Retry the normal 1080p search size only when no pair was found. Never
        // crop away a conflicting or ambiguous reading to manufacture a value.
        var compact = Rectangle.Intersect(region, new Rectangle(0, 0, 320, 200));
        return reading is not null || rejected || compact == region
            ? reading : ReadRegion(frame, compact, cancellationToken, out _);
    }

    private ExperienceReading? ReadRegion(Bitmap frame, Rectangle region, CancellationToken cancellationToken,
        out bool rejected, double? initialScale = null)
    {
        rejected = false;
        using var crop = frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var bgr = CompanionFrameDecoder.Decode(crop);
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        var scale = initialScale ?? Math.Clamp(400d / region.Height, 1, 2);

        ExperienceReading? accepted = null;
        double? observedPercentHeight = null;
        Rectangle? observedPercentBounds = null;
        for (var variant = 0; variant < 3; variant++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var large = new Mat();
            // Keep the last preparation near the OCR engine's useful text size.
            // Its scale comes only from geometry, never from guessed XP digits.
            var variantScale = variant == 2
                ? Math.Clamp(observedPercentHeight is { } height
                    ? Math.Round(40d / height * 2) / 2 : scale * 1.25, .75, 4)
                : scale;
            var sourceBounds = variant == 2 && observedPercentBounds is { } percentBounds
                ? Rectangle.Intersect(new Rectangle(0, 0, gray.Width, gray.Height), Rectangle.FromLTRB(
                    (int)Math.Floor(percentBounds.Left - percentBounds.Width * .08),
                    (int)Math.Floor(percentBounds.Top - percentBounds.Height * 5.5),
                    (int)Math.Ceiling(percentBounds.Right + percentBounds.Width * .08),
                    (int)Math.Ceiling(percentBounds.Bottom + percentBounds.Height * .5)))
                : new Rectangle(0, 0, gray.Width, gray.Height);
            if (sourceBounds.Width < 4 || sourceBounds.Height < 4) { rejected = true; return null; }
            using var source = new Mat(gray, new Rect(sourceBounds.X, sourceBounds.Y, sourceBounds.Width, sourceBounds.Height));
            Cv2.Resize(source, large, new CvSize((int)Math.Round(source.Width * variantScale),
                (int)Math.Round(source.Height * variantScale)), interpolation: InterpolationFlags.Cubic);
            using var prepared = new Mat();
            if (variant == 0) Cv2.BitwiseNot(large, prepared);
            else Cv2.Threshold(large, prepared, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
            using var padded = new Mat();
            Cv2.CopyMakeBorder(prepared, padded, 16, 16, 16, 16, BorderTypes.Constant, Scalar.All(255));
            var result = _recognize?.Invoke(padded, cancellationToken) ?? _engine!.Recognize(padded, cancellationToken);
            var percentageWords = result.Words.Where(word => ValidGeometry(word.Geometry)
                && ParsePercent(word.Text) is not null).ToArray();
            if (percentageWords.Length == 1)
            {
                observedPercentHeight = percentageWords[0].Geometry.Height / variantScale;
                var bounds = percentageWords[0].Geometry;
                observedPercentBounds = Rectangle.FromLTRB(
                    sourceBounds.X + (int)Math.Floor((bounds.X - 16) / variantScale),
                    sourceBounds.Y + (int)Math.Floor((bounds.Y - 16) / variantScale),
                    sourceBounds.X + (int)Math.Ceiling((bounds.X + bounds.Width - 16) / variantScale),
                    sourceBounds.Y + (int)Math.Ceiling((bounds.Y + bounds.Height - 16) / variantScale));
            }
            var reading = Parse(result, out var ambiguous);
            if (ambiguous) { rejected = true; return null; }
            if (reading is null) continue;
            if (accepted is not null && accepted != reading) { rejected = true; return null; }
            accepted = reading;
        }
        return accepted;
    }

    internal static Rectangle HudRegion(System.Drawing.Size frame)
    {
        var scale = Math.Clamp(frame.Height / 1080d, 1, 2);
        return new Rectangle(0, 0, Math.Min(frame.Width, (int)Math.Ceiling(320 * scale)),
            Math.Min(frame.Height, (int)Math.Ceiling(200 * scale)));
    }

    internal static Rectangle CalibratedHudRegion(System.Drawing.Size frame, double uiScale)
    {
        // Nominal BDO HUD units, not screen pixels: the ~90 x 80 level panel
        // plus a small margin, scaled exactly like the game. The OCR zoom uses
        // the inverse UI scale so its text size stays comparable at any resolution.
        return new Rectangle(0, 0, Math.Min(frame.Width, (int)Math.Ceiling(110 * uiScale)),
            Math.Min(frame.Height, (int)Math.Ceiling(90 * uiScale)));
    }

    internal static ExperienceReading? Parse(CompanionOcrResult result)
        => Parse(result, out _);

    private static ExperienceReading? Parse(CompanionOcrResult result, out bool ambiguous)
    {
        ambiguous = false;
        var words = result.Words.Where(word => ValidGeometry(word.Geometry)).ToArray();
        if (words.Length is 0 or > 150) return null;
        var pairs = new List<ExperienceReading>();
        foreach (var percentage in words)
        {
            if (ParsePercent(percentage.Text) is not { } percent) continue;
            foreach (var levelWord in words)
            {
                var levelText = levelWord.Text.Trim();
                var levelMatch = LevelText().Match(levelText);
                if (!levelMatch.Success || !int.TryParse(levelMatch.Groups["level"].Value, out var level)
                    || level is < 1 or > 100) continue;
                var upper = levelWord.Geometry;
                var lower = percentage.Geometry;
                var verticalGap = lower.Y - (upper.Y + upper.Height);
                var upperCenter = upper.X + upper.Width / 2;
                var lowerCenter = lower.X + lower.Width / 2;
                // A HUD XP read requires the large level above the smaller decimal
                // percentage, centered in the same column, with a nearby baseline.
                if (upper.Height < lower.Height * 1.4 || upper.Height > lower.Height * 5
                    || verticalGap < -lower.Height * .15 || verticalGap > upper.Height * .85
                    || Math.Abs(upperCenter - lowerCenter) > lower.Width * .28
                    || upper.Width > lower.Width * 1.8 || upper.Width < lower.Width * .25) continue;
                pairs.Add(new ExperienceReading(level, percent));
            }
        }
        ambiguous = pairs.Count > 1;
        return pairs.Count == 1 ? pairs[0] : null;
    }

    internal static decimal? ParsePercent(string text)
    {
        var match = PercentText().Match(text.Trim());
        if (!match.Success || !decimal.TryParse(match.Groups["value"].Value.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value is < 0 or >= 100)
            return null;
        return value;
    }

    private static bool ValidGeometry(CompanionOcrWordGeometry geometry)
        => geometry.Status == CompanionOcrGeometryStatus.Success
            && float.IsFinite(geometry.X) && float.IsFinite(geometry.Y)
            && float.IsFinite(geometry.Width) && float.IsFinite(geometry.Height)
            && geometry.X >= 0 && geometry.Y >= 0 && geometry.Width > 0 && geometry.Height > 0;

    [GeneratedRegex(@"^(?:(?:Lv\.?|v)\s*)?(?<level>[1-9][0-9]?|100)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelText();

    [GeneratedRegex(@"^(?<value>(?:0|[1-9][0-9]?)[.,][0-9]{3})\s*%$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentText();

    public void Dispose() { _disposed = true; _engine = null; }
}
