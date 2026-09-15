using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class QuantityCharacterConfidenceReviewTests
{
    private const string Item = "Scorched Belt Ornament";

    [Theory]
    [InlineData(10, 101, .6494229f, .39569637f)]
    [InlineData(10, 101, .6617799f, .39821726f)]
    [InlineData(12, 121, .48806792f, .55301154f)]
    public async Task HighRowAverageDoesNotAllowAnUncertainExtraDigitToReplaceTheTemplate(
        int baselineAmount, int proposed, float first, float second)
    {
        var baseline = Row(baselineAmount);
        var engine = new Engine(Read(proposed, first), Read(proposed, second));
        using var review = Reviewer(engine);
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);

        Assert.Same(baseline, result.Observation);
        Assert.Equal(2, engine.Calls);
        Assert.Equal(new double?[] { first, second }, result.Diagnostics!.Readings.Select(read => read.QuantityConfidence));
        Assert.All(result.Diagnostics.Readings, reading => Assert.Null(reading.Quantity));
        Assert.All(result.Diagnostics.Readings, reading => Assert.Contains($"x {proposed}", reading.Text));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(104)]
    [InlineData(338)]
    public async Task GenuineLargeAmountsWithReliableDigitsStillReplaceAnUntrustedTemplate(int quantity)
    {
        using var review = Reviewer(new Engine(Read(quantity, .98f), Read(quantity, .99f)));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(Row(10)), default);
        Assert.Equal(quantity, result.Observation!.Quantity);
        Assert.Equal("observation-revised", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(.99f, .70f)]
    [InlineData(.70f, .99f)]
    public async Task BothViewsMustSupplyReliableQuantityEvidence(float first, float second)
    {
        var baseline = Row(10);
        using var review = Reviewer(new Engine(Read(101, first), Read(101, second)));
        using var image = Band();
        Assert.Same(baseline, (await review.ReviewAsync(image, Input(baseline), default)).Observation);
    }

    [Theory]
    [InlineData(.90f, true)]
    [InlineData(.899f, false)]
    public async Task InclusiveDigitThresholdAndFinalQuantityConfidenceUseTheSuppliedDigitScore(float confidence, bool accepted)
    {
        using var review = Reviewer(new Engine(Read(101, confidence), Read(101, .96f)));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(Row(10)), default);
        Assert.Equal(accepted ? 101 : 10, result.Observation!.Quantity);
        Assert.Equal(accepted ? (double)confidence : 0, result.Observation.QuantityConfidence);
    }

    [Fact]
    public async Task MissingQuantityRecoveryKeepsItsExistingAcceptanceRulesAndReportsWeakDigitEvidence()
    {
        using var review = Reviewer(new Engine(Read(10, .7f), Read(10, .8f)));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(Row(null)), default);
        Assert.Equal(10, result.Observation!.Quantity);
        Assert.Equal((double).7f, result.Observation.QuantityConfidence);
    }

    [Fact]
    public async Task AgreementWithAnExistingAmountDoesNotRemoveTheBaselineBecauseOfWeakDigits()
    {
        var baseline = Row(10);
        using var review = Reviewer(new Engine(Read(10, .7f), Read(10, .8f)));
        using var image = Band();
        Assert.Same(baseline, (await review.ReviewAsync(image, Input(baseline), default)).Observation);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-.1f)]
    [InlineData(1.1f)]
    public async Task InvalidDigitScoresDoNotSupplyQuantityEvidence(float score)
    {
        var baseline = Row(10);
        using var review = Reviewer(new Engine(Read(101, score), Read(101, score)));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);
        Assert.Same(baseline, result.Observation);
        Assert.All(result.Diagnostics!.Readings, reading => Assert.Equal(0, reading.QuantityConfidence));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task MisalignedCharacterArraysCannotValidateAnAmount(int difference)
    {
        var baseline = Row(10);
        var reading = Read(101, .99f);
        reading = reading with { CharacterConfidences = Enumerable.Repeat(.99f, reading.Text.Length + difference).ToArray() };
        using var review = Reviewer(new Engine(reading, reading));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);
        Assert.Same(baseline, result.Observation);
        Assert.All(result.Diagnostics!.Readings, read => Assert.Equal(0, read.QuantityConfidence));
    }

    [Fact]
    public async Task OptionalLegacyEnginesRetainTheirExistingConsensusContract()
    {
        var reading = Read(101, .99f) with { CharacterConfidences = [] };
        using var review = Reviewer(new Engine(reading, reading));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(Row(10)), default);
        Assert.Equal(101, result.Observation!.Quantity);
        Assert.All(result.Diagnostics!.Readings, read => Assert.Null(read.QuantityConfidence));
    }

    [Fact]
    public async Task WeakNameCharacterDoesNotInvalidateOtherwiseReliableQuantityDigits()
    {
        var reading = Read(101, .99f);
        var scores = reading.CharacterConfidences.ToArray();
        scores[0] = .6f;
        reading = reading with { CharacterConfidences = scores };
        using var review = Reviewer(new Engine(reading, reading));
        using var image = Band();
        Assert.Equal(101, (await review.ReviewAsync(image, Input(Row(10)), default)).Observation!.Quantity);
    }

    [Fact]
    public async Task AnomalyCorrectionAlsoRequiresReliableDigits()
    {
        var baseline = Row(101);
        using var review = Reviewer(new Engine(Read(10, .99f), Read(10, .5f)));
        using var image = Band();
        var input = Input(baseline) with { QuantityAnomaly = new(101, 10, "history", 16) };
        Assert.Same(baseline, (await review.ReviewAsync(image, input, default)).Observation);
    }

    [Fact]
    public async Task CatalogFixedUnitNameRecoveryDoesNotDependOnTheSuffixDigits()
    {
        const string item = "BON Origin Shard";
        var baseline = Row(null) with { RawText = item, ItemName = item, NameConfidence = .7 };
        var read = Read(101, .1f, item);
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([item]), _ => new Engine(read, read));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline) with { Bounds = _ => new(1, 1) }, default);
        Assert.Equal(1, result.Observation!.Quantity);
        Assert.True(result.Observation.UsesFixedUnitQuantity);
    }

    [Fact]
    public async Task PositiveAndImpossibleZeroConsensusRetainsExistingQuantitySemantics()
    {
        using var review = Reviewer(new Engine(Read(6, .99f), Read(0, .99f)));
        using var image = Band();
        Assert.Equal(6, (await review.ReviewAsync(image, Input(Row(null)), default)).Observation!.Quantity);
    }

    private static LootObservation Row(int? quantity) => new(LootSource.Normal, 1, Item, Item,
        quantity, 1, 0, null, null) { NativeY = 200 };
    private static LootRowReviewInput Input(LootObservation row) => new(row, LootSource.Normal, 1, 200,
        row.Quantity ?? -1, .84153f, _ => new(2, 1000), _ => true);
    private static BackgroundLootRowReview Reviewer(Engine engine) => new(new CompanionItemMatcher([Item]), _ => engine);
    private static SecondaryLootOcrResult Read(int quantity, float lastDigitScore, string item = Item)
    {
        var text = $"{item} x {quantity}";
        var scores = Enumerable.Repeat(.999f, text.Length).ToArray();
        scores[^1] = lastDigitScore;
        return new(text, .99f, new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0))
        { CharacterConfidences = Array.AsReadOnly(scores) };
    }
    private static Mat Band()
    {
        var image = new Mat(50, 385, MatType.CV_8UC3, Scalar.Black);
        Cv2.Line(image, new(5, 20), new(250, 30), Scalar.White, 2);
        return image;
    }
    private sealed class Engine(params SecondaryLootOcrResult[] readings) : ISecondaryLootOcrRecognizer
    {
        private readonly Queue<SecondaryLootOcrResult> _readings = new(readings);
        public int Calls { get; private set; }
        public string BackendName => "quantity-confidence-test";
        public string LanguageTag => "en-US";
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        { Calls++; return _readings.Dequeue(); }
        public void Dispose() { }
    }
}
