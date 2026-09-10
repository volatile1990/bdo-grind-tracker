using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Security.Cryptography;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class SecondaryLootOcrRecognizerTests
{
    [Fact]
    public void MissingModelReportsItsExpectedLocalPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var error = Assert.Throws<FileNotFoundException>(() => PaddleLootOcrRecognizer.Create("de-DE", directory));
        Assert.Equal(Path.Combine(directory, "inference.onnx"), error.FileName);
    }

    [Fact]
    public void BundledModelHasTheVerifiedUpstreamBytes()
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "data", "ocr", "paddle-v6-small", "inference.onnx"));
        Assert.Equal(PaddleLootOcrRecognizer.ModelSha256, Convert.ToHexString(SHA256.HashData(file)));
        using var dictionary = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "data", "ocr", "paddle-v6-small", "characters.json"));
        Assert.Equal("BF37AA18B879FE7D9B7A70112C59BF6E3E994EAB8FF7B22882E3D76E485C9DF1",
            Convert.ToHexString(SHA256.HashData(dictionary)));
    }

    [Theory]
    [InlineData("en-US", "Black Stone x 17")]
    [InlineData("de-DE", "Bruchstück x 12")]
    public void BundledNativeEngineRecognizesARealRenderedRow(string language, string text)
    {
        using var engine = PaddleLootOcrRecognizer.Create(language);
        using var image = RenderRow(text);
        var result = engine.Recognize(image);
        Assert.Equal(text.Replace(" ", ""), result.Text.Replace(" ", ""));
        Assert.InRange(result.Confidence, 0.8f, 1f);
        Assert.Equal(CompanionOcrGeometryStatus.Missing, result.FirstWord.Status);
        Assert.Empty(result.Words);
        Assert.Equal(language, engine.LanguageTag);
    }

    [Fact]
    public void CtcCollapsesRepeatsButPreservesRepeatedDigitsSeparatedByBlankAndUnicode()
    {
        string[] alphabet = ["blank", "1", "ü", "𠀀"];
        var indices = new[] { 0, 1, 1, 0, 1, 2, 2, 3, 0 };
        var scores = indices.SelectMany(index => Enumerable.Range(0, alphabet.Length)
            .Select(i => i == index ? .99f : .001f)).ToArray();
        var result = PaddleLootOcrRecognizer.Decode(scores, alphabet);
        Assert.Equal("11ü𠀀", result.Text);
        Assert.InRange(result.Confidence, .989f, .991f);
    }

    [Fact]
    public void TensorKeepsBgrOrderAspectRatioAndNormalizedZeroPadding()
    {
        using var image = new Mat(48, 100, MatType.CV_8UC3, new Scalar(0, 127, 255));
        var (pixels, width) = PaddleLootOcrRecognizer.PrepareInput(image);
        Assert.Equal(320, width);
        Assert.Equal(-1, pixels[0]);
        Assert.InRange(pixels[48 * width], -.005f, 0);
        Assert.Equal(1, pixels[2 * 48 * width]);
        Assert.Equal(0, pixels[100]);
        Assert.Equal(0, pixels[^1]);
    }

    [Fact]
    public async Task SeparateWorkersCanReadEnglishAndGermanAtTheSameTime()
    {
        using var english = PaddleLootOcrRecognizer.Create("en-US");
        using var german = PaddleLootOcrRecognizer.Create("de-DE");
        using var englishImage = RenderRow("Black Stone x 17");
        using var germanImage = RenderRow("Bruchstück x 12");

        var results = await Task.WhenAll(
            Task.Run(() => english.Recognize(englishImage)),
            Task.Run(() => german.Recognize(germanImage)));

        Assert.Equal("BlackStonex17", results[0].Text.Replace(" ", ""));
        Assert.Equal("Bruchstückx12", results[1].Text.Replace(" ", ""));
    }

    [Fact]
    public void CancelledReadDoesNotEnterNativeRecognition()
    {
        using var engine = PaddleLootOcrRecognizer.Create("en-US");
        using var image = RenderRow("Black Stone x 17");
        Assert.Throws<OperationCanceledException>(() =>
            engine.Recognize(image, new CancellationToken(canceled: true)));
        Assert.Equal("BlackStonex17", engine.Recognize(image).Text.Replace(" ", ""));
    }

    [Fact]
    public void DisposedEngineRejectsFurtherReadAndCanBeDisposedTwice()
    {
        using var engine = PaddleLootOcrRecognizer.Create("en-US");
        using var image = RenderRow("Black Stone x 17");
        engine.Dispose();
        engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.Recognize(image));
    }

    [Fact]
    public void EmptyOrNonByteImagesAreRejectedBeforeNativeRecognition()
    {
        using var engine = PaddleLootOcrRecognizer.Create("en-US");
        using var empty = new Mat();
        using var floatingPoint = new Mat(30, 200, MatType.CV_32FC1, Scalar.All(0));
        Assert.Throws<ArgumentException>(() => engine.Recognize(empty));
        Assert.Throws<ArgumentException>(() => engine.Recognize(floatingPoint));
    }

    private static Mat RenderRow(string text)
    {
        using var font = new Font("Segoe UI", 36, FontStyle.Regular, GraphicsUnit.Pixel);
        const int padding = 4;
        // This recognition-only engine receives a cropped text line in production.
        // Size the fixture from its line height so normalization does not shrink
        // the glyphs together with an unrelated, mostly empty panel background.
        using var bitmap = new Bitmap(900, font.Height + 2 * padding, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(text, font, Brushes.Black, padding, padding);
        graphics.Flush();
        return CompanionFrameDecoder.Decode(bitmap);
    }
}
