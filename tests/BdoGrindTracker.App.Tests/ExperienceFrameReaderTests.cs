using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class ExperienceFrameReaderTests(ITestOutputHelper output)
{
    [Fact]
    public void ReadsActualUserExperienceWithNativeWindowsOcr()
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable; independent parser tests remain active."); return; }
        using var frame = Load();
        using var reader = new ExperienceFrameReader((image, token) =>
        {
            var recognized = engine.Recognize(image, token);
            output.WriteLine($"{engine.LanguageTag}: {recognized.Text}");
            foreach (var word in recognized.Words) output.WriteLine($"{word.Text}: {word.Geometry}");
            return recognized;
        });
        Assert.Equal(new ExperienceReading(61, .579m), reader.Read(frame, CancellationToken.None));
    }

    public static IEnumerable<object[]> FrameScales()
    {
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1440), (3840, 2160) })
        foreach (var scale in new[] { .75, 1, 1.25, 1.5, 2 })
            yield return [width, height, scale];
    }

    [Theory]
    [MemberData(nameof(FrameScales))]
    public void ReadsScaledRelocatedHudWithinTopLeftFrame(int width, int height, double scale)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable."); return; }
        using var original = Load();
        using var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(34, 42, 38));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle(37, 19, (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale)));
        }
        using var reader = new ExperienceFrameReader((image, token) =>
        {
            var recognized = engine.Recognize(image, token);
            output.WriteLine($"{width}×{height}, scale {scale}: {recognized.Text}");
            foreach (var word in recognized.Words) output.WriteLine($"{word.Text}: {word.Geometry}");
            return recognized;
        });
        Assert.Equal(new ExperienceReading(61, .579m), reader.Read(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2.5f)]
    [InlineData(5f)]
    public void ToneMappedHdrPreservesExactPercent(float whiteLevel)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable."); return; }
        using var original = Load();
        using var frame = LootScrollGaugeDetectorTests.ToneMapHdr(original, whiteLevel);
        using var reader = new ExperienceFrameReader((image, token) =>
        {
            var recognized = engine.Recognize(image, token);
            output.WriteLine($"HDR {whiteLevel}: {recognized.Text}");
            return recognized;
        });
        Assert.Equal(new ExperienceReading(61, .579m), reader.Read(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("0.579%", "0.579")]
    [InlineData("0,579 %", "0.579")]
    [InlineData("99.999%", "99.999")]
    [InlineData("0.000%", "0.000")]
    [InlineData("14,625%", "14.625")]
    public void PreservesExactlyThreeDecimalDigits(string text, string expected)
        => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ExperienceFrameReader.ParsePercent(text));

    [Theory]
    [InlineData("0.57%")]
    [InlineData("0.5790%")]
    [InlineData("100.000%")]
    [InlineData("-0.579%")]
    [InlineData("0.579")]
    [InlineData("O.579%")]
    [InlineData("0.5?9%")]
    [InlineData("99%")]
    [InlineData("Attack speed 0.579%")]
    public void InvalidOrImprecisePercentagesAreUnknown(string text) => Assert.Null(ExperienceFrameReader.ParsePercent(text));

    [Fact]
    public void RequiresSpatiallyRelatedLargeLevelAboveSmallPercentage()
    {
        Assert.Equal(new ExperienceReading(61, .579m), ExperienceFrameReader.Parse(Result(
            Word("61", 24, 10, 55, 45), Word("0.579%", 13, 68, 76, 18))));
        Assert.Null(ExperienceFrameReader.Parse(Result(Word("61", 24, 10, 55, 45), Word("0.579%", 150, 68, 76, 18))));
        Assert.Null(ExperienceFrameReader.Parse(Result(Word("61", 24, 110, 55, 45), Word("0.579%", 13, 68, 76, 18))));
        Assert.Null(ExperienceFrameReader.Parse(Result(Word("61", 24, 10, 25, 18), Word("0.579%", 13, 35, 76, 18))));
        Assert.Null(ExperienceFrameReader.Parse(Result(Word("61", 24, 10, 55, 45), Word("0.579%", 13, 180, 76, 18))));
        Assert.Null(ExperienceFrameReader.Parse(new CompanionOcrResult("61\n0.579%", default)));
    }

    [Fact]
    public void ConflictingPreparationsDoNotInventExperienceProgress()
    {
        using var frame = new Bitmap(1920, 1080);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) => Result(Word("61", 24, 10, 55, 45),
            Word(++calls == 1 ? "0.579%" : "0.578%", 13, 68, 76, 18)));
        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void OcrUsesOnlyBoundedTopLeftCrop()
    {
        using var frame = new Bitmap(3840, 2160);
        var calls = 0;
        using var reader = new ExperienceFrameReader((image, _) =>
        {
            Assert.InRange(image.Width, 20, 2592);
            Assert.InRange(image.Height, 20, 1632);
            calls++;
            return Result(Word("61", 24, 10, 55, 45), Word("0.579%", 13, 68, 76, 18));
        });
        Assert.Equal(new ExperienceReading(61, .579m), reader.Read(frame, CancellationToken.None));
        Assert.Equal(3, calls);
        Assert.Equal(new Rectangle(0, 0, 640, 400), ExperienceFrameReader.HudRegion(frame.Size));
    }

    [Fact]
    public void MultipleLevelPercentagePairsAreAmbiguous()
    {
        Assert.Null(ExperienceFrameReader.Parse(Result(Word("61", 24, 10, 55, 45), Word("0.579%", 13, 68, 76, 18),
            Word("62", 204, 10, 55, 45), Word("50.125%", 193, 68, 76, 18))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousPreparationCannotBeOverriddenByASinglePair(bool ambiguousFirst)
    {
        using var frame = new Bitmap(1920, 1080);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) =>
        {
            var ambiguous = ++calls == (ambiguousFirst ? 1 : 2);
            return ambiguous
                ? Result(Word("61", 24, 10, 55, 45), Word("0.579%", 13, 68, 76, 18),
                    Word("62", 204, 10, 55, 45), Word("50.125%", 193, 68, 76, 18))
                : Result(Word("61", 24, 10, 55, 45), Word("0.579%", 13, 68, 76, 18));
        });
        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Equal(ambiguousFirst ? 1 : 2, calls);
    }

    [Theory]
    [InlineData("Lv.61", 61)]
    [InlineData("Lv 61", 61)]
    [InlineData("v61", 61)]
    [InlineData("100", 100)]
    public void RecognizesLevelLabelAndSharedLevelRange(string label, int level)
        => Assert.Equal(new ExperienceReading(level, .579m), ExperienceFrameReader.Parse(Result(
            Word(label, 24, 10, 55, 45), Word("0.579%", 13, 68, 76, 18))));

    private static Bitmap Load() => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "experience", "level-61-0.579-user-20260910.png"));
    private static CompanionOcrWord Word(string text, float x, float y, float width, float height)
        => new(text, new(CompanionOcrGeometryStatus.Success, x, y, width, height));
    private static CompanionOcrResult Result(params CompanionOcrWord[] words)
        => new(string.Join(" ", words.Select(word => word.Text)), words.FirstOrDefault()?.Geometry ?? default) { Words = words };
}
