using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class PrimaryQuantityReadPropagationTests
{
    [Fact]
    public async Task SameFrameNameHintsAreBoundedPerOriginalNormalRowAndNeverSharedWithRare()
    {
        var names = new Names();
        var recovery = new Recovery(names);
        var review = new Review();
        using var analyzer = new CompanionLootFrameAnalyzer(
            new CompanionCalibration("", "", "", 400, 300, 800, 600, 1f, CompanionFontType.StrongSword, 0, false)
            { HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300 },
            new CompanionItemMatcher(["Black Stone"]), new Rows(), names,
            rareRowPipeline: new RareRows(), normalRecovery: recovery, rowReview: review,
            quantityBoundsResolver: (_, _) => null);
        using var frame = new Bitmap(800, 600);

        for (var generation = 1; generation <= 2; generation++)
        {
            names.Generation = generation;
            review.Calls.Clear();
            await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch.AddSeconds(generation),
                false, true, CancellationToken.None);

            Assert.Equal(3 * generation, names.Calls); // Two initial normal reads and one rare read per frame.
            var normalCalls = review.Calls.Where(call => call.Source == LootSource.Normal).ToArray();
            Assert.Equal(2, normalCalls.Length);
            foreach (var call in normalCalls)
            {
                Assert.Equal(3, call.PrimaryQuantityReads.Count); // Initial plus two existing recovery hints.
                Assert.Equal(call.NativeY == 200 ? 1.5f : 2f, call.PrimaryQuantityReads[0].NameScale);
                Assert.Equal(call.NativeY == 200 ? 10f : 0f, call.PrimaryQuantityReads[0].NormalizedNameTop);
                Assert.Equal(new[] { 1.25f, 1.75f }, call.PrimaryQuantityReads.Skip(1).Select(read => read.NameScale));
                Assert.All(call.PrimaryQuantityReads.Skip(1), read => Assert.Equal(0f, read.NormalizedNameTop));
                Assert.All(call.PrimaryQuantityReads, read =>
                    Assert.StartsWith($"frame{generation}-y{call.NativeY}-", read.Reading.Words[0].Text));
                Assert.True(Assert.IsAssignableFrom<IList<PrimaryLootQuantityRead>>(call.PrimaryQuantityReads).IsReadOnly);
            }
            Assert.Empty(Assert.Single(review.Calls, call => call.Source == LootSource.Rare).PrimaryQuantityReads);
        }
    }

    private sealed class Rows : ICompanionNormalRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            new Row(y, y is not (200 or 250), y / 50);
        public void Dispose() { }
    }

    private sealed class RareRows : ICompanionRareRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) => new Row(0, false, 9);
        public void Dispose() { }
    }

    private sealed class Row(int y, bool blank, int marker) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => -1;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => y == 200 ? 1.5f : 2f;
        public float NormalizedNameTop => y == 200 ? 10f : 0f;
        public Mat? NameImage { get; } = blank ? null : new Mat(1, 1, MatType.CV_8UC1, new Scalar(marker));
        public void Dispose() => NameImage?.Dispose();
    }

    private sealed class Names : ICompanionNameRecognizer
    {
        public int Generation { get; set; }
        public int Calls { get; private set; }
        public string BackendName => "test-primary";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            Calls++;
            return Reading(image.At<byte>(0, 0) * 50, "initial");
        }
        public CompanionOcrResult Reading(int y, string variant) =>
            new("Black Stone", new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20))
            {
                Words = [new($"frame{Generation}-y{y}-{variant}", new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20))],
            };
    }

    private sealed class Recovery(Names names) : INormalLootRecovery
    {
        public LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original, LootObservation? baseline,
            int slot, float uiScale, NormalLootRecoveryBudget budget, CancellationToken cancellationToken) => baseline;
        public LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original, LootObservation? baseline,
            int slot, float uiScale, NormalLootRecoveryBudget budget, CancellationToken cancellationToken,
            Action<CompanionOcrResult, float>? onRead)
        {
            onRead?.Invoke(names.Reading(original.Y, "grayscale"), 1.25f);
            onRead?.Invoke(names.Reading(original.Y, "adaptive"), 1.75f);
            onRead?.Invoke(names.Reading(original.Y, "over-budget-fake"), 1f);
            return baseline;
        }
    }

    private sealed class Review : ILootRowReview
    {
        public List<LootRowReviewInput> Calls { get; } = [];
        public void ConfigureLanguage(string languageTag) { }
        public Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input, CancellationToken cancellationToken)
        {
            Calls.Add(input);
            return Task.FromResult(new LootRowReviewResult(input.Baseline, null));
        }
        public void Dispose() { }
    }
}
