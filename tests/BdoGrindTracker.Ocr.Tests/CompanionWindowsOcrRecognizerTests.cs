using OpenCvSharp;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionWindowsOcrRecognizerTests
{
    [Theory]
    [InlineData("en-US", "Black Stone x 17", .75f)]
    [InlineData("en-US", "Black Stone x 17", 1.5f)]
    [InlineData("de-DE", "Bruchstück x 12", .75f)]
    [InlineData("de-DE", "Bruchstück x 12", 1.5f)]
    public void AvailableNativeEngineReturnsOwnedWordGeometryFromTheOriginalRead(
        string language, string text, float scale)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate(language, requirePreferredLanguage: true);
        if (engine is null) return; // Optional Windows language packages are machine-local.
        using var bitmap = new Bitmap((int)(900 * scale), (int)(100 * scale), PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 36 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(text, font, Brushes.Black, 18 * scale, 20 * scale);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        using var image = Cv2.ImDecode(stream.ToArray(), ImreadModes.Color);

        var result = engine.Recognize(image);

        Assert.Equal(text, result.Text);
        Assert.Equal(text.Split(' '), result.Words.Select(word => word.Text));
        Assert.Equal(result.FirstWord, result.Words[0].Geometry);
        Assert.InRange(result.Words.Count, 1, 64);
        Assert.All(result.Words, word =>
        {
            Assert.Equal(CompanionOcrGeometryStatus.Success, word.Geometry.Status);
            Assert.InRange(word.Geometry.X, 0, image.Width - word.Geometry.Width);
            Assert.InRange(word.Geometry.Y, 0, image.Height - word.Geometry.Height);
        });
        Assert.True(Assert.IsAssignableFrom<IList<CompanionOcrWord>>(result.Words).IsReadOnly);
    }

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
