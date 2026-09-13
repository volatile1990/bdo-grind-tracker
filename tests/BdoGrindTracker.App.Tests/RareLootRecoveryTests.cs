using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class RareLootRecoveryTests
{
    private const string Ring = "Twilight of the End - Ring";
    private const string Material = "Black Crystal Fragment";

    [Theory]
    [InlineData("Twilight of the End - Ring")]
    [InlineData("Twilight of the End - Ring x7")]
    [InlineData("Dämmerung des Endes – Ring")]
    public void BlankNativeBandCanRecoverAFixedUnitRingInEitherLanguage(string text)
    {
        using var source = Band();
        var reads = new Reads(_ => Ocr(text, x: 250, y: 10));
        var recovery = Create(reads);

        var result = recovery.Recover(source, new Row(), null, 0, 1, Budget(), default);

        Assert.NotNull(result);
        Assert.Equal(Ring, result.ItemName);
        Assert.Equal(1, result.Quantity);
        Assert.Equal(LootSource.Rare, result.Source);
        Assert.Equal(0, result.NativeY);
        Assert.True(result.UsesFixedUnitQuantity);
        Assert.False(result.UsesImplicitUnitQuantity);
        Assert.Equal(1, reads.Calls);
    }

    [Theory]
    [InlineData("x12345", 12345)]
    [InlineData("×42", 42)]
    [InlineData("X 42", 42)]
    [InlineData("x4O", null)]
    [InlineData("x4 2", null)]
    [InlineData("x0", null)]
    [InlineData("x2147483648", null)]
    [InlineData("", null)]
    public void VariableQuantityRequiresTheSameCompleteSuffixAsNormalRecovery(string suffix, int? expected)
    {
        using var source = Band();
        var reads = new Reads(_ => Ocr(Material + " " + suffix));
        var result = Create(reads).Recover(source, new Row(), null, 0, 1, Budget(), default);

        Assert.NotNull(result);
        Assert.Equal(Material, result.ItemName);
        Assert.Equal(expected, result.Quantity);
        Assert.False(result.UsesImplicitUnitQuantity);
        Assert.Equal(expected is null ? 2 : 1, reads.Calls);
    }

    [Fact]
    public void AdaptiveSecondReadCanRescueAnUnreadableGrayscaleRead()
    {
        using var source = Band();
        var reads = new Reads(call => Ocr(call == 1 ? "scenery" : Ring));

        var result = Create(reads).Recover(source, new Row(), null, 0, 1, Budget(), default);

        Assert.Equal(Ring, result!.ItemName);
        Assert.Equal(2, reads.Calls);
    }

    [Fact]
    public void OutOfBandGeometryCannotCreateARecoveredDrop()
    {
        using var source = Band();
        var reads = new Reads(_ => Ocr(Ring, y: 95));

        Assert.Null(Create(reads).Recover(source, new Row(), null, 0, 1, Budget(), default));
        Assert.Equal(2, reads.Calls);
    }

    [Fact]
    public void RecoveryPreservesAnAcceptedItemWhileLookingForItsAmount()
    {
        using var source = Band();
        var baseline = Observation(Material, null);
        var reads = new Reads(_ => Ocr(Ring));

        Assert.Same(baseline, Create(reads).Recover(source, new Row(), baseline, 0, 1, Budget(), default));
        Assert.Equal(2, reads.Calls);
    }

    [Fact]
    public void CompleteBaselineAndUniformBackgroundDoNotSpendRecoveryBudget()
    {
        using var source = Band();
        using var uniform = new Mat(60, 385, MatType.CV_8UC3, new Scalar(120, 120, 120));
        var baseline = Observation(Material, 42);
        var reads = new Reads(_ => throw new InvalidOperationException("OCR is unnecessary."));
        var recovery = Create(reads);

        Assert.Same(baseline, recovery.Recover(source, new Row(), baseline, 0, 1, Budget(), default));
        Assert.Null(recovery.Recover(uniform, new Row(), null, 0, 1, Budget(), default));
        Assert.Equal(0, reads.Calls);
    }

    [Fact]
    public void FailedOptionalReadsPreserveBaselineAndUseTheSharedBudget()
    {
        using var source = Band();
        var baseline = Observation(Material, null);
        var reads = new Reads(_ => throw new InvalidOperationException("Model unavailable."));
        var recovery = Create(reads);
        var budget = Budget();

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Same(baseline, recovery.Recover(source, new Row(), baseline, 0, 1, budget, default));

        Assert.Equal(NormalLootRecoveryBudget.MaximumOcrCalls, reads.Calls);
        Assert.Equal(reads.Calls, budget.Errors);
        Assert.False(budget.CanContinue);
    }

    [Fact]
    public void CancellationPropagatesWithoutCreatingAnObservation()
    {
        using var source = Band();
        using var cancellation = new CancellationTokenSource();
        var reads = new Reads(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var recovery = Create(reads);
        var budget = Budget();

        Assert.Throws<OperationCanceledException>(() => recovery.Recover(source, new Row(), null,
            0, 1, budget, cancellation.Token));
        Assert.Equal(0, budget.Errors);
    }

    [Theory]
    [InlineData("x12345", 12345)]
    [InlineData("×42", 42)]
    [InlineData("x4O", null)]
    [InlineData("x4 2", null)]
    [InlineData("x0", null)]
    [InlineData("x2147483648", null)]
    [InlineData("", null)]
    public async Task RealSpecialPreparationAndAnalyzerKeepOnlyCompleteVariableQuantities(string suffix, int? expected)
    {
        var reads = new Reads(_ => Ocr(Material + " " + suffix));
        using var analyzer = Analyzer(reads);
        using var bitmap = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(bitmap, DateTimeOffset.UnixEpoch, default);

        var observed = Assert.Single(result.Observations, row => row.Source == LootSource.Rare);
        Assert.Equal(Material, observed.ItemName);
        Assert.Equal(expected, observed.Quantity);
        Assert.False(observed.UsesImplicitUnitQuantity);
        Assert.Equal(0, result.Recovery.OcrCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeometryRejectedSpecialTextCannotReenterThroughTheRawCounter(bool toneMapped)
    {
        var reads = new Reads(_ => Ocr(Ring, y: 95));
        using var analyzer = Analyzer(reads);
        using var bitmap = new Bitmap(800, 600);

        for (var index = 0; index < 6; index++)
        {
            var result = await analyzer.AnalyzeAsync(bitmap, DateTimeOffset.UnixEpoch.AddMilliseconds(index * 200),
                isHdr: false, isToneMapped: toneMapped, default);
            Assert.Empty(result.NewEvents);
            Assert.Empty(result.LootProjection!.Totals);
        }
        Assert.Empty(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(2)).LootProjection!.Totals);
    }

    [Theory]
    [InlineData("[System] The ancient gates are opening")]
    [InlineData("Current location: 123 456")]
    [InlineData("Unknown item x42")]
    public async Task ReadableSceneryOutsideTheCatalogDoesNotBecomeASpecialDrop(string text)
    {
        var reads = new Reads(_ => Ocr(text));
        using var analyzer = Analyzer(reads);
        using var bitmap = new Bitmap(800, 600);

        for (var index = 0; index < 6; index++)
        {
            var result = await analyzer.AnalyzeAsync(bitmap, DateTimeOffset.UnixEpoch.AddMilliseconds(index * 200), default);
            Assert.All(result.Observations, observation => Assert.NotNull(observation.RejectionReason));
            Assert.Empty(result.NewEvents);
            Assert.Empty(result.LootProjection!.Totals);
        }
    }

    [Fact]
    public async Task ABlankPrimaryMaskStillRunsSpecialRecoveryAndSecondaryReviewWithoutDuplicatingTheRow()
    {
        var reads = new Reads(_ => Ocr(Ring));
        var review = new ObservingReview();
        using var analyzer = Analyzer(reads, new BlankRareRows(), review);
        using var bitmap = new Bitmap(800, 600);
        for (var index = 0; index < 4; index++)
        {
            var result = await analyzer.AnalyzeAsync(bitmap, DateTimeOffset.UnixEpoch.AddMilliseconds(index * 200), default);
            var row = Assert.Single(result.Observations);
            Assert.Equal(LootSource.Rare, row.Source);
            Assert.Equal(0, row.Slot);
            Assert.Equal(0, row.NativeY);
            Assert.Equal(Ring, row.ItemName);
            Assert.Equal(1, row.Quantity);
            Assert.Equal(1, result.RareRecovery.RowsRecovered);
            Assert.Equal(NormalLootRecoveryDiagnostics.Empty, result.Recovery);
            Assert.Equal(1, result.LootProjection!.Totals[Ring]);
            Assert.Equal(1, result.LootProjection.ConfirmedDropCount);
        }
        Assert.Equal(4, review.Inputs.Count);
        Assert.All(review.Inputs, input =>
        {
            Assert.Equal(LootSource.Rare, input.Source);
            Assert.True(input.ApplyNormalSafetyRules);
            Assert.Equal(Ring, input.Baseline!.ItemName);
        });
    }

    [Fact]
    public void CompletingBeforeTheFirstCaptureDoesNotPublishAnUnrecordedParsingContext()
    {
        using var analyzer = Analyzer(new Reads(_ => throw new InvalidOperationException("No capture expected.")));
        var completed = analyzer.CompleteSession(DateTimeOffset.UnixEpoch);
        Assert.Empty(completed.NewEvents);
        Assert.Null(completed.LootProjection);
        Assert.Null(completed.LifetimeParsingContext);
        Assert.Null(completed.TrackingResult.LifetimeParsingContext);
    }

    [Theory]
    [InlineData(7, 7, 7)]
    [InlineData(7, 8, null)]
    public async Task SpecialMissingQuantityRequiresTwoAgreeingSecondaryOcrViews(int first, int second, int? expected)
    {
        var geometry = new CompanionOcrWordGeometry(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20);
        var engine = new SecondaryReads(new([new(Material + " x" + first, .99f, geometry),
            new(Material + " x" + second, .99f, geometry)]));
        var review = new BackgroundLootRowReview(new CompanionItemMatcher([Ring, Material]), _ => engine);
        using var analyzer = Analyzer(new Reads(_ => Ocr(Material)), review: review);
        using var bitmap = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(bitmap, DateTimeOffset.UnixEpoch, default);
        var row = Assert.Single(result.Observations);
        Assert.Equal(LootSource.Rare, row.Source);
        Assert.Equal(expected, row.Quantity);
        Assert.Equal(new[] { 3, 1 }, engine.Channels);
        var diagnostic = Assert.Single(result.RowReviews);
        Assert.Equal(LootSource.Rare, diagnostic.Source);
        Assert.Equal(2, diagnostic.Readings.Count);
        if (expected is null) Assert.Empty(result.LootProjection!.Totals);
        else Assert.Equal(expected.Value, result.LootProjection!.Totals[Material]);
    }

    private static CompanionLootFrameAnalyzer Analyzer(Reads reads,
        ICompanionRareRowPipeline? rareRows = null, ILootRowReview? review = null)
    {
        var calibration = new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt",
            400, 300, 800, 600, 1, CompanionFontType.StrongSword, 0, false,
            HasRareLootAnchor: true, RareLootAnchorX: 500, RareLootAnchorY: 300);
        var context = new LifetimeParsingContext(0,
            [new(Ring, [], true), new(Material, [], false)]);
        return new(calibration, new CompanionItemMatcher([Ring, Material]), new EmptyNormalRows(), reads,
            reconciliation: new LifetimeNormalReconciliationAdapter(context, useVisualSlotCoverage: true),
            rareRowPipeline: rareRows, rowReview: review,
            rareRecovery: Create(reads), frameDecoder: new DecodedFrame(calibration),
            quantityBoundsResolver: (_, name) => name == Ring ? new DropQuantityBounds(1, 1) : null,
            specialReconciliation: new LifetimeNormalReconciliationAdapter(context, useVisualSlotCoverage: false,
                source: LootSource.Rare, slotCount: 1));
    }

    private static NormalLootRecovery Create(Reads reads)
    {
        var recovery = new NormalLootRecovery(new CompanionItemMatcher([Ring, Material]), reads, source: LootSource.Rare);
        recovery.ConfigureQuantityBounds(name => name == Ring ? new DropQuantityBounds(1, 1) : null);
        return recovery;
    }

    private static LootObservation Observation(string item, int? quantity) =>
        new(LootSource.Rare, 0, item, item, quantity, 1, 0, null, null) { NativeY = 0 };

    private static CompanionOcrResult Ocr(string text, float x = 20, float y = 35) =>
        new(text, new(CompanionOcrGeometryStatus.Success, x, y, 100, 20));

    private static Mat Band()
    {
        var band = new Mat(60, 385, MatType.CV_8UC3, new Scalar(35, 35, 35));
        Cv2.Rectangle(band, new Rect(20, 20, 200, 20), new Scalar(120, 120, 120), -1);
        return band;
    }

    private static NormalLootRecoveryBudget Budget() => new(new FrozenTime());
    private sealed class FrozenTime : TimeProvider { public override long GetTimestamp() => 0; }

    private sealed class EmptyNormalRows : ICompanionNormalRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float scale, int font, bool hdr) => new Row(y);
        public void Dispose() { }
    }

    private sealed class BlankRareRows : ICompanionRareRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float scale, int font, bool hdr) => new Row(y);
        public void Dispose() { }
    }

    private sealed class ObservingReview : ILootRowReview
    {
        public List<LootRowReviewInput> Inputs { get; } = [];
        public void ConfigureLanguage(string languageTag) { }
        public Task<LootRowReviewResult> ReviewAsync(Mat band, LootRowReviewInput input, CancellationToken token)
        {
            Inputs.Add(input);
            return Task.FromResult(new LootRowReviewResult(input.Baseline, null));
        }
        public void Dispose() { }
    }

    private sealed class SecondaryReads(Queue<SecondaryLootOcrResult> reads) : ISecondaryLootOcrRecognizer
    {
        public List<int> Channels { get; } = [];
        public string BackendName => "test-secondary";
        public string LanguageTag => "en-US";
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken token = default)
        { Channels.Add(image.Channels()); return reads.Dequeue(); }
        public void Dispose() { }
    }

    private sealed class DecodedFrame(CompanionCalibration calibration) : ICompanionBitmapDecoder
    {
        public Mat Decode(Bitmap bitmap)
        {
            var frame = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC3, new Scalar(35, 35, 35));
            var region = CompanionNormalLootGeometry.CalculateRareBandCrop(calibration);
            using var band = new Mat(frame, new Rect(region.X, region.Y, region.Width, region.Height));
            using var hsv = new Mat(1, 1, MatType.CV_8UC3, new Scalar(20, 80, 220));
            using var color = new Mat();
            Cv2.CvtColor(hsv, color, ColorConversionCodes.HSV2BGR);
            var pixel = color.At<Vec3b>(0, 0);
            Cv2.PutText(band, "Black Crystal Fragment x42", new OpenCvSharp.Point(5, 39),
                HersheyFonts.HersheySimplex, .6, new Scalar(pixel.Item0, pixel.Item1, pixel.Item2), 2, LineTypes.AntiAlias);
            return frame;
        }
    }

    private sealed class Reads(Func<int, CompanionOcrResult> read) : ICompanionNameRecognizer
    {
        public string BackendName => "test";
        public string LanguageTag => "en-US";
        public int Calls { get; private set; }
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) => read(++Calls);
    }

    private sealed class Row(int y = 0) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => true;
        public int RecognizedTextWidth => 642;
        public int TemplateQuantity => -1;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => 1;
        public Mat? NameImage => null;
        public void Dispose() { }
    }
}
