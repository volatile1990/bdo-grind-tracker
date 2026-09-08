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
}
