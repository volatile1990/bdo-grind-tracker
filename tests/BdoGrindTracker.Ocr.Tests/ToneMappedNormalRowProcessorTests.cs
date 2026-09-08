using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class ToneMappedNormalRowProcessorTests
{
    [Fact]
    public void PaleNeutralLettersSurviveWhileDarkerNeutralSceneryIsRemoved()
    {
        using var source = new Mat(100, 400, MatType.CV_8UC3, new Scalar(210, 210, 210));
        Cv2.Rectangle(source, new Rect(100, 35, 30, 20), new Scalar(232, 232, 232), -1);

        using var result = ToneMappedNormalRowProcessor.Process(source, y: 224);

        Assert.False(result.IsBlank);
        Assert.Equal(224, result.Y);
        Assert.NotNull(result.NameImage);
        Assert.True(PixelAtOriginalPosition(result, 115, 45) > 200);
        Assert.Equal(0, PixelAtOriginalPosition(result, 200, 45));
        Assert.Equal(-1, result.TemplateQuantity);
    }

    [Fact]
    public void DigitLikeStrokeInsideItemNameCannotCropAwayTheRestOfTheRow()
    {
        using var source = new Mat(100, 500, MatType.CV_8UC3, new Scalar(100, 100, 100));
        Cv2.Rectangle(source, new Rect(160, 35, 5, 24), new Scalar(232, 232, 232), -1);
        Cv2.Rectangle(source, new Rect(450, 35, 25, 24), new Scalar(232, 232, 232), -1);

        using var result = ToneMappedNormalRowProcessor.Process(source, 50);

        Assert.False(result.IsBlank);
        Assert.Equal(490, result.RecognizedTextWidth);
        Assert.Equal(-1, result.TemplateQuantity);
        Assert.Equal(0, result.LeftmostQuantityX);
        Assert.True(PixelAtOriginalPosition(result, 462, 45) > 200);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(210)]
    public void EmptyDarkOrPaleScenicBandIsBlank(int value)
    {
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(value, value, value));

        using var result = ToneMappedNormalRowProcessor.Process(source, 298);

        Assert.True(result.IsBlank);
        Assert.Null(result.NameImage);
        Assert.Equal(-1, result.TemplateQuantity);
    }

    [Fact]
    public void BrightPixelsAboveAndBelowTheTextLineDoNotCreateAnOcrRow()
    {
        using var source = new Mat(100, 400, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(source, new Rect(20, 0, 360, 15), Scalar.White, -1);
        Cv2.Rectangle(source, new Rect(20, 80, 360, 20), Scalar.White, -1);

        using var result = ToneMappedNormalRowProcessor.Process(source, 0);

        Assert.True(result.IsBlank);
        Assert.Null(result.NameImage);
    }

    [Fact]
    public void ReturnedImageOwnsItsPixelsAfterSourceIsDisposed()
    {
        var source = new Mat(100, 400, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(source, new Rect(100, 35, 100, 20), new Scalar(232, 232, 232), -1);
        using var result = ToneMappedNormalRowProcessor.Process(source, 0);
        using var before = result.NameImage!.Clone();

        source.SetTo(Scalar.Black);
        source.Dispose();

        Assert.Equal(0d, Cv2.Norm(before, result.NameImage!, NormTypes.INF));
        Assert.True(Cv2.CountNonZero(result.NameImage!) > 0);
    }

    [Fact]
    public void DisposingResultLeavesCallerOwnedSourceUnchanged()
    {
        using var source = new Mat(100, 400, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(source, new Rect(100, 35, 100, 20), new Scalar(232, 232, 232), -1);
        using var before = source.Clone();

        ToneMappedNormalRowProcessor.Process(source, 0).Dispose();

        Assert.Equal(0d, Cv2.Norm(before, source, NormTypes.INF));
    }

    [Fact]
    public void InvalidSourceFailsBeforeCreatingAnOcrImage()
    {
        Assert.Throws<ArgumentNullException>(() => ToneMappedNormalRowProcessor.Process(null!, 0));
        using var empty = new Mat();
        using var gray = new Mat(100, 400, MatType.CV_8UC1);
        using var bgra = new Mat(100, 400, MatType.CV_8UC4);
        using var floatingPoint = new Mat(100, 400, MatType.CV_32FC3);
        foreach (var invalid in new[] { empty, gray, bgra, floatingPoint })
            Assert.Equal("sourceBand", Assert.Throws<ArgumentException>(() =>
                ToneMappedNormalRowProcessor.Process(invalid, 0)).ParamName);
    }

    private static byte PixelAtOriginalPosition(CompanionNormalRowResult result, int x, int y) =>
        result.NameImage!.At<byte>((int)(y * result.NameScale), (int)((x - 10) * result.NameScale));
}
