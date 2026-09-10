using BdoGrindTracker.App.UI;
using BdoGrindTracker.Ocr;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;

namespace BdoGrindTracker.App.Analysis;

internal sealed record LootScrollGaugeMatch(LootScrollReading Reading, Rectangle Bounds);

/// <summary>
/// Reads the opaque centre of BDO's movable loot-scroll gauge. The bag/cross and
/// bag/chevrons are language-independent and remain visible with the level menu closed.
/// Neither missing HUD pixels nor tooltip text are evidence that a scroll is inactive.
/// </summary>
internal sealed class LootScrollGaugeDetector : ILootScrollFrameDetector
{
    private const int TemplateSize = 56;
    private const double MinimumCoarseScore = 0.72;
    private const double MinimumVerifiedScore = 0.92;
    private static readonly Rect CoreBounds = new(10, 8, 35, 36);
    private readonly Lazy<Template[]> _templates = new(LoadTemplates);
    private bool _disposed;

    public LootScrollReading Analyze(Bitmap frame, CancellationToken cancellationToken)
        => FindGauge(frame, cancellationToken)?.Reading ?? LootScrollReading.Unknown;

    internal LootScrollGaugeMatch? FindGauge(Bitmap frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.Width < 28 || frame.Height < 28)
            return null;

        using var bgr = CompanionFrameDecoder.Decode(frame);
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        // Only the search pass is reduced. Confirmation uses the original capture.
        var reduction = Math.Min(1d, 1920d / Math.Max(frame.Width, frame.Height));
        using var search = new Mat();
        Cv2.Resize(gray, search, new CvSize(
            Math.Max(1, (int)Math.Round(frame.Width * reduction)),
            Math.Max(1, (int)Math.Round(frame.Height * reduction))),
            interpolation: InterpolationFlags.Area);

        var candidates = new List<Candidate>();
        foreach (var template in _templates.Value)
        {
            using var core = new Mat(template.Image, CoreBounds);
            for (var diameter = 28; diameter <= 112; diameter += 4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scale = diameter / (double)TemplateSize;
                using var resized = new Mat();
                Cv2.Resize(core, resized, new CvSize(
                    Math.Max(6, (int)Math.Round(core.Width * scale * reduction)),
                    Math.Max(6, (int)Math.Round(core.Height * scale * reduction))),
                    interpolation: InterpolationFlags.Area);
                if (resized.Width > search.Width || resized.Height > search.Height)
                    continue;
                using var scores = new Mat();
                Cv2.MatchTemplate(search, resized, scores, TemplateMatchModes.CCoeffNormed);
                // Preserve a second location for the later ambiguity check.
                for (var match = 0; match < 2; match++)
                {
                    Cv2.MinMaxLoc(scores, out _, out var score, out _, out var location);
                    if (!double.IsFinite(score) || score < MinimumCoarseScore)
                        break;
                    candidates.Add(new Candidate(template, diameter,
                        (int)Math.Round(location.X / reduction - CoreBounds.X * scale),
                        (int)Math.Round(location.Y / reduction - CoreBounds.Y * scale), score));
                    var suppression = new Rect(
                        Math.Max(0, location.X - resized.Width / 2),
                        Math.Max(0, location.Y - resized.Height / 2), 0, 0);
                    suppression.Width = Math.Min(scores.Width - suppression.X, resized.Width);
                    suppression.Height = Math.Min(scores.Height - suppression.Y, resized.Height);
                    using var suppressed = new Mat(scores, suppression);
                    suppressed.SetTo(Scalar.All(-1));
                }
            }
        }

        var verified = new List<Match>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score).Take(36))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = Verify(gray, candidate, cancellationToken);
            if (match is not null)
                verified.Add(match);
        }
        if (verified.Count == 0)
            return null;

        var best = verified.MaxBy(m => m.Score)!;
        // Multiple separated matches are ambiguous, regardless of their labels.
        if (verified.Any(m => DistanceSquared(m.Center, best.Center) > 16 * 16))
            return null;
        var competing = verified.Where(m => m.Reading != best.Reading)
            .OrderByDescending(m => m.Score).FirstOrDefault();
        if (competing is not null && best.Score - competing.Score < 0.025)
        {
            return best.Reading.Status == LootScrollStatus.Active
                && competing.Reading.Status == LootScrollStatus.Active
                ? new LootScrollGaugeMatch(new LootScrollReading(LootScrollStatus.Active), best.Bounds)
                : null;
        }
        return new LootScrollGaugeMatch(best.Reading, best.Bounds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_templates.IsValueCreated) return;
        foreach (var template in _templates.Value)
        {
            template.Image.Dispose();
            template.Mask.Dispose();
        }
    }

    private static Match? Verify(Mat frame, Candidate candidate, CancellationToken cancellationToken)
    {
        Match? best = null;
        for (var size = Math.Max(28, candidate.Diameter - 4);
             size <= Math.Min(112, candidate.Diameter + 4); size++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var resized = new Mat();
            using var mask = new Mat();
            Cv2.Resize(candidate.Template.Image, resized, new CvSize(size, size),
                interpolation: InterpolationFlags.Area);
            Cv2.Resize(candidate.Template.Mask, mask, new CvSize(size, size),
                interpolation: InterpolationFlags.Nearest);
            // Only pixels used by the mask must be in the frame. A clipped outer
            // rim or plus button must not hide a fully visible bag and level glyph.
            var symbolBounds = Cv2.BoundingRect(mask);
            using var symbol = new Mat(resized, symbolBounds);
            using var symbolMask = new Mat(mask, symbolBounds);
            var x = Math.Max(0, candidate.X + symbolBounds.X - 8);
            var y = Math.Max(0, candidate.Y + symbolBounds.Y - 8);
            var right = Math.Min(frame.Width, candidate.X + symbolBounds.Right + 8);
            var bottom = Math.Min(frame.Height, candidate.Y + symbolBounds.Bottom + 8);
            if (right - x < symbol.Width || bottom - y < symbol.Height) continue;
            using var area = new Mat(frame, new Rect(x, y, right - x, bottom - y));
            using var scores = new Mat();
            Cv2.MatchTemplate(area, symbol, scores, TemplateMatchModes.CCoeffNormed, symbolMask);
            Cv2.MinMaxLoc(scores, out _, out var score, out _, out var location);
            if (!double.IsFinite(score) || score < MinimumVerifiedScore
                || (best is not null && score <= best.Score)) continue;
            using var actual = new Mat(area, new Rect(location.X, location.Y, symbol.Width, symbol.Height));
            Cv2.MeanStdDev(actual, out var actualMean, out var actualDeviation, symbolMask);
            Cv2.MeanStdDev(symbol, out var referenceMean, out var referenceDeviation, symbolMask);
            if (actualDeviation.Val0 < 4 || referenceDeviation.Val0 < 4) continue;
            var gain = actualDeviation.Val0 / referenceDeviation.Val0;
            using var normalized = new Mat();
            using var observed = new Mat();
            symbol.ConvertTo(normalized, MatType.CV_32F, gain,
                actualMean.Val0 - referenceMean.Val0 * gain);
            actual.ConvertTo(observed, MatType.CV_32F);
            using var difference = new Mat();
            Cv2.Absdiff(observed, normalized, difference);
            // The HDR capture's fixed tone mapping can brighten this same glyph
            // considerably. Confirm its pixel structure after fitting brightness
            // and contrast; never infer the status from how dark the bag looks.
            // Keep the strict masked correlation and reject low-contrast imagery.
            if (Cv2.Mean(difference, symbolMask).Val0 / actualDeviation.Val0 > 0.32) continue;
            best = new Match(candidate.Template.Reading, score,
                new Rectangle(x + location.X - symbolBounds.X,
                    y + location.Y - symbolBounds.Y, size, size));
        }
        return best;
    }

    private static Template[] LoadTemplates()
    {
        var templates = new List<Template>();
        try
        {
            foreach (var (name, reading) in new[]
            {
                ("inactive", new LootScrollReading(LootScrollStatus.Inactive)),
                ("active-1", new LootScrollReading(LootScrollStatus.Active, 1)),
                ("active-2", new LootScrollReading(LootScrollStatus.Active, 2))
            })
            {
                using var stream = typeof(LootScrollGaugeDetector).Assembly.GetManifestResourceStream(
                    $"BdoGrindTracker.App.LootScroll.{name}.png")
                    ?? throw new InvalidOperationException($"Missing loot-scroll template: {name}.");
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                var image = Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
                var mask = new Mat(TemplateSize, TemplateSize, MatType.CV_8UC1, Scalar.All(0));
                // Exclude the translucent outer HUD, changing time, level menu and
                // plus button (including its hover effect), retaining the bag and ×/arrows.
                Cv2.Circle(mask, new CvPoint(27, 27), 23, Scalar.All(255), -1);
                Cv2.Circle(mask, new CvPoint(49, 49), 14, Scalar.All(0), -1);
                templates.Add(new Template(reading, image, mask));
            }
            return templates.ToArray();
        }
        catch
        {
            foreach (var template in templates)
            {
                template.Image.Dispose();
                template.Mask.Dispose();
            }
            throw;
        }
    }

    private static long DistanceSquared(CvPoint a, CvPoint b)
        => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);
    private sealed record Template(LootScrollReading Reading, Mat Image, Mat Mask);
    private sealed record Candidate(Template Template, int Diameter, int X, int Y, double Score);
    private sealed record Match(LootScrollReading Reading, double Score, Rectangle Bounds)
    {
        public CvPoint Center => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
    }
}
