using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class RareLootRecoveryPreprocessorTests
{
    [Theory]
    [InlineData(NormalLootRecoveryVariant.Grayscale)]
    [InlineData(NormalLootRecoveryVariant.AdaptiveThreshold)]
    public void RecoveryKeepsTheCompleteSpecialBandWhenNativeColorMaskLosesItsText(NormalLootRecoveryVariant variant)
    {
        using var source = new Mat(60, 385, MatType.CV_8UC3, new Scalar(35, 35, 35));
        Cv2.PutText(source, "Twilight of the End - Ring", new OpenCvSharp.Point(2, 39),
            HersheyFonts.HersheySimplex, .65, new Scalar(105, 105, 105), 2, LineTypes.AntiAlias);
        using var baseline = new CompanionRareRowProcessor().Process(source, 0, 1, 0);
        using var original = source.Clone();
        using var recovery = RareLootRecoveryPreprocessor.Prepare(source, variant);

        Assert.True(baseline.IsBlank);
        Assert.Equal(642, recovery.RecognizedTextWidth);
        Assert.Equal(647, recovery.NameImage.Width);
        Assert.Equal(100, recovery.NameImage.Height);
        Assert.Equal(1f, recovery.NameScale);
        Assert.Null(recovery.QuantityImage);
        Cv2.MinMaxLoc(recovery.NameImage, out double minimum, out double maximum);
        Assert.True(maximum > minimum + 20);
        Assert.Equal(0, Cv2.Norm(original, source, NormTypes.INF));
        source.SetTo(Scalar.Black);
        Assert.True(Cv2.CountNonZero(recovery.NameImage) > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(180)]
    [InlineData(255)]
    public void ConstantBandsDoNotCreateLocalContrast(int level)
    {
        using var source = new Mat(60, 385, MatType.CV_8UC3, new Scalar(level, level, level));
        using var result = RareLootRecoveryPreprocessor.Prepare(source, NormalLootRecoveryVariant.AdaptiveThreshold);

        Assert.Equal(0, Cv2.CountNonZero(result.NameImage));
        Assert.Null(result.QuantityImage);
    }

    [Theory]
    [InlineData(0, 0, 25, 20, true)]
    [InlineData(250, 75, 100, 20, true)]
    [InlineData(-1, 30, 100, 20, false)]
    [InlineData(25, -1, 100, 20, false)]
    [InlineData(600, 35, 100, 20, false)]
    [InlineData(25, 90, 100, 20, false)]
    [InlineData(25, 35, 0, 20, false)]
    [InlineData(25, 35, 100, 0, false)]
    [InlineData(float.NaN, 35, 100, 20, false)]
    public void GeometryUsesTheFullAnnouncementInsteadOfNormalLeftAndTopOffsets(
        float x, float y, float width, float height, bool accepted) =>
        Assert.Equal(accepted, RareLootRecoveryPreprocessor.PassesGeometryGate(
            new(CompanionOcrGeometryStatus.Success, x, y, width, height), 642));

    [Theory]
    [InlineData(CompanionOcrGeometryStatus.Missing)]
    [InlineData(CompanionOcrGeometryStatus.Error)]
    public void RecoveryRequiresActualWordGeometry(CompanionOcrGeometryStatus status) =>
        Assert.False(RareLootRecoveryPreprocessor.PassesGeometryGate(new(status, 25, 35, 100, 20), 642));

    [Fact]
    public void InvalidInputIsRejectedBeforeImagePreparation()
    {
        using var empty = new Mat();
        using var gray = new Mat(60, 385, MatType.CV_8UC1, Scalar.Black);
        using var valid = new Mat(60, 385, MatType.CV_8UC3, Scalar.Black);
        Assert.Throws<ArgumentNullException>(() => RareLootRecoveryPreprocessor.Prepare(null!, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentException>(() => RareLootRecoveryPreprocessor.Prepare(empty, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentException>(() => RareLootRecoveryPreprocessor.Prepare(gray, NormalLootRecoveryVariant.Grayscale));
        Assert.Throws<ArgumentOutOfRangeException>(() => RareLootRecoveryPreprocessor.Prepare(valid, (NormalLootRecoveryVariant)42));
    }
}
