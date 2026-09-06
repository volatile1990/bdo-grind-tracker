using BdoGrindTracker.Ocr;
using OpenCvSharp;
using System.Security.Cryptography;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionDigitTemplateLoaderTests
{
    [Fact]
    public void EmbeddedCatalogContainsCompanionsExactThreeCompleteDigitTables()
    {
        Assert.Equal(
            "8B75E114D3D33D227A01CDFE592F13AA65133A36363EAA2E6A82E5ADFC77EFCF",
            CompanionDigitCatalog.CompanionExecutableSha256);
        Assert.Equal(30, CompanionDigitCatalog.Provenance.Count);

        var sources = CompanionDigitCatalog.CreateSources();
        Assert.Equal(30, sources.Count);
        foreach (var font in Enum.GetValues<CompanionUiFontType>())
        {
            Assert.Equal(
                Enumerable.Range(0, 10),
                sources
                    .Where(source => source.FontType == font)
                    .Select(source => source.Digit));
        }

        foreach (var (provenance, source) in
                 CompanionDigitCatalog.Provenance.Zip(sources))
        {
            Assert.Equal(provenance.FontType, source.FontType);
            Assert.Equal(provenance.Digit, source.Digit);
            Assert.Equal(
                provenance.PngSha256,
                Convert.ToHexString(SHA256.HashData(source.ImageBytes.Span)));
            using var decoded = Cv2.ImDecode(
                source.ImageBytes.ToArray(),
                ImreadModes.Unchanged);
            Assert.False(decoded.Empty());
            Assert.Equal(provenance.Width, decoded.Width);
            Assert.Equal(provenance.Height, decoded.Height);
        }
    }

    [Theory]
    [InlineData(null, CompanionUiFontType.CabinDroid)]
    [InlineData(0, CompanionUiFontType.DejaVu)]
    [InlineData(2, CompanionUiFontType.StrongSword)]
    [InlineData(1, CompanionUiFontType.StrongSword)]
    [InlineData(9, CompanionUiFontType.StrongSword)]
    public void MapGameOptionUiFontType_UsesCompanionMapping(
        int? gameOptionValue,
        CompanionUiFontType expected)
    {
        Assert.Equal(expected, CompanionDigitTemplateLoader.MapGameOptionUiFontType(gameOptionValue));
    }

    [Fact]
    public void Load_SelectsOneFontAndReturnsPresentDigitsInAscendingOrder()
    {
        var sources = new[]
        {
            CreateSource(CompanionUiFontType.DejaVu, 8, rows: 8, columns: 5, seed: 8),
            CreateSource(CompanionUiFontType.StrongSword, 1, rows: 8, columns: 5, seed: 101),
            CreateSource(CompanionUiFontType.DejaVu, 2, rows: 8, columns: 5, seed: 2),
        };
        var loader = new CompanionDigitTemplateLoader();

        using var result = loader.Load(
            sources,
            CompanionUiFontType.DejaVu,
            targetHeight: 12f,
            threshold: 0.7f);

        Assert.Equal(new[] { 2, 8 }, result.Templates.Select(template => template.Digit));
        Assert.All(result.Templates, template => Assert.Equal(12, template.Image.Rows));
        Assert.All(result.Templates, template => Assert.Equal(8, template.Image.Cols));
        Assert.All(result.Templates, template => Assert.Equal(MatType.CV_8UC1, template.Image.Type()));
        Assert.All(result.Templates, template => Assert.Equal(0.7f, template.Threshold));
    }

    [Fact]
    public void Load_DecodesGrayscaleThenBlursBeforeCubicUpscaling()
    {
        using var colorSource = CreateColorImage(rows: 6, columns: 4);
        Cv2.ImEncode(".png", colorSource, out var encoded);
        var source = new CompanionEncodedDigitTemplateSource(
            CompanionUiFontType.StrongSword,
            Digit: 3,
            encoded);
        var loader = new CompanionDigitTemplateLoader();

        using var result = loader.Load(
            new[] { source },
            CompanionUiFontType.StrongSword,
            targetHeight: 9f,
            threshold: 0.8f);

        using var expectedGray = Cv2.ImDecode(encoded, ImreadModes.Grayscale);
        using var expectedBlur = new Mat();
        Cv2.GaussianBlur(
            expectedGray,
            expectedBlur,
            new OpenCvSharp.Size(3, 3),
            0,
            0,
            BorderTypes.Default);
        using var expected = new Mat();
        Cv2.Resize(
            expectedBlur,
            expected,
            new OpenCvSharp.Size(6, 9),
            0,
            0,
            InterpolationFlags.Cubic);

        AssertImagesEqual(expected, Assert.Single(result.Templates).Image);
    }

    [Fact]
    public void Load_UsesAreaWhenTargetHeightDoesNotUpscale()
    {
        var source = CreateSource(
            CompanionUiFontType.CabinDroid,
            digit: 4,
            rows: 10,
            columns: 7,
            seed: 14);
        var loader = new CompanionDigitTemplateLoader();

        using var result = loader.Load(
            new[] { source },
            CompanionUiFontType.CabinDroid,
            targetHeight: 5f,
            threshold: 0.8f);

        using var expectedGray = Cv2.ImDecode(source.ImageBytes.Span, ImreadModes.Grayscale);
        using var expectedBlur = new Mat();
        Cv2.GaussianBlur(
            expectedGray,
            expectedBlur,
            new OpenCvSharp.Size(3, 3),
            0,
            0,
            BorderTypes.Default);
        using var expected = new Mat();
        Cv2.Resize(
            expectedBlur,
            expected,
            new OpenCvSharp.Size(4, 5),
            0,
            0,
            InterpolationFlags.Area);

        AssertImagesEqual(expected, Assert.Single(result.Templates).Image);
    }

    [Fact]
    public void Load_UsesRoundfAwayFromZeroForTargetDimensions()
    {
        var source = CreateSource(
            CompanionUiFontType.StrongSword,
            digit: 5,
            rows: 4,
            columns: 2,
            seed: 5);
        var loader = new CompanionDigitTemplateLoader();

        using var result = loader.Load(
            new[] { source },
            CompanionUiFontType.StrongSword,
            targetHeight: 5f,
            threshold: 0.8f);

        var image = Assert.Single(result.Templates).Image;
        Assert.Equal(5, image.Rows);
        Assert.Equal(3, image.Cols);
    }

    [Theory]
    [MemberData(nameof(NormalThresholdCases))]
    public void GetNormalQuantityThreshold_ReproducesWorkerTable(
        CompanionUiFontType font,
        float uiScale,
        float[] expected)
    {
        var actual = Enumerable.Range(0, 10)
            .Select(digit => CompanionDigitTemplateLoader.GetNormalQuantityThreshold(font, uiScale, digit))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LoadNormalQuantity_UsesFixedTwentyFourPixelHeightAndWorkerThresholds()
    {
        var sources = Enumerable.Range(0, 10)
            .Select(digit => CreateSource(
                CompanionUiFontType.StrongSword,
                digit,
                rows: 8,
                columns: 4,
                seed: digit))
            .ToArray();
        var loader = new CompanionDigitTemplateLoader();

        using var result = loader.LoadNormalQuantity(
            sources,
            CompanionUiFontType.StrongSword,
            uiScale: 0.9f);

        Assert.All(result.Templates, template => Assert.Equal(24, template.Image.Rows));
        Assert.Equal(0.75f, result.Templates.Single(template => template.Digit == 1).Threshold);
        Assert.Equal(0.64f, result.Templates.Single(template => template.Digit == 8).Threshold);
    }

    [Theory]
    [InlineData(CompanionUiFontType.StrongSword, false, 22, 0.72f, 13, 0.76f)]
    [InlineData(CompanionUiFontType.DejaVu, false, 23, 0.72f, 13, 0.68f)]
    [InlineData(CompanionUiFontType.CabinDroid, false, 23, 0.72f, 13, 0.68f)]
    [InlineData(CompanionUiFontType.StrongSword, true, 27, 0.8f, 10, 0.7f)]
    public void LoadConfiguredPair_ReproducesBothCallSiteRecipes(
        CompanionUiFontType font,
        bool customHp,
        int firstHeight,
        float firstThreshold,
        int secondHeight,
        float secondThreshold)
    {
        var sources = new[]
        {
            CreateSource(font, 1, rows: 8, columns: 4, seed: 1),
            CreateSource(font, 4, rows: 8, columns: 4, seed: 4),
            CreateSource(font, 8, rows: 8, columns: 4, seed: 8),
        };
        var loader = new CompanionDigitTemplateLoader();

        using var result = loader.LoadConfiguredPair(sources, font, customHp, uiScale: 1f);

        Assert.All(result.First.Templates, template => Assert.Equal(firstHeight, template.Image.Rows));
        Assert.All(result.Second.Templates, template => Assert.Equal(secondHeight, template.Image.Rows));
        Assert.Equal(firstThreshold + 0.02f, Threshold(result.First, 1));
        Assert.Equal(firstThreshold, Threshold(result.First, 4));
        Assert.Equal(firstThreshold - 0.05f, Threshold(result.First, 8));
        Assert.Equal(secondThreshold + 0.02f, Threshold(result.Second, 1));
        Assert.Equal(secondThreshold, Threshold(result.Second, 4));
        Assert.Equal(secondThreshold - 0.05f, Threshold(result.Second, 8));
    }

    public static TheoryData<CompanionUiFontType, float, float[]> NormalThresholdCases => new()
    {
        {
            CompanionUiFontType.StrongSword,
            0.9f,
            new[] { 0.62f, 0.75f, 0.75f, 0.7f, 0.74f, 0.74f, 0.7f, 0.787f, 0.64f, 0.76f }
        },
        {
            CompanionUiFontType.StrongSword,
            1f,
            new[] { 0.62f, 0.77f, 0.75f, 0.74f, 0.74f, 0.74f, 0.7f, 0.787f, 0.72f, 0.76f }
        },
        {
            CompanionUiFontType.StrongSword,
            1.1f,
            new[] { 0.62f, 0.77f, 0.75f, 0.7f, 0.74f, 0.74f, 0.7f, 0.787f, 0.64f, 0.76f }
        },
        {
            CompanionUiFontType.DejaVu,
            0.9f,
            new[] { 0.61f, 0.77f, 0.75f, 0.74f, 0.6f, 0.74f, 0.69f, 0.787f, 0.7f, 0.745f }
        },
        {
            CompanionUiFontType.DejaVu,
            1.1f,
            new[] { 0.61f, 0.77f, 0.75f, 0.74f, 0.6f, 0.74f, 0.69f, 0.787f, 0.75f, 0.745f }
        },
        {
            CompanionUiFontType.CabinDroid,
            0.9f,
            new[] { 0.61f, 0.77f, 0.738f, 0.705f, 0.63f, 0.74f, 0.69f, 0.787f, 0.72f, 0.745f }
        },
    };

    private static float Threshold(CompanionDigitTemplateSet set, int digit)
    {
        return set.Templates.Single(template => template.Digit == digit).Threshold;
    }

    private static CompanionEncodedDigitTemplateSource CreateSource(
        CompanionUiFontType font,
        int digit,
        int rows,
        int columns,
        int seed)
    {
        using var image = new Mat(rows, columns, MatType.CV_8UC1);
        var random = new Random(0x713 + seed);
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                image.Set(y, x, (byte)random.Next(0, 256));
            }
        }

        Cv2.ImEncode(".png", image, out var encoded);
        return new CompanionEncodedDigitTemplateSource(font, digit, encoded);
    }

    private static Mat CreateColorImage(int rows, int columns)
    {
        var image = new Mat(rows, columns, MatType.CV_8UC3);
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                image.Set(y, x, new Vec3b(
                    (byte)(10 + (x * 20)),
                    (byte)(30 + (y * 15)),
                    (byte)(200 - (x * 10) - (y * 5))));
            }
        }

        return image;
    }

    private static void AssertImagesEqual(Mat expected, Mat actual)
    {
        Assert.Equal(expected.Type(), actual.Type());
        Assert.Equal(expected.Rows, actual.Rows);
        Assert.Equal(expected.Cols, actual.Cols);
        using var difference = new Mat();
        Cv2.Absdiff(expected, actual, difference);
        Assert.Equal(0, Cv2.CountNonZero(difference));
    }
}
