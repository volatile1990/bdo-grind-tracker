using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionQuantityRecognizerTests
{
    [Fact]
    public void Recognize_ComposesDigitsFromLeftToRightAfterBackwardGrouping()
    {
        using var one = CreateTemplate(1);
        using var two = CreateTemplate(2);
        using var three = CreateTemplate(3);
        using var region = CreateRegion();
        Stamp(region, one, 8, 7);
        Stamp(region, two, 26, 7);
        Stamp(region, three, 44, 7);

        using var recognizer = CreateRecognizer(
            (1, one, 0.999f),
            (2, two, 0.999f),
            (3, three, 0.999f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(8, result.X);
        Assert.Equal(123, result.Quantity);
        Assert.InRange(result.AverageScore, 0.999f, 1.001f);
    }

    [Fact]
    public void Recognize_KeepsOnlyTheRightmostConnectedGroup()
    {
        using var one = CreateTemplate(1);
        using var two = CreateTemplate(2);
        using var three = CreateTemplate(3);
        using var region = CreateRegion();
        Stamp(region, one, 4, 7);
        Stamp(region, two, 45, 7);
        Stamp(region, three, 63, 7);

        using var recognizer = CreateRecognizer(
            (1, one, 0.999f),
            (2, two, 0.999f),
            (3, three, 0.999f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(45, result.X);
        Assert.Equal(23, result.Quantity);
    }

    [Fact]
    public void Recognize_ConnectsAnAdjacentChainEvenWhenItsEndpointsAreFartherThanThirtyPixels()
    {
        using var one = CreateTemplate(1);
        using var two = CreateTemplate(2);
        using var three = CreateTemplate(3);
        using var region = CreateRegion(width: 120);
        Stamp(region, one, 5, 7);
        Stamp(region, two, 34, 7);
        Stamp(region, three, 63, 7);

        using var recognizer = CreateRecognizer(
            (1, one, 0.999f),
            (2, two, 0.999f),
            (3, three, 0.999f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(123, result.Quantity);
    }

    [Theory]
    [InlineData(30, 12)]
    [InlineData(31, 2)]
    public void Recognize_UsesInclusiveThirtyPixelGroupingBoundary(int xDistance, int expected)
    {
        using var one = CreateTemplate(1);
        using var two = CreateTemplate(2);
        using var region = CreateRegion();
        Stamp(region, one, 8, 7);
        Stamp(region, two, 8 + xDistance, 7);

        using var recognizer = CreateRecognizer(
            (1, one, 0.999f),
            (2, two, 0.999f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Quantity);
    }

    [Fact]
    public void Recognize_DeduplicatesCandidatesWhoseSquaredDistanceIsBelowOneHundredSixty()
    {
        using var four = CreateTemplate(4);
        using var two = CreateTemplate(2);
        using var region = CreateRegion();
        Stamp(region, four, 8, 7);
        Stamp(region, two, 20, 7);

        using var recognizer = CreateRecognizer(
            (4, four, 0.90f),
            (2, two, 0.50f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(20, result.X);
        Assert.Equal(2, result.Quantity);
    }

    [Fact]
    public void Recognize_DoesNotDeduplicateCandidatesAtSquaredDistanceOneHundredSixtyOrMore()
    {
        using var four = CreateTemplate(4);
        using var two = CreateTemplate(2);
        using var region = CreateRegion();
        Stamp(region, four, 8, 7);
        Stamp(region, two, 21, 7);

        using var recognizer = CreateRecognizer(
            (4, four, 0.90f),
            (2, two, 0.50f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(42, result.Quantity);
    }

    [Fact]
    public void Recognize_KeepsTheHigherScoreToThresholdRatioForOverlappingMatches()
    {
        using var sharedImage = CreateTemplate(9);
        using var region = CreateRegion();
        Stamp(region, sharedImage, 18, 7);

        using var recognizer = CreateRecognizer(
            (3, sharedImage, 0.95f),
            (7, sharedImage, 0.70f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(7, result.Quantity);
    }

    [Fact]
    public void Recognize_ReconcilesAgainstTheFirstNearbyCandidateInEncounterOrder()
    {
        using var one = CreateTemplate(1);
        using var two = CreateTemplate(2);
        using var three = CreateTemplate(3);
        using var region = CreateRegion();
        Stamp(region, one, 0, 7);
        Stamp(region, two, 20, 7);
        Stamp(region, three, 11, 7);

        using var recognizer = CreateRecognizer(
            (1, one, 0.95f),
            (2, two, 0.95f),
            (3, three, 0.50f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(32, result.Quantity);
    }

    [Fact]
    public void Recognize_DiscardsAnIsolatedZeroAndRetriesThePreviousGroup()
    {
        using var five = CreateTemplate(5);
        using var zero = CreateTemplate(0);
        using var region = CreateRegion();
        Stamp(region, five, 5, 7);
        Stamp(region, zero, 50, 7);

        using var recognizer = CreateRecognizer(
            (5, five, 0.999f),
            (0, zero, 0.999f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(5, result.Quantity);
        Assert.Equal(5, result.X);
    }

    [Fact]
    public void Recognize_ReturnsNullForAnIsolatedZero()
    {
        using var zero = CreateTemplate(0);
        using var region = CreateRegion();
        Stamp(region, zero, 18, 7);
        using var recognizer = CreateRecognizer((0, zero, 0.999f));

        var result = recognizer.Recognize(region);

        Assert.Null(result);
    }

    [Fact]
    public void Recognize_AcceptsCallerSuppliedEncodedTemplateBytes()
    {
        using var six = CreateTemplate(6);
        using var region = CreateRegion();
        Stamp(region, six, 18, 7);
        Cv2.ImEncode(".png", six, out var encoded);

        using var recognizer = new CompanionQuantityRecognizer(
            new[] { new CompanionEncodedQuantityTemplate(6, encoded, 0.999f) });

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(6, result.Quantity);
    }

    [Fact]
    public void Recognize_UsesNormalWorkerSpecialOneBeforeRegularComposition()
    {
        using var one = CreateTemplate(1);
        using var region = CreateRegion(width: 100, height: 64);
        Stamp(region, one, 20, 45);
        using var recognizer = CreateRecognizer((1, one, 0.9f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(0, result.X);
        Assert.Equal(1, result.Quantity);
        Assert.Equal(0.9f, result.AverageScore);
        Assert.True(result.IsSpecialOne);
    }

    [Theory]
    [InlineData(41)]
    [InlineData(49)]
    public void Recognize_SpecialOneIncludesBothYEndpoints(int y)
    {
        using var one = CreateTemplate(1);
        using var region = CreateRegion(width: 110, height: 66);
        Stamp(region, one, 74, y);
        using var recognizer = CreateRecognizer((1, one, 0.9f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(0, result.X);
        Assert.Equal(1, result.Quantity);
        Assert.Equal(0.9f, result.AverageScore);
        Assert.True(result.IsSpecialOne);
    }

    [Theory]
    [InlineData(74, 40)]
    [InlineData(74, 50)]
    [InlineData(75, 45)]
    public void Recognize_SpecialOneUsesExactExclusiveXAndInclusiveYBounds(int x, int y)
    {
        using var one = CreateTemplate(1);
        using var region = CreateRegion(width: 110, height: 66);
        Stamp(region, one, x, y);
        using var recognizer = CreateRecognizer((1, one, 0.999f));

        var result = recognizer.Recognize(region);

        Assert.NotNull(result);
        Assert.Equal(x, result.X);
        Assert.Equal(1, result.Quantity);
    }

    private static CompanionQuantityRecognizer CreateRecognizer(
        params (int Digit, Mat Image, float Threshold)[] templates)
    {
        return new CompanionQuantityRecognizer(
            templates.Select(template => new CompanionQuantityTemplate(
                template.Digit,
                template.Image,
                template.Threshold)));
    }

    private static Mat CreateRegion(int width = 100, int height = 28)
    {
        var region = new Mat(height, width, MatType.CV_8UC1);
        var random = new Random(0x51A7);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                region.Set(y, x, (byte)random.Next(0, 256));
            }
        }

        return region;
    }

    private static Mat CreateTemplate(int identity)
    {
        const int rows = 8;
        const int columns = 8;
        var template = new Mat(rows, columns, MatType.CV_8UC1);
        var random = new Random(0x2F31 + (identity * 7919));
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                template.Set(y, x, (byte)random.Next(0, 256));
            }
        }

        return template;
    }

    private static void Stamp(Mat destination, Mat template, int x, int y)
    {
        var templateColumns = template.Cols;
        var templateRows = template.Rows;
        using var target = new Mat(destination, new Rect(x, y, templateColumns, templateRows));
        template.CopyTo(target);
    }
}
