using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class ConfiguredExperienceReaderTests(ITestOutputHelper output)
{
    private int _ocrCalls;
    public static IEnumerable<object[]> DisplayConfigurations()
    {
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (2560, 1440),
            (3440, 1440), (3840, 2160), (5120, 1440), (7680, 4320) })
        foreach (var scale in new[] { .75, 1, 1.25, 1.49, 2 })
            yield return [width, height, scale];
    }

    [Theory]
    [MemberData(nameof(DisplayConfigurations))]
    public void ReadsIndependentResolutionAndUiScale(int width, int height, double scale)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable."); return; }
        using var original = Load("level-61-0.579-user-20260910.png");
        using var frame = Compose(original, width, height, scale);
        using var reader = Create(engine, new(width, height, scale));
        Assert.Equal(new ExperienceReading(61, .579m), reader.Read(frame, CancellationToken.None));
        Assert.InRange(_ocrCalls, 1, 3); // The calibrated crop itself must work, without the legacy fallback.
    }

    [Theory]
    [InlineData(1280, 720, 1)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(2560, 1440, 1.49)]
    [InlineData(3440, 1440, 2)]
    [InlineData(3840, 2160, 1.49)]
    [InlineData(5120, 1440, 1)]
    [InlineData(7680, 4320, 2.5)]
    [InlineData(3840, 2160, 3)]
    public void ReadsRealHudWithNeighbouringIcons(int width, int height, double scale)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable."); return; }
        using var original = Load("level-65-38.907-hud-user-20260911.png");
        using var frame = Compose(original, width, height, scale / 1.49);
        using var reader = Create(engine, new(width, height, scale));
        Assert.Equal(new ExperienceReading(65, 38.907m), reader.Read(frame, CancellationToken.None));
        Assert.InRange(_ocrCalls, 1, 3);
    }

    [Theory]
    [InlineData(.5, 55, 45)]
    [InlineData(.75, 83, 68)]
    [InlineData(1, 110, 90)]
    [InlineData(1.49, 164, 135)]
    [InlineData(2, 220, 180)]
    [InlineData(3, 330, 270)]
    public void CropUsesGameUiScaleInsteadOfResolution(double scale, int width, int height)
    {
        Assert.Equal(new Rectangle(0, 0, width, height), ExperienceFrameReader.CalibratedHudRegion(new(1280, 720), scale));
        Assert.Equal(new Rectangle(0, 0, width, height), ExperienceFrameReader.CalibratedHudRegion(new(7680, 4320), scale));
        Assert.Equal(new Rectangle(0, 0, 40, 30), ExperienceFrameReader.CalibratedHudRegion(new(40, 30), scale));
    }

    [Fact]
    public void ChangedSavedScaleIsReadForEveryObservation()
    {
        using var frame = new Bitmap(3840, 2160);
        var current = new ExperienceHudConfiguration(3840, 2160, 1);
        var configurationsRead = 0;
        var dimensions = new List<Size>();
        using var reader = new ExperienceFrameReader((image, _) =>
        {
            dimensions.Add(new Size(image.Width, image.Height));
            return Reading("38.907%");
        }, () => { configurationsRead++; return current; });
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        current = current with { UiScale = 2 };
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        Assert.Equal(2, configurationsRead);
        // Double the game HUD size produces the same normalized OCR dimensions.
        Assert.Equal(dimensions[0], dimensions[3]);
    }

    [Fact]
    public void ConfigurationFromAnotherFrameSizeIsNotUsed()
    {
        using var frame = new Bitmap(3840, 2160);
        var dimensions = new List<Size>();
        using var reader = new ExperienceFrameReader((image, _) =>
        {
            dimensions.Add(new Size(image.Width, image.Height));
            return Reading("38.907%");
        }, () => new(1920, 1080, 1));
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        Assert.Equal(new Size(672, 432), dimensions[0]);
    }

    [Fact]
    public void CalibratedConflictingReadingsCannotFallBackToAnotherCrop()
    {
        using var frame = new Bitmap(3840, 2160);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) => Reading(++calls == 2 ? "38.908%" : "38.907%"),
            () => new(3840, 2160, 1.49));
        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void HiddenHudProducesNoExperience()
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) return;
        using var frame = new Bitmap(1920, 1080);
        using var reader = Create(engine, new(1920, 1080, 1));
        Assert.Null(reader.Read(frame, CancellationToken.None));
    }

    private ExperienceFrameReader Create(CompanionWindowsOcrRecognizer engine, ExperienceHudConfiguration configuration)
        => new((image, token) =>
        {
            _ocrCalls++;
            var result = engine.Recognize(image, token);
            output.WriteLine($"{configuration}, {result.Text}");
            return result;
        }, () => configuration);

    private static Bitmap Load(string name) => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "experience", name));

    private static Bitmap Compose(Bitmap original, int width, int height, double scale)
    {
        var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(frame);
        graphics.PageUnit = GraphicsUnit.Pixel;
        graphics.Clear(Color.FromArgb(34, 42, 38));
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(original, new Rectangle(0, 0, (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale)),
            new Rectangle(0, 0, original.Width, original.Height), GraphicsUnit.Pixel);
        return frame;
    }

    private static CompanionOcrResult Reading(string percent)
    {
        CompanionOcrWord[] words = [new("65", new(CompanionOcrGeometryStatus.Success, 24, 10, 55, 45)),
            new(percent, new(CompanionOcrGeometryStatus.Success, 13, 68, 76, 18))];
        return new(string.Join(" ", words.Select(w => w.Text)), words[0].Geometry) { Words = words };
    }
}
