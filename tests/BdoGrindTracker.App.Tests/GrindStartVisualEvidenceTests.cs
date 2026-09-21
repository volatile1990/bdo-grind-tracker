using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindStartVisualEvidenceTests
{
    private const int Width = 360;
    private const int Height = 24;

    [Fact]
    public void BlankAndSmoothChangingBackgroundsAreNotPlausibleLoot()
    {
        foreach (var phase in new[] { 0, 4, 30, 60 })
        {
            var pixels = Background(phase);
            Assert.False(Evidence(pixels).IsPlausible);
        }
        Assert.False(Evidence(new byte[Width * Height]).IsPlausible);
    }

    [Theory]
    [InlineData(.03)]
    [InlineData(.1)]
    [InlineData(.5)]
    [InlineData(1)]
    public void SceneNoiseAndSpecklesDoNotLookLikeAnAlignedTextLine(double density)
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var random = new Random(seed);
            var pixels = Background();
            for (var index = 0; index < pixels.Length; index++)
                if (random.NextDouble() < density) pixels[index] = (byte)random.Next(256);
            Assert.False(Evidence(pixels).IsPlausible);
        }
    }

    [Fact]
    public void NewTextStructureWakesButRepeatingItDoesNot()
    {
        var blank = Evidence(Background());
        var text = Evidence(Text());
        Assert.True(text.IsPlausible);
        Assert.True(text.IsNewComparedTo(blank));
        Assert.False(text.IsNewComparedTo(text));
        Assert.False(blank.IsNewComparedTo(text));
    }

    [Fact]
    public void MateriallyDifferentGlyphsWake()
    {
        var before = Evidence(Text());
        var after = Evidence(Text(alternate: true));
        Assert.True(after.IsPlausible);
        Assert.True(after.IsNewComparedTo(before));
    }

    [Fact]
    public void ChangedQuantityGlyphCanWakeWithTheSameLongItemName()
    {
        var beforePixels = Text();
        var afterPixels = Text();
        Fill(beforePixels, 260, 5, 2, 14, 220);
        Fill(beforePixels, 260, 5, 9, 2, 220);
        Fill(beforePixels, 260, 17, 8, 2, 220);
        Fill(afterPixels, 266, 5, 2, 14, 220);
        Fill(afterPixels, 260, 11, 8, 2, 220);
        Assert.True(Evidence(afterPixels).IsNewComparedTo(Evidence(beforePixels)));
    }

    [Fact]
    public void BackgroundMovementAndOnePixelRowJitterDoNotWakeUnchangedGlyphs()
    {
        var before = Evidence(Text());
        var after = Evidence(Text(phase: 15, verticalOffset: 1));
        Assert.True(after.IsPlausible);
        Assert.False(after.IsNewComparedTo(before));
    }

    [Fact]
    public void FadingOutDoesNotWakeButFreshCopyAfterFadingDoes()
    {
        var fresh = Evidence(Text());
        var faded = Evidence(Text(opacity: .4));
        Assert.True(faded.IsPlausible);
        Assert.False(faded.IsNewComparedTo(fresh));
        Assert.True(fresh.IsNewComparedTo(faded));
    }

    [Fact]
    public void TinyMarksOrBroadSceneEdgesDoNotWake()
    {
        var pixels = Background();
        Fill(pixels, 20, 8, 2, 2, 220);
        Fill(pixels, 28, 8, 2, 2, 220);
        Assert.False(Evidence(pixels).IsPlausible);
        pixels = Background();
        Fill(pixels, 10, 5, 160, 14, 220);
        Assert.False(Evidence(pixels).IsPlausible);
    }

    [Fact]
    public void ImageGeometryChangeRequiresANewBaseline()
    {
        var current = Evidence(Text());
        var differentGeometry = GrindStartVisualEvidence.Create(new byte[100 * Height], 100, Height);
        Assert.False(current.IsNewComparedTo(differentGeometry));
    }

    private static GrindStartVisualEvidence Evidence(byte[] pixels) => GrindStartVisualEvidence.Create(pixels, Width, Height);

    private static byte[] Background(int phase = 0)
    {
        var pixels = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            pixels[y * Width + x] = (byte)(25 + 10 * Math.Sin((x + phase) / 25d) + y / 3d);
        return pixels;
    }

    // Connected strokes in a common 14-pixel name band, with deterministic
    // variations. No fonts, operating-system drawing or OCR are needed.
    private static byte[] Text(int phase = 0, int verticalOffset = 0, bool alternate = false, double opacity = 1)
    {
        var pixels = Background(phase);
        var value = (byte)(30 + 190 * opacity);
        for (var glyph = 0; glyph < 10; glyph++)
        {
            var x = 5 + glyph * 15;
            var y = 5 + verticalOffset;
            if (alternate) x += 5;
            Fill(pixels, x, y, 2, 14, value);
            Fill(pixels, x, y, 9, 2, value);
            Fill(pixels, x, y + (glyph % 2 == 0 ? 6 : 12), 8, 2, value);
        }
        return pixels;
    }

    private static void Fill(byte[] pixels, int left, int top, int width, int height, byte value)
    {
        for (var y = top; y < top + height; y++)
        for (var x = left; x < left + width; x++) pixels[y * Width + x] = value;
    }
}
