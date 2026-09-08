using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionNormalRowProcessorTests
{
    [Theory]
    [InlineData(129, 10)]
    [InlineData(259, 10)]
    [InlineData(260, 0)]
    [InlineData(350, 0)]
    public void NameOriginMetadataMatchesTheActualCropAtTheNarrowNameBoundary(int width, int expectedTop)
    {
        using var source = new Mat(100, width, MatType.CV_8UC3, Scalar.All(100));
        var glyphs = new Rect(35, 25, Math.Min(80, width - 40), 40);
        Cv2.Rectangle(source, glyphs, Scalar.All(232), -1);
        using var expectedSource = new Mat(100, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(expectedSource, glyphs, Scalar.All(232), -1);
        var processor = new CompanionNormalRowProcessor(null);

        using var result = processor.Process(source, 250, 1f, fontType: 2);

        Assert.False(result.IsBlank);
        Assert.Equal((float)expectedTop, result.NormalizedNameTop);
        using var expectedCrop = new Mat(expectedSource,
            new Rect(10, expectedTop, result.RecognizedTextWidth, 100 - 2 * expectedTop));
        using var expectedScaled = new Mat();
        using var expected = new Mat();
        Cv2.Resize(expectedCrop, expectedScaled, new OpenCvSharp.Size(), result.NameScale, result.NameScale,
            InterpolationFlags.Cubic);
        Cv2.CopyMakeBorder(expectedScaled, expected, 0, 0, 0, 5, BorderTypes.Constant, Scalar.Black);
        Assert.Equal(0d, Cv2.Norm(expected, result.NameImage!, NormTypes.INF));
    }

    [Fact]
    public void ToneMappedNarrowNameStillHasAFullHeightOrigin()
    {
        using var source = new Mat(100, 200, MatType.CV_8UC3, Scalar.All(100));
        Cv2.Rectangle(source, new Rect(35, 25, 80, 40), Scalar.All(232), -1);
        using var result = ToneMappedNormalRowProcessor.Process(source, 250);

        Assert.False(result.IsBlank);
        Assert.True(result.RecognizedTextWidth < 250);
        Assert.Equal(0f, result.NormalizedNameTop);
        Assert.Equal(100f, result.NameImage!.Height / result.NameScale);
    }

    [Theory]
    [InlineData(59.999f, 160)]
    [InlineData(60f, 170)]
    [InlineData(80f, 171)]
    [InlineData(110f, 172)]
    [InlineData(115f, 175)]
    [InlineData(120f, 176)]
    [InlineData(125f, 178)]
    [InlineData(130f, 180)]
    [InlineData(135f, 181)]
    [InlineData(145f, 182)]
    [InlineData(155f, 183)]
    [InlineData(165f, 185)]
    public void MinimumValueUsesCompanionsOrderedLumaThresholds(double averageLuma, int expected)
    {
        Assert.Equal(expected, CompanionNormalRowProcessor.CalculateMinimumValue(averageLuma));
    }

    [Theory]
    [InlineData(119, 2f)]
    [InlineData(120, 1.75f)]
    [InlineData(349, 1.75f)]
    [InlineData(350, 1.5f)]
    public void NameScaleUsesCompanionsWidthBands(int width, float expected)
    {
        Assert.Equal(expected, CompanionNormalRowProcessor.CalculateNameScale(width));
    }

    [Fact]
    public void BlankGateUsesInclusivePointNineNineThreeRatio()
    {
        using var exactlyBlank = Mat.Zeros(100, 100, MatType.CV_8UC1).ToMat();
        using var justNonBlank = exactlyBlank.Clone();
        for (var index = 0; index < 70; index++)
        {
            exactlyBlank.Set(index / 10, index % 10, (byte)255);
        }

        for (var index = 0; index < 71; index++)
        {
            justNonBlank.Set(index / 10, index % 10, (byte)255);
        }

        Assert.True(CompanionNormalRowProcessor.IsBlank(exactlyBlank));
        Assert.False(CompanionNormalRowProcessor.IsBlank(justNonBlank));
    }

    [Fact]
    public void SpecialOneKeepsCompanionsFinalZeroCoordinate()
    {
        var recognition = new CompanionQuantityRecognition(
            X: 0,
            Quantity: 1,
            AverageScore: 0.77f,
            IsSpecialOne: true);

        var leftmostX = CompanionNormalRowProcessor.ResolveLeftmostQuantityX(recognition);

        Assert.Equal(0, leftmostX);
        Assert.Equal(
            754,
            CompanionNormalRowProcessor.ResolveNameWidth(
                normalizedWidth: 764,
                hasQuantity: true,
                leftmostQuantityX: leftmostX));
    }

    [Fact]
    public void RegularQuantityCoordinatesAreTranslatedFromTheQuantityCrop()
    {
        var recognition = new CompanionQuantityRecognition(
            X: 17,
            Quantity: 42,
            AverageScore: 0.8f);

        Assert.Equal(137, CompanionNormalRowProcessor.ResolveLeftmostQuantityX(recognition));
    }

    [Fact]
    public void ProcessUsesCompanionsBgrWorkerContract()
    {
        using var bgra = new Mat(75, 573, MatType.CV_8UC4, Scalar.Black);
        var processor = new CompanionNormalRowProcessor(quantityRecognizer: null);

        var error = Assert.Throws<ArgumentException>(() =>
            processor.Process(bgra, y: 298, uiScale: 1.49f, fontType: 2));

        Assert.Equal("sourceBand", error.ParamName);
    }

    [Fact]
    public void MainMaskThresholdDependsOnAverageLumaAndNotSlotY()
    {
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(100, 100, 100));
        Cv2.Rectangle(source, new Rect(150, 20, 250, 30), new Scalar(175, 175, 175), -1);
        var processor = new CompanionNormalRowProcessor(quantityRecognizer: null);

        using var top = processor.Process(source, y: 0, uiScale: 1.49f, fontType: 0);
        using var bottom = processor.Process(source, y: 298, uiScale: 1.49f, fontType: 0);

        Assert.False(top.IsBlank);
        Assert.False(bottom.IsBlank);
        Assert.Equal(top.AverageLuma, bottom.AverageLuma);
        Assert.Equal(top.RecognizedTextWidth, bottom.RecognizedTextWidth);
        Assert.Equal(0d, Cv2.Norm(top.NameImage!, bottom.NameImage!, NormTypes.INF));
    }

    [Fact]
    public void EmptyTranslucentBandSurvivesMeanGateButFailsPixelGate()
    {
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(100, 100, 100));
        var processor = new CompanionNormalRowProcessor(quantityRecognizer: null);

        using var result = processor.Process(source, y: 298, uiScale: 1.49f, fontType: 2);

        Assert.True(result.IsBlank);
        Assert.Equal(CompanionNormalRowProcessor.MissingQuantity, result.TemplateQuantity);
        Assert.Null(result.NameImage);
        Assert.Equal(754, result.RecognizedTextWidth);
    }

    [Fact]
    public void BlankWidthUsesNormalizedColumnsMinusTenForShortFinalBand()
    {
        using var source = new Mat(74, 573, MatType.CV_8UC3, Scalar.Black);
        var processor = new CompanionNormalRowProcessor(quantityRecognizer: null);

        using var result = processor.Process(source, y: 373, uiScale: 1.49f, fontType: 2);

        Assert.True(result.IsBlank);
        Assert.Equal(764, result.RecognizedTextWidth);
    }

    [Fact]
    public void NormalPathProducesTheVerifiedNoQuantityNameGeometry()
    {
        using var source = Mat.Zeros(75, 573, MatType.CV_8UC3).ToMat();
        Cv2.Rectangle(source, new Rect(150, 20, 250, 30), new Scalar(200, 200, 200), -1);
        var processor = new CompanionNormalRowProcessor(quantityRecognizer: null);

        using var result = processor.Process(source, y: 298, uiScale: 1.49f, fontType: 2);

        Assert.False(result.IsBlank);
        Assert.Equal(754, result.RecognizedTextWidth);
        Assert.Equal(1.5f, result.NameScale);
        Assert.Equal(CompanionNormalRowProcessor.MissingQuantity, result.TemplateQuantity);
        Assert.Equal(0, result.LeftmostQuantityX);
        Assert.NotNull(result.NameImage);
        Assert.Equal(150, result.NameImage!.Rows);
        Assert.Equal(1136, result.NameImage.Cols);
    }

    [Fact]
    public void HdrPathUsesCompanionsBrightPixelBandInsteadOfTheSdrRange()
    {
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(100, 100, 100));
        Cv2.Rectangle(source, new Rect(150, 20, 250, 30), Scalar.White, -1);
        var processor = new CompanionNormalRowProcessor(quantityRecognizer: null);

        using var sdr = processor.Process(
            source,
            y: 298,
            uiScale: 1.49f,
            fontType: 2,
            isHdr: false);
        using var hdr = processor.Process(
            source,
            y: 298,
            uiScale: 1.49f,
            fontType: 2,
            isHdr: true);

        Assert.True(sdr.IsBlank);
        Assert.False(hdr.IsBlank);
        Assert.NotNull(hdr.NameImage);
        Assert.True(Cv2.CountNonZero(hdr.NameImage!) > 0);
    }

    [Theory]
    [InlineData(189d, 250d, 158d, 23d)]
    [InlineData(190d, 254d, 20d, 8d)]
    public void HdrMaskUsesCompanionsLumaSplit(
        double averageLuma,
        double expectedMinimumValue,
        double expectedMaximumHue,
        double expectedMaximumSaturation)
    {
        using var hsv = new Mat(1, 1, MatType.CV_8UC3, Scalar.Black);
        using var actual = new Mat();

        CompanionNormalRowProcessor.CreateTextMask(
            hsv,
            averageLuma,
            uiScale: 1f,
            fontType: 2,
            isHdr: true,
            actual);

        // Exercise the exact endpoints independently; these are the two native
        // HDR scalars selected at the 190-luma boundary.
        using var atMinimum = new Mat(
            1,
            1,
            MatType.CV_8UC3,
            new Scalar(0, 0, expectedMinimumValue));
        using var overHue = new Mat(
            1,
            1,
            MatType.CV_8UC3,
            new Scalar(expectedMaximumHue + 1, expectedMaximumSaturation, 255));
        using var minimumMask = new Mat();
        using var hueMask = new Mat();
        CompanionNormalRowProcessor.CreateTextMask(
            atMinimum,
            averageLuma,
            1f,
            2,
            true,
            minimumMask);
        CompanionNormalRowProcessor.CreateTextMask(
            overHue,
            averageLuma,
            1f,
            2,
            true,
            hueMask);

        Assert.Equal(255, minimumMask.At<byte>(0, 0));
        Assert.Equal(0, hueMask.At<byte>(0, 0));
    }
}
