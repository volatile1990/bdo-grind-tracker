using System.Drawing.Imaging;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class ExperienceHudContextTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void ReadsRealHudWithNeighbouringStatsAndIconsAtDifferentResolutions(int width, int height)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable; independent parser tests remain active."); return; }
        using var hud = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "experience",
            "level-65-38.907-hud-user-20260911.png"));
        using var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.Clear(Color.FromArgb(34, 42, 38));
            // Use explicit pixel bounds: DrawImageUnscaled still applies image DPI,
            // which otherwise shrinks this 144-DPI capture in a 96-DPI test host.
            var bounds = new Rectangle(0, 0, hud.Width, hud.Height);
            graphics.DrawImage(hud, bounds, bounds, GraphicsUnit.Pixel);
        }
        using var reader = new ExperienceFrameReader((image, token) =>
        {
            var result = engine.Recognize(image, token);
            output.WriteLine($"{width}×{height}, {engine.LanguageTag}: {result.Text}");
            return result;
        });

        Assert.Equal(new ExperienceReading(65, 38.907m), reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void UnreadableLargeHudCanBeReadFromTheCompactFallback()
    {
        using var frame = new Bitmap(3840, 2160);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) => ++calls <= 3 ? Result() : Reading("38.907%"));

        Assert.Equal(new ExperienceReading(65, 38.907m), reader.Read(frame, CancellationToken.None));
        Assert.Equal(6, calls);
    }

    [Fact]
    public void AValidPrimaryReadingDoesNotTriggerTheFallback()
    {
        using var frame = new Bitmap(3840, 2160);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) => ++calls == 1 ? Reading("38.907%") : Result());

        Assert.Equal(new ExperienceReading(65, 38.907m), reader.Read(frame, CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AmbiguousPrimaryPreparationCannotBeOverriddenByTheFallback(int ambiguousCall)
    {
        using var frame = new Bitmap(3840, 2160);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) => ++calls == ambiguousCall
            ? Result(Word("65", 24, 10, 55, 45), Word("38.907%", 13, 68, 76, 18),
                Word("62", 204, 10, 55, 45), Word("50.125%", 193, 68, 76, 18))
            : Reading("38.907%"));

        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Equal(ambiguousCall, calls);
    }

    [Fact]
    public void ConflictingPrimaryPreparationsCannotBeOverriddenByTheFallback()
    {
        using var frame = new Bitmap(3840, 2160);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) => Reading(++calls == 2 ? "38.908%" : "38.907%"));

        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    [InlineData(320, 200)]
    public void AlreadyCompactUnreadableHudIsNotAnalyzedTwice(int width, int height)
    {
        using var frame = new Bitmap(width, height);
        var calls = 0;
        using var reader = new ExperienceFrameReader((_, _) => { calls++; return Result(); });

        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Equal(3, calls);
    }

    private static CompanionOcrResult Reading(string percentage)
        => Result(Word("65", 24, 10, 55, 45), Word(percentage, 13, 68, 76, 18));

    private static CompanionOcrWord Word(string text, float x, float y, float width, float height)
        => new(text, new(CompanionOcrGeometryStatus.Success, x, y, width, height));

    private static CompanionOcrResult Result(params CompanionOcrWord[] words)
        => new(string.Join(" ", words.Select(word => word.Text)), words.FirstOrDefault()?.Geometry ?? default) { Words = words };
}
