using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeRawAnalyzerIntegrationTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string ForeignTrash = "Branch of Abundance";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(true, 6, 6)]
    [InlineData(false, 6, 1)]
    public async Task MissingTemplatesStillExposeEveryRawSlotWhileHistoricalModeKeepsItsTrim(
        bool rawMode, int prepared, int primaryReads)
    {
        var rows = FiveDropsAndAnOlderPoisonRow();
        using var analyzer = Analyzer(rows, rawMode);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);

        Assert.Equal(prepared, result.PreparedRowCount);
        Assert.Equal(primaryReads, rows.ReadY.Count);
        Assert.Equal(primaryReads, result.Observations.Count);
        Assert.Equal(primaryReads, result.OcrRowCount);
        Assert.All(result.Observations, observation =>
        {
            // The existing geometry gate still rejects these primary reads.
            // Only lifetime-v2's separate text interpretation may recover them.
            Assert.Equal("ocr-geometry", observation.RejectionReason);
            Assert.Null(observation.ItemName);
            Assert.Null(observation.Quantity);
        });
        if (rawMode)
        {
            Assert.Equal(new[] { 0, 50, 100, 150, 200, 250 }, rows.ReadY);
            Assert.Equal(20, result.LootProjection!.Totals[Helmet]);
            Assert.Equal(5, result.LootProjection.ConfirmedDropCount);
            Assert.Equal(LootSpotCatalog.MagaiaId, result.SpotId);
            Assert.Contains("+lifetime-v2", result.VariantName);
        }
        else
        {
            Assert.Equal(new[] { 250 }, rows.ReadY);
            Assert.Empty(result.LootProjection!.Totals);
            Assert.Null(result.SpotId);
            Assert.Contains("+lifetime-v1", result.VariantName);
        }
    }

    [Fact]
    public async Task OldestOcrCropRemainsDiagnosableWithoutCountingItAsASixthModelSlot()
    {
        var rows = FiveDropsAndAnOlderPoisonRow();
        var recovery = new Recovery();
        var review = new Review();
        using var analyzer = Analyzer(rows, true, recovery, review);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);

        // Six crops preserve the established OCR/recovery work order. The
        // deliberately readable old sixth row must never enter the five-slot model.
        Assert.Contains(0, rows.PreparedY);
        Assert.Contains(0, rows.ReadY);
        Assert.NotEmpty(recovery.Slots);
        Assert.All(recovery.Slots, slot => Assert.InRange(slot, 0, 5));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, review.Inputs.Select(input => input.Slot).Order());
        Assert.All(review.Inputs, input =>
        {
            Assert.True(input.NativeY >= 0);
            Assert.False(input.ReviewMissingAlignmentAnchor);
        });
        Assert.Equal(20, result.LootProjection!.Totals[Helmet]);
        Assert.False(result.LootProjection.Totals.ContainsKey("Black Stone"));
    }

    [Fact]
    public async Task FrameAndTrackingSnapshotsCarryTheAppliedSpotContextAndResetRestoresFullCatalog()
    {
        var rows = FiveDropsAndAnOlderPoisonRow();
        using var analyzer = Analyzer(rows, true);
        using var frame = new Bitmap(800, 600);

        // Five distinct raw-only drops establish the same spot evidence as
        // accepted primary readings. Their initial interpretation used all items.
        var first = await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        Assert.Equal(LootSpotCatalog.MagaiaId, first.SpotId);
        Assert.Equal(0, first.LifetimeParsingContext!.Revision);
        Assert.Contains(first.LifetimeParsingContext.Catalog, item => item.Name == ForeignTrash);

        var second = await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(200), CancellationToken.None);
        Assert.Equal(LootSpotCatalog.MagaiaId, second.SpotId);
        Assert.Equal(1, second.LifetimeParsingContext!.Revision);
        Assert.DoesNotContain(second.LifetimeParsingContext.Catalog, item => item.Name == ForeignTrash);
        Assert.Equal(second.LifetimeParsingContext.Revision, second.TrackingResult.LifetimeParsingContext!.Revision);
        Assert.True(second.LifetimeParsingContext.HasSameCatalog(second.TrackingResult.LifetimeParsingContext));
        Assert.Equal(second.LootProjection, second.TrackingResult.LootProjection);

        var complete = analyzer.CompleteSession(Start.AddMilliseconds(300));
        Assert.Equal(second.LifetimeParsingContext.Revision, complete.LifetimeParsingContext!.Revision);
        Assert.True(complete.LifetimeParsingContext.HasSameCatalog(complete.TrackingResult.LifetimeParsingContext!));
        analyzer.Reset();
        rows.PresentY = [250];
        var restarted = await analyzer.AnalyzeAsync(frame, Start.AddSeconds(1), CancellationToken.None);
        Assert.Null(restarted.SpotId);
        Assert.Equal(0, restarted.LifetimeParsingContext!.Revision);
        Assert.Contains(restarted.LifetimeParsingContext.Catalog, item => item.Name == ForeignTrash);
        Assert.Equal(4, restarted.LootProjection!.Totals[Helmet]);
    }

    private static Rows FiveDropsAndAnOlderPoisonRow() => new();

    [Fact]
    public async Task VisualModePreservesRawOcrRecoveryAndReviewInputsIncludingTheSixthCrop()
    {
        var originalRows = FiveDropsAndAnOlderPoisonRow();
        var visualRows = FiveDropsAndAnOlderPoisonRow();
        var originalRecovery = new Recovery();
        var visualRecovery = new Recovery();
        var originalReview = new Review();
        var visualReview = new Review();
        using var original = Analyzer(originalRows, true, originalRecovery, originalReview);
        using var visual = Analyzer(visualRows, true, visualRecovery, visualReview, visualCoverage: true);
        using var frame = new Bitmap(800, 600);
        for (var index = 0; index < 8; index++)
        {
            var at = Start.AddMilliseconds(index * 200);
            var before = await original.AnalyzeAsync(frame, at, CancellationToken.None);
            var after = await visual.AnalyzeAsync(frame, at, CancellationToken.None);
            Assert.Equal(before.Observations, after.Observations.Select(row => row with { OccupancyEvidence = null }));
            Assert.Equal(before.PreparedRowCount, after.PreparedRowCount);
            Assert.Equal(6, after.PreparedRowCount);
            Assert.Equal(before.OcrRowCount, after.OcrRowCount);
            Assert.Equal(before.SlotRegions, after.SlotRegions);
            Assert.Contains("+lifetime-v3+visual-occupancy-v1", after.VariantName);
            Assert.DoesNotContain("alignment-review-v1", after.VariantName);
            Assert.DoesNotContain("visual-appearance-v1", after.VariantName);
        }
        Assert.Equal(originalRows.PreparedY, visualRows.PreparedY);
        Assert.Equal(originalRows.ReadY, visualRows.ReadY);
        Assert.Equal(originalRecovery.Slots, visualRecovery.Slots);
        Assert.Equal(originalReview.Inputs.Count, visualReview.Inputs.Count);
        foreach (var (before, after) in originalReview.Inputs.Zip(visualReview.Inputs))
        {
            Assert.Equal(before.Baseline, after.Baseline);
            Assert.Equal((before.Source, before.Slot, before.NativeY, before.TemplateQuantity,
                    before.TemplateScore, before.UiScale, before.ReviewMissingAlignmentAnchor),
                (after.Source, after.Slot, after.NativeY, after.TemplateQuantity,
                    after.TemplateScore, after.UiScale, after.ReviewMissingAlignmentAnchor));
            Assert.Equal(before.PrimaryQuantityReads, after.PrimaryQuantityReads);
            Assert.Equal(before.QuantityAnomaly, after.QuantityAnomaly);
            foreach (var name in new[] { Helmet, "Ancient Spirit Dust", ForeignTrash, "Black Stone" })
            {
                Assert.Equal(before.Bounds(name), after.Bounds(name));
                Assert.Equal(before.Allows(name), after.Allows(name));
            }
        }
    }

    private static CompanionLootFrameAnalyzer Analyzer(Rows rows, bool raw,
        INormalLootRecovery? recovery = null, ILootRowReview? review = null, bool visualCoverage = false)
    {
        string[] names = [Helmet, "Ancient Spirit Dust", ForeignTrash, "Black Stone"];
        var calibration = new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt",
            400, 300, 800, 600, 1, CompanionFontType.StrongSword, 0, false);
        var context = raw ? new LifetimeParsingContext(0,
            names.Select(name => new LifetimeParsingCatalogEntry(name, [])).ToArray()) : null;
        return new(calibration, new CompanionItemMatcher(names), rows, new Names(rows),
            new LifetimeNormalReconciliationAdapter(context, visualCoverage), normalRecovery: recovery, rowReview: review,
            quantityBoundsResolver: (_, _) => new(1, 1000));
    }

    private sealed class Rows : ICompanionNormalRowPipeline
    {
        public HashSet<int> PresentY { get; set; } = [0, 50, 100, 150, 200, 250];
        public List<int> PreparedY { get; } = [];
        public List<int> ReadY { get; } = [];
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr)
        {
            PreparedY.Add(y);
            return new Row(y, !PresentY.Contains(y));
        }
        public void Dispose() { }
    }

    private sealed class Row(int y, bool blank) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 300;
        public int TemplateQuantity => -1;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => 1;
        public Mat? NameImage { get; } = blank ? null : new Mat(1, 1, MatType.CV_8UC1, new Scalar(y / 50 + 1));
        public void Dispose() => NameImage?.Dispose();
    }

    private sealed class Names(Rows rows) : ICompanionNameRecognizer
    {
        public string BackendName => "integration-test";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            var y = (image.At<byte>(0, 0) - 1) * 50;
            rows.ReadY.Add(y);
            return new(y == 0 ? "Black Stone x9999" : Helmet + " x4",
                new(CompanionOcrGeometryStatus.Success, 1000, 30, 100, 20));
        }
    }

    private sealed class Recovery : INormalLootRecovery
    {
        public List<int> Slots { get; } = [];
        public LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original,
            LootObservation? baseline, int slot, float uiScale,
            NormalLootRecoveryBudget budget, CancellationToken cancellationToken)
        {
            Slots.Add(slot);
            budget.TryBeginOcr();
            return baseline;
        }
    }

    private sealed class Review : ILootRowReview
    {
        public List<LootRowReviewInput> Inputs { get; } = [];
        public void ConfigureLanguage(string languageTag) { }
        public Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input,
            CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            return Task.FromResult(new LootRowReviewResult(input.Baseline, null));
        }
        public void Dispose() { }
    }
}
