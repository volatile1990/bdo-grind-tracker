using System.Globalization;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal sealed record CombatStatsReading(int Ap, int Dp, CombatStatsCategory Category);
internal interface ICombatStatsFrameReader : IDisposable
{
    CombatStatsReading? Read(Bitmap frame, CancellationToken cancellationToken);
}

internal enum CombatHudIcon { Sword, Shield }

/// <summary>Reads the top-left sword/AP and shield/DP row. Color comes from the numbers,
/// never from the golden icons. Unrecognized layouts or mixed colors produce no reading.</summary>
internal sealed class CombatStatsFrameReader : ICombatStatsFrameReader
{
    private readonly Func<Mat, CancellationToken, CompanionOcrResult>? _recognize;
    private readonly Func<ExperienceHudConfiguration?>? _readConfiguration;
    private CompanionWindowsOcrRecognizer? _engine;
    private bool _disposed;

    internal CombatStatsFrameReader(Func<Mat, CancellationToken, CompanionOcrResult>? recognize = null,
        Func<ExperienceHudConfiguration?>? readConfiguration = null)
    {
        _recognize = recognize;
        _readConfiguration = readConfiguration;
    }

    public CombatStatsReading? Read(Bitmap frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = _readConfiguration?.Invoke();
        var scale = configuration is { } hud && hud.ScreenWidth == frame.Width && hud.ScreenHeight == frame.Height &&
            double.IsFinite(hud.UiScale) && hud.UiScale is >= .5 and <= 3 ? hud.UiScale : (double?)null;
        var region = HudRegion(frame.Size, scale);
        if (region.Width < 60 || region.Height < 20) return null;
        if (_recognize is null && (_engine ??= CompanionWindowsOcrRecognizer.TryCreate()) is null) return null;
        using var crop = frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var bgr = CompanionFrameDecoder.Decode(crop);
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        var zoom = scale is { } uiScale ? Math.Clamp(3 / uiScale, 1, 5) : 2d;
        using var large = new Mat();
        Cv2.Resize(gray, large, new OpenCvSharp.Size((int)Math.Round(gray.Width * zoom),
            (int)Math.Round(gray.Height * zoom)), interpolation: InterpolationFlags.Cubic);
        CombatStatsReading? accepted = null;
        for (var variant = 0; variant < 2; variant++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var prepared = new Mat();
            if (variant == 0) Cv2.BitwiseNot(large, prepared);
            else Cv2.Threshold(large, prepared, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
            using var padded = new Mat();
            Cv2.CopyMakeBorder(prepared, padded, 16, 16, 16, 16, BorderTypes.Constant, Scalar.All(255));
            var recognized = _recognize?.Invoke(padded, cancellationToken) ?? _engine!.Recognize(padded, cancellationToken);
            var words = recognized.Words.Select(word => new CompanionOcrWord(word.Text,
                new CompanionOcrWordGeometry(word.Geometry.Status,
                    (float)((word.Geometry.X - 16) / zoom), (float)((word.Geometry.Y - 16) / zoom),
                    (float)(word.Geometry.Width / zoom), (float)(word.Geometry.Height / zoom)))).ToArray();
            var actual = recognized with { Words = words };
            var reading = Parse(actual, new System.Drawing.Size(bgr.Width, bgr.Height),
                bounds => NumberColor(bgr, bounds), (bounds, icon) => MatchesIcon(bgr, bounds, icon), out var ambiguous);
            if (ambiguous || accepted is not null && reading is not null && accepted != reading) return null;
            accepted ??= reading;
        }
        return accepted;
    }

    internal static Rectangle HudRegion(System.Drawing.Size size, double? uiScale = null)
    {
        var scale = uiScale ?? Math.Clamp(size.Height / 1080d, 1, 2);
        return new(0, 0, Math.Min(size.Width, (int)Math.Ceiling(680 * scale)),
            Math.Min(size.Height, (int)Math.Ceiling(150 * scale)));
    }

    internal static CombatStatsReading? Parse(CompanionOcrResult result, System.Drawing.Size cropSize,
        Func<Rectangle, CombatStatsCategory?> color, Func<Rectangle, CombatHudIcon, bool> icon) =>
        Parse(result, cropSize, color, icon, out _);

    private static CombatStatsReading? Parse(CompanionOcrResult result, System.Drawing.Size cropSize,
        Func<Rectangle, CombatStatsCategory?> color, Func<Rectangle, CombatHudIcon, bool> icon, out bool ambiguous)
    {
        ambiguous = false;
        var words = result.Words.Where(word => ValidGeometry(word.Geometry)).ToArray();
        if (words.Length is 0 or > 100) return null;
        var readings = new List<CombatStatsReading>();
        foreach (var ap in words)
        {
            if (ParseNumber(ap.Text) is not { } attack) continue;
            var a = ap.Geometry;
            foreach (var dp in words)
            {
                if (ReferenceEquals(ap, dp) || ParseNumber(dp.Text) is not { } defense) continue;
                var d = dp.Geometry;
                var height = (a.Height + d.Height) / 2;
                var gap = d.X - (a.X + a.Width);
                // AP/DP sit on one baseline, with a full shield between the numbers.
                // The nearby weight's "224 / 224" has neither this gap nor these icons.
                if (height < 7 || height > cropSize.Height * .5 || a.Y > cropSize.Height * .5 ||
                    a.X < height * 3 || a.Height / d.Height is < .65f or > 1.5f ||
                    Math.Abs((a.Y + a.Height / 2) - (d.Y + d.Height / 2)) > height * .35 ||
                    gap < height * 1.1 || gap > height * 5 ||
                    a.Width / height is < .25f or > 5 || d.Width / height is < .25f or > 5) continue;
                var attackBounds = Bounds(a);
                var defenseBounds = Bounds(d);
                if (!Contains(cropSize, attackBounds) || !Contains(cropSize, defenseBounds)) continue;
                var sword = IconBounds(a);
                var shield = IconBounds(d);
                if (!Contains(cropSize, sword) || !Contains(cropSize, shield) ||
                    !icon(sword, CombatHudIcon.Sword) || !icon(shield, CombatHudIcon.Shield)) continue;
                var attackCategory = color(attackBounds);
                var defenseCategory = color(defenseBounds);
                if (attackCategory is null || attackCategory != defenseCategory) continue;
                readings.Add(new(attack, defense, attackCategory.Value));
            }
        }
        ambiguous = readings.Count > 1;
        return readings.Count == 1 ? readings[0] : null;
    }

    internal static int? ParseNumber(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length is > 0 and <= 5 && trimmed.All(char.IsAsciiDigit) &&
            int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number is > 0 and <= CombatStatsState.MaximumStatValue ? number : null;
    }

    private static Rectangle Bounds(CompanionOcrWordGeometry word) => Rectangle.FromLTRB(
        (int)Math.Floor(word.X), (int)Math.Floor(word.Y), (int)Math.Ceiling(word.X + word.Width), (int)Math.Ceiling(word.Y + word.Height));

    private static Rectangle IconBounds(CompanionOcrWordGeometry word) => Rectangle.FromLTRB(
        (int)Math.Floor(word.X - word.Height * 2.9), (int)Math.Floor(word.Y - word.Height * .5),
        (int)Math.Ceiling(word.X - word.Height * .4), (int)Math.Ceiling(word.Y + word.Height * 1.7));

    private static bool Contains(System.Drawing.Size size, Rectangle bounds) => bounds.Width > 0 && bounds.Height > 0 &&
        bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= size.Width && bounds.Bottom <= size.Height;

    private static bool ValidGeometry(CompanionOcrWordGeometry value) => value.Status == CompanionOcrGeometryStatus.Success &&
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Width) && float.IsFinite(value.Height) &&
        value.X >= 0 && value.Y >= 0 && value.Width > 0 && value.Height > 0;

    internal static CombatStatsCategory? NumberColor(Mat image, Rectangle bounds)
    {
        if (!Contains(new(image.Width, image.Height), bounds)) return null;
        var colors = new List<Color>(bounds.Width * bounds.Height);
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var pixel = image.At<Vec3b>(y, x);
            colors.Add(Color.FromArgb(pixel.Item2, pixel.Item1, pixel.Item0));
        }
        return ClassifyNumberColors(colors);
    }

    internal static CombatStatsCategory? ClassifyNumberColors(IReadOnlyList<Color> pixels) =>
        CombatStatsColorClassifier.Classify(pixels);

    internal static bool MatchesIcon(Mat image, Rectangle bounds, CombatHudIcon icon)
    {
        if (!Contains(new(image.Width, image.Height), bounds)) return false;
        var mask = new bool[bounds.Width * bounds.Height];
        for (var y = 0; y < bounds.Height; y++)
        for (var x = 0; x < bounds.Width; x++)
        {
            var c = image.At<Vec3b>(bounds.Top + y, bounds.Left + x);
            // The observed HUD uses warm sword/shield icons for every number category.
            mask[y * bounds.Width + x] = c.Item2 >= 150 && c.Item1 >= 135 &&
                c.Item0 <= Math.Min(c.Item2, c.Item1) + 12 && c.Item2 + c.Item1 - 2 * c.Item0 >= 12;
        }
        List<(int X, int Y)> largest = [];
        for (var index = 0; index < mask.Length; index++)
        {
            if (!mask[index]) continue;
            var component = new List<(int X, int Y)>();
            var pending = new Stack<int>();
            pending.Push(index); mask[index] = false;
            while (pending.TryPop(out var at))
            {
                var x = at % bounds.Width; var y = at / bounds.Width;
                component.Add((x, y));
                for (var ny = Math.Max(0, y - 1); ny <= Math.Min(bounds.Height - 1, y + 1); ny++)
                for (var nx = Math.Max(0, x - 1); nx <= Math.Min(bounds.Width - 1, x + 1); nx++)
                {
                    var neighbor = ny * bounds.Width + nx;
                    if (!mask[neighbor]) continue;
                    mask[neighbor] = false; pending.Push(neighbor);
                }
            }
            if (component.Count > largest.Count) largest = component;
        }
        if (largest.Count < 12) return false;
        var left = largest.Min(p => p.X); var right = largest.Max(p => p.X);
        var top = largest.Min(p => p.Y); var bottom = largest.Max(p => p.Y);
        var width = right - left + 1; var height = bottom - top + 1;
        if (width < bounds.Width * .3 || height < bounds.Height * .4 || height > bounds.Height * .95) return false;
        var density = largest.Count / (double)(width * height);
        var meanX = largest.Average(p => p.X); var meanY = largest.Average(p => p.Y);
        var covariance = largest.Sum(p => (p.X - meanX) * (p.Y - meanY));
        var varianceX = largest.Sum(p => Math.Pow(p.X - meanX, 2));
        var varianceY = largest.Sum(p => Math.Pow(p.Y - meanY, 2));
        var correlation = covariance / Math.Sqrt(varianceX * varianceY);
        if (!double.IsFinite(correlation)) return false;
        if (icon == CombatHudIcon.Sword) return width / (double)height is >= .65 and <= 1.4 &&
            density is >= .15 and <= .65 && correlation >= .6;
        var lower = largest.Where(p => p.Y >= top + height * .8).ToArray();
        return width / (double)height is >= .5 and <= 1.1 && density is >= .25 and <= .9 &&
            Math.Abs(correlation) <= .3 && lower.Length >= 2 && lower.Max(p => p.X) - lower.Min(p => p.X) + 1 < width * .9;
    }

    public void Dispose() { _disposed = true; _engine = null; }
}
