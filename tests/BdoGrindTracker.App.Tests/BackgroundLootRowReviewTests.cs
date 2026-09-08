using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class BackgroundLootRowReviewTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Crystal = "BON Wandering Origin Crystal";
    private static LootObservation Row(int? quantity = null, string? text = null) =>
        new(LootSource.Normal, 2, text ?? Helmet, Helmet, quantity, 1, 0, null, null) { NativeY = 150 };
    private static LootRowReviewInput Input(LootObservation? row) => new(row, LootSource.Normal, 2, 150, -1, 0,
        _ => new(4, 1000), _ => true);
    private static Mat Band()
    {
        var image = new Mat(50, 385, MatType.CV_8UC3, Scalar.Black);
        Cv2.Line(image, new(5, 12), new(270, 30), Scalar.White, 2);
        return image;
    }
    private static SecondaryLootOcrResult Read(string text, float confidence = .99f) =>
        new(text, confidence, new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0));

    [Theory]
    [InlineData("Elion Follower's Helmet x 6", 6)]
    [InlineData("Elion Follower's Helmet", 6)]
    public async Task GoodPrimaryResultSkipsNativeFallback(string text, int amount)
    {
        var baseline = Row(amount, text);
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]), _ =>
            throw new Exception("Good Windows OCR must not enter Paddle"));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline) with { TemplateQuantity = 6, TemplateScore = .96f }, default);
        Assert.Same(baseline, result.Observation);
        Assert.Null(result.Diagnostics);
    }

    [Fact]
    public async Task FixedOneDoesNotReadQuantity()
    {
        var baseline = Row(1, Crystal + " x ???") with { ItemName = Crystal };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Crystal]), _ => throw new Exception());
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline) with { Bounds = _ => new(1, 1) }, default);
        Assert.Null(result.Diagnostics);
        Assert.Same(baseline, result.Observation);
    }

    [Theory]
    [InlineData("6", "6", 6)]
    [InlineData("6", "0", 6)]
    [InlineData("0", "6", 6)]
    [InlineData("1", "1", 1)]
    [InlineData("6", "8", null)]
    [InlineData("0", "0", null)]
    [InlineData("6", "IO", null)]
    [InlineData("6", "", null)]
    public async Task ConsensusRespectsPositiveConflictsAndDoesNotInventMissingSuffix(string first, string second, int? expected)
    {
        var engine = new Engine(new([Read(Helmet + " x " + first), Read(Helmet + " x " + second)]));
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]), _ => engine);
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(Row()), default);
        Assert.Equal(expected, result.Observation!.Quantity);
        Assert.Equal(2, result.Observation.Slot);
        Assert.Equal(150, result.Observation.NativeY);
        Assert.Equal(2, result.Diagnostics!.Readings.Count);
        Assert.Equal([3, 1], engine.Channels);
    }

    [Theory]
    [InlineData("de-DE", "Helm eines Anhängers Elions x 6")]
    [InlineData("en-US", "Elion Follower's Helmet x 6")]
    [InlineData("en-US", "Elion Follower's Helmet'x6")]
    [InlineData("en-US", "Elion Follower's Helmet-x6.")]
    public async Task LocalizedNamesResolveToTheSameCanonicalItem(string language, string text)
    {
        var engine = new Engine(new([Read(text), Read(text)]));
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]), _ => engine);
        review.ConfigureLanguage(language);
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(Row()), default);
        Assert.Equal(Helmet, result.Observation!.ItemName);
        Assert.Equal(6, result.Observation.Quantity);
        Assert.Equal(language, result.Diagnostics!.Language);
    }

    [Fact]
    public async Task LowScoreAndDifferentItemCannotReplaceAnAcceptedPrimaryName()
    {
        foreach (var read in new[] { Read(Helmet + " x 6", .94f), Read("Black Stone x 6") })
        {
            var baseline = Row();
            using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet, "Black Stone"]),
                _ => new Engine(new([read, read])));
            using var image = Band();
            Assert.Same(baseline, (await review.ReviewAsync(image, Input(baseline), default)).Observation);
        }
    }

    [Fact]
    public async Task SceneryOutsidePoolAndModelFailureLeavePrimaryObservationsIntact()
    {
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]), _ =>
            throw new FileNotFoundException("model missing"));
        using var image = Band();
        var scenery = Row() with { ItemName = null, RawText = "strange stones", RejectionReason = "unknown" };
        Assert.Null((await review.ReviewAsync(image, Input(scenery), default)).Diagnostics);
        var outside = Row();
        Assert.Null((await review.ReviewAsync(image, Input(outside) with { Allows = _ => false }, default)).Diagnostics);
        var result = await review.ReviewAsync(image, Input(outside), default);
        Assert.Same(outside, result.Observation);
        Assert.Equal(1, result.Diagnostics!.Errors);
    }

    [Fact]
    public async Task CancellationDoesNotReturnAnObservationAndPoolCanBeReused()
    {
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(Helmet + " x 6"), Read(Helmet + " x 6")])));
        using var image = Band();
        await Assert.ThrowsAsync<OperationCanceledException>(() => review.ReviewAsync(image, Input(Row()), new(true)));
        Assert.Equal(6, (await review.ReviewAsync(image, Input(Row()), default)).Observation!.Quantity);
    }

    private sealed class Engine(Queue<SecondaryLootOcrResult> reads) : ISecondaryLootOcrRecognizer
    {
        public List<int> Channels { get; } = [];
        public string BackendName => "paddle-test";
        public string LanguageTag => "en-US";
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        { Channels.Add(image.Channels()); return reads.Dequeue(); }
        public void Dispose() { }
    }
}
