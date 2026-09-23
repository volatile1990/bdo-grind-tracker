using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class BackgroundLootRowReviewTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Crystal = "BON Wandering Origin Crystal";
    private const string Earring = "Apeiron Earring";
    private const string Twilight = "Twilight Earring";
    private const string Necklace = "Twilight of the End - Necklace";
    private const string TwilightRing = "Twilight of the End - Ring";
    private static LootObservation Row(int? quantity = null, string? text = null) =>
        new(LootSource.Normal, 2, text ?? Helmet, Helmet, quantity, 1, 0, null, null) { NativeY = 150 };
    private static LootRowReviewInput Input(LootObservation? row) => new(row, LootSource.Normal, 2, 150, -1, 0,
        _ => new(4, 1000), _ => true);
    private static LootObservation RareRow() =>
        new(LootSource.Rare, 0, Earring + " x 1", Earring, 1, 1, 1, null, null);
    private static LootRowReviewInput RareInput(LootObservation? row) =>
        new(row, LootSource.Rare, 0, 25, -1, 0, _ => new(1, 1), _ => true);
    private static Mat Band()
    {
        var image = new Mat(50, 385, MatType.CV_8UC3, Scalar.Black);
        Cv2.Line(image, new(5, 12), new(270, 30), Scalar.White, 2);
        return image;
    }
    private static SecondaryLootOcrResult Read(string text, float confidence = .99f) =>
        new(text, confidence, new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0));

    [Theory]
    [InlineData("en-US", "Elion Follower's Helmet x 6")]
    [InlineData("de-DE", "Helm eines Anhängers Elions x 6")]
    public async Task ExplicitMissingAnchorProbeRequiresTwoMatchingReads(string language, string text)
    {
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(text), Read(text)])));
        review.ConfigureLanguage(language);
        using var image = Band();
        Assert.Null((await review.ReviewAsync(image, Input(null), default)).Diagnostics);
        var result = await review.ReviewAsync(image, Input(null) with { ReviewMissingAlignmentAnchor = true }, default);
        Assert.Equal("missing-alignment-anchor", result.Diagnostics!.Reason);
        Assert.True(result.Observation!.IsAlignmentAnchor);
        Assert.Equal(6, result.Observation.Quantity);
        Assert.Equal(Helmet, result.Observation.ItemName);
    }

    [Theory]
    [InlineData("6", "0", .99)]
    [InlineData("6", "4", .99)]
    [InlineData("6", "", .99)]
    [InlineData("6", "6", .94)]
    [InlineData("2", "2", .99)]
    [InlineData("1001", "1001", .99)]
    public async Task MissingAnchorCannotUseAClampedOrSingleQuantity(string a, string b, float confidence)
    {
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(Helmet + " x " + a, confidence), Read(Helmet + " x " + b, confidence)])));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(null) with { ReviewMissingAlignmentAnchor = true }, default);
        Assert.Null(result.Observation);
    }

    [Fact]
    public async Task AnchorProbeCannotReplaceAPrimaryRow()
    {
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]), _ => throw new Exception());
        using var image = Band();
        var baseline = Row();
        Assert.Same(baseline, (await review.ReviewAsync(image,
            Input(baseline) with { ReviewMissingAlignmentAnchor = true }, default)).Observation);
    }

    [Fact]
    public async Task EquallyGoodRareReadingsPreserveThePrimaryObservation()
    {
        var baseline = RareRow() with { RawText = Earring + " x ???" };
        var engine = new Engine(new([Read(Earring), Read(Earring)]));
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]), _ => engine);
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline), default);
        Assert.Equal([3, 1], engine.Channels);
        Assert.Equal("rare-ocr-review", result.Diagnostics!.Reason);
        Assert.Equal("rare-primary-selected", result.Diagnostics.Outcome);
        Assert.Null(result.Observation!.RejectionReason);
        Assert.Equal(Earring, result.Observation.ItemName);
        Assert.Equal(1, result.Observation.Quantity);
        Assert.Equal(baseline.RawText, result.Observation.RawText);
        Assert.Same(baseline, result.Observation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(.7)]
    public async Task MatchingPaddleViewsWinWhenThePrimaryNameIsDifferent(double primaryConfidence)
    {
        var baseline = RareRow() with { NameConfidence = primaryConfidence };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring, Twilight]),
            _ => new Engine(new([Read(Twilight), Read(Twilight)])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline), default);
        Assert.Equal(Twilight, result.Observation!.ItemName);
        Assert.Equal(Twilight, result.Observation.RawText);
        Assert.Equal(1, result.Observation.Quantity);
        Assert.Null(result.Observation.RejectionReason);
        Assert.Equal("rare-secondary-selected", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(Earring, Twilight, .99)]
    [InlineData(Earring, Earring, .94)]
    [InlineData(Earring, "", .99)]
    [InlineData("scenery", "scenery", .99)]
    [InlineData(Twilight, "", .99)]
    public async Task UnreadableOrEquallyStrongPaddleViewsPreserveAConfidentRare(string first, string second, float confidence)
    {
        var baseline = RareRow();
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring, Twilight]),
            _ => new Engine(new([Read(first, confidence), Read(second, confidence)])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline), default);
        Assert.Same(baseline, result.Observation);
        Assert.Equal("rare-primary-selected", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RareSecondaryReadingDoesNotRequireAPrimaryCatalogHint(bool missingBaseline)
    {
        var baseline = missingBaseline ? null : RareRow() with
        { ItemName = null, RawText = "unidentified text", RejectionReason = "unknown" };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Twilight]),
            _ => new Engine(new([Read(Twilight), Read(Twilight)])));
        using var image = Band();
        var result = await review.ReviewAsync(image,
            RareInput(baseline) with { ReviewMissingAlignmentAnchor = true }, default);
        Assert.Equal(Twilight, result.Observation!.ItemName);
        Assert.Equal(1, result.Observation.Quantity);
        Assert.Equal(Twilight, result.Observation.RawText);
        Assert.Null(result.Observation.RejectionReason);
        Assert.False(result.Observation.IsAlignmentAnchor);
        Assert.Equal(2, result.Diagnostics!.Readings.Count);
        Assert.Equal("rare-secondary-selected", result.Diagnostics.Outcome);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OneGoodPaddleViewIsEnoughWhenTheOtherIsUnreadableOrFails(bool goodFirst, bool otherThrows)
    {
        SecondaryLootOcrResult? unusable = otherThrows ? null : Read("unreadable scenery");
        var engine = new FallibleEngine(new(goodFirst ? [Read(Earring), unusable] : [unusable, Read(Earring)]));
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]), _ => engine);
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(null), default);
        Assert.Equal(Earring, result.Observation!.ItemName);
        Assert.Equal(Earring, result.Observation.RawText);
        Assert.Equal(1, result.Observation.Quantity);
        Assert.True(result.Observation.UsesFixedUnitQuantity);
        Assert.Null(result.Observation.RejectionReason);
        Assert.Equal("rare-secondary-selected", result.Diagnostics!.Outcome);
        Assert.Equal(otherThrows ? 1 : 0, result.Diagnostics.Errors);
        Assert.Equal(2, engine.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RareModelOrRecognitionFailurePreservesAGoodPrimary(bool failDuringRecognition)
    {
        var baseline = RareRow();
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => failDuringRecognition ? new FallibleEngine(new([null, null]))
                : throw new FileNotFoundException("model missing"));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline), default);
        Assert.Same(baseline, result.Observation);
        Assert.Equal("rare-primary-selected", result.Diagnostics!.Outcome);
        Assert.True(result.Diagnostics.Errors > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoUsableRareReadingRemainsExcluded(bool modelMissing)
    {
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => modelMissing ? throw new FileNotFoundException("model missing")
                : new Engine(new([Read("scenery"), Read("scenery")])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(null), default);
        AssertRejectedRare(result, modelMissing ? "" : "scenery", modelMissing
            ? BackgroundLootRowReview.UnconfirmedRareReason : LootObservation.RareOcrNoMatchReason);
        Assert.Equal("rare-no-valid-reading", result.Diagnostics!.Outcome);
        Assert.Equal(modelMissing ? 1 : 0, result.Diagnostics.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoodPrimaryRareSurvivesMissingImageInformation(bool emptyImage)
    {
        var baseline = RareRow();
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => throw new InvalidOperationException("No model needed for an empty crop"));
        using var image = emptyImage ? new Mat() : new Mat(50, 385, MatType.CV_8UC3, Scalar.Black);
        var result = await review.ReviewAsync(image, RareInput(baseline), default);
        Assert.Same(baseline, result.Observation);
        Assert.Equal("rare-primary-selected", result.Diagnostics!.Outcome);
        Assert.Equal(0, result.Diagnostics.Errors);
    }

    [Theory]
    [InlineData("6", "7")]
    [InlineData("6", "")]
    public async Task DisagreeingRareQuantitiesDoNotVetoAValidPrimary(string first, string second)
    {
        var baseline = RareRow() with { ItemName = Helmet, RawText = Helmet + " x 6", Quantity = 6 };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(Helmet + " x " + first), Read(Helmet + " x " + second)])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline) with { Bounds = _ => new(4, 1000) }, default);
        Assert.Same(baseline, result.Observation);
        Assert.Equal("rare-primary-selected", result.Diagnostics!.Outcome);
    }

    [Fact]
    public async Task ExactPaddleNameBeatsAFuzzyPrimaryWithoutASecondPaddleVote()
    {
        var baseline = RareRow() with { NameConfidence = .96 };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring, Twilight]),
            _ => new Engine(new([Read(Twilight), Read("unreadable")])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline), default);
        Assert.Equal(Twilight, result.Observation!.ItemName);
        Assert.Equal(1, result.Observation.NameConfidence);
        Assert.Equal("rare-secondary-selected", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrongerStandalonePaddleNameWinsDespiteDifferentItemsAndLowerEngineConfidence(bool exactFirst)
    {
        var exact = Read(Necklace, .96f);
        var fuzzy = Read("Twilight of the End - Rinq", .99f);
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Necklace, TwilightRing]),
            _ => new Engine(new(exactFirst ? [exact, fuzzy] : [fuzzy, exact])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(null), default);
        Assert.Equal(Necklace, result.Observation!.ItemName);
        Assert.Equal(Necklace, result.Observation.RawText);
        Assert.Equal(1, result.Observation.NameConfidence);
        Assert.Equal("rare-secondary-selected", result.Diagnostics!.Outcome);
        Assert.Contains(result.Diagnostics.Readings, reading => reading.ItemName == TwilightRing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquallyExactStandalonePaddleNamesUseEngineConfidence(bool strongerFirst)
    {
        var stronger = Read(Necklace, .99f);
        var weaker = Read(TwilightRing, .96f);
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Necklace, TwilightRing]),
            _ => new Engine(new(strongerFirst ? [stronger, weaker] : [weaker, stronger])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(null), default);
        Assert.Equal(Necklace, result.Observation!.ItemName);
        Assert.Equal("rare-secondary-selected", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(Earring, .94)]
    [InlineData("Apeiron Xarring", .99)]
    public async Task AnUnreliableStandalonePaddleReadingCannotCreateARare(string text, float confidence)
    {
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => new Engine(new([Read(text, confidence), Read("")])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(null), default);
        AssertRejectedRare(result, text);
        Assert.Equal("rare-no-valid-reading", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RareCandidatesOutsideTheAllowedPoolStayExcluded(bool hasPrimary)
    {
        var baseline = hasPrimary ? RareRow() : null;
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => new Engine(new([Read(Earring), Read(Earring)])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline) with { Allows = _ => false }, default);
        AssertRejectedRare(result, baseline?.RawText ?? Earring, LootObservation.RareOcrNoMatchReason);
        Assert.Equal("rare-no-valid-reading", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("1001")]
    [InlineData("")]
    public async Task InvalidStandaloneRareQuantitiesStayExcluded(string quantity)
    {
        var text = Helmet + " x " + quantity;
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(text), Read("")])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(null) with { Bounds = _ => new(4, 1000) }, default);
        AssertRejectedRare(result, text);
        Assert.Equal("rare-no-valid-reading", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RarePrimaryAndPaddleAcceptAnUnverifiedMaximum(bool hasPrimary)
    {
        const int quantity = 1234;
        var text = Helmet + " x " + quantity;
        var baseline = hasPrimary ? RareRow() with
            { ItemName = Helmet, RawText = text, Quantity = quantity } : null;
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new(hasPrimary ? [Read(""), Read("")] : [Read(text), Read("")])));
        using var image = Band();

        var result = await review.ReviewAsync(image,
            RareInput(baseline) with { Bounds = _ => new(4, null) }, default);

        Assert.Equal(Helmet, result.Observation!.ItemName);
        Assert.Equal(quantity, result.Observation.Quantity);
        Assert.Null(result.Observation.RejectionReason);
        Assert.Equal(hasPrimary ? "rare-primary-selected" : "rare-secondary-selected", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StandaloneVariableRareRejectsWeakOrMalformedDigitConfidence(bool incompletePrimary, bool malformed)
    {
        const string item = "Tungrad Ruins Fragment";
        const string text = item + " x 66";
        var scores = Enumerable.Repeat(.99f, malformed ? text.Length - 1 : text.Length).ToArray();
        if (!malformed) scores[^1] = .2f;
        var weak = Read(text) with { CharacterConfidences = Array.AsReadOnly(scores) };
        var baseline = incompletePrimary ? RareRow() with
            { ItemName = item, RawText = item, Quantity = null } : null;
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([item]),
            _ => new Engine(new([weak, Read("")])));
        using var image = Band();

        var result = await review.ReviewAsync(image,
            RareInput(baseline) with { Bounds = _ => new(1, 1000) }, default);

        Assert.Null(result.Observation!.ItemName);
        Assert.Null(result.Observation.Quantity);
        Assert.Equal(BackgroundLootRowReview.UnconfirmedRareReason, result.Observation.RejectionReason);
        Assert.Null(result.Diagnostics!.Readings[0].Quantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandaloneVariableRareAcceptsStrongDigitConfidence(bool incompletePrimary)
    {
        const string item = "Tungrad Ruins Fragment";
        const string text = item + " x 66";
        var scores = Enumerable.Repeat(.99f, text.Length).ToArray();
        scores[^1] = .95f;
        var strong = Read(text) with { CharacterConfidences = Array.AsReadOnly(scores) };
        var baseline = incompletePrimary ? RareRow() with
            { ItemName = item, RawText = item, Quantity = null } : null;
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([item]),
            _ => new Engine(new([strong, Read("")])));
        using var image = Band();

        var result = await review.ReviewAsync(image,
            RareInput(baseline) with { Bounds = _ => new(1, 1000) }, default);

        Assert.Equal(item, result.Observation!.ItemName);
        Assert.Equal(66, result.Observation.Quantity);
        Assert.Null(result.Observation.RejectionReason);
        Assert.Equal("rare-secondary-selected", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("scenery", false)]
    [InlineData(Earring, true)]
    [InlineData("Apeiron Xarring", true)]
    public async Task UnusableRareHintsPreserveContinuityButBackgroundAllowsAbsence(string text, bool hasHint)
    {
        var baseline = RareRow() with { RawText = text, ItemName = null, Quantity = null, RejectionReason = "unknown" };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => new Engine(new([Read(""), Read("")])));
        using var image = Band();
        var result = await review.ReviewAsync(image, RareInput(baseline), default);
        AssertRejectedRare(result, text, hasHint
            ? BackgroundLootRowReview.UnconfirmedRareReason : LootObservation.RareOcrNoMatchReason);
        Assert.Equal("rare-no-valid-reading", result.Diagnostics!.Outcome);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("scenery", false)]
    [InlineData("scenery", true)]
    public async Task SuccessfulBackgroundViewAllowsAbsenceDespiteAnotherViewsFailure(string text, bool failedFirst)
    {
        SecondaryLootOcrResult?[] reads = failedFirst ? [null, Read(text)] : [Read(text), null];
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => new FallibleEngine(new(reads)));
        using var image = Band();

        var result = await review.ReviewAsync(image, RareInput(null), default);

        Assert.Equal(LootObservation.RareOcrNoMatchReason, result.Observation!.RejectionReason);
        Assert.Equal(1, result.Diagnostics!.Errors);
        Assert.Single(result.Diagnostics.Readings, reading => reading.Error is null);
    }

    [Fact]
    public async Task ABlankGapWithOneFailedOcrViewSeparatesTwoIdenticalRareDrops()
    {
        var context = new LifetimeParsingContext(0,
            [new(Earring, [], true, LootSource.Rare)]);
        var counter = new LifetimeNormalReconciliationAdapter(context, false, LootSource.Rare, 1);
        var secondary = new Queue<SecondaryLootOcrResult?>(Enumerable.Range(0, 10)
            .SelectMany(_ => new SecondaryLootOcrResult?[] { null, Read("") }));
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Earring]),
            _ => new FallibleEngine(secondary));
        using var image = Band();
        var at = DateTimeOffset.UnixEpoch;
        for (var frame = 0; frame < 5; frame++)
            counter.ProcessObservations([RareRow()], at.AddMilliseconds(frame * 200));

        for (var frame = 5; frame < 15; frame++)
        {
            var absent = await review.ReviewAsync(image, RareInput(null), default);
            Assert.Equal(LootObservation.RareOcrNoMatchReason, absent.Observation!.RejectionReason);
            counter.ProcessObservations([absent.Observation], at.AddMilliseconds(frame * 200));
        }
        for (var frame = 15; frame < 20; frame++)
            counter.ProcessObservations([RareRow()], at.AddMilliseconds(frame * 200));

        counter.Complete();
        Assert.Equal(2, counter.Projection!.Totals[Earring]);
        Assert.Equal(2, counter.Projection.SupportedDropCount);
    }

    private static void AssertRejectedRare(LootRowReviewResult result, string rawText,
        string rejectionReason = BackgroundLootRowReview.UnconfirmedRareReason)
    {
        Assert.NotNull(result.Observation);
        Assert.Equal(rejectionReason, result.Observation.RejectionReason);
        Assert.Null(result.Observation.ItemName);
        Assert.Null(result.Observation.Quantity);
        Assert.Equal(rawText, result.Observation.RawText);
        Assert.Equal(LootSource.Rare, result.Observation.Source);
        Assert.Equal(0, result.Observation.Slot);
        Assert.Equal(25, result.Observation.NativeY);
    }

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
    [InlineData("en-US", "Elion Follower(s Helmet x IO", "Elion Follower's Helmet x 10", 1)]
    [InlineData("de-DE", "Helm eines Anhängers Elions x IO", "Helm eines Anhängers Elions x 10", 1)]
    [InlineData("en-US", "Elion Follower(s Helmet x 10", "Elion Follower's Helmet x 10", 1)]
    [InlineData("en-US", "Elion Follower(s Helmet x 1001", "Elion Follower's Helmet x 10", 1001)]
    public async Task NameReviewAlsoRepairsAnIndependentlyUnreliableQuantity(string language, string primary, string secondary, int amount)
    {
        var baseline = Row(amount, primary) with { NameConfidence = .7 };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(secondary), Read(secondary)])));
        review.ConfigureLanguage(language);
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);
        Assert.Equal("uncertain-name", result.Diagnostics!.Reason);
        Assert.Equal("observation-revised", result.Diagnostics.Outcome);
        Assert.Equal(10, result.Observation!.Quantity);
        Assert.Equal(Helmet, result.Observation.ItemName);
    }

    [Theory]
    [InlineData("Elion Follower(s Helmet x 6", -1, 0)]
    [InlineData("Elion Follower(s Helmet", 6, .96)]
    public async Task NameReviewPreservesAnIndependentlyTrustedPrimaryQuantity(string primary, int template, float score)
    {
        var baseline = Row(6, primary) with { NameConfidence = .7 };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(Helmet + " x 10"), Read(Helmet + " x 10")])));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline) with { TemplateQuantity = template, TemplateScore = score }, default);
        Assert.Equal("uncertain-name", result.Diagnostics!.Reason);
        Assert.Equal(6, result.Observation!.Quantity);
        Assert.Equal("baseline-confirmed", result.Diagnostics.Outcome);
    }

    [Theory]
    [InlineData("10", "7", .99)]
    [InlineData("10", "", .99)]
    [InlineData("10", "10", .94)]
    public async Task NameAndQuantityReviewStillRequiresReliablePaddleConsensus(string first, string second, float confidence)
    {
        var baseline = Row(1, Helmet + " x IO") with { NameConfidence = .7 };
        using var review = new BackgroundLootRowReview(new CompanionItemMatcher([Helmet]),
            _ => new Engine(new([Read(Helmet + " x " + first, confidence), Read(Helmet + " x " + second, confidence)])));
        using var image = Band();
        var result = await review.ReviewAsync(image, Input(baseline), default);
        Assert.Same(baseline, result.Observation);
        Assert.NotEqual("observation-revised", result.Diagnostics!.Outcome);
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

    private sealed class FallibleEngine(Queue<SecondaryLootOcrResult?> reads) : ISecondaryLootOcrRecognizer
    {
        public int Calls { get; private set; }
        public string BackendName => "paddle-test";
        public string LanguageTag => "en-US";
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        {
            Calls++;
            return reads.Dequeue() ?? throw new InvalidOperationException("unreadable Paddle view");
        }
        public void Dispose() { }
    }
}
