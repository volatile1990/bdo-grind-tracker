using System.Drawing.Imaging;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class LootCapturePreviewBuilderTests
{
    [Fact]
    public void CropsContainExactlyTheSelectedPixelsAndTheFrameRemainsCallerOwned()
    {
        using var frame = Pattern(24, 18);
        var normal = new Rectangle(2, 3, 7, 8);
        var rare = new Rectangle(13, 9, 6, 4);
        var configuration = Configuration(frame.Width, frame.Height, normal, rare);
        var before = DateTimeOffset.UtcNow;

        var preview = LootCapturePreviewBuilder.Create(frame, configuration);

        Assert.Null(preview.Error);
        Assert.Same(configuration, preview.Configuration);
        Assert.InRange(preview.CapturedAt!.Value, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, preview.CapturedAt.Value.Offset);
        Assert.StartsWith("data:image/jpeg;base64,", preview.ImageDataUrl);
        Assert.StartsWith("data:image/png;base64,", preview.NormalImageDataUrl);
        Assert.StartsWith("data:image/png;base64,", preview.RareImageDataUrl);
        using var snapshot = Decode(preview.ImageDataUrl!);
        using var normalImage = Decode(preview.NormalImageDataUrl!);
        using var rareImage = Decode(preview.RareImageDataUrl!);
        Assert.Equal(frame.Size, snapshot.Size);
        AssertCrop(frame, normal, normalImage);
        AssertCrop(frame, rare, rareImage);
        frame.SetPixel(0, 0, Color.Magenta);
        Assert.Equal(Color.Magenta.ToArgb(), frame.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void FullPreviewResizesProportionallyWhileCropsKeepTheirOriginalDimensions()
    {
        using var frame = new Bitmap(2400, 1200, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.CornflowerBlue);
        var normal = new Rectangle(1933, 533, 201, 151);
        var rare = new Rectangle(400, 211, 347, 55);

        var preview = LootCapturePreviewBuilder.Create(frame, Configuration(2400, 1200, normal, rare));

        Assert.Null(preview.Error);
        using var snapshot = Decode(preview.ImageDataUrl!);
        using var normalImage = Decode(preview.NormalImageDataUrl!);
        using var rareImage = Decode(preview.RareImageDataUrl!);
        Assert.Equal(new Size(1600, 800), snapshot.Size);
        Assert.Equal(normal.Size, normalImage.Size);
        Assert.Equal(rare.Size, rareImage.Size);
        Assert.Equal(Color.CornflowerBlue.ToArgb(), normalImage.GetPixel(200, 150).ToArgb());
        Assert.Equal(Color.CornflowerBlue.ToArgb(), rareImage.GetPixel(346, 54).ToArgb());
    }

    [Theory]
    [InlineData(25, 18)]
    [InlineData(24, 19)]
    [InlineData(48, 36)]
    public void ResolutionMismatchReturnsAnExplanationWithoutMisleadingImages(int expectedWidth, int expectedHeight)
    {
        using var frame = Pattern(24, 18);
        var configuration = Configuration(expectedWidth, expectedHeight, new(2, 3, 7, 8), new(13, 9, 6, 4));

        var preview = LootCapturePreviewBuilder.Create(frame, configuration);

        Assert.Contains("24 × 18", preview.Error);
        Assert.Contains($"{expectedWidth} × {expectedHeight}", preview.Error);
        AssertNoImages(preview);
        Assert.Equal(24, frame.Width);
        frame.SetPixel(0, 0, Color.White);
    }

    [Fact]
    public void MissingSpecialBoundsKeepsTheNormalPreviewAndTheConfigurationStatus()
    {
        using var frame = Pattern(24, 18);
        var configuration = Configuration(24, 18, new(2, 3, 7, 8), null) with
        {
            RareStatus = "Special-Droplog ist in dieser Konfiguration ausgeblendet."
        };

        var preview = LootCapturePreviewBuilder.Create(frame, configuration);

        Assert.Null(preview.Error);
        Assert.NotNull(preview.ImageDataUrl);
        Assert.NotNull(preview.NormalImageDataUrl);
        Assert.Null(preview.RareImageDataUrl);
        Assert.Equal(configuration.RareStatus, preview.Configuration!.RareStatus);
        Assert.NotNull(preview.CapturedAt);
    }

    [Theory]
    [InlineData(false, -1, 0, 5, 5)]
    [InlineData(false, 0, -1, 5, 5)]
    [InlineData(false, 0, 0, 0, 5)]
    [InlineData(false, 0, 0, 5, 0)]
    [InlineData(false, 22, 0, 5, 5)]
    [InlineData(false, 0, 16, 5, 5)]
    [InlineData(true, -1, 0, 5, 5)]
    [InlineData(true, 0, 0, 0, 5)]
    [InlineData(true, int.MaxValue, 0, int.MaxValue, 5)]
    public void InvalidBoundsNeverProduceAClippedOrShiftedCrop(bool rare, int x, int y, int width, int height)
    {
        using var frame = Pattern(24, 18);
        var configuration = Configuration(24, 18, new(2, 3, 7, 8), new(13, 9, 6, 4));
        var invalid = new Rectangle(x, y, width, height);
        configuration = rare ? configuration with { RareBounds = invalid } : configuration with { NormalBounds = invalid };

        var preview = LootCapturePreviewBuilder.Create(frame, configuration);

        Assert.Contains(rare ? "Special-Droplog" : "normalen Droplogs", preview.Error);
        AssertNoImages(preview);
    }

    [Fact]
    public void MissingNormalBoundsAndInvalidConfigurationRemainVisibleErrors()
    {
        using var frame = Pattern(24, 18);
        var configuration = Configuration(24, 18, null, new(13, 9, 6, 4));
        var missing = LootCapturePreviewBuilder.Create(frame, configuration);
        var invalid = LootCapturePreviewBuilder.Create(frame, configuration with { Error = "Die Konfiguration ist nicht lesbar." });

        Assert.Contains("normalen Droplogs", missing.Error);
        Assert.Equal("Die Konfiguration ist nicht lesbar.", invalid.Error);
        AssertNoImages(missing);
        AssertNoImages(invalid);
    }

    private static CaptureConfigurationOption Configuration(int width, int height, Rectangle? normal, Rectangle? rare) =>
        new("GameVariable.xml", "Testprofil", null, width, height, 1, normal, rare, "Special-Droplog verfügbar.");

    private static Bitmap Pattern(int width, int height)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                image.SetPixel(x, y, Color.FromArgb(255, x * 7, y * 11, (x + y) * 5));
        return image;
    }

    private static Bitmap Decode(string dataUrl)
    {
        using var bytes = new MemoryStream(Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]));
        using var image = new Bitmap(bytes);
        return image.Clone(new Rectangle(Point.Empty, image.Size), PixelFormat.Format32bppArgb);
    }

    private static void AssertCrop(Bitmap frame, Rectangle bounds, Bitmap crop)
    {
        Assert.Equal(bounds.Size, crop.Size);
        for (var y = 0; y < bounds.Height; y++)
            for (var x = 0; x < bounds.Width; x++)
                Assert.Equal(frame.GetPixel(bounds.X + x, bounds.Y + y).ToArgb(), crop.GetPixel(x, y).ToArgb());
    }

    private static void AssertNoImages(CaptureConfigurationPreview preview)
    {
        Assert.Null(preview.ImageDataUrl);
        Assert.Null(preview.NormalImageDataUrl);
        Assert.Null(preview.RareImageDataUrl);
        Assert.Null(preview.CapturedAt);
    }
}
