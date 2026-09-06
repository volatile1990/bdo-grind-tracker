using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionWindowsOcrRecognizerTests
{
    [Theory]
    [InlineData(CompanionOcrGeometryStatus.Missing, 0f, 0f)]
    [InlineData(CompanionOcrGeometryStatus.Success, 201f, 50f)]
    [InlineData(CompanionOcrGeometryStatus.Success, 20f, 18f)]
    [InlineData(CompanionOcrGeometryStatus.Success, 20f, 78f)]
    public void RareModeBypassesTheNormalFirstWordGeometryGate(
        CompanionOcrGeometryStatus status,
        float x,
        float y)
    {
        var geometry = new CompanionOcrWordGeometry(status, x, y, 10f, 10f);

        Assert.False(CompanionWindowsOcrRecognizer.PassesGeometryGate(
            geometry,
            uiScale: 1.49f,
            rareDropMode: false));
        Assert.True(CompanionWindowsOcrRecognizer.PassesGeometryGate(
            geometry,
            uiScale: 1.49f,
            rareDropMode: true));
    }

    [Theory]
    [InlineData(1f, 200f, 26f, true)]
    [InlineData(1f, 200f, 70f, true)]
    [InlineData(1f, 201f, 40f, false)]
    [InlineData(1f, 100f, 25.99f, false)]
    [InlineData(1.01f, 100f, 19f, true)]
    [InlineData(1.01f, 100f, 77f, true)]
    [InlineData(1.01f, 100f, 77.01f, false)]
    public void NormalGeometryGateUsesVerifiedInclusiveBounds(
        float scale,
        float x,
        float y,
        bool expected)
    {
        var geometry = new CompanionOcrWordGeometry(
            CompanionOcrGeometryStatus.Success,
            x,
            y,
            10,
            10);

        Assert.Equal(
            expected,
            CompanionWindowsOcrRecognizer.PassesNormalGeometryGate(geometry, scale));
    }

    [Fact]
    public void GeometryHresultBypassesRejectButMissingWordDoesNot()
    {
        var error = new CompanionOcrWordGeometry(
            CompanionOcrGeometryStatus.Error,
            0,
            0,
            0,
            0);
        var missing = new CompanionOcrWordGeometry(
            CompanionOcrGeometryStatus.Missing,
            0,
            0,
            0,
            0);

        Assert.True(CompanionWindowsOcrRecognizer.PassesNormalGeometryGate(error, 1.49f));
        Assert.False(CompanionWindowsOcrRecognizer.PassesNormalGeometryGate(missing, 1.49f));
    }

    [Fact]
    public void RecognizerAcceptsOnlySupportedEightBitLayouts()
    {
        (MatType Type, bool Expected)[] cases =
        [
            (MatType.CV_8UC1, true),
            (MatType.CV_8UC3, true),
            (MatType.CV_8UC4, true),
            (MatType.CV_8UC2, false),
            (MatType.CV_16UC1, false),
        ];

        foreach (var (type, expected) in cases)
        {
            using var image = new Mat(8, 8, type);
            Assert.Equal(expected, CompanionWindowsOcrRecognizer.SupportsImage(image));
        }
    }

    [Fact]
    public void InvalidPreferredLanguageTagIsNotSilentlyReplaced()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CompanionWindowsOcrRecognizer.TryCreate("en_US"));

        Assert.Contains("BCP-47", exception.Message, StringComparison.Ordinal);
    }

}
