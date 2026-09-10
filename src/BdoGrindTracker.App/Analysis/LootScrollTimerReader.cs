using System.Globalization;
using System.Text.RegularExpressions;
using BdoGrindTracker.Ocr;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

namespace BdoGrindTracker.App.Analysis;

internal sealed record LootScrollTimerReading(TimeSpan RemainingTime, TimeSpan Resolution);

internal interface ILootScrollTimerReader : IDisposable
{
    LootScrollTimerReading? Read(Bitmap frame, Rectangle gauge, CancellationToken cancellationToken);
}

/// <summary>Reads only the remaining-time label next to a positively located HUD symbol.</summary>
internal sealed partial class LootScrollTimerReader : ILootScrollTimerReader
{
    private readonly Func<Mat, CancellationToken, string>? _recognize;
    private CompanionWindowsOcrRecognizer? _engine;
    private bool _disposed;

    internal LootScrollTimerReader(Func<Mat, CancellationToken, string>? recognize = null) => _recognize = recognize;

    public LootScrollTimerReading? Read(Bitmap frame, Rectangle gauge, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var region = TimerRegion(frame.Size, gauge);
        if (region.Width < 8 || region.Height < 4) return null;
        if (_recognize is null)
        {
            _engine ??= CompanionWindowsOcrRecognizer.TryCreate();
            if (_engine is null) return null;
        }

        using var crop = frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var bgr = CompanionFrameDecoder.Decode(crop);
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        using var normalized = new Mat();
        Cv2.Normalize(gray, normalized, 0, 255, NormTypes.MinMax);
        using var large = new Mat();
        var scale = Math.Clamp(96d / gray.Height, 1, 4);
        Cv2.Resize(normalized, large, new CvSize((int)Math.Round(gray.Width * scale),
            (int)Math.Round(gray.Height * scale)), interpolation: InterpolationFlags.Cubic);

        // Compare both small preparations of the once-per-minute label. A valid
        // misread must not win merely because that preparation happened to run first.
        LootScrollTimerReading? accepted = null;
        for (var variant = 0; variant < 2; variant++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var prepared = new Mat();
            if (variant == 0) Cv2.BitwiseNot(large, prepared);
            else Cv2.Threshold(large, prepared, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
            using var padded = new Mat();
            Cv2.CopyMakeBorder(prepared, padded, 12, 12, 12, 12, BorderTypes.Constant, Scalar.All(255));
            var text = _recognize?.Invoke(padded, cancellationToken) ?? _engine!.Recognize(padded, cancellationToken).Text;
            if (TryParse(text) is not { } result) continue;
            if (accepted is not null && accepted != result) return null;
            accepted = result;
        }
        return accepted;
    }

    internal static Rectangle TimerRegion(System.Drawing.Size frame, Rectangle gauge)
    {
        if (gauge.Width <= 0 || gauge.Height <= 0) return Rectangle.Empty;
        var left = (int)Math.Floor(gauge.Left - gauge.Width * 3.2);
        var top = (int)Math.Floor(gauge.Top + gauge.Height * .48);
        var right = gauge.Left;
        var bottom = (int)Math.Ceiling(gauge.Top + gauge.Height * .94);
        return Rectangle.Intersect(new Rectangle(System.Drawing.Point.Empty, frame),
            Rectangle.FromLTRB(left, top, right, bottom));
    }

    internal static LootScrollTimerReading? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 80) return null;
        var match = TimeLabel().Match(text);
        if (!match.Success || !match.Groups["h"].Success && !match.Groups["m"].Success && !match.Groups["s"].Success)
            return null;
        static int Value(Group group) => group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 0;
        var hours = Value(match.Groups["h"]);
        var minutes = Value(match.Groups["m"]);
        var seconds = Value(match.Groups["s"]);
        if (hours > 24 || minutes > 59 || seconds > 59) return null;
        var resolution = match.Groups["s"].Success ? TimeSpan.FromSeconds(1)
            : match.Groups["m"].Success ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1);
        return new(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds), resolution);
    }

    [GeneratedRegex(@"^\s*(?:(?<h>[0-9]{1,2})\s*(?:h|Std\.?)\s*)?(?:(?<m>[0-9]{1,2})\s*(?:m|Min\.?)\s*)?(?:(?<s>[0-9]{1,2})\s*(?:s|Sek\.?)\s*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeLabel();

    public void Dispose() { _disposed = true; _engine = null; }
}
