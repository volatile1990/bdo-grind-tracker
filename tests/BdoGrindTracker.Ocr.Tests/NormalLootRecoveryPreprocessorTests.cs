using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class NormalLootRecoveryPreprocessorTests
{
    [Theory]
    [InlineData(NormalLootRecoveryVariant.Grayscale)]
    [InlineData(NormalLootRecoveryVariant.AdaptiveThreshold)]
    public void DimTextRejectedByBaselineIsStillAvailableForRecovery(NormalLootRecoveryVariant variant)
    {
        using var source = NewBand();
        DrawText(source, "Black Crystal Fragment", 20, brightness: 105);
        DrawText(source, "42", 580, brightness: 105);
        using var baseline = new CompanionNormalRowProcessor(null).Process(source, 0, 1f, 0);
        using var recovery = NormalLootRecoveryPreprocessor.Prepare(source, variant);

        Assert.True(baseline.IsBlank);
        Assert.NotNull(recovery.QuantityImage);
        Assert.Equal(MatType.CV_8UC1, recovery.NameImage.Type());
        Assert.Equal(690, recovery.RecognizedTextWidth);
        Assert.Equal(1.5f, recovery.NameScale);
        Assert.Equal(150, recovery.NameImage.Height);
        Assert.Equal(1040, recovery.NameImage.Width);
        Cv2.MinMaxLoc(recovery.NameImage, out double minimum, out double maximum);
        Assert.True(maximum > minimum + 20);
        Assert.True(Cv2.CountNonZero(recovery.QuantityImage!) > 0);
    }

    [Theory]
    [InlineData(NormalLootRecoveryVariant.Grayscale)]
    [InlineData(NormalLootRecoveryVariant.AdaptiveThreshold)]
    public void QuantityCropContainsOnlyRightmostSeparatedBlock(NormalLootRecoveryVariant variant)
    {
        using var quantityOnly = NewBand();
        DrawText(quantityOnly, "42", 580);
        using var withNameAndEarlierNumber = quantityOnly.Clone();
        DrawText(withNameAndEarlierNumber, "Black Crystal", 20);
        DrawText(withNameAndEarlierNumber, "999", 400);
        using var expected = NormalLootRecoveryPreprocessor.Prepare(quantityOnly, variant);
        using var actual = NormalLootRecoveryPreprocessor.Prepare(withNameAndEarlierNumber, variant);

        Assert.NotNull(expected.QuantityImage);
        Assert.NotNull(actual.QuantityImage);
        Assert.Equal(expected.QuantityImage!.Size(), actual.QuantityImage!.Size());
        Assert.Equal(0d, Cv2.Norm(expected.QuantityImage, actual.QuantityImage, NormTypes.INF));
        Assert.True(actual.QuantityImage.Width < 150);
        Assert.True(actual.QuantityImage.Height >= 48);
    }

    [Theory]
    [InlineData(NormalLootRecoveryVariant.Grayscale)]
    [InlineData(NormalLootRecoveryVariant.AdaptiveThreshold)]
    public void AdjacentDigitsRemainOneGroup(NormalLootRecoveryVariant variant)
    {
        using var single = NewBand();
        DrawText(single, "1", 580);
        using var multiple = NewBand();
        DrawText(multiple, "124", 580);
        using var singleView = NormalLootRecoveryPreprocessor.Prepare(single, variant);
        using var multipleView = NormalLootRecoveryPreprocessor.Prepare(multiple, variant);

        Assert.NotNull(singleView.QuantityImage);
        Assert.NotNull(multipleView.QuantityImage);
        Assert.True(multipleView.QuantityImage!.Width > singleView.QuantityImage!.Width * 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(120)]
    [InlineData(255)]
    public void ConstantBandsDoNotBecomeAdaptiveForegroundOrQuantities(int brightness)
    {
        using var source = NewBand(brightness);
        using var grayscale = NormalLootRecoveryPreprocessor.Prepare(source, NormalLootRecoveryVariant.Grayscale);
        using var adaptive = NormalLootRecoveryPreprocessor.Prepare(source, NormalLootRecoveryVariant.AdaptiveThreshold);

        Assert.Null(grayscale.QuantityImage);
        Assert.Null(adaptive.QuantityImage);
        Assert.Equal(0, Cv2.CountNonZero(adaptive.NameImage));
        Assert.Equal(brightness, grayscale.NameImage.At<byte>(20, 20));
    }

    [Fact]
    public void RecoveryDoesNotRequireQuantityRegionToProduceNameView()
    {
        using var source = new Mat(100, 120, MatType.CV_8UC3, Scalar.Black);
        DrawText(source, "Dust", 20);
        using var recovery = NormalLootRecoveryPreprocessor.Prepare(source, NormalLootRecoveryVariant.Grayscale);

        Assert.Null(recovery.QuantityImage);
        Assert.Equal(110, recovery.RecognizedTextWidth);
        Assert.Equal(2f, recovery.NameScale);
        Assert.Equal(200, recovery.NameImage.Height);
        Assert.Equal(225, recovery.NameImage.Width);
    }

    [Fact]
    public void TruncatedGroupAtLeftOfQuantityRegionDoesNotMakeAnIsolatedQuantity()
    {
        using var source = NewBand();
        Cv2.Rectangle(source, new Rect(110, 45, 40, 20), Scalar.White, -1);
        using var recovery = NormalLootRecoveryPreprocessor.Prepare(source, NormalLootRecoveryVariant.AdaptiveThreshold);

        Assert.Null(recovery.QuantityImage);
        Assert.True(Cv2.CountNonZero(recovery.NameImage) > 0);
    }

    [Theory]
    [InlineData(NormalLootRecoveryVariant.Grayscale)]
    [InlineData(NormalLootRecoveryVariant.AdaptiveThreshold)]
    public void QuantityRegionExcludesTextAboveAndLeftOfItsBounds(NormalLootRecoveryVariant variant)
    {
        using var source = NewBand();
        Cv2.Rectangle(source, new Rect(40, 45, 15, 20), Scalar.White, -1);
        Cv2.Rectangle(source, new Rect(600, 4, 20, 15), Scalar.White, -1);
        using var recovery = NormalLootRecoveryPreprocessor.Prepare(source, variant);

        Assert.Null(recovery.QuantityImage);
    }

    [Fact]
    public void NonNormalizedSourceRetainsFullBandYGeometry()
    {
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(35, 35, 35));
        using var recovery = NormalLootRecoveryPreprocessor.Prepare(source, NormalLootRecoveryVariant.Grayscale);

        Assert.Equal(754, recovery.RecognizedTextWidth);
        Assert.Equal(1.5f, recovery.NameScale);
        Assert.Equal(150, recovery.NameImage.Height);
        Assert.Equal(1136, recovery.NameImage.Width);
    }

    [Theory]
    [InlineData(NormalLootRecoveryVariant.Grayscale)]
    [InlineData(NormalLootRecoveryVariant.AdaptiveThreshold)]
    public void SourceIsUnchangedAndOutputHasIndependentOwnership(NormalLootRecoveryVariant variant)
    {
        using var source = NewBand();
        DrawText(source, "42", 580);
        using var before = source.Clone();
        var recovery = NormalLootRecoveryPreprocessor.Prepare(source, variant);
        var name = recovery.NameImage;
        var quantity = recovery.QuantityImage;
        Assert.NotNull(quantity);
        Assert.Equal(0d, Cv2.Norm(before, source, NormTypes.INF));

        source.SetTo(Scalar.Black);
        Assert.True(Cv2.CountNonZero(name) > 0);
        Assert.True(Cv2.CountNonZero(quantity!) > 0);
        recovery.Dispose();
        recovery.Dispose();

        Assert.True(name.IsDisposed);
        Assert.True(quantity!.IsDisposed);
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public void InvalidInputsAreReportedBeforeNativePreparation()
    {
        using var empty = new Mat();
        using var gray = new Mat(100, 700, MatType.CV_8UC1, Scalar.Black);
        using var bgra = new Mat(100, 700, MatType.CV_8UC4, Scalar.Black);
        using var tooNarrow = new Mat(100, 10, MatType.CV_8UC3, Scalar.Black);
        using var valid = NewBand();

        Assert.Throws<ArgumentNullException>(() => NormalLootRecoveryPreprocessor.Prepare(null!, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentException>(() => NormalLootRecoveryPreprocessor.Prepare(empty, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentException>(() => NormalLootRecoveryPreprocessor.Prepare(gray, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentException>(() => NormalLootRecoveryPreprocessor.Prepare(bgra, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentException>(() => NormalLootRecoveryPreprocessor.Prepare(tooNarrow, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentOutOfRangeException>(() => NormalLootRecoveryPreprocessor.Prepare(valid, (NormalLootRecoveryVariant)42));
    }

    private static Mat NewBand(int brightness = 35) =>
        new(100, 700, MatType.CV_8UC3, new Scalar(brightness, brightness, brightness));

    private static void DrawText(Mat image, string text, int left, int brightness = 190) =>
        Cv2.PutText(image, text, new OpenCvSharp.Point(left, 75), HersheyFonts.HersheySimplex,
            0.7, new Scalar(brightness, brightness, brightness), 2, LineTypes.AntiAlias);
}
