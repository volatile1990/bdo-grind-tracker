using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class TrashQuantityAnomalyAnalyzerTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-10T15:00:00Z");

    [Theory]
    [InlineData("*Eli n Fo ldwur'sHelmet x 43", 4, "anomaly-quantity-corrected")]
    [InlineData("Elion Follower's Helmet x45", 4, "anomaly-quantity-corrected")]
    [InlineData("Elion Follower's Helmet x60", 60, "baseline-confirmed")]
    [InlineData("Elion Follower's Helmet x338", 338, "baseline-confirmed")]
    public async Task LearnedAnomaliesReachTheRealReviewAndOnlyPlausibleConsensusChangesTheCounterInput(
        string windowsText, int paddleQuantity, string outcome)
    {
        var windows = new WindowsReader();
        var rows = new Rows();
        var counter = new Counter { Next = EightCountedDrops() };
        var paddle = new SecondaryReader { Text = Helmet + " x" + paddleQuantity };
        var review = new BackgroundLootRowReview(Matcher(), _ => paddle);
        using var analyzer = Create(rows, windows, counter, review);
        using var frame = NonUniformFrame();
        await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        Assert.Equal(0, paddle.Calls); // Ordinary x4 does not need a second OCR pass.
        windows.NormalText = windowsText;

        var result = await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(470), CancellationToken.None);

        var diagnostic = Assert.Single(result.RowReviews);
        Assert.Equal("trash-quantity-anomaly", diagnostic.Reason);
        Assert.Equal("history", diagnostic.QuantityAnomaly!.Basis);
        Assert.Equal(4, diagnostic.QuantityAnomaly.TypicalQuantity);
        Assert.Equal(8, diagnostic.QuantityAnomaly.SampleCount);
        Assert.Equal(outcome, diagnostic.Outcome);
        Assert.Equal(2, paddle.Calls);
        Assert.Equal(2, diagnostic.Readings.Count);
        Assert.Equal(paddleQuantity, Assert.Single(result.Observations).Quantity);
        Assert.Equal((uint)paddleQuantity, Assert.Single(counter.InputFrames.Last()).Count);
        Assert.Equal(2, windows.Calls);
        if (windowsText.StartsWith('*')) Assert.True(diagnostic.Before!.NameConfidence < .8);
    }

    [Fact]
    public async Task RepeatedVisibleFramesDoNotBecomeRepeatedHistoryVotes()
    {
        var windows = new WindowsReader();
        var counter = new Counter { Next = [Counted(4)] };
        var review = new CaptureReview();
        using var analyzer = Create(new Rows(), windows, counter, review);
        using var frame = NonUniformFrame();
        for (var index = 0; index < 12; index++)
            await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(index * 470), CancellationToken.None);
        windows.NormalText = Helmet + " x43";

        await analyzer.AnalyzeAsync(frame, Start.AddSeconds(6), CancellationToken.None);

        var anomaly = review.Inputs.Last().QuantityAnomaly;
        Assert.NotNull(anomaly);
        Assert.Equal("catalog", anomaly.Basis);
        Assert.Equal(0, anomaly.SampleCount);
        Assert.Equal(2, anomaly.TypicalQuantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EstimatesAndQuantityRevisionsDoNotTrainTheTypicalDrop(bool revision)
    {
        var windows = new WindowsReader();
        var counter = new Counter
        {
            Next = EightCountedDrops().Select(entry => revision
                ? entry with { Revision = 1, QuantityDelta = 2 }
                : entry with { IsMinimumQuantityEstimate = true }).ToArray(),
        };
        var review = new CaptureReview();
        using var analyzer = Create(new Rows(), windows, counter, review);
        using var frame = NonUniformFrame();
        await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        windows.NormalText = Helmet + " x43";

        await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(470), CancellationToken.None);

        Assert.Equal("catalog", review.Inputs.Last().QuantityAnomaly!.Basis);
        Assert.Equal(0, review.Inputs.Last().QuantityAnomaly!.SampleCount);
    }

    [Fact]
    public async Task FinalizedBatchDropsAreLearnedAndSessionResetClearsTheHistory()
    {
        var windows = new WindowsReader();
        var counter = new Counter { OnComplete = EightCountedDrops() };
        var review = new CaptureReview();
        using var analyzer = Create(new Rows(), windows, counter, review);
        using var frame = NonUniformFrame();
        await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        analyzer.CompleteSession(Start);
        windows.NormalText = Helmet + " x45";
        await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(470), CancellationToken.None);
        Assert.Equal("history", review.Inputs.Last().QuantityAnomaly!.Basis);
        Assert.Equal(8, review.Inputs.Last().QuantityAnomaly!.SampleCount);

        analyzer.Reset();
        await analyzer.AnalyzeAsync(frame, Start.AddSeconds(2), CancellationToken.None);

        Assert.Equal("catalog", review.Inputs.Last().QuantityAnomaly!.Basis);
        Assert.Equal(0, review.Inputs.Last().QuantityAnomaly!.SampleCount);
    }

    [Fact]
    public async Task DisablingTheExperimentSuppliesNoAnomalyFlag()
    {
        var windows = new WindowsReader();
        var counter = new Counter { Next = EightCountedDrops() };
        var review = new CaptureReview();
        using var analyzer = Create(new Rows(), windows, counter, review, enabled: false);
        using var frame = NonUniformFrame();
        await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        windows.NormalText = Helmet + " x43";

        var result = await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(470), CancellationToken.None);

        Assert.Null(review.Inputs.Last().QuantityAnomaly);
        Assert.Equal(43, Assert.Single(result.Observations).Quantity);
        Assert.DoesNotContain("trash-quantity-anomaly", result.VariantName);
        Assert.Equal(2, windows.Calls);
    }

    [Theory]
    [InlineData(LootSource.Normal)]
    [InlineData(LootSource.Rare)]
    public async Task TheLearnedTrashHistoryNeverFlagsNonTrashOrRareRows(LootSource source)
    {
        var windows = new WindowsReader();
        var rows = new Rows();
        var rareRows = new RareRows();
        var counter = new Counter { Next = EightCountedDrops() };
        var review = new CaptureReview();
        using var analyzer = Create(rows, windows, counter, review, rareRows: source == LootSource.Rare ? rareRows : null);
        using var frame = NonUniformFrame();
        await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        if (source == LootSource.Normal) windows.NormalText = "Black Stone x43";
        else
        {
            rows.Visible = false;
            rareRows.Visible = true;
            windows.RareText = Helmet + " x43";
        }
        review.Inputs.Clear();

        await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(470), CancellationToken.None);

        var input = Assert.Single(review.Inputs);
        Assert.Equal(source, input.Source);
        Assert.Null(input.QuantityAnomaly);
    }

    private static CompanionRecognizedEntry[] EightCountedDrops() =>
        Enumerable.Range(0, 8).Select(_ => Counted(4)).ToArray();

    private static CompanionRecognizedEntry Counted(uint quantity) =>
        new(Helmet, quantity, 250) { EventId = Guid.NewGuid(), TotalDropQuantity = (int)quantity };

    private static CompanionLootFrameAnalyzer Create(Rows rows, WindowsReader windows, Counter counter,
        ILootRowReview review, bool enabled = true, RareRows? rareRows = null)
    {
        var calibration = new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt",
            400, 300, 800, 600, 1f, CompanionFontType.StrongSword, 0, false)
        { HasRareLootAnchor = rareRows is not null, RareLootAnchorX = 600, RareLootAnchorY = 300 };
        return new(calibration, Matcher(), rows, windows, reconciliation: counter,
            rareRowPipeline: rareRows, rowReview: review, reviewTrashQuantityAnomalies: enabled);
    }

    private static CompanionItemMatcher Matcher() => new(new[] { Helmet, "Black Stone" });

    private static Bitmap NonUniformFrame()
    {
        var frame = new Bitmap(800, 600);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.Black);
        graphics.FillRectangle(Brushes.White, 300, 415, 90, 20);
        return frame;
    }

    private sealed class Rows : ICompanionNormalRowPipeline
    {
        public bool Visible { get; set; } = true;
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            new Prepared(y, !Visible || y != 250, 77);
        public void Dispose() { }
    }

    private sealed class RareRows : ICompanionRareRowPipeline
    {
        public bool Visible { get; set; }
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            new Prepared(y, !Visible, 88);
        public void Dispose() { }
    }

    private sealed class Prepared(int y, bool blank, byte marker) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => blank ? -1 : 4;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => .99f;
        public float NameScale => 1;
        public Mat? NameImage { get; } = blank ? null : new Mat(1, 1, MatType.CV_8UC1, new Scalar(marker));
        public void Dispose() => NameImage?.Dispose();
    }

    private sealed class WindowsReader : ICompanionNameRecognizer
    {
        public string BackendName => "test-windows";
        public string LanguageTag => "en-US";
        public string NormalText { get; set; } = Helmet + " x4";
        public string RareText { get; set; } = Helmet + " x4";
        public int Calls { get; private set; }
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            Calls++;
            return new(image.At<byte>(0, 0) == 88 ? RareText : NormalText,
                new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
        }
    }

    private sealed class Counter : ICompanionReconciliation
    {
        public CompanionRecognizedEntry[] Next { get; set; } = [];
        public CompanionRecognizedEntry[] OnComplete { get; set; } = [];
        public List<IReadOnlyList<CompanionRecognizedEntry>> InputFrames { get; } = [];
        public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries)
        {
            InputFrames.Add(entries);
            var next = Next;
            Next = [];
            return next;
        }
        public IReadOnlyList<CompanionRecognizedEntry> Complete()
        {
            var next = OnComplete;
            OnComplete = [];
            return next;
        }
        public void Reset() { Next = []; OnComplete = []; InputFrames.Clear(); }
    }

    private sealed class CaptureReview : ILootRowReview
    {
        public List<LootRowReviewInput> Inputs { get; } = [];
        public void ConfigureLanguage(string languageTag) { }
        public Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input, CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            return Task.FromResult(new LootRowReviewResult(input.Baseline, null));
        }
        public void Dispose() { }
    }

    private sealed class SecondaryReader : ISecondaryLootOcrRecognizer
    {
        public string BackendName => "test-paddle";
        public string LanguageTag => "en-US";
        public string Text { get; init; } = Helmet + " x4";
        public int Calls { get; private set; }
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new(Text, .99f, new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
        }
        public void Dispose() { }
    }
}
