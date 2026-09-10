using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class TrashQuantityAnomalyReviewTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Stone = "Black Stone";

    [Theory]
    [InlineData(43, .739130437374115)]
    [InlineData(45, 1)]
    public async Task TwoIndependentFourReadsCorrectTheRecordedDigitInsertionEvenWithACompleteWindowsSuffix(
        int originalQuantity, double nameConfidence)
    {
        var baseline = Row(originalQuantity) with { NameConfidence = nameConfidence };
        var engine = new Engine(Read(Helmet, "4", .9808287f), Read(Helmet, "4", .9963562f));
        using var review = Reviewer(engine);
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);

        Assert.Equal("trash-quantity-anomaly", result.Diagnostics!.Reason);
        Assert.Equal("anomaly-quantity-corrected", result.Diagnostics.Outcome);
        Assert.Equal(4, result.Observation!.Quantity);
        Assert.Equal(Helmet, result.Observation.ItemName);
        Assert.Equal(2, result.Observation.Slot);
        Assert.Equal(224, result.Observation.NativeY);
        Assert.Equal(LootSource.Normal, result.Observation.Source);
        Assert.Equal(originalQuantity, baseline.Quantity);
        Assert.Equal(2, engine.Calls);
    }

    [Theory]
    // All 15 large drop origins visually verified in the user's recording.
    [InlineData(1344, 48)]
    [InlineData(1486, 54)]
    [InlineData(1585, 56)]
    [InlineData(1626, 60)]
    [InlineData(1634, 64)]
    [InlineData(1641, 60)]
    [InlineData(1665, 104)]
    [InlineData(1683, 84)]
    [InlineData(1698, 84)]
    [InlineData(2687, 54)]
    [InlineData(2819, 50)]
    [InlineData(4062, 50)]
    [InlineData(4179, 56)]
    [InlineData(4323, 48)]
    [InlineData(4484, 338)]
    public async Task ConfirmedRealLargeDropsArePreservedRatherThanCapped(int sourceSequence, int quantity)
    {
        var baseline = Row(quantity);
        var text = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var review = Reviewer(new Engine(Read(Helmet, text), Read(Helmet, text)));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);

        Assert.True(result.Observation!.Quantity == quantity, $"Changed verified source drop in frame {sourceSequence}.");
        Assert.Same(baseline, result.Observation);
        Assert.Equal("baseline-confirmed", result.Diagnostics!.Outcome);
        Assert.Equal(1000u, result.Observation.QuantityBounds!.Maximum);
    }

    [Theory]
    [InlineData("4", "0")]
    [InlineData("0", "4")]
    [InlineData("0", "0")]
    [InlineData("4", null)]
    [InlineData(null, "4")]
    [InlineData("4", "IO")]
    [InlineData("4", "45")]
    [InlineData("4", "6")]
    [InlineData("1", "1")]
    [InlineData("1001", "1001")]
    public async Task ZeroMissingConflictingOrIllegalAmountsDoNotReplaceTheWindowsObservation(string? first, string? second)
    {
        var baseline = Row(43);
        using var review = Reviewer(new Engine(Read(Helmet, first), Read(Helmet, second)));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);

        Assert.Same(baseline, result.Observation);
        Assert.Equal(43, result.Observation!.Quantity);
        Assert.NotEqual("anomaly-quantity-corrected", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(.94f, .99f)]
    [InlineData(.99f, .94f)]
    public async Task BothQuantityReadingsMustHaveSufficientConfidence(float firstConfidence, float secondConfidence)
    {
        var baseline = Row(43);
        using var review = Reviewer(new Engine(Read(Helmet, "4", firstConfidence), Read(Helmet, "4", secondConfidence)));
        using var image = Band();
        Assert.Same(baseline, (await review.ReviewAsync(image, Input(baseline), default)).Observation);
    }

    [Theory]
    [InlineData(.739130437374115)]
    [InlineData(1)]
    public async Task AQuantityReviewCannotReplaceTheItemEvenWhenItsPrimaryNameWasUncertain(double nameConfidence)
    {
        var baseline = Row(43) with { NameConfidence = nameConfidence };
        using var review = Reviewer(new Engine(Read(Stone, "4"), Read(Stone, "4")));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);

        Assert.Same(baseline, result.Observation);
        Assert.Equal(Helmet, result.Observation!.ItemName);
    }

    [Theory]
    [InlineData("history", 4, 19, true)]
    [InlineData("history", 4, 20, false)]
    [InlineData("catalog", 2, 15, true)]
    [InlineData("catalog", 2, 16, false)]
    [InlineData("history", 4, 338, false)]
    public async Task AReplacementMustBePlausibleForTheAnomalyContextWithoutCappingTheOriginalDrop(
        string basis, int typicalQuantity, int proposed, bool accepted)
    {
        var baseline = Row(43);
        var text = proposed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var review = Reviewer(new Engine(Read(Helmet, text), Read(Helmet, text)));
        using var image = Band();
        var result = await review.ReviewAsync(image,
            Input(baseline) with { QuantityAnomaly = new(43, typicalQuantity, basis, 10) }, default);

        Assert.Equal(accepted ? proposed : 43, result.Observation!.Quantity);
        if (!accepted) Assert.Same(baseline, result.Observation);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task OrdinaryExplicitTwoAndFourReadsStillSkipOptionalQuantityOcr(int quantity)
    {
        var baseline = Row(quantity);
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => throw new InvalidOperationException("Ordinary quantities must not start optional OCR."));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline) with { QuantityAnomaly = null }, default);

        Assert.Same(baseline, result.Observation);
        Assert.Null(result.Diagnostics);
    }

    [Fact]
    public async Task NameReviewWithoutAnAnomalyRetainsTheEstablishedPrimaryQuantityProtection()
    {
        var baseline = Row(43) with { NameConfidence = .739130437374115 };
        using var review = Reviewer(new Engine(Read(Helmet, "4"), Read(Helmet, "4")));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline) with { QuantityAnomaly = null }, default);

        Assert.Same(baseline, result.Observation);
        Assert.Equal("uncertain-name", result.Diagnostics!.Reason);
    }

    [Fact]
    public async Task FailureOfTheOptionalModelDoesNotDeleteOrReduceLoot()
    {
        var baseline = Row(338);
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => throw new FileNotFoundException("Model unavailable."));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);

        Assert.Same(baseline, result.Observation);
        Assert.Equal(1, result.Diagnostics!.Errors);
    }

    [Fact]
    public async Task ACorrectionMustRespectTheExistingCountOneTrashFilter()
    {
        const string item = "Decayed Cloth";
        var baseline = Row(11) with { ItemName = item, RawText = item + " x 11", QuantityBounds = new(1, 100) };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([item]),
            _ => new Engine(Read(item, "1"), Read(item, "1")));
        using var image = Band();
        var input = new LootRowReviewInput(baseline, LootSource.Normal, 2, 224, -1, 0,
            _ => new(1, 100), _ => true)
        { UiScale = 1.49, QuantityAnomaly = new(11, 1, "history", 10) };
        var result = await review.ReviewAsync(image, input, default);
        Assert.Same(baseline, result.Observation);
        Assert.Equal("anomaly-quantity-filtered", result.Diagnostics!.Outcome);
    }

    private static LootObservation Row(int quantity) =>
        new(LootSource.Normal, 2, $"{Helmet} x {quantity}", Helmet, quantity, 1, 0, null, null)
        { NativeY = 224, QuantityBounds = new(2, 1000) };

    private static LootRowReviewInput Input(LootObservation baseline) =>
        new(baseline, LootSource.Normal, 2, 224, -1, 0, _ => new(2, 1000), _ => true)
        { UiScale = 1.49, QuantityAnomaly = new(baseline.Quantity!.Value, 4, "history", 10) };

    private static BackgroundLootRowReview Reviewer(Engine engine) =>
        new(new CompanionItemMatcher([Helmet, Stone]), _ => engine);

    private static SecondaryLootOcrResult Read(string item, string? amount, float confidence = .99f) =>
        new(amount is null ? item : $"{item} x {amount}", confidence,
            new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0));

    private static Mat Band()
    {
        var image = new Mat(75, 451, MatType.CV_8UC3, Scalar.Black);
        Cv2.Line(image, new(5, 20), new(300, 40), Scalar.White, 2);
        return image;
    }

    private sealed class Engine(params SecondaryLootOcrResult[] readings) : ISecondaryLootOcrRecognizer
    {
        private readonly Queue<SecondaryLootOcrResult> _readings = new(readings);
        public int Calls { get; private set; }
        public string BackendName => "paddle-anomaly-test";
        public string LanguageTag => "en-US";
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _readings.Dequeue();
        }
        public void Dispose() { }
    }
}
