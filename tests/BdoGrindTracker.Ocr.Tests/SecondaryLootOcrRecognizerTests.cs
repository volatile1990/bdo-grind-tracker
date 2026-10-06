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

    [Theory]
    [InlineData(1, 100, 31)]
    [InlineData(3, 703, 43)]
    [InlineData(4, 3500, 41)]
    public void TensorMatchesLegacyElementAccessBitForBitForStridedInput(int channels, int width, int height)
    {
        using var parent = new Mat(height + 4, width + 8, MatType.CV_8UC(channels));
        var parentHeight = parent.Height;
        var parentWidth = parent.Width;
        for (var y = 0; y < parentHeight; y++)
        for (var x = 0; x < parentWidth; x++)
        {
            var blue = (byte)((x * 13 + y * 37) % 256);
            var green = (byte)((x * 43 + y * 11) % 256);
            var red = (byte)((x * 7 + y * 29) % 256);
            if (channels == 1) parent.Set(y, x, blue);
            else if (channels == 3) parent.Set(y, x, new Vec3b(blue, green, red));
            else parent.Set(y, x, new Vec4b(blue, green, red, (byte)((x + y) % 256)));
        }
        using var image = new Mat(parent, new Rect(3, 2, width, height));
        using var before = image.Clone();
        Assert.False(image.IsContinuous());

        var expected = PrepareInputWithLegacyElementAccess(image);
        var actual = PaddleLootOcrRecognizer.PrepareInput(image);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Pixels.Select(BitConverter.SingleToInt32Bits),
            actual.Pixels.Select(BitConverter.SingleToInt32Bits));
        Assert.Equal(0d, Cv2.Norm(before, image, NormTypes.INF));
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

    private static (float[] Pixels, int Width) PrepareInputWithLegacyElementAccess(Mat image)
    {
        var ratio = 48d * image.Width / image.Height;
        var width = Math.Clamp((int)ratio, 320, 3200);
        var resizedWidth = Math.Min(width, (int)Math.Ceiling(ratio));
        using var bgr = new Mat();
        if (image.Channels() == 1) Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
        else if (image.Channels() == 4) Cv2.CvtColor(image, bgr, ColorConversionCodes.BGRA2BGR);
        else image.CopyTo(bgr);
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new OpenCvSharp.Size(resizedWidth, 48), interpolation: InterpolationFlags.Linear);
        var pixels = new float[3 * 48 * width];
        for (var y = 0; y < 48; y++)
        for (var x = 0; x < resizedWidth; x++)
        {
            var pixel = resized.At<Vec3b>(y, x);
            for (var channel = 0; channel < 3; channel++)
                pixels[channel * 48 * width + y * width + x] = pixel[channel] / 127.5f - 1;
        }
        return (pixels, width);
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
