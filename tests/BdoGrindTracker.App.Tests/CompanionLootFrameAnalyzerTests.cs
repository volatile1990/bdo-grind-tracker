using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    [Fact]
    public async Task LostPanelPositionStopsTheRealAnalyzerBeforeReadingAnyMoreRows()
    {
        var readable = true;
        var rows = new Rows();
        var calibration = Calibration();
        var guard = new LootPanelCaptureGuard(calibration, () =>
            readable ? calibration : throw new InvalidDataException("main panel hidden"));
        using var analyzer = new CompanionLootFrameAnalyzer(calibration, Matcher(), rows, new Names(rows),
            captureGuard: guard);
        using var frame = new Bitmap(800, 600);
        var first = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Empty(first.NewEvents);
        Assert.True(analyzer.IsAvailable);
        var rowCount = rows.ProcessedY.Count;
        readable = false;
        await Assert.ThrowsAsync<LootPanelUnavailableException>(() =>
            analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch.AddSeconds(2), CancellationToken.None));
        Assert.Equal(rowCount, rows.ProcessedY.Count);
        Assert.False(analyzer.IsAvailable);
        Assert.Equal(LootPanelCaptureGuard.MissingPanelMessage, analyzer.Status);
        analyzer.Reset();
        Assert.False(analyzer.IsAvailable);
    }

    [Theory]
    [InlineData("Black Crystal Fragment x0", 0)]
    [InlineData("Black Crystal Fragment x00", -1)]
    [InlineData("Black Crystal Fragment", 0)]
    public async Task ZeroQuantityUsesMissingQuantityPathAcrossBatchAndResume(string text, int template)
    {
        var rows = new Rows(new Input(250, text, template));
        var missingRows = new Rows(new Input(250, "Black Crystal Fragment", -1));
        using var analyzer = Create(rows);
        using var missing = Create(missingRows);
        using var frame = new Bitmap(800, 600);
        var emitted = new List<LootEventView>();
        for (var index = 0; index < CompanionFrameReconciler.BatchSize; index++)
        {
            var now = DateTimeOffset.UnixEpoch.AddMilliseconds(index * 450);
            var result = await analyzer.AnalyzeAsync(frame, now, CancellationToken.None);
            var expected = await missing.AnalyzeAsync(frame, now, CancellationToken.None);
            Assert.Null(Assert.Single(result.Observations).Quantity);
            Assert.Equal(expected.NewEvents.Select(e => (e.ItemName, e.Quantity)),
                result.NewEvents.Select(e => (e.ItemName, e.Quantity)));
            emitted.AddRange(result.NewEvents);
        }
        Assert.NotEmpty(emitted);
        Assert.All(emitted, entry => Assert.True(entry.Quantity > 0));
        Assert.Equal(missing.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents.Select(e => (e.ItemName, e.Quantity)),
            analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents.Select(e => (e.ItemName, e.Quantity)));

        rows.Values = [new(250, "Black Crystal Fragment x6", 6)];
        await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch.AddSeconds(10), CancellationToken.None);
        Assert.Equal(6, Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(11)).NewEvents).Quantity);
    }

    [Fact]
    public async Task ZeroOcrDoesNotReplaceAValidPositiveTemplate()
    {
        var rows = new Rows(new Input(250, "Black Crystal Fragment x0", 6));
        using var analyzer = Create(rows);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        Assert.Equal(6, Assert.Single(result.Observations).Quantity);
        Assert.Equal(6, Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents).Quantity);
    }

    [Fact]
    public async Task RareZeroOcrUsesExistingUnitFallbackWithoutCrashingLedger()
    {
        var rows = new Rows { RareText = "BON Origin Shard x0" };
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
        {
            HasRareLootAnchor = true,
            RareLootAnchorX = 600,
            RareLootAnchorY = 300,
        }, Matcher(), rows, new Names(rows), rareRowPipeline: new RareRows());
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        var completed = analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(1, Assert.Single(result.Observations).Quantity);
        Assert.Equal(1, Assert.Single(completed.NewEvents).Quantity);
    }

    public static IEnumerable<object[]> GlobalDropsBySpotAndChannel()
    {
        foreach (var trash in new[] { "Branch of Abundance", "Black Crystal Fragment", "Elion Follower's Helmet" })
            foreach (var item in LootSpotCatalog.SharedGlobalItems)
                foreach (var source in new[] { LootSource.Normal, LootSource.Rare })
                    yield return [trash, item, source];
    }

    [Theory]
    [MemberData(nameof(GlobalDropsBySpotAndChannel))]
    public async Task GlobalLootSurvivesAlreadyLockedSpotInBothChannels(
        string trash, string item, LootSource source)
    {
        var rows = new Rows(new Input(250, trash, 17));
        var calibration = Calibration();
        using var analyzer = new CompanionLootFrameAnalyzer(
            source == LootSource.Rare ? calibration with
            {
                HasRareLootAnchor = true,
                RareLootAnchorX = 600,
                RareLootAnchorY = 300,
            } : calibration, Matcher(), rows, new Names(rows),
            rareRowPipeline: source == LootSource.Rare ? new RareRows() : null);
        using var frame = new Bitmap(800, 600);
        rows.RareText = "";
        var initial = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.NotNull(initial.SpotId);
        rows.Values = source == LootSource.Normal ? [new(250, item, 3)] : [];
        rows.RareText = source == LootSource.Rare ? item + " x3" : "";
        var next = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch.AddMilliseconds(450), CancellationToken.None);
        Assert.Equal(initial.SpotId, next.SpotId);
        var observation = Assert.Single(next.Observations);
        Assert.Equal(source, observation.Source);
        Assert.Equal(item, observation.ItemName);
        Assert.Null(observation.RejectionReason);
        var completed = analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1));
        var counted = Assert.Single(completed.NewEvents, result => result.ItemName == item);
        var expected = item == "Pure Black Stone" || item == "Laila's Petal" && trash == "Elion Follower's Helmet" ? 1 : 3;
        Assert.Equal(expected, counted.Quantity);
    }

    [Theory]
    [InlineData("Branch of Abundance", LootSpotCatalog.AphrodonId)]
    [InlineData("Black Crystal Fragment", LootSpotCatalog.HermesiaId)]
    [InlineData("Elion Follower's Helmet", LootSpotCatalog.MagaiaId)]
    [InlineData("Black CrystaI Fragment", LootSpotCatalog.HermesiaId)]
    public async Task TrashSelectsSpotAndSingleReadSurvivesCompletion(string text, string spotId)
    {
        var rows = new Rows(new Input(250, text, 17));
        using var analyzer = Create(rows);
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(spotId, result.SpotId);
        Assert.Empty(result.NewEvents); // native batch, not another observation requirement
        var observation = Assert.Single(result.Observations);
        Assert.Null(observation.RejectionReason);
        Assert.Equal(250, observation.NativeY);
        Assert.Equal(0, observation.Slot);
        Assert.Equal(17, Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents).Quantity);
        Assert.Empty(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents);
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
    }

    [Fact]
    public async Task UnknownSpotDoesNotBlockOriginalMatcher()
    {
        using var analyzer = Create(new Rows(new Input(250, "Black Gem Fragment x10")));
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Null(result.SpotId);
        Assert.Null(Assert.Single(result.Observations).RejectionReason);
        Assert.Equal("Black Gem Fragment", Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents).ItemName);
    }

    [Fact]
    public async Task SpotFilterAppliesToWholeDetectionFrameWithoutRematching()
    {
        using var analyzer = Create(new Rows(new Input(250, "Black Crystal Fragment x17"),
            new(200, "Black Gem Fragment x10"), new(150, "BON Origin Shard x1")));
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        var rejected = Assert.Single(result.Observations, row => row.RejectionReason is not null);
        Assert.Equal("Black Gem Fragment", rejected.ItemName);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason, rejected.RejectionReason);
        Assert.Equal(["Black Crystal Fragment", "BON Origin Shard"],
            analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents.Select(row => row.ItemName));
    }

    [Fact]
    public async Task NewestTrashWinsAndLockChangesOnlyOnReset()
    {
        var rows = new Rows(new Input(250, "Branch of Abundance x9"), new(200, "Black Crystal Fragment x17"));
        using var analyzer = Create(rows);
        using var frame = new Bitmap(800, 600);
        var first = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(LootSpotCatalog.AphrodonId, first.SpotId);
        rows.Values = [new(250, "Elion Follower's Helmet x18")];
        var second = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(LootSpotCatalog.AphrodonId, second.SpotId);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason, Assert.Single(second.Observations).RejectionReason);
        analyzer.Reset();
        var third = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(LootSpotCatalog.MagaiaId, third.SpotId);
        Assert.Null(Assert.Single(third.Observations).RejectionReason);
    }

    [Theory]
    [InlineData(LootSource.Normal)]
    [InlineData(LootSource.Rare)]
    public async Task EventLootIsAlwaysCountedInBothChannelsIncludingAfterReset(LootSource source)
    {
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17"));
        if (source == LootSource.Normal)
            rows.Values = [new(250, "Black Crystal Fragment x17"), new(200, "[Event] Mysterious Ore x1")];
        else
            rows.RareText = "[Event] Mysterious Ore x1";
        var calibration = Calibration();
        using var analyzer = new CompanionLootFrameAnalyzer(
            source == LootSource.Rare ? calibration with
            {
                HasRareLootAnchor = true,
                RareLootAnchorX = 600,
                RareLootAnchorY = 300,
            } : calibration, Matcher(), rows, new Names(rows),
            rareRowPipeline: source == LootSource.Rare ? new RareRows() : null);
        using var frame = new Bitmap(800, 600);
        for (var session = 0; session < 2; session++)
        {
            var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
            Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
            var observation = Assert.Single(result.Observations, row => row.ItemName == "[Event] Mysterious Ore");
            Assert.Null(observation.RejectionReason);
            Assert.Equal(source, observation.Source);
            var counted = Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents,
                row => row.ItemName == "[Event] Mysterious Ore");
            Assert.Equal(1, counted.Quantity);
            analyzer.Reset();
        }
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.95f)]
    public async Task ParsedOcrQuantityHasOriginalPrecedenceOverTemplate(float score)
    {
        using var analyzer = Create(new Rows(new Input(250, "Black Crystal Fragment x17", 71, score)));
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(17, Assert.Single(result.Observations).Quantity);
        Assert.Equal(17, Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents).Quantity);
    }

    [Fact]
    public async Task MissingQuantityIsPassedAsNativeSentinel()
    {
        var reconciliation = new Reconciliation();
        using var analyzer = Create(new Rows(new Input(250, "Black Crystal Fragment", -1)), reconciliation);
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Null(Assert.Single(result.Observations).Quantity);
        Assert.Equal(uint.MaxValue, Assert.Single(reconciliation.Frames.Single()).Count);
        Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
    }

    [Fact]
    public async Task NativeLeadingRowTrimAndNewestFirstInputAreRestored()
    {
        var rows = new Rows(Enumerable.Range(0, 6).Select(i =>
            new Input(i * 50, "BON Origin Shard", i < 2 ? -1 : 1)).ToArray());
        var reconciliation = new Reconciliation();
        using var analyzer = Create(rows, reconciliation);
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, true, CancellationToken.None);
        Assert.Equal([250, 200, 150, 100, 50, 0], rows.ProcessedY);
        Assert.Equal([50, 100, 150, 200, 250], rows.OcrY);
        Assert.Equal([250, 200, 150, 100, 50], reconciliation.Frames.Single().Select(row => row.Y));
        Assert.Equal(5, result.OcrRowCount);
        Assert.All(rows.Hdr, Assert.True);
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
    }

    [Theory]
    [InlineData(new int[] { }, 0)]
    [InlineData(new int[] { -1, -1, -1 }, 2)]
    [InlineData(new int[] { -1, -1, 10 }, 1)]
    [InlineData(new int[] { 1, -1, 10 }, 0)]
    public void RetainedStartMatchesBaseline(int[] quantities, int expected) =>
        Assert.Equal(expected, CompanionLootFrameAnalyzer.CalculateRetainedStartIndex(quantities));

    [Fact]
    public async Task OriginalWonTextRepairIsNoLongerRejectedByOwnPrefixRule()
    {
        using var analyzer = Create(new Rows(new Input(250, "V ON Origin Shard", 1)));
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        var observation = Assert.Single(result.Observations);
        Assert.Equal("WON Origin Shard", observation.ItemName);
        Assert.Null(observation.RejectionReason);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(450)]
    [InlineData(3000)]
    public async Task AnalysisAndRecordedInputsUseSameNativeCounterAtAnyCadence(int milliseconds)
    {
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17"));
        using var analyzer = Create(rows);
        var replay = new CompanionDiagnosticCounter(Matcher().CatalogEntries);
        using var frame = new Bitmap(800, 600);
        var total = 0;
        for (var i = 0; i < 43; i++)
        {
            rows.Values = i % 7 == 4 ? [] : [new(250, $"Black Crystal Fragment x{i % 3 + 15}")];
            var now = DateTimeOffset.UnixEpoch.AddMilliseconds(i * milliseconds);
            var result = await analyzer.AnalyzeAsync(frame, now, CancellationToken.None);
            var expected = replay.ProcessFrame(now, result.Observations, false);
            Assert.Equal(expected.NewEvents.Select(e => (e.ItemName, e.Quantity)),
                result.NewEvents.Select(e => (e.ItemName, e.Quantity)));
            total += result.NewEvents.Sum(e => e.Quantity);
        }
        var completed = analyzer.CompleteSession(DateTimeOffset.UnixEpoch);
        Assert.Equal(replay.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents.Select(e => (e.ItemName, e.Quantity)),
            completed.NewEvents.Select(e => (e.ItemName, e.Quantity)));
        Assert.True(total > 0);
    }

    [Fact]
    public async Task RareGeometryAndSignedDeltasRemainNative()
    {
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17"));
        var rare = new RareRows();
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
        {
            HasRareLootAnchor = true,
            RareLootAnchorX = 600,
            RareLootAnchorY = 300,
        }, Matcher(), rows, new Names(rows), rareRowPipeline: rare,
            rareReconciliation: new RareReconciliation());
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, true, CancellationToken.None);
        Assert.Equal(-1, Assert.Single(result.NewEvents).Quantity);
        Assert.Equal(7, result.PreparedRowCount);
        Assert.Equal("BON Origin Shard", Assert.Single(result.Observations, o => o.Source == LootSource.Rare).ItemName);
        Assert.True(rare.IsHdr);
        Assert.True(rare.Row!.Disposed);
    }

    private static CompanionLootFrameAnalyzer Create(Rows rows, ICompanionReconciliation? reconciliation = null) =>
        new(Calibration(), Matcher(), rows, new Names(rows), reconciliation);

    private static CompanionItemMatcher Matcher() => new(new string[]
    {
        "BON Origin Shard", "WON Origin Shard", "JIN Origin Shard", "Black Gem Fragment",
        "Black Crystal Fragment", "Branch of Abundance", "Elion Follower's Helmet", "[Event] Mysterious Ore",
    }.Concat(LootSpotCatalog.SharedGlobalItems));

    private static CompanionCalibration Calibration() => new("profile", "gamevariable.xml",
        "GameOption.txt", 400, 300, 800, 600, 1f, CompanionFontType.StrongSword, 0, false);

    private sealed record Input(int Y, string Text, int Quantity = 1, float Score = 0.1f);

    private sealed class Rows(params Input[] values) : ICompanionNormalRowPipeline
    {
        public string RareText { get; set; } = "BON Origin Shard";
        public Input[] Values { get; set; } = values;
        public List<int> ProcessedY { get; } = [];
        public List<int> OcrY { get; } = [];
        public List<bool> Hdr { get; } = [];
        public List<Row> Prepared { get; } = [];
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr)
        {
            Assert.Equal(385, band.Width);
            Assert.Equal(50, band.Height);
            ProcessedY.Add(y);
            Hdr.Add(isHdr);
            var input = Values.FirstOrDefault(value => value.Y == y);
            var row = new Row(y, input is null, input?.Quantity ?? -1, input?.Score ?? 0);
            Prepared.Add(row);
            return row;
        }
        public void Dispose() { }
    }

    private sealed class Row(int y, bool blank, int quantity, float score, byte? marker = null) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => quantity;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => score;
        public float NameScale => 1;
        public Mat? NameImage { get; } = blank ? null : new Mat(1, 1, MatType.CV_8UC1, new Scalar(marker ?? y / 50 + 1));
        public bool Disposed { get; private set; }
        public void Dispose() { NameImage?.Dispose(); Disposed = true; }
    }

    private sealed class Names(Rows rows) : ICompanionNameRecognizer
    {
        public string BackendName => "test-ocr";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            var marker = image.At<byte>(0, 0);
            var y = (marker - 1) * 50;
            if (marker != 9) rows.OcrY.Add(y);
            return new(marker == 9 ? rows.RareText : rows.Values.Single(value => value.Y == y).Text,
                new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
        }
    }

    private sealed class Reconciliation : ICompanionReconciliation
    {
        public List<IReadOnlyList<CompanionRecognizedEntry>> Frames { get; } = [];
        public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries)
        {
            Frames.Add(entries);
            return [];
        }
        public IReadOnlyList<CompanionRecognizedEntry> Complete() => [];
        public void Reset() => Frames.Clear();
    }

    private sealed class RareRows : ICompanionRareRowPipeline
    {
        public Row? Row { get; private set; }
        public bool IsHdr { get; private set; }
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr)
        {
            IsHdr = isHdr;
            return Row = new Row(y, false, -1, 0, 9);
        }
        public void Dispose() { }
    }

    private sealed class RareReconciliation : ICompanionRareReconciliation
    {
        public IReadOnlyList<CompanionRareCountDelta> ProcessFrame(IReadOnlyList<CompanionRareRecognizedEntry> entries)
        {
            Assert.Equal(1, Assert.Single(entries).Count);
            return [new("BON Origin Shard", -1)];
        }
        public IReadOnlyList<CompanionRareCountDelta> Complete() => [];
        public void Reset() { }
    }
}
