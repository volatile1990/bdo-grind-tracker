using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class ToneMappedRareRowProcessorTests
{
    [Theory]
    [InlineData(60, 385)]
    [InlineData(75, 481)]
    [InlineData(90, 578)]
    public void ColoredTextOutsideNativeMaskSurvivesWithFullBandGeometry(int height, int width)
    {
        using var source = new Mat(height, width, MatType.CV_8UC3, new Scalar(35, 35, 35));
        Cv2.PutText(source, "Twilight of the End - Ring", new OpenCvSharp.Point(2, height * 2 / 3),
            HersheyFonts.HersheySimplex, height / 100d, new Scalar(40, 95, 135), 2, LineTypes.AntiAlias);
        using var before = source.Clone();
        using var native = new CompanionRareRowProcessor().Process(source, 0, 1, 0);
        using var prepared = ToneMappedRareRowProcessor.Process(source, 37);

        Assert.True(native.IsBlank);
        Assert.False(prepared.IsBlank);
        Assert.Equal(37, prepared.Y);
        Assert.Equal(100, prepared.NameImage!.Height);
        Assert.Equal(prepared.RecognizedTextWidth + 5, prepared.NameImage.Width);
        Assert.Equal(1f, prepared.NameScale);
        Assert.Equal(-1, prepared.TemplateQuantity);
        Assert.Equal(0, Cv2.Norm(before, source, NormTypes.INF));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    [InlineData(255)]
    public void UniformBackgroundRemainsBlank(int level)
    {
        using var source = new Mat(60, 385, MatType.CV_8UC3, new Scalar(level, level, level));
        using var result = ToneMappedRareRowProcessor.Process(source, 0);

        Assert.True(result.IsBlank);
        Assert.Null(result.NameImage);
        Assert.Equal(642, result.RecognizedTextWidth);
    }
}
