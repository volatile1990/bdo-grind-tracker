using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class PaddlePrimaryAnalyzerTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-10T15:00:00Z");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedPrimaryReadSkipsWindowsAndKeepsItsCompleteQuantity(bool toneMapped)
    {
        var rows = new Rows(250) { TemplateQuantity = 43 };
        var windows = new WindowsNames { Text = Helmet + " x43" };
        var primary = new Primary((_, source, slot, y) => Observation(source, slot, y, Helmet, 2));
        using var analyzer = Create(rows, windows, primary);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, toneMapped, toneMapped, CancellationToken.None);

        Assert.Equal(0, windows.Calls);
        Assert.Equal(6, primary.Calls.Count);
        var observed = Assert.Single(result.Observations);
        Assert.Equal(2, observed.Quantity);
        Assert.Equal(250, observed.NativeY);
        Assert.Equal(0, observed.Slot);
        Assert.Equal(LootSpotCatalog.MagaiaId, result.SpotId);
        Assert.Contains(result.RowReviews, review => review.Reason == "primary-ocr" && review.Outcome == "accepted");
        Assert.Equal(2, Assert.Single(analyzer.CompleteSession(At).NewEvents).Quantity);
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
    }

    [Fact]
    public async Task MissingDigitTemplatesDoNotTrimAnOccupiedPrimaryRow()
    {
        var rows = new Rows(0) { TemplateQuantity = -1, ProvideNameImage = false };
        var windows = new WindowsNames();
        var primary = new Primary((_, source, slot, y) => Observation(source, slot, y, Helmet, 2))
            { NormalYs = new HashSet<int> { 0 } };
        using var analyzer = Create(rows, windows, primary);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.Equal(6, primary.Calls.Count);
        Assert.Contains(primary.Calls, call => call.Y == 0);
        Assert.Equal(5, Assert.Single(result.Observations).Slot);
        Assert.Equal(0, windows.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbstentionOrReaderFailureUsesTheOriginalWindowsFallback(bool throws)
    {
        var rows = new Rows(250) { TemplateQuantity = 7 };
        var windows = new WindowsNames { Text = Helmet + " x6" };
        var primary = new Primary((_, _, _, _) =>
            throws ? throw new InvalidOperationException("test engine failure") : null);
        using var analyzer = Create(rows, windows, primary);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.Equal(6, primary.Calls.Count);
        Assert.Equal(1, windows.Calls);
        var observed = Assert.Single(result.Observations);
        Assert.Equal(6, observed.Quantity);
        Assert.Equal(250, observed.NativeY);
        Assert.Equal(0, observed.Slot);
        Assert.Null(observed.RejectionReason);
        Assert.Equal(6, Assert.Single(analyzer.CompleteSession(At).NewEvents).Quantity);
    }

    [Fact]
    public async Task WindowsFallbackStillRequiresItsOriginalGeometryGate()
    {
        var rows = new Rows(250);
        var windows = new WindowsNames { Geometry = new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0) };
        using var analyzer = Create(rows, windows, new Primary((_, _, _, _) => null));
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.Equal("ocr-geometry", Assert.Single(result.Observations).RejectionReason);
        Assert.Empty(analyzer.CompleteSession(At).NewEvents);
    }

    [Fact]
    public async Task PrimaryReaderReceivesTheOriginalNormalAndRareBands()
    {
        var calibration = Calibration() with
        { HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300 };
        var normalBounds = CompanionNormalLootGeometry.CalculateSlotCrops(calibration)[0];
        var rareBounds = CompanionNormalLootGeometry.CalculateRareBandCrop(calibration);
        var rows = new Rows(250);
        var rareRows = new RareRows();
        var windows = new WindowsNames();
        var primary = new Primary((_, source, slot, y) =>
            Observation(source, slot, y, source == LootSource.Normal ? Helmet : "JIN Origin Shard", 2));
        using var analyzer = new CompanionLootFrameAnalyzer(calibration, Matcher(), rows, windows,
            rareRowPipeline: rareRows, primaryRowReader: primary);
        using var frame = new Bitmap(800, 600);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.Black);
            using var normalBrush = new SolidBrush(Color.FromArgb(90, 60, 30));
            using var rareBrush = new SolidBrush(Color.FromArgb(180, 150, 120));
            graphics.FillRectangle(normalBrush, normalBounds);
            graphics.FillRectangle(rareBrush, rareBounds);
        }

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.Equal(0, windows.Calls);
        Assert.Equal(6, primary.Calls.Count(call => call.Source == LootSource.Normal));
        var normal = Assert.Single(primary.Calls, call => call.Source == LootSource.Normal && call.Y == 250);
        Assert.Equal((normalBounds.Width, normalBounds.Height), (normal.Width, normal.Height));
        Assert.Equal(new Vec3b(30, 60, 90), normal.Pixel);
        Assert.Equal((0, 250), (normal.Slot, normal.Y));
        var rare = Assert.Single(primary.Calls, call => call.Source == LootSource.Rare);
        Assert.Equal((rareBounds.Width, rareBounds.Height), (rare.Width, rare.Height));
        Assert.Equal(new Vec3b(120, 150, 180), rare.Pixel);
        Assert.Equal((0, 0), (rare.Slot, rare.Y));
        Assert.Equal(normalBounds, result.SlotRegions[0]);
        Assert.Equal(rareBounds, result.RareBandRegion);
        Assert.True(rareRows.Prepared!.Disposed);
    }

    [Fact]
    public async Task PrimaryResultsRetainSpotFilteringAndQuantityBounds()
    {
        var rows = new Rows(250, 200);
        var windows = new WindowsNames();
        var primary = new Primary((_, source, slot, y) =>
            Observation(source, slot, y, y == 250 ? Helmet : "Black Gem Fragment", y == 250 ? 1 : 10))
            { NormalYs = new HashSet<int> { 200, 250 } };
        using var analyzer = Create(rows, windows, primary);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.Equal(0, windows.Calls);
        Assert.Equal(LootSpotCatalog.MagaiaId, result.SpotId);
        var helmet = Assert.Single(result.Observations, row => row.ItemName == Helmet);
        Assert.Equal(1, helmet.Quantity); // Preserve the actual input for diagnostics.
        Assert.Equal(2u, helmet.QuantityBounds!.Minimum);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason,
            Assert.Single(result.Observations, row => row.ItemName == "Black Gem Fragment").RejectionReason);
        var counted = Assert.Single(analyzer.CompleteSession(At).NewEvents);
        Assert.Equal(Helmet, counted.ItemName);
        Assert.Equal(2, counted.Quantity);
    }

    [Fact]
    public async Task WindowsRecoveryAndBackgroundReviewCannotOverwriteAcceptedPaddleQuantities()
    {
        var rows = new Rows(250);
        var recovery = new ConflictingRecovery();
        var review = new ConflictingReview();
        var windows = new WindowsNames { Text = Helmet + " x43" };
        var primary = new Primary((_, source, slot, y) => Observation(source, slot, y, Helmet, 2));
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration(), Matcher(), rows, windows,
            normalRecovery: recovery, rowReview: review, primaryRowReader: primary);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.Equal(2, Assert.Single(result.Observations).Quantity);
        Assert.DoesNotContain(250, recovery.Ys);
        Assert.Empty(review.Inputs);
        Assert.Equal(0, windows.Calls);
        Assert.Equal(2, Assert.Single(analyzer.CompleteSession(At).NewEvents).Quantity);
    }

    [Theory]
    [InlineData(LootSource.Normal)]
    [InlineData(LootSource.Rare)]
    public async Task PaddleCanReadALegacyBlankBandWithoutWindowsOrRecoveryOverwritingIt(LootSource source)
    {
        // The real 43/45 failures were IsBlank=true with no prepared NameImage.
        // The native band still contained legible text, so this gate must not hide it from Paddle.
        var calibration = Calibration() with
        { HasRareLootAnchor = source == LootSource.Rare, RareLootAnchorX = 600, RareLootAnchorY = 300 };
        var rows = new Rows();
        var rareRows = new RareRows { IsBlank = true };
        var windows = new WindowsNames();
        var recovery = new ConflictingRecovery();
        var review = new ConflictingReview();
        var expectedY = source == LootSource.Normal ? 150 : 0;
        var expectedQuantity = source == LootSource.Normal ? 4 : 1;
        var primary = new Primary((_, channel, slot, y) =>
            Observation(channel, slot, y, source == LootSource.Normal ? Helmet : "JIN Origin Shard", expectedQuantity))
            { NormalYs = source == LootSource.Normal ? new HashSet<int> { 150 } : new HashSet<int>() };
        using var analyzer = new CompanionLootFrameAnalyzer(calibration, Matcher(), rows, windows,
            rareRowPipeline: source == LootSource.Rare ? rareRows : null,
            normalRecovery: recovery, rowReview: review, primaryRowReader: primary);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.All(rows.Prepared, row => { Assert.True(row.IsBlank); Assert.Null(row.NameImage); });
        if (source == LootSource.Rare) Assert.True(rareRows.Prepared!.IsBlank);
        var observed = Assert.Single(result.Observations);
        Assert.Equal(source, observed.Source);
        Assert.Equal(expectedY, observed.NativeY);
        Assert.Equal(expectedQuantity, observed.Quantity);
        Assert.Equal(0, windows.Calls);
        Assert.Empty(review.Inputs);
        if (source == LootSource.Normal) Assert.DoesNotContain(expectedY, recovery.Ys);
        Assert.Equal(expectedQuantity, Assert.Single(analyzer.CompleteSession(At).NewEvents).Quantity);
        Assert.Contains(result.RowReviews, diagnostic => diagnostic.Source == source &&
            diagnostic.NativeY == expectedY && diagnostic.Outcome == "accepted");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlankPaddleAbstentionsPreserveTheExistingWindowsBlankGate(bool includeRare)
    {
        var calibration = Calibration() with
        { HasRareLootAnchor = includeRare, RareLootAnchorX = 600, RareLootAnchorY = 300 };
        var rows = new Rows();
        var windows = new WindowsNames();
        var primary = new Primary((_, _, _, _) => null) { NormalYs = new HashSet<int>() };
        using var analyzer = new CompanionLootFrameAnalyzer(calibration, Matcher(), rows, windows,
            rareRowPipeline: includeRare ? new RareRows { IsBlank = true } : null, primaryRowReader: primary);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, At, CancellationToken.None);

        Assert.Equal(includeRare ? 7 : 6, primary.Calls.Count);
        Assert.Equal(primary.Calls.Count, result.RowReviews.Count);
        Assert.All(result.RowReviews, diagnostic => Assert.Empty(diagnostic.Readings));
        Assert.Equal(0, result.OcrRowCount); // Empty attempts do not fabricate OCR readings.
        Assert.Equal(0, windows.Calls);
        Assert.Empty(result.Observations);
        Assert.Empty(analyzer.CompleteSession(At).NewEvents);
    }

    [Fact]
    public async Task LanguageChangesResetSpotSelectionAndDisposeHasSingleOwnership()
    {
        var rows = new Rows(250);
        var windows = new WindowsNames();
        var item = Helmet;
        var primary = new Primary((_, source, slot, y) => Observation(source, slot, y, item, 2));
        var analyzer = new CompanionLootFrameAnalyzer(Calibration(), Matcher(), rows, windows,
            configureGameLanguage: _ => windows.LanguageTag = "de-DE", primaryRowReader: primary);
        using var frame = new Bitmap(800, 600);
        try
        {
            Assert.Equal("en-US", Assert.Single(primary.Languages));
            Assert.Equal(LootSpotCatalog.MagaiaId,
                (await analyzer.AnalyzeAsync(frame, At, CancellationToken.None)).SpotId);
            analyzer.CompleteSession(At);
            analyzer.ConfigureGameLanguage("German");
            Assert.Equal("de-DE", primary.Languages.Last());
            analyzer.Reset();
            item = "Branch of Abundance";
            var next = await analyzer.AnalyzeAsync(frame, At.AddSeconds(1), CancellationToken.None);
            Assert.Equal(LootSpotCatalog.AphrodonId, next.SpotId);
            Assert.Null(Assert.Single(next.Observations).RejectionReason);
        }
        finally
        {
            analyzer.Dispose();
            analyzer.Dispose();
        }
        Assert.Equal(1, primary.DisposeCalls);
        Assert.True(rows.Disposed);
    }

    [Fact]
    public async Task CancellationDuringPrimaryRecognitionDoesNotFallBackToWindows()
    {
        var rows = new Rows(250);
        var windows = new WindowsNames();
        using var cancellation = new CancellationTokenSource();
        var primary = new Primary((_, _, _, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        using var analyzer = Create(rows, windows, primary);
        using var frame = new Bitmap(800, 600);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            analyzer.AnalyzeAsync(frame, At, cancellation.Token));

        Assert.Equal(0, windows.Calls);
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
    }

    private static CompanionLootFrameAnalyzer Create(Rows rows, WindowsNames windows, Primary primary) =>
        new(Calibration(), Matcher(), rows, windows, primaryRowReader: primary);

    private static CompanionCalibration Calibration() => new("profile", "gamevariable.xml", "GameOption.txt",
        400, 300, 800, 600, 1f, CompanionFontType.StrongSword, 0, false);

    private static CompanionItemMatcher Matcher() => new(new[]
        { Helmet, "JIN Origin Shard", "Black Gem Fragment", "Branch of Abundance" }
        .Concat(LootSpotCatalog.SharedGlobalItems));

    private static LootObservation Observation(LootSource source, int slot, int y, string name, int quantity) =>
        new(source, slot, $"{name} x{quantity}", name, quantity, 1, .99, null, null) { NativeY = y };

    private sealed record BandCall(LootSource Source, int Slot, int Y, int Width, int Height, Vec3b Pixel);

    private sealed class Primary(Func<Mat, LootSource, int, int, LootObservation?> read) : ILootPrimaryRowReader
    {
        // Independent of the legacy prepared-row blank mask: unlisted bands abstain.
        public IReadOnlySet<int> NormalYs { get; init; } = new HashSet<int> { 250 };
        public List<BandCall> Calls { get; } = [];
        public List<string> Languages { get; } = [];
        public int DisposeCalls { get; private set; }
        public void ConfigureLanguage(string languageTag) => Languages.Add(languageTag);
        public PrimaryReadResult Read(Mat originalBand, LootSource source, int slot, int nativeY, double uiScale,
            Func<string, DropQuantityBounds?> bounds, Func<string, bool> allows, CancellationToken cancellationToken)
        {
            Calls.Add(new(source, slot, nativeY, originalBand.Width, originalBand.Height, originalBand.At<Vec3b>(0, 0)));
            var configured = source == LootSource.Rare || NormalYs.Contains(nativeY);
            var observation = configured ? read(originalBand, source, slot, nativeY) : null;
            return new(observation, new(source, nativeY, "primary-ocr", "test-paddle", Languages.Last(),
                observation is not null ? "accepted" : configured ? "no-consensus" : "no-image-information",
                1, null, observation, [], 0));
        }
        public void Dispose() => DisposeCalls++;
    }

    private sealed class Rows(params int[] occupiedYs) : ICompanionNormalRowPipeline
    {
        public int TemplateQuantity { get; init; } = 4;
        public bool ProvideNameImage { get; init; } = true;
        public List<Prepared> Prepared { get; } = [];
        public bool Disposed { get; private set; }
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr)
        {
            var row = new Prepared(y, !occupiedYs.Contains(y), TemplateQuantity, ProvideNameImage);
            Prepared.Add(row);
            return row;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Prepared(int y, bool blank, int template, bool provideNameImage) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => blank ? -1 : template;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => .99f;
        public float NameScale => 1;
        public Mat? NameImage { get; } = blank || !provideNameImage ? null : new Mat(1, 1, MatType.CV_8UC1, new Scalar(77));
        public bool Disposed { get; private set; }
        public void Dispose() { NameImage?.Dispose(); Disposed = true; }
    }

    private sealed class WindowsNames : ICompanionNameRecognizer
    {
        public string BackendName => "test-windows";
        public string LanguageTag { get; set; } = "en-US";
        public string Text { get; init; } = Helmet + " x4";
        public CompanionOcrWordGeometry Geometry { get; init; } = new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20);
        public int Calls { get; private set; }
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            Assert.Equal(77, image.At<byte>(0, 0));
            Calls++;
            return new(Text, Geometry);
        }
    }

    private sealed class RareRows : ICompanionRareRowPipeline
    {
        public bool IsBlank { get; init; }
        public Prepared? Prepared { get; private set; }
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            Prepared = new(y, IsBlank, 1, true);
        public void Dispose() { }
    }

    private sealed class ConflictingRecovery : INormalLootRecovery
    {
        public List<int> Ys { get; } = [];
        public LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original, LootObservation? baseline,
            int slot, float uiScale, NormalLootRecoveryBudget budget, CancellationToken cancellationToken)
        {
            Ys.Add(original.Y);
            return baseline is null ? null : Observation(LootSource.Normal, slot, original.Y, Helmet, 99);
        }
    }

    private sealed class ConflictingReview : ILootRowReview
    {
        public List<LootRowReviewInput> Inputs { get; } = [];
        public void ConfigureLanguage(string languageTag) { }
        public Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input, CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            return Task.FromResult(new LootRowReviewResult(
                Observation(input.Source, input.Slot, input.NativeY, Helmet, 99), null));
        }
        public void Dispose() { }
    }
}
