using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionRareRowProcessorTests
{
    [Fact]
    public void ModeOneDoesNotApplyTheNormalDarkMeanEarlyReturn()
    {
        using var source = Mat.Zeros(75, 573, MatType.CV_8UC3).ToMat();
        Cv2.Rectangle(
            source,
            new Rect(100, 20, 120, 10),
            ToBgr(hue: 20, saturation: 80, value: 200),
            -1);
        var processor = new CompanionRareRowProcessor();

        using var result = processor.Process(source, y: 0, uiScale: 1.49f, fontType: 0);

        Assert.True(result.AverageLuma < 25d);
        Assert.False(result.IsBlank);
        Assert.Equal(CompanionNormalRowProcessor.MissingQuantity, result.TemplateQuantity);
        Assert.Equal(0, result.LeftmostQuantityX);
        Assert.Equal(0f, result.QuantityScore);
    }

    [Fact]
    public void ModeOneUsesTheFullNormalizedRowAndForcesScaleOne()
    {
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(100, 100, 100));
        Cv2.Rectangle(
            source,
            new Rect(140, 20, 250, 30),
            ToBgr(hue: 20, saturation: 50, value: 200),
            -1);
        var processor = new CompanionRareRowProcessor();

        using var result = processor.Process(source, y: 0, uiScale: 1.49f, fontType: 0);

        Assert.False(result.IsBlank);
        Assert.Equal(764, result.RecognizedTextWidth);
        Assert.Equal(1f, result.NameScale);
        Assert.NotNull(result.NameImage);
        Assert.Equal(100, result.NameImage!.Rows);
        Assert.Equal(769, result.NameImage.Cols);
    }

    [Fact]
    public void ModeOneFontGreaterThanOneUsesThePreOrHsvTextMask()
    {
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(100, 100, 100));
        Cv2.Rectangle(
            source,
            new Rect(140, 20, 250, 30),
            ToBgr(hue: 20, saturation: 50, value: 200),
            -1);
        var processor = new CompanionRareRowProcessor();

        using var result = processor.Process(source, y: 0, uiScale: 1.49f, fontType: 2);

        Assert.False(result.IsBlank);
        Assert.NotNull(result.NameImage);
        Assert.True(Cv2.CountNonZero(result.NameImage!) > 0);
    }

    [Fact]
    public void ModeOneBlankContractKeepsTheFullNormalizedWidth()
    {
        using var source = new Mat(74, 573, MatType.CV_8UC3, Scalar.Black);
        var processor = new CompanionRareRowProcessor();

        using var result = processor.Process(source, y: 0, uiScale: 1.49f, fontType: 2);

        Assert.True(result.IsBlank);
        Assert.Equal(774, result.RecognizedTextWidth);
        Assert.Equal(CompanionNormalRowProcessor.MissingQuantity, result.TemplateQuantity);
        Assert.Equal(1f, result.NameScale);
        Assert.Null(result.NameImage);
    }

    [Fact]
    public void ModeOneUsesCompanionsBgrWorkerContract()
    {
        using var bgra = new Mat(75, 573, MatType.CV_8UC4, Scalar.Black);
        var processor = new CompanionRareRowProcessor();

        var error = Assert.Throws<ArgumentException>(() =>
            processor.Process(bgra, y: 0, uiScale: 1.49f, fontType: 2));

        Assert.Equal("sourceBand", error.ParamName);
    }

    [Fact]
    public void ModeOneHdrAndSdrUseTheirDistinctNativeColorWindows()
    {
        using var hdrPixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar(0, 0, 160));
        using var sdrPixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 35, 140));
        using var hdrMask = new Mat();
        using var sdrMask = new Mat();
        using var hdrPixelUnderSdr = new Mat();
        using var sdrPixelUnderHdr = new Mat();

        CompanionRareRowProcessor.CreateInitialTextMask(
            hdrPixel,
            isHdr: true,
            destination: hdrMask);
        CompanionRareRowProcessor.CreateInitialTextMask(
            sdrPixel,
            isHdr: false,
            destination: sdrMask);
        CompanionRareRowProcessor.CreateInitialTextMask(
            hdrPixel,
            isHdr: false,
            destination: hdrPixelUnderSdr);
        CompanionRareRowProcessor.CreateInitialTextMask(
            sdrPixel,
            isHdr: true,
            destination: sdrPixelUnderHdr);

        Assert.Equal(255, hdrMask.At<byte>(0, 0));
        Assert.Equal(255, sdrMask.At<byte>(0, 0));
        Assert.Equal(0, hdrPixelUnderSdr.At<byte>(0, 0));
        Assert.Equal(0, sdrPixelUnderHdr.At<byte>(0, 0));
    }

    private static Scalar ToBgr(byte hue, byte saturation, byte value)
    {
        using var hsv = new Mat(
            1,
            1,
            MatType.CV_8UC3,
            new Scalar(hue, saturation, value));
        using var bgr = new Mat();
        Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
        var pixel = bgr.At<Vec3b>(0, 0);
        return new Scalar(pixel.Item0, pixel.Item1, pixel.Item2);
    }
}
