using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class NormalLootRecoveryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void CompleteBaselineIsReturnedWithoutPreparationOrOcr()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(17);
        var names = new Recognizer((_, _) => throw new InvalidOperationException("Must not OCR."));
        var recovery = new NormalLootRecovery(Matcher(), names,
            (_, _) => throw new InvalidOperationException("Must not prepare."));
        var budget = Budget();

        var result = recovery.Recover(source, original, baseline, 0, 1f, budget, default);

        Assert.Same(baseline, result);
        Assert.Equal(0, names.Calls);
        Assert.Equal(0, budget.OcrCalls);
        Assert.Equal(0, budget.Errors);
    }

    [Fact]
    public void MissingQuantityReadsIsolatedDigitsBeforeNameAndKeepsAllBaselineMetadata()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(null);
        var images = new Images();
        var names = new Recognizer((image, _) =>
        {
            Assert.True(image.Width < 150, "The first OCR image must be the isolated quantity, not the name.");
            return Ocr("42");
        });
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);
        var budget = Budget();

        var result = recovery.Recover(source, original, baseline, 0, 1f, budget, default);

        Assert.Equal(baseline with { Quantity = 42 }, result);
        Assert.Equal(1, names.Calls);
        Assert.Equal(1, budget.OcrCalls);
        Assert.Equal([NormalLootRecoveryVariant.Grayscale], images.Variants);
        images.AssertDisposed();
    }

    [Fact]
    public void FirstSuccessfulVariantWinsWithoutSearchingForTheLargestNumber()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var images = new Images();
        var responses = new Queue<string>(["12", "999"]);
        var names = new Recognizer((_, _) => Ocr(responses.Dequeue()));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        var result = recovery.Recover(source, original, Observation(null), 0, 1f, Budget(), default);

        Assert.Equal(12, result!.Quantity);
        Assert.Equal("999", Assert.Single(responses));
        Assert.Equal([NormalLootRecoveryVariant.Grayscale], images.Variants);
        images.AssertDisposed();
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("0042", 42)]
    [InlineData(" 42 ", 42)]
    [InlineData("x42", 42)]
    [InlineData("X 42", 42)]
    [InlineData("×42", 42)]
    [InlineData("2147483647", int.MaxValue)]
    public void QuantityParserAcceptsOnlyAWholePositiveAsciiNumberWithOptionalMultiplier(string text, int expected)
    {
        Assert.True(NormalLootRecovery.TryParseQuantity(text, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("x0")]
    [InlineData("-42")]
    [InlineData("+42")]
    [InlineData("4.2")]
    [InlineData("4,2")]
    [InlineData("4 2")]
    [InlineData("4\n2")]
    [InlineData("42 items")]
    [InlineData("BON Origin Shard x1")]
    [InlineData("I")]
    [InlineData("xI")]
    [InlineData("４２")]
    [InlineData("٤٢")]
    [InlineData("2147483648")]
    [InlineData("999999999999999999999999999999999999")]
    [InlineData("42x")]
    [InlineData("xx42")]
    public void QuantityParserDoesNotRepairLettersOrJoinSeparateNumbers(string? text)
    {
        Assert.False(NormalLootRecovery.TryParseQuantity(text, out _));
    }

    [Fact]
    public void FailedGrayscaleReadingFallsThroughToAdaptiveQuantityReading()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var images = new Images();
        var names = new Recognizer((_, call) => Ocr(call == 3 ? "42" : "unreadable"));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        var result = recovery.Recover(source, original, Observation(null), 0, 1f, Budget(), default);

        Assert.Equal(42, result!.Quantity);
        Assert.Equal(3, names.Calls);
        Assert.Equal([NormalLootRecoveryVariant.Grayscale, NormalLootRecoveryVariant.AdaptiveThreshold], images.Variants);
        images.AssertDisposed();
    }

    [Theory]
    [InlineData("x4O")]
    [InlineData("x4 2")]
    [InlineData("xI")]
    [InlineData("x42junk")]
    [InlineData("x2147483648")]
    public void WholeRowRetryCannotTurnAnIncompleteNumberIntoAQuantity(string suffix)
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var images = new Images();
        var names = new Recognizer((image, _) => Ocr(image.Width < 150
            ? "unreadable" : "Black Crystal Fragment " + suffix));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        var result = recovery.Recover(source, original, Observation(null), 0, 1f, Budget(), default);

        Assert.NotNull(result);
        Assert.Null(result.Quantity);
        Assert.Equal("Black Crystal Fragment", result.ItemName);
        Assert.Equal(2, images.Variants.Count);
        images.AssertDisposed();
    }

    [Theory]
    [InlineData("x12345", 12345)]
    [InlineData("×42", 42)]
    [InlineData("X 42", 42)]
    public void WholeRowRetryUsesTheFullNumericSuffixWithoutLegacyFourDigitTruncation(string suffix, int expected)
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var images = new Images();
        var names = new Recognizer((image, _) => Ocr(image.Width < 150
            ? "unreadable" : "Black Crystal Fragment " + suffix));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        var result = recovery.Recover(source, original, Observation(null), 0, 1f, Budget(), default);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Quantity);
        Assert.Equal("Black Crystal Fragment", result.ItemName);
        Assert.Equal(2, names.Calls);
        images.AssertDisposed();
    }

    [Fact]
    public void RecoveringANameRetainsKnownTemplateQuantityDespiteAConflictingOcrSuffix()
    {
        using var source = SyntheticBand();
        using var original = new Row(17);
        var images = new Images();
        var names = new Recognizer((image, _) =>
        {
            Assert.True(image.Width > 500);
            return Ocr("Black Crystal Fragment x999");
        });
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        var result = recovery.Recover(source, original, null, 2, 1f, Budget(), default);

        Assert.NotNull(result);
        Assert.Equal("Black Crystal Fragment", result.ItemName);
        Assert.Equal(17, result.Quantity);
        Assert.Equal(original.Y, result.NativeY);
        Assert.Equal(2, result.Slot);
        Assert.Equal(1, names.Calls);
        images.AssertDisposed();
    }

    [Fact]
    public void AnAlreadyRecognizedItemIsNeverReplacedToObtainAQuantity()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(null);
        var images = new Images();
        var names = new Recognizer((image, _) => Ocr(image.Width < 150 ? "unreadable" : "Branch of Abundance x88"));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        var result = recovery.Recover(source, original, baseline, 0, 1f, Budget(), default);

        Assert.Same(baseline, result);
        Assert.Null(result!.Quantity);
        Assert.Equal(4, names.Calls);
        images.AssertDisposed();
    }

    [Fact]
    public void PreparationFailurePreservesTheOriginalObservation()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(null);
        var names = new Recognizer((_, _) => throw new InvalidOperationException("Must not OCR."));
        var recovery = new NormalLootRecovery(Matcher(), names,
            (_, _) => throw new ArgumentException("Synthetic preprocessing failure."));
        var budget = Budget();

        var result = recovery.Recover(source, original, baseline, 0, 1f, budget, default);

        Assert.Same(baseline, result);
        Assert.Equal(2, budget.Errors);
        Assert.Equal(0, names.Calls);
    }

    [Fact]
    public void OcrFailuresPreserveBaselineAndDisposeAllAdditionalImages()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(null);
        var images = new Images();
        var names = new Recognizer((_, _) => throw new InvalidOperationException("Synthetic OCR failure."));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);
        var budget = Budget();

        var result = recovery.Recover(source, original, baseline, 0, 1f, budget, default);

        Assert.Same(baseline, result);
        Assert.Equal(2, budget.Errors);
        Assert.Equal(2, names.Calls);
        images.AssertDisposed();
    }

    [Fact]
    public void CancellationPropagatesFromOcrAndStillDisposesPreparedImages()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        using var cancellation = new CancellationTokenSource();
        var images = new Images();
        var names = new Recognizer((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);
        var budget = Budget();

        var error = Assert.Throws<OperationCanceledException>(() =>
            recovery.Recover(source, original, Observation(null), 0, 1f, budget, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, budget.Errors);
        images.AssertDisposed();
    }

    [Fact]
    public void AlreadyCanceledRecoveryDoesNotPrepareOrReadImages()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var images = new Images();
        var names = new Recognizer((_, _) => Ocr("42"));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        Assert.Throws<OperationCanceledException>(() => recovery.Recover(source, original,
            Observation(null), 0, 1f, Budget(), new CancellationToken(canceled: true)));

        Assert.Empty(images.Variants);
        Assert.Equal(0, names.Calls);
    }

    [Fact]
    public void SharedFrameBudgetStopsAfterExactlyEightAdditionalOcrCalls()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(null);
        var images = new Images();
        var names = new Recognizer((_, _) => Ocr("unreadable"));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);
        var budget = Budget();

        for (var slot = 0; slot < 3; slot++)
            Assert.Same(baseline, recovery.Recover(source, original, baseline, slot, 1f, budget, default));

        Assert.Equal(8, budget.OcrCalls);
        Assert.Equal(8, names.Calls);
        Assert.Equal(4, images.Variants.Count);
        Assert.False(budget.CanContinue);
        images.AssertDisposed();
    }

    [Fact]
    public void ElapsedTimeBudgetStopsBeforeStartingTheNextReadAt120Milliseconds()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(null);
        var time = new FakeTimeProvider();
        var budget = new NormalLootRecoveryBudget(time);
        var images = new Images();
        var names = new Recognizer((_, _) =>
        {
            time.Advance(TimeSpan.FromMilliseconds(120));
            return Ocr("unreadable");
        });
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        var result = recovery.Recover(source, original, baseline, 0, 1f, budget, default);

        Assert.Same(baseline, result);
        Assert.Equal(1, names.Calls);
        Assert.Equal(1, budget.OcrCalls);
        Assert.False(budget.CanContinue);
        Assert.Single(images.Variants);
        images.AssertDisposed();
    }

    [Fact]
    public void ExhaustedTimeBudgetDoesNotEvenPrepareAnotherVariant()
    {
        using var source = SyntheticBand();
        using var original = new Row();
        var baseline = Observation(null);
        var time = new FakeTimeProvider();
        var budget = new NormalLootRecoveryBudget(time);
        time.Advance(TimeSpan.FromMilliseconds(120));
        var images = new Images();
        var names = new Recognizer((_, _) => Ocr("42"));
        var recovery = new NormalLootRecovery(Matcher(), names, images.Prepare);

        Assert.Same(baseline, recovery.Recover(source, original, baseline, 0, 1f, budget, default));
        Assert.Empty(images.Variants);
        Assert.Equal(0, names.Calls);
    }

    [Fact]
    public void AvailableWindowsOcrReadsTheRealSyntheticQuantityCropOffline()
    {
        // This uses generated pixels only: no desktop, game, window capture or input.
        var recognizer = CompanionWindowsOcrRecognizer.TryCreate("en-US");
        output.WriteLine($"Installed OCR engine: {recognizer?.LanguageTag ?? "unavailable"}");
        if (recognizer is null) return;
        using var source = SyntheticBand();
        using var images = NormalLootRecoveryPreprocessor.Prepare(source, NormalLootRecoveryVariant.Grayscale);

        Assert.NotNull(images.QuantityImage);
        var result = recognizer.Recognize(images.QuantityImage!);
        output.WriteLine($"Synthetic isolated quantity read: {result.Text}");

        Assert.True(NormalLootRecovery.TryParseQuantity(result.Text, out var quantity), result.Text);
        Assert.Equal(42, quantity);
    }

    [Fact]
    public void AvailableWindowsOcrRecoversAWholeDimBandRejectedByTheBaselineOffline()
    {
        // Exercise both actual OCR reads and their real first-word geometry. The
        // fixture is generated here, never sampled from a desktop or game window.
        var engine = CompanionWindowsOcrRecognizer.TryCreate("en-US");
        output.WriteLine($"Installed OCR engine: {engine?.LanguageTag ?? "unavailable"}");
        if (engine is null) return;
        using var source = new Mat(100, 700, MatType.CV_8UC3, new Scalar(35, 35, 35));
        Cv2.PutText(source, "Black Crystal Fragment", new OpenCvSharp.Point(20, 55),
            HersheyFonts.HersheySimplex, 0.7, new Scalar(140, 140, 140), 2, LineTypes.AntiAlias);
        Cv2.PutText(source, "42", new OpenCvSharp.Point(580, 75),
            HersheyFonts.HersheySimplex, 0.7, new Scalar(140, 140, 140), 2, LineTypes.AntiAlias);
        using var baseline = new CompanionNormalRowProcessor(null).Process(source, 250, 1f, 0);
        Assert.True(baseline.IsBlank);
        using var original = new Row(blank: baseline.IsBlank);
        var images = new Images();
        var actualOcr = new Recognizer((image, call) =>
        {
            var read = engine.Recognize(image);
            output.WriteLine($"Actual read {call}: {read.Text}; first-word={read.FirstWord}");
            return read;
        });
        var recovery = new NormalLootRecovery(Matcher(), actualOcr, images.Prepare);

        var recovered = recovery.Recover(source, original, null, 0, 1f, Budget(), default);

        Assert.NotNull(recovered);
        Assert.Equal("Black Crystal Fragment", recovered.ItemName);
        Assert.Equal(42, recovered.Quantity);
        Assert.Null(recovered.RejectionReason);
        Assert.InRange(actualOcr.Calls, 1, NormalLootRecoveryBudget.MaximumOcrCalls);
        images.AssertDisposed();
    }

    private static CompanionItemMatcher Matcher() => new(new[] { "Black Crystal Fragment", "Branch of Abundance" });

    private static NormalLootRecoveryBudget Budget() => new(new FakeTimeProvider());

    private static LootObservation Observation(int? quantity) =>
        new(LootSource.Normal, 0, "Black Crystal Fragment", "Black Crystal Fragment", quantity,
            0.91, 0, 123, null) { NativeY = 250 };

    private static CompanionOcrResult Ocr(string text) =>
        new(text, new(CompanionOcrGeometryStatus.Success, 20, 35, 100, 20));

    private static Mat SyntheticBand()
    {
        var source = new Mat(100, 700, MatType.CV_8UC3, new Scalar(35, 35, 35));
        Cv2.PutText(source, "Black Crystal Fragment", new OpenCvSharp.Point(20, 65),
            HersheyFonts.HersheySimplex, 0.7, new Scalar(140, 140, 140), 2, LineTypes.AntiAlias);
        Cv2.PutText(source, "42", new OpenCvSharp.Point(580, 75),
            HersheyFonts.HersheySimplex, 0.7, new Scalar(140, 140, 140), 2, LineTypes.AntiAlias);
        return source;
    }

    private sealed class Images
    {
        public List<NormalLootRecoveryVariant> Variants { get; } = [];
        public List<NormalLootRecoveryImages> Prepared { get; } = [];

        public NormalLootRecoveryImages Prepare(Mat source, NormalLootRecoveryVariant variant)
        {
            Variants.Add(variant);
            var images = NormalLootRecoveryPreprocessor.Prepare(source, variant);
            Prepared.Add(images);
            return images;
        }

        public void AssertDisposed()
        {
            Assert.NotEmpty(Prepared);
            Assert.All(Prepared, images =>
            {
                Assert.True(images.NameImage.IsDisposed);
                Assert.True(images.QuantityImage is null || images.QuantityImage.IsDisposed);
            });
        }
    }

    private sealed class Recognizer(Func<Mat, int, CompanionOcrResult> recognize) : ICompanionNameRecognizer
    {
        public string BackendName => "test-ocr";
        public string LanguageTag => "en-US";
        public int Calls { get; private set; }
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) => recognize(image, ++Calls);
    }

    private sealed class Row(int quantity = -1, bool blank = false) : ICompanionPreparedRow
    {
        public int Y => 250;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 690;
        public int TemplateQuantity => quantity;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => 1.5f;
        public Mat? NameImage => null;
        public void Dispose() { }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
