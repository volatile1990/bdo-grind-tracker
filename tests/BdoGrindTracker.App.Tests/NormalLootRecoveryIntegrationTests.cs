using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class NormalLootRecoveryIntegrationTests
{
    [Fact]
    public async Task SuccessfulBaselineRowsNeverReachRecoveryOrChangeQuantity()
    {
        var rows = new Rows(AllSlots("BON Origin Shard", 17));
        var recovery = new Recovery(_ => Observation("Black Gem Fragment", 999));
        var reconciliation = new Reconciliation();
        using var analyzer = Create(rows, recovery, reconciliation);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        Assert.Empty(recovery.Calls);
        Assert.Equal(NormalLootRecoveryDiagnostics.Empty, result.Recovery);
        Assert.Equal(6, result.Observations.Count);
        Assert.All(result.Observations, row =>
        {
            Assert.Equal("BON Origin Shard", row.ItemName);
            Assert.Equal(17, row.Quantity);
            Assert.Null(row.RejectionReason);
        });
        Assert.Equal(6, result.OcrRowCount);
        Assert.Equal([250, 200, 150, 100, 50, 0], reconciliation.Frames.Single().Select(row => row.Y));
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
    }

    [Fact]
    public async Task MissingQuantityIsFilledWithoutChangingAnyOtherBaselineField()
    {
        var recovery = new Recovery(call => call.Row.Y == 250
            ? Observation("Black Crystal Fragment", 17) with
            {
                Source = LootSource.Rare, Slot = 99, NativeY = 999,
                RawText = "fallback text", NameConfidence = .1, QuantityConfidence = .9,
            }
            : null);
        var reconciliation = new Reconciliation();
        using var analyzer = Create(new Rows(new Input(250, "Black CrystaI Fragment", -1)),
            recovery, reconciliation);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        var original = Assert.Single(recovery.Calls, call => call.Row.Y == 250).Baseline;
        Assert.NotNull(original);
        Assert.Null(original.Quantity);
        Assert.Equal(original with { Quantity = 17 }, Assert.Single(result.Observations));
        Assert.Equal(17u, Assert.Single(reconciliation.Frames.Single()).Count);
        Assert.Equal(1, result.Recovery.QuantitiesRecovered);
        Assert.Equal(0, result.Recovery.RowsRecovered);
        Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
    }

    [Theory]
    [InlineData("no-result")]
    [InlineData("different-item")]
    [InlineData("rejected")]
    [InlineData("zero")]
    [InlineData("still-missing")]
    public async Task FailedOrConflictingQuantityRecoveryPreservesOriginalNativeSentinel(string outcome)
    {
        var recovery = new Recovery(call => call.Row.Y != 250 ? null : outcome switch
        {
            "different-item" => Observation("Branch of Abundance", 71),
            "rejected" => Observation("Black Crystal Fragment", 71) with { RejectionReason = "failed" },
            "zero" => Observation("Black Crystal Fragment", 0),
            "still-missing" => Observation("Black Crystal Fragment", null),
            _ => null,
        });
        var reconciliation = new Reconciliation();
        using var analyzer = Create(new Rows(new Input(250, "Black Crystal Fragment", -1)),
            recovery, reconciliation);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        var original = Assert.Single(recovery.Calls, call => call.Row.Y == 250).Baseline;
        Assert.Equal(original, Assert.Single(result.Observations));
        Assert.Equal(uint.MaxValue, Assert.Single(reconciliation.Frames.Single()).Count);
        Assert.Equal(0, result.Recovery.QuantitiesRecovered);
        Assert.Equal(0, result.Recovery.RowsRecovered);
        Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
    }

    [Fact]
    public async Task RejectedBlankAndTrimmedRowsAreRescuedOnceInNativeNewestFirstOrder()
    {
        var rows = new Rows(new Input(0, "Caphras Stone", -1),
            new(50, "zzzzzzzzzzzzzzzzzzzz", -1), new(100, "Black Stone", 1),
            new(200, "zzzzzzzzzzzzzzzzzzzz", 1), new(250, "Ancient Spirit Dust", 2));
        var recovery = new Recovery(call => call.Row.Y switch
        {
            0 => Observation("Caphras Stone", 7),
            50 => Observation("Laila's Petal", 3),
            150 => Observation("BON Origin Shard", 4),
            200 => Observation("Black Gem Fragment", 5),
            _ => throw new InvalidOperationException("Successful rows must not be retried."),
        });
        var reconciliation = new Reconciliation();
        using var analyzer = Create(rows, recovery, reconciliation);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        Assert.DoesNotContain(0, rows.OcrY); // Trimmed by the unchanged original path.
        Assert.Null(Assert.Single(recovery.Calls, call => call.Row.Y == 0).Baseline);
        Assert.True(Assert.Single(recovery.Calls, call => call.Row.Y == 150).Row.IsBlank);
        Assert.NotNull(Assert.Single(recovery.Calls, call => call.Row.Y == 50).Baseline!.RejectionReason);
        Assert.NotNull(Assert.Single(recovery.Calls, call => call.Row.Y == 200).Baseline!.RejectionReason);
        Assert.Equal([250, 200, 150, 100, 50, 0], result.Observations.Select(row => row.NativeY!.Value));
        Assert.Equal([0, 1, 2, 3, 4, 5], result.Observations.Select(row => row.Slot));
        Assert.Equal([2u, 5u, 4u, 1u, 3u, 7u], reconciliation.Frames.Single().Select(row => row.Count));
        Assert.All(result.Observations, row =>
        {
            Assert.Equal(LootSource.Normal, row.Source);
            Assert.Null(row.RejectionReason);
        });
        Assert.Equal(6, result.Observations.Select(row => row.NativeY).Distinct().Count());
        Assert.Equal(4, result.Recovery.RowsRecovered);
        Assert.Equal(0, result.Recovery.QuantitiesRecovered);
    }

    [Fact]
    public async Task BaselineTrashHasPriorityAndRescuedRowsStillUseTheExistingSpotPool()
    {
        var recovery = new Recovery(call => call.Row.Y switch
        {
            250 => Observation("Branch of Abundance", 71),
            150 => Observation("Black Gem Fragment", 10),
            100 => Observation("Ancient Spirit Dust", 3),
            _ => null,
        });
        var reconciliation = new Reconciliation();
        using var analyzer = Create(new Rows(new Input(200, "Black Crystal Fragment", 17)),
            recovery, reconciliation);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
        Assert.DoesNotContain(recovery.Calls, call => call.Row.Y == 200);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason,
            Assert.Single(result.Observations, row => row.ItemName == "Branch of Abundance").RejectionReason);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason,
            Assert.Single(result.Observations, row => row.ItemName == "Black Gem Fragment").RejectionReason);
        Assert.Null(Assert.Single(result.Observations, row => row.ItemName == "Ancient Spirit Dust").RejectionReason);
        Assert.Equal(["Black Crystal Fragment", "Ancient Spirit Dust"],
            reconciliation.Frames.Single().Select(row => row.Name));
    }

    [Fact]
    public async Task RescuedTrashCanSelectAnUnknownSpotAndFilterTheSameFrame()
    {
        var recovery = new Recovery(call => call.Row.Y == 250 ? Observation("Branch of Abundance", 17) : null);
        var reconciliation = new Reconciliation();
        using var analyzer = Create(new Rows(new Input(200, "Black Gem Fragment", 10),
            new(150, "Ancient Spirit Dust", 3)), recovery, reconciliation);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        Assert.Equal(LootSpotCatalog.AphrodonId, result.SpotId);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason,
            Assert.Single(result.Observations, row => row.ItemName == "Black Gem Fragment").RejectionReason);
        Assert.Equal(["Branch of Abundance", "Ancient Spirit Dust"],
            reconciliation.Frames.Single().Select(row => row.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RescuedEventRowsRespectExistingOptIn(bool includeEventLoot)
    {
        var recovery = new Recovery(call => call.Row.Y == 200 ? Observation("[Event] Mysterious Ore", 1) : null);
        var reconciliation = new Reconciliation();
        using var analyzer = Create(new Rows(new Input(250, "Black Crystal Fragment", 17)),
            recovery, reconciliation);
        analyzer.ConfigureLootFilter(includeEventLoot);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        var item = Assert.Single(result.Observations, row => row.ItemName == "[Event] Mysterious Ore");
        Assert.Equal(includeEventLoot ? null : AutomaticLootSpotLock.OutsideSpotPoolReason, item.RejectionReason);
        Assert.Equal(includeEventLoot ? 2 : 1, reconciliation.Frames.Single().Count);
    }

    [Fact]
    public async Task RarePipelineNeverReachesNormalRecoveryAndKeepsSignedDeltas()
    {
        var rows = new Rows(AllSlots("BON Origin Shard", 1));
        var rareRows = new RareRows();
        var rareReconciliation = new RareReconciliation();
        var recovery = new Recovery(_ => throw new InvalidOperationException("No normal row needs recovery."));
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
        {
            HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300,
        }, Matcher(), rows, new Names(rows), reconciliation: new Reconciliation(),
            rareRowPipeline: rareRows, rareReconciliation: rareReconciliation, normalRecovery: recovery);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, true, CancellationToken.None);

        Assert.Empty(recovery.Calls);
        Assert.Equal(NormalLootRecoveryDiagnostics.Empty, result.Recovery);
        Assert.Equal(7, result.OcrRowCount);
        var rare = Assert.Single(result.Observations, row => row.Source == LootSource.Rare);
        Assert.Equal("BON Origin Shard", rare.ItemName);
        Assert.Equal(1, rare.Quantity);
        Assert.Equal(1, Assert.Single(rareReconciliation.Frames.Single()).Count);
        Assert.Equal(-1, Assert.Single(result.NewEvents).Quantity);
        Assert.True(rareRows.IsHdr);
        Assert.True(rareRows.Row!.Disposed);
    }

    [Fact]
    public async Task OptionalOcrIsLimitedToEightCallsAndRotatesRowsWithoutTouchingBaseline()
    {
        var rows = new Rows(AllSlots("zzzzzzzzzzzzzzzzzzzz", 1));
        var recovery = new Recovery(_ => null, ocrCallsPerRow: 2);
        var reconciliation = new Reconciliation();
        using var analyzer = Create(rows, recovery, reconciliation);
        using var frame = new Bitmap(800, 600);
        var firstRows = new List<int>();

        for (var index = 0; index < 3; index++)
        {
            recovery.Calls.Clear();
            var result = await analyzer.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(index * 450), CancellationToken.None);

            Assert.Equal(8, result.Recovery.OcrCalls);
            Assert.Equal(4, result.Recovery.RowsAttempted);
            Assert.Equal(14, result.OcrRowCount);
            Assert.Equal(6, result.Observations.Count);
            Assert.All(result.Observations, row => Assert.NotNull(row.RejectionReason));
            Assert.Equal(Enumerable.Range(index, 4).Select(i => i * 50), recovery.Calls.Select(call => call.Row.Y));
            firstRows.Add(recovery.Calls[0].Row.Y);
            Assert.All(recovery.Calls, call => Assert.Same(recovery.Calls[0].Budget, call.Budget));
        }

        Assert.Equal([0, 50, 100], firstRows);
        Assert.All(reconciliation.Frames, entries => Assert.Empty(entries));
        Assert.Equal(18, rows.OcrY.Count);
        analyzer.Reset();
        recovery.Calls.Clear();
        await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch.AddSeconds(2), CancellationToken.None);
        Assert.Equal(0, recovery.Calls[0].Row.Y);
    }

    [Theory]
    [InlineData("missing-quantity")]
    [InlineData("rejected")]
    [InlineData("blank")]
    public async Task OneRecoveredFrameCanBeCountedWithoutASecondObservation(string originalState)
    {
        var rows = originalState switch
        {
            "missing-quantity" => new Rows(new Input(250, "Black Crystal Fragment", -1)),
            "rejected" => new Rows(new Input(250, "zzzzzzzzzzzzzzzzzzzz", 1)),
            _ => new Rows(),
        };
        var recovery = new Recovery(call => call.Row.Y == 250 ? Observation("Black Crystal Fragment", 17) : null);
        using var analyzer = Create(rows, recovery);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);

        Assert.Empty(result.NewEvents); // Companion's existing batch is retained.
        Assert.Equal(17, Assert.Single(result.Observations).Quantity);
        var counted = Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents);
        Assert.Equal("Black Crystal Fragment", counted.ItemName);
        Assert.Equal(17, counted.Quantity);
        Assert.Empty(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents);
    }

    private static CompanionLootFrameAnalyzer Create(Rows rows, Recovery recovery,
        ICompanionReconciliation? reconciliation = null) =>
        new(Calibration(), Matcher(), rows, new Names(rows), reconciliation, normalRecovery: recovery,
            quantityBoundsResolver: (_, _) => null); // Synthetic recovery quantities, not the live drop catalog.

    private static CompanionCalibration Calibration() => new("profile", "gamevariable.xml",
        "GameOption.txt", 400, 300, 800, 600, 1f, CompanionFontType.StrongSword, 0, false);

    private static CompanionItemMatcher Matcher() => new(new[]
    {
        "BON Origin Shard", "WON Origin Shard", "JIN Origin Shard", "Black Gem Fragment",
        "Black Crystal Fragment", "Branch of Abundance", "Elion Follower's Helmet", "[Event] Mysterious Ore",
    }.Concat(LootSpotCatalog.SharedGlobalItems));

    // Deliberately wrong source/position: integration must anchor rescues to the original normal row.
    private static LootObservation Observation(string item, int? quantity) =>
        new(LootSource.Rare, 99, $"rescued {item}", item, quantity, .75, 0, null, null) { NativeY = 999 };

    private static Input[] AllSlots(string text, int quantity) =>
        Enumerable.Range(0, 6).Select(index => new Input(index * 50, text, quantity)).ToArray();

    private sealed record Input(int Y, string Text, int Quantity);

    private sealed class Rows(params Input[] inputs) : ICompanionNormalRowPipeline
    {
        public Input[] Inputs { get; } = inputs;
        public List<int> OcrY { get; } = [];
        public List<Row> Prepared { get; } = [];
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr)
        {
            var input = Inputs.FirstOrDefault(value => value.Y == y);
            var row = new Row(y, input is null, input?.Quantity ?? -1);
            Prepared.Add(row);
            return row;
        }
        public void Dispose() { }
    }

    private sealed class Row(int y, bool blank, int quantity, byte? marker = null) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => quantity;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => .1f;
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
            if (marker == 9)
                return new("BON Origin Shard", new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
            var y = (marker - 1) * 50;
            rows.OcrY.Add(y);
            return new(rows.Inputs.Single(value => value.Y == y).Text,
                new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
        }
    }

    private sealed record RecoveryCall(ICompanionPreparedRow Row, LootObservation? Baseline,
        int Slot, NormalLootRecoveryBudget Budget);

    private sealed class Recovery(Func<RecoveryCall, LootObservation?> recover, int ocrCallsPerRow = 1) : INormalLootRecovery
    {
        public List<RecoveryCall> Calls { get; } = [];
        public LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original,
            LootObservation? baseline, int slot, float uiScale, NormalLootRecoveryBudget budget,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(385, sourceBand.Width);
            Assert.Equal(50, sourceBand.Height);
            Assert.Equal(MatType.CV_8UC3, sourceBand.Type());
            Assert.Equal(1f, uiScale);
            Assert.Equal(5 - original.Y / 50, slot);
            var call = new RecoveryCall(original, baseline, slot, budget);
            Calls.Add(call);
            for (var index = 0; index < ocrCallsPerRow; index++)
                if (!budget.TryBeginOcr()) return null;
            return recover(call);
        }
    }

    private sealed class Reconciliation : ICompanionReconciliation
    {
        public List<IReadOnlyList<CompanionRecognizedEntry>> Frames { get; } = [];
        public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries)
        {
            Frames.Add(entries.ToArray());
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
            return Row = new Row(y, false, -1, 9);
        }
        public void Dispose() { }
    }

    private sealed class RareReconciliation : ICompanionRareReconciliation
    {
        public List<IReadOnlyList<CompanionRareRecognizedEntry>> Frames { get; } = [];
        public IReadOnlyList<CompanionRareCountDelta> ProcessFrame(IReadOnlyList<CompanionRareRecognizedEntry> entries)
        {
            Frames.Add(entries.ToArray());
            return [new("BON Origin Shard", -1)];
        }
        public IReadOnlyList<CompanionRareCountDelta> Complete() => [];
        public void Reset() => Frames.Clear();
    }
}
