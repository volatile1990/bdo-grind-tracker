using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class PaddleLootRowPreprocessorTests
{
    [Theory]
    [InlineData(.75)]
    [InlineData(1)]
    [InlineData(1.49)]
    [InlineData(2)]
    public void CropTracksGameUiScaleAndPreservesTextWithoutThresholds(double scale)
    {
        using var band = new Mat((int)Math.Round(50 * scale), (int)Math.Round(385 * scale),
            MatType.CV_8UC3, new Scalar(90, 120, 140));
        using var original = PaddleLootRowPreprocessor.Prepare(band, scale, false, false);
        using var gray = PaddleLootRowPreprocessor.Prepare(band, scale, false, true);
        Assert.Equal(band.Width - (int)Math.Round(5 * scale), original.Width);
        Assert.Equal((int)Math.Round(38 * scale) - (int)Math.Round(10 * scale), original.Height);
        Assert.Equal(new Vec3b(90, 120, 140), original.At<Vec3b>(0, 0));
        Assert.Equal(original.Size(), gray.Size());
        Assert.Equal(1, gray.Channels());
        Assert.InRange(gray.At<byte>(0, 0), (byte)91, (byte)139);
    }

    [Fact]
    public void RareAnnouncementRetainsItsDistinctCalibratedBand()
    {
        using var band = new Mat(60, 385, MatType.CV_8UC3, new Scalar(50, 70, 90));
        using var prepared = PaddleLootRowPreprocessor.Prepare(band, 1, true, false);
        Assert.Equal(band.Size(), prepared.Size());
    }

    [Theory]
    [InlineData(.75)]
    [InlineData(1)]
    [InlineData(1.49)]
    [InlineData(2)]
    public void RareWordGeometryKeepsTheWholeLineAndBothHorizontalEdges(double scale)
    {
        using var band = new Mat((int)Math.Round(60 * scale), (int)Math.Round(385 * scale),
            MatType.CV_8UC3, new Scalar(50, 70, 90));
        // The first word alone omits an upper accent and a later descender.
        // The union of all words establishes normalized y=38..68.
        CompanionOcrWord[] words =
        [
            Word("Twilight", 3, 40, 102, 24),
            Word("of", 115, 38, 25, 24),
            Word("-", 247, 50, 12, 3),
            Word("Necklace", 267, 39, 112, 29),
        ];
        var top = (int)Math.Floor(30.5 * band.Height / 100);
        var bottom = (int)Math.Ceiling(75.5 * band.Height / 100);
        var leftPixel = new Vec3b(10, 20, 200);
        var rightPixel = new Vec3b(200, 20, 10);
        band.Set(top, 0, leftPixel);
        band.Set(bottom - 1, band.Width - 1, rightPixel);

        using var prepared = PaddleLootRowPreprocessor.Prepare(band, scale, true, false, words);
        using var gray = PaddleLootRowPreprocessor.Prepare(band, scale, true, true, words);

        Assert.Equal(band.Width, prepared.Width);
        Assert.Equal(bottom - top, prepared.Height);
        Assert.Equal(leftPixel, prepared.At<Vec3b>(0, 0));
        Assert.Equal(rightPixel, prepared.At<Vec3b>(prepared.Height - 1, prepared.Width - 1));
        Assert.Equal(prepared.Size(), gray.Size());
        Assert.Equal(1, gray.Channels());
    }

    [Theory]
    [MemberData(nameof(UnusableRareWords))]
    public void UnusableOrMultilineGeometryRetainsTheFullRareBand(CompanionOcrWord[] words)
    {
        using var band = new Mat(89, 574, MatType.CV_8UC3, new Scalar(50, 70, 90));
        using var prepared = PaddleLootRowPreprocessor.Prepare(band, 1.49, true, false, words);
        Assert.Equal(band.Size(), prepared.Size());
    }

    public static TheoryData<CompanionOcrWord[]> UnusableRareWords => new()
    {
        { [] },
        { [Word("missing", 0, 40, 100, 20) with
            { Geometry = new(CompanionOcrGeometryStatus.Missing, 0, 40, 100, 20) }] },
        { [Word("negative", 0, -1, 100, 20)] },
        { [Word("outside", 0, 90, 100, 20)] },
        { [Word("outside", 640, 40, 100, 20)] },
        { [Word("empty", 0, 40, 100, 0)] },
        { [Word("nonfinite", float.NaN, 40, 100, 20)] },
        { [Word("nonfinite", 0, 40, 100, float.PositiveInfinity)] },
        { [Word("PRI:", 0, 10, 60, 15), Word("Apeiron", 0, 50, 100, 20)] },
    };

    [Fact]
    public void NormalRowsKeepTheirCalibratedCropWhenRareGeometryIsSupplied()
    {
        using var band = new Mat(50, 385, MatType.CV_8UC3, new Scalar(50, 70, 90));
        using var expected = PaddleLootRowPreprocessor.Prepare(band, 1, false, false);
        using var actual = PaddleLootRowPreprocessor.Prepare(band, 1, false, false,
            [Word("text", 0, 40, 100, 20)]);
        Assert.Equal(expected.Size(), actual.Size());
    }

    private static CompanionOcrWord Word(string text, float x, float y, float width, float height) =>
        new(text, new(CompanionOcrGeometryStatus.Success, x, y, width, height));
}
