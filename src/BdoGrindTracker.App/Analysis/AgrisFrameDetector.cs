using BdoGrindTracker.App.UI;
using BdoGrindTracker.Ocr;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

namespace BdoGrindTracker.App.Analysis;

internal sealed record AgrisReading(AgrisStatus Status)
{
    public static AgrisReading Unknown { get; } = new(AgrisStatus.Unknown);
}

internal interface IAgrisFrameDetector : IDisposable
{
    AgrisReading Analyze(Bitmap frame, CancellationToken cancellationToken);
}

/// <summary>Finds Agris' brazier glyph, then checks its colour and the rotating ring separately.</summary>
internal sealed class AgrisFrameDetector : IAgrisFrameDetector
{
    private const int ReferenceSize = 56;
    private static readonly Rect GlyphBounds = new(14, 7, 29, 38);
    private readonly Lazy<Reference[]> _references = new(LoadReferences);
    private Rectangle? _lastBounds;
    private bool _disposed;

    public AgrisReading Analyze(Bitmap frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.Width < 30 || frame.Height < 30) return AgrisReading.Unknown;
        using var bgr = CompanionFrameDecoder.Decode(frame);
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        if (_lastBounds is { } previous)
        {
            var local = Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height),
                Rectangle.Inflate(previous, 16, 16));
            var match = Find(gray, local, cancellationToken, previous.Width);
            if (match is not null)
            {
                _lastBounds = match.Bounds;
                return Classify(bgr, match);
            }
        }
        var found = Find(gray, new Rectangle(0, 0, frame.Width, frame.Height), cancellationToken);
        _lastBounds = found?.Bounds;
        return found is null ? AgrisReading.Unknown : Classify(bgr, found);
    }

    private Match? Find(Mat gray, Rectangle area, CancellationToken cancellationToken, int? knownSize = null)
    {
        using var original = new Mat(gray, new Rect(area.X, area.Y, area.Width, area.Height));
        var reduction = Math.Min(1d, 1600d / Math.Max(area.Width, area.Height));
        using var search = new Mat();
        Cv2.Resize(original, search, new CvSize(Math.Max(1, (int)Math.Round(area.Width * reduction)),
            Math.Max(1, (int)Math.Round(area.Height * reduction))), interpolation: InterpolationFlags.Area);
        var candidates = new List<Candidate>();
        foreach (var reference in _references.Value)
        {
            using var glyph = new Mat(reference.Image, GlyphBounds);
            for (var size = knownSize is { } known ? Math.Max(30, known - 4) : 30;
                 size <= (knownSize is { } current ? Math.Min(114, current + 4) : 114); size += 4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scale = size / (double)ReferenceSize;
                using var resized = new Mat();
                Cv2.Resize(glyph, resized, new CvSize(Math.Max(6, (int)Math.Round(glyph.Width * scale * reduction)),
                    Math.Max(6, (int)Math.Round(glyph.Height * scale * reduction))), interpolation: InterpolationFlags.Area);
                if (resized.Width > search.Width || resized.Height > search.Height) continue;
                using var scores = new Mat();
                Cv2.MatchTemplate(search, resized, scores, TemplateMatchModes.CCoeffNormed);
                for (var locationIndex = 0; locationIndex < 2; locationIndex++)
                {
                    Cv2.MinMaxLoc(scores, out _, out var score, out _, out var location);
                    if (!double.IsFinite(score) || score < 0.70) break;
                    candidates.Add(new Candidate(reference, size,
                        area.X + (int)Math.Round(location.X / reduction - GlyphBounds.X * scale),
                        area.Y + (int)Math.Round(location.Y / reduction - GlyphBounds.Y * scale), score));
                    var suppress = new Rect(Math.Max(0, location.X - resized.Width / 2),
                        Math.Max(0, location.Y - resized.Height / 2), 0, 0);
                    suppress.Width = Math.Min(scores.Width - suppress.X, resized.Width);
                    suppress.Height = Math.Min(scores.Height - suppress.Y, resized.Height);
                    using var suppressed = new Mat(scores, suppress);
                    suppressed.SetTo(Scalar.All(-1));
                }
            }
        }
        var verified = new List<Match>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score).Take(20))
        {
            var match = Verify(gray, candidate, cancellationToken);
            if (match is not null) verified.Add(match);
        }
        var best = verified.MaxBy(m => m.Score);
        if (best is null) return null;
        return verified.Any(m => Math.Abs(m.Bounds.X - best.Bounds.X) > 14
            || Math.Abs(m.Bounds.Y - best.Bounds.Y) > 14) ? null : best;
    }

    private static Match? Verify(Mat frame, Candidate candidate, CancellationToken cancellationToken)
    {
        Match? best = null;
        for (var size = Math.Max(30, candidate.Size - 4); size <= Math.Min(114, candidate.Size + 4); size++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var resized = new Mat();
            using var mask = new Mat();
            Cv2.Resize(candidate.Reference.Image, resized, new CvSize(size, size), interpolation: InterpolationFlags.Area);
            Cv2.Resize(candidate.Reference.Mask, mask, new CvSize(size, size), interpolation: InterpolationFlags.Nearest);
            var x = Math.Max(0, candidate.X - 5);
            var y = Math.Max(0, candidate.Y - 5);
            var right = Math.Min(frame.Width, candidate.X + size + 5);
            var bottom = Math.Min(frame.Height, candidate.Y + size + 5);
            if (right - x < size || bottom - y < size) continue;
            using var area = new Mat(frame, new Rect(x, y, right - x, bottom - y));
            using var scores = new Mat();
            Cv2.MatchTemplate(area, resized, scores, TemplateMatchModes.CCoeffNormed, mask);
            Cv2.MinMaxLoc(scores, out _, out var score, out _, out var location);
            if (!double.IsFinite(score) || score < 0.91 || best is not null && score <= best.Score) continue;
            using var actual = new Mat(area, new Rect(location.X, location.Y, size, size));
            Cv2.MeanStdDev(actual, out _, out var deviation, mask);
            if (deviation.Val0 < 5) continue;
            best = new Match(new Rectangle(x + location.X, y + location.Y, size, size), score, candidate.Reference);
        }
        return best;
    }

    private static AgrisReading Classify(Mat bgr, Match match)
    {
        using var region = new Mat(bgr, new Rect(match.Bounds.X, match.Bounds.Y, match.Bounds.Width, match.Bounds.Height));
        using var normalized = new Mat();
        Cv2.Resize(region, normalized, new CvSize(ReferenceSize, ReferenceSize), interpolation: InterpolationFlags.Area);
        var glyphPixels = 0;
        var goldenGlyphPixels = 0;
        for (var y = 0; y < ReferenceSize; y++)
        for (var x = 0; x < ReferenceSize; x++)
        {
            if (match.Reference.Foreground.At<byte>(y, x) == 0) continue;
            glyphPixels++;
            if (IsGold(normalized.At<Vec3b>(y, x))) goldenGlyphPixels++;
        }
        if (glyphPixels == 0 || goldenGlyphPixels < glyphPixels * 0.65)
            return new AgrisReading(AgrisStatus.Inactive);

        // The arc rotates. Count bright golden angular sectors in an annulus;
        // neither its angle nor the ring pixels occur in the glyph template.
        var center = 27.5;
        var backgroundLevels = new List<double>();
        for (var y = 6; y < 50; y++)
        for (var x = 6; x < 50; x++)
        {
            var radiusSquared = (x - center) * (x - center) + (y - center) * (y - center);
            if (radiusSquared is < 18 * 18 or > 21 * 21
                || match.Reference.Foreground.At<byte>(y, x) != 0) continue;
            backgroundLevels.Add(Brightness(normalized.At<Vec3b>(y, x)));
        }
        backgroundLevels.Sort();
        var backgroundLevel = backgroundLevels[backgroundLevels.Count / 2];
        var brightSectors = 0;
        const int sectors = 48;
        for (var index = 0; index < sectors; index++)
        {
            var angle = 2 * Math.PI * index / sectors;
            var bright = false;
            for (var radius = 24d; radius <= 27; radius += .75)
            {
                var x = (int)Math.Round(center + Math.Cos(angle) * radius);
                var y = (int)Math.Round(center + Math.Sin(angle) * radius);
                var pixel = normalized.At<Vec3b>(y, x);
                if (IsGold(pixel) && Brightness(pixel) >= backgroundLevel + 12)
                    bright = true;
            }
            if (bright) brightSectors++;
        }
        return new AgrisReading(brightSectors >= 10 ? AgrisStatus.Active : AgrisStatus.Inactive);
    }

    private static bool IsGold(Vec3b pixel)
        // HDR compresses warm highlights towards white. Measure chroma against
        // the remaining blue-channel headroom. Real WGC highlights reach RGB
        // (245, 243, 242), so retain their 3/1 channel differences after 8-bit
        // quantization; neutral gray still cannot pass either chroma check.
        => pixel.Item2 >= 70 && pixel.Item2 - pixel.Item0 >= Math.Max(3, (255 - pixel.Item0) * .12)
            && pixel.Item1 - pixel.Item0 >= Math.Max(1, (255 - pixel.Item0) * .04)
            && pixel.Item2 >= pixel.Item1;

    private static double Brightness(Vec3b pixel)
        => .114 * pixel.Item0 + .587 * pixel.Item1 + .299 * pixel.Item2;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_references.IsValueCreated) return;
        foreach (var reference in _references.Value)
        {
            reference.Image.Dispose();
            reference.Mask.Dispose();
            reference.Foreground.Dispose();
        }
    }

    private static Reference[] LoadReferences()
    {
        var references = new List<Reference>();
        foreach (var name in new[] { "glyph-gray", "glyph-gold" })
        {
            using var stream = typeof(AgrisFrameDetector).Assembly.GetManifestResourceStream($"BdoGrindTracker.App.Agris.{name}.png")
                ?? throw new InvalidOperationException($"Missing Agris reference: {name}.");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var image = Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
            var mask = new Mat(ReferenceSize, ReferenceSize, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(mask, GlyphBounds, Scalar.All(255), -1);
            using var inner = new Mat(ReferenceSize, ReferenceSize, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Circle(inner, new OpenCvSharp.Point(28, 28), 21, Scalar.All(255), -1);
            Cv2.BitwiseAnd(mask, inner, mask);
            Cv2.Rectangle(mask, new Rect(35, 0, 21, 20), Scalar.All(0), -1);
            Cv2.MeanStdDev(image, out var mean, out var deviation, mask);
            var foreground = new Mat();
            Cv2.Threshold(image, foreground, mean.Val0 + deviation.Val0 * .3, 255, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(foreground, mask, foreground);
            references.Add(new Reference(image, mask, foreground));
        }
        return references.ToArray();
    }

    private sealed record Reference(Mat Image, Mat Mask, Mat Foreground);
    private sealed record Candidate(Reference Reference, int Size, int X, int Y, double Score);
    private sealed record Match(Rectangle Bounds, double Score, Reference Reference);
}
