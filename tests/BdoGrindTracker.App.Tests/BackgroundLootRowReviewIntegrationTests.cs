using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class BackgroundLootRowReviewIntegrationTests
{
    [Fact]
    public async Task OutOfOrderReviewsWaitForEveryRowAndReplaceOnlyOriginalSourcePositionAndSlot()
    {
        var rows = new Rows(new(200, "BON Origin Shard", 3), new(250, "BON Origin Shard", 4));
        var review = new PendingReview();
        var normal = new RecordingReconciliation();
        var rare = new RecordingRareReconciliation();
        using var analyzer = Create(rows, review, normal, rare);
        using var frame = new Bitmap(800, 600);

        var analyzing = analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(3, review.Calls.Count);
        var rareCall = Assert.Single(review.Calls, call => call.Input.Source == LootSource.Rare);
        var newest = Assert.Single(review.Calls, call => call.Input.Source == LootSource.Normal && call.Input.NativeY == 250);
        var oldest = Assert.Single(review.Calls, call => call.Input.Source == LootSource.Normal && call.Input.NativeY == 200);
        rareCall.Complete(Misplaced(rareCall.Input.Baseline!, 1));
        newest.Complete(Misplaced(newest.Input.Baseline!, 12));

        Assert.False(analyzing.IsCompleted);
        Assert.Empty(normal.Frames);
        Assert.Empty(rare.Frames);
        Assert.All(rows.Prepared, row => Assert.False(row.Disposed));
        oldest.Complete(Misplaced(oldest.Input.Baseline!, 11));
        var result = await analyzing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, result.Observations.Count);
        Assert.Equal([(LootSource.Normal, 250, 0, 12), (LootSource.Normal, 200, 1, 11), (LootSource.Rare, 0, 0, 1)],
            result.Observations.Select(row => (row.Source, row.NativeY!.Value, row.Slot, row.Quantity!.Value)));
        Assert.Equal([250, 200], Assert.Single(normal.Frames).Select(row => row.Y));
        Assert.Equal([12u, 11u], normal.Frames[0].Select(row => row.Count));
        Assert.Equal(0, Assert.Single(Assert.Single(rare.Frames)).Y);
        Assert.Empty(result.NewEvents);
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
        Assert.Equal(3, result.RowReviews.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrowingPersistentLogWithDelayedReviewsPreservesCorrectedBaselineWithoutAdditionalCounts(bool identicalItems)
    {
        string[] items = identicalItems ? Enumerable.Repeat("Black Stone", 5).ToArray()
            : ["Black Stone", "Caphras Stone", "Ancient Spirit Dust", "Laila's Petal", "Pure Black Stone"];
        var quantities = items.Distinct().Select((name, index) => (name, quantity: index + 2))
            .ToDictionary(value => value.name, value => value.quantity);
        var rows = new Rows();
        var oracleRows = new Rows();
        var review = new PendingReview();
        var normal = new RecordingReconciliation(useRealCounter: true);
        using var analyzer = Create(rows, review, normal);
        using var oracle = Create(oracleRows);
        using var frame = new Bitmap(800, 600);
        var actualEvents = new List<(string, int)>();
        var oracleEvents = new List<(string, int)>();
        var index = 0;

        // The first drop persists as new rows arrive underneath; repeated full
        // frames are additional views of those five drops, not additional loot.
        foreach (var visibleCount in new[] { 1, 2, 5, 5, 5, 0 })
        {
            rows.Inputs = items.Take(visibleCount).Select((name, slot) =>
                new Input(250 - (visibleCount - 1 - slot) * 50, name, 1)).ToArray();
            oracleRows.Inputs = rows.Inputs.Select(row => row with { Quantity = quantities[row.Text] }).ToArray();
            review.Calls.Clear();
            var timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(index++ * 450);
            var before = normal.Frames.Count;
            var analyzing = analyzer.AnalyzeAsync(frame, timestamp, CancellationToken.None);
            if (visibleCount != 0)
            {
                Assert.False(analyzing.IsCompleted);
                Assert.Equal(before, normal.Frames.Count);
                foreach (var call in review.Calls.AsEnumerable().Reverse())
                    call.Complete(call.Input.Baseline! with { Quantity = quantities[call.Input.Baseline!.ItemName!] });
            }
            var actual = await analyzing.WaitAsync(TimeSpan.FromSeconds(2));
            var expected = await oracle.AnalyzeAsync(frame, timestamp, CancellationToken.None);
            Assert.Equal(visibleCount, actual.Observations.Count);
            Assert.Equal(before + 1, normal.Frames.Count);
            actualEvents.AddRange(actual.NewEvents.Select(value => (value.ItemName, value.Quantity)));
            oracleEvents.AddRange(expected.NewEvents.Select(value => (value.ItemName, value.Quantity)));
        }

        var completedAt = DateTimeOffset.UnixEpoch.AddSeconds(5);
        actualEvents.AddRange(analyzer.CompleteSession(completedAt).NewEvents.Select(value => (value.ItemName, value.Quantity)));
        oracleEvents.AddRange(oracle.CompleteSession(completedAt).NewEvents.Select(value => (value.ItemName, value.Quantity)));
        Assert.Equal(oracleEvents, actualEvents);
        // The legacy overlap heuristic can overcount indistinguishable rows.
        // This review layer must not add counts to either baseline scenario;
        // distinct identities additionally give an unambiguous physical oracle.
        if (!identicalItems)
        {
            Assert.Equal(5, actualEvents.Count);
            Assert.Equal(20, actualEvents.Sum(value => value.Item2));
        }
        Assert.Equal(identicalItems ? 1 : 5, actualEvents.Select(value => value.Item1).Distinct().Count());
        Assert.Empty(analyzer.CompleteSession(completedAt).NewEvents);
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
    }

    [Fact]
    public async Task DelayedRareReviewPreservesNormalThenRareReconciliationAndSignedEventOrder()
    {
        var calls = new List<string>();
        var rows = new Rows(new Input(250, "BON Origin Shard", 1));
        var review = new PendingReview();
        var normal = new RecordingReconciliation(onProcess: entries =>
        {
            calls.Add("normal");
            return [new("BON Origin Shard", entries.Single().Count, 250)];
        });
        var rare = new RecordingRareReconciliation(onProcess: _ =>
        {
            calls.Add("rare");
            return [new("BON Origin Shard", -1)];
        });
        using var analyzer = Create(rows, review, normal, rare);
        using var frame = new Bitmap(800, 600);
        var analyzing = analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        var normalCall = Assert.Single(review.Calls, call => call.Input.Source == LootSource.Normal);
        normalCall.Complete(normalCall.Input.Baseline! with { Quantity = 2 });
        Assert.Empty(calls);
        var rareCall = Assert.Single(review.Calls, call => call.Input.Source == LootSource.Rare);
        rareCall.Complete(rareCall.Input.Baseline);
        var result = await analyzing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["normal", "rare"], calls);
        Assert.Equal([2, -1], result.NewEvents.Select(value => value.Quantity));
        Assert.All(result.NewEvents, value => Assert.Equal("BON Origin Shard", value.ItemName));
        Assert.Empty(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)).NewEvents);
    }

    [Fact]
    public async Task ReviewEvidenceRoundTripsWithoutCreatingExtraReplayEvents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BdoGrindTracker-ReviewIntegration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rows = new Rows(new Input(250, "BON Origin Shard", 1));
            var review = new PendingReview();
            using var analyzer = Create(rows, review);
            using var frame = new Bitmap(800, 600);
            var analyzing = analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
            var call = Assert.Single(review.Calls);
            call.Complete(call.Input.Baseline! with { Quantity = 2 });
            var result = await analyzing.WaitAsync(TimeSpan.FromSeconds(2));
            using var recording = DiagnosticRecordingSession.Start(directory);
            recording.RecordFrame(DateTimeOffset.UnixEpoch, result.Observations, result.TrackingResult,
                frame, null, null, rowReviews: result.RowReviews);
            var completedAt = DateTimeOffset.UnixEpoch.AddSeconds(1);
            recording.RecordCompletion(completedAt, analyzer.CompleteSession(completedAt).TrackingResult);
            recording.Dispose();

            Assert.Null(recording.LastError);
            var recorded = JsonSerializer.Deserialize<LootDiagnosticEntry>(File.ReadAllLines(recording.RecordingPath!)[1],
                LootDiagnosticFormat.JsonOptions)!;
            var evidence = Assert.Single(recorded.RowReviews!);
            Assert.Equal(LootSource.Normal, evidence.Source);
            Assert.Equal(250, evidence.NativeY);
            Assert.Equal(1, evidence.Before!.Quantity);
            Assert.Equal(2, evidence.After!.Quantity);
            Assert.Equal("test-secondary", evidence.Backend);
            Assert.Equal("en-US", evidence.Language);
            Assert.Equal("observation-revised", evidence.Outcome);
            Assert.Equal("BON Origin Shard x2", Assert.Single(evidence.Readings).Text);
            var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
            Assert.True(replay.TotalsMatch);
            Assert.True(replay.EventTimelineMatches);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static LootObservation Misplaced(LootObservation original, int quantity) => original with
    {
        Quantity = quantity, NativeY = 999, Slot = 99,
        Source = original.Source == LootSource.Normal ? LootSource.Rare : LootSource.Normal
    };

    private static CompanionLootFrameAnalyzer Create(Rows rows, ILootRowReview? review = null,
        ICompanionReconciliation? normal = null, ICompanionRareReconciliation? rare = null) => new(
            new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt", 400, 300, 800, 600,
                1f, CompanionFontType.StrongSword, 0, false)
            { HasRareLootAnchor = rare is not null, RareLootAnchorX = 600, RareLootAnchorY = 300 },
            new CompanionItemMatcher(new[] { "BON Origin Shard" }.Concat(LootSpotCatalog.SharedGlobalItems)),
            rows, new Names(rows), reconciliation: normal, rareRowPipeline: rare is null ? null : new RareRows(),
            rareReconciliation: rare, quantityBoundsResolver: (_, _) => null, rowReview: review);

    private sealed record Input(int Y, string Text, int Quantity);

    private sealed class Rows(params Input[] inputs) : ICompanionNormalRowPipeline
    {
        public Input[] Inputs { get; set; } = inputs;
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
        public string BackendName => "test-primary";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            var marker = image.At<byte>(0, 0);
            return new(marker == 9 ? "BON Origin Shard" : rows.Inputs.Single(value => value.Y == (marker - 1) * 50).Text,
                new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
        }
    }

    private sealed class PendingReview : ILootRowReview
    {
        public List<ReviewCall> Calls { get; } = [];
        public void ConfigureLanguage(string languageTag) { }
        public Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input, CancellationToken cancellationToken)
        {
            var call = new ReviewCall(input);
            Calls.Add(call);
            return call.Completion.Task;
        }
        public void Dispose() { }
    }

    private sealed class ReviewCall(LootRowReviewInput input)
    {
        public LootRowReviewInput Input { get; } = input;
        public TaskCompletionSource<LootRowReviewResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(LootObservation? result) => Completion.SetResult(new(result,
            new(Input.Source, Input.NativeY, "quantity-disagreement", "test-secondary", "en-US", "observation-revised",
                10, Input.Baseline, result,
                [new("Grayscale", $"{result?.ItemName} x{result?.Quantity}", .99, result?.ItemName, result?.Quantity)], 0)));
    }

    private sealed class RecordingReconciliation(bool useRealCounter = false,
        Func<IReadOnlyList<CompanionRecognizedEntry>, IReadOnlyList<CompanionRecognizedEntry>>? onProcess = null) : ICompanionReconciliation
    {
        private readonly CompanionFrameReconciler _counter = new();
        public List<IReadOnlyList<CompanionRecognizedEntry>> Frames { get; } = [];
        public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries)
        {
            Frames.Add(entries.ToArray());
            return onProcess?.Invoke(entries) ?? (useRealCounter ? _counter.ProcessFrame(entries) : []);
        }
        public IReadOnlyList<CompanionRecognizedEntry> Complete() => useRealCounter ? _counter.Complete() : [];
        public void Reset() { Frames.Clear(); _counter.Reset(); }
    }

    private sealed class RareRows : ICompanionRareRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) => new Row(y, false, 1, 9);
        public void Dispose() { }
    }

    private sealed class RecordingRareReconciliation(
        Func<IReadOnlyList<CompanionRareRecognizedEntry>, IReadOnlyList<CompanionRareCountDelta>>? onProcess = null) : ICompanionRareReconciliation
    {
        public List<IReadOnlyList<CompanionRareRecognizedEntry>> Frames { get; } = [];
        public IReadOnlyList<CompanionRareCountDelta> ProcessFrame(IReadOnlyList<CompanionRareRecognizedEntry> entries)
        {
            Frames.Add(entries.ToArray());
            return onProcess?.Invoke(entries) ?? [];
        }
        public IReadOnlyList<CompanionRareCountDelta> Complete() => [];
        public void Reset() => Frames.Clear();
    }

}
