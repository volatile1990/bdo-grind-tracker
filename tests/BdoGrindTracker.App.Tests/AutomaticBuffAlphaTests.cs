using BdoGrindTracker.App.Analysis;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffAlphaTests
{
    [Fact]
    public void InvisibleRgbDoesNotChangeDecodedArtwork()
    {
        using var first = new Mat(16, 16, MatType.CV_8UC4, new Scalar(255, 0, 128, 0));
        using var second = new Mat(16, 16, MatType.CV_8UC4, new Scalar(0, 255, 64, 0));
        for (var y = 4; y < 12; y++)
            for (var x = 4; x < 12; x++)
            {
                var pixel = new Vec4b((byte)(x * 15), (byte)(y * 17), 200, 255);
                first.Set(y, x, pixel);
                second.Set(y, x, pixel);
            }

        using var decodedFirst = AutomaticBuffFrameReader.DecodeIcon(Encode(first));
        using var decodedSecond = AutomaticBuffFrameReader.DecodeIcon(Encode(second));

        Assert.Equal(MatType.CV_8UC3, decodedFirst.Type());
        Assert.Equal(0d, Cv2.Norm(decodedFirst, decodedSecond, NormTypes.INF));
        Assert.Equal(new Vec3b(32, 32, 32), decodedFirst.At<Vec3b>(0, 0));
        Assert.Equal(new Vec3b(120, 136, 200), decodedFirst.At<Vec3b>(8, 8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(255)]
    public void AlphaBlendsEachChannelAgainstNeutralThirtyTwo(int alpha)
    {
        using var source = new Mat(8, 8, MatType.CV_8UC4, new Scalar(80, 160, 240, alpha));

        using var decoded = AutomaticBuffFrameReader.DecodeIcon(Encode(source));

        var pixel = decoded.At<Vec3b>(3, 3);
        for (var channel = 0; channel < 3; channel++)
        {
            var expected = (80 * (channel + 1) * alpha + 32 * (255 - alpha) + 127) / 255;
            Assert.InRange((int)pixel[channel], Math.Max(0, expected - 1), Math.Min(255, expected + 1));
        }
        if (alpha == 255) Assert.Equal(new Vec3b(80, 160, 240), pixel);
    }

    [Fact]
    public void ArtworkWithoutAlphaRemainsUnchanged()
    {
        using var source = new Mat(16, 16, MatType.CV_8UC3);
        var height = source.Height;
        var width = source.Width;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                source.Set(y, x, new Vec3b((byte)(x * 13), (byte)(y * 15), (byte)((x + y) * 7)));

        using var decoded = AutomaticBuffFrameReader.DecodeIcon(Encode(source));

        Assert.Equal(source.Size(), decoded.Size());
        Assert.Equal(source.Type(), decoded.Type());
        Assert.Equal(0d, Cv2.Norm(source, decoded, NormTypes.INF));
    }

    [Theory]
    [InlineData(.75, 1)]
    [InlineData(1, .5)]
    [InlineData(1, 1)]
    [InlineData(1.5, 1.5)]
    public void EveryTransparentIdentitySurvivesActualCompositionToneAndScaling(double scale, double gamma)
    {
        using var references = new References();
        var transparent = references.Items.Where(item => item.HasTransparency).ToArray();
        Assert.NotEmpty(transparent);
        Assert.Contains(transparent, item => item.Template.CandidateBuffIds.Contains("harmony-draught-edania"));
        Assert.Contains(transparent, item => item.Template.CandidateBuffIds.Contains("perfume-of-tenacity"));

        using var transfer = new Mat(1, 256, MatType.CV_8UC1);
        for (var value = 0; value < 256; value++)
            transfer.Set(0, value, (byte)Math.Round(255 * (.12 + .75 * Math.Pow(value / 255d, gamma))));

        foreach (var reference in transparent)
        {
            // Produce the observed picture independently from DecodeIcon: drawing
            // the original PNG onto a solid surface applies its actual alpha.
            using var bitmap = CompositeWithGraphics(reference.Encoded);
            using var composed = BdoGrindTracker.Ocr.CompanionFrameDecoder.Decode(bitmap);
            using var resized = new Mat();
            Cv2.Resize(composed, resized, new((int)Math.Round(composed.Width * scale),
                (int)Math.Round(composed.Height * scale)), interpolation: InterpolationFlags.Linear);
            using var observed = new Mat();
            Cv2.LUT(resized, transfer, observed);

            var scores = references.Items.Select(item => new
                { item.Template, Score = BuffIconSimilarity.Score(observed, item.Image) })
                .OrderByDescending(item => item.Score).ToArray();
            var winner = scores[0];
            var runnerUp = scores.First(item => item.Template.GroupId != winner.Template.GroupId);

            Assert.Equal(reference.Template.GroupId, winner.Template.GroupId);
            Assert.Equal(reference.Template.CandidateBuffIds, winner.Template.CandidateBuffIds);
            Assert.True(BuffIconSimilarity.IsDecisive(winner.Score, runnerUp.Score),
                $"{reference.Template.Id}, scale {scale}, gamma {gamma}: {winner.Score:F5} vs {runnerUp.Template.Id} {runnerUp.Score:F5}");
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(20, 80, 160)]
    [InlineData(255, 255, 255)]
    public void CompositedTemplatesCannotTurnFlatPatchesIntoSymbolEvidence(int blue, int green, int red)
    {
        using var references = new References();
        using var observed = new Mat(48, 48, MatType.CV_8UC3, new Scalar(blue, green, red));

        Assert.All(references.Items, item => Assert.Equal(0, BuffIconSimilarity.Score(observed, item.Image)));
    }

    [Fact]
    public void SixteenPixelImmortalHarmonyRemainsAmbiguousDespiteTheCorrectLeadingMatch()
    {
        using var references = new References();
        var reference = references.Items.Single(item =>
            item.Template.CandidateBuffIds.SequenceEqual(["immortal-harmony-draught-demihuman"]));
        using var bitmap = CompositeWithGraphics(reference.Encoded);
        using var composed = BdoGrindTracker.Ocr.CompanionFrameDecoder.Decode(bitmap);
        Assert.Equal(32, composed.Width);
        Assert.Equal(32, composed.Height);
        using var resized = new Mat();
        Cv2.Resize(composed, resized, new(16, 16), interpolation: InterpolationFlags.Linear);
        using var transfer = new Mat(1, 256, MatType.CV_8UC1);
        for (var value = 0; value < 256; value++)
            transfer.Set(0, value, (byte)Math.Round(255 * (.12 + .75 * Math.Pow(value / 255d, 1))));
        using var observed = new Mat();
        Cv2.LUT(resized, transfer, observed);

        var scores = references.Items.Select(item => new
            { item.Template, Score = BuffIconSimilarity.Score(observed, item.Image) })
            .OrderByDescending(item => item.Score).ToArray();
        var winner = scores[0];
        var runnerUp = scores.First(item => item.Template.GroupId != winner.Template.GroupId);

        Assert.Equal(reference.Template.GroupId, winner.Template.GroupId);
        Assert.Equal(["immortal-harmony-draught-human"], runnerUp.Template.CandidateBuffIds);
        Assert.False(BuffIconSimilarity.IsDecisive(winner.Score, runnerUp.Score),
            $"Tiny symbols must remain unknown: {winner.Score:F5} vs {runnerUp.Template.Id} {runnerUp.Score:F5}");
    }

    private static byte[] Encode(Mat image)
    {
        Cv2.ImEncode(".png", image, out var bytes);
        return bytes;
    }

    private static Bitmap CompositeWithGraphics(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded);
        using var source = new Bitmap(stream);
        var result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.FromArgb(32, 32, 32));
        graphics.DrawImageUnscaled(source, 0, 0);
        return result;
    }

    private sealed class References : IDisposable
    {
        internal sealed record Item(AutomaticBuffCatalog.Template Template, byte[] Encoded, Mat Image, bool HasTransparency);
        internal Item[] Items { get; }

        internal References()
        {
            var catalog = AutomaticBuffCatalog.Default;
            Items = catalog.Templates.Select(template =>
            {
                using var stream = catalog.OpenIcon(template)!;
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                var encoded = bytes.ToArray();
                using var original = Cv2.ImDecode(encoded, ImreadModes.Unchanged);
                var hasTransparency = false;
                if (original.Channels() == 4)
                {
                    using var alpha = new Mat();
                    Cv2.ExtractChannel(original, alpha, 3);
                    Cv2.MinMaxLoc(alpha, out double minimum, out double _);
                    hasTransparency = minimum < 255;
                }
                return new Item(template, encoded, AutomaticBuffFrameReader.DecodeIcon(encoded), hasTransparency);
            }).ToArray();
        }

        public void Dispose()
        {
            foreach (var item in Items) item.Image.Dispose();
        }
    }
}
