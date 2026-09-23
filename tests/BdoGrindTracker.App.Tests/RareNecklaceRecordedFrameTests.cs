using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class RareNecklaceRecordedFrameTests
{
    private const string Necklace = "Twilight of the End - Necklace";

    [WindowsOcrTheory("en-US")]
    [Trait("Category", "WindowsOcr")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedHdrNecklaceSelectsBestReadingAndCountsEachArrivalOnce(bool secondArrival)
    {
        string[] items = [Necklace, "Twilight of the End - Ring", "Apeiron Earring"];
        var context = new LifetimeParsingContext(0, items.Select(name =>
            new LifetimeParsingCatalogEntry(name, [], true, LootSource.Rare)).ToArray());
        var matcher = new CompanionItemMatcher(items);
        var calibration = new CompanionCalibration("recorded", "gamevariable.xml", "GameOption.txt",
            3574, 1860, 3840, 2160, 1.49f, CompanionFontType.StrongSword, 0, false)
        { HasRareLootAnchor = true, RareLootAnchorX = 3077, RareLootAnchorY = 831 };
        var rareBounds = CompanionNormalLootGeometry.CalculateRareBandCrop(calibration);
        using var review = new BackgroundLootRowReview(matcher, language => PaddleLootOcrRecognizer.Create(language));
        using var analyzer = new CompanionLootFrameAnalyzer(calibration, matcher, new EmptyNormalRows(),
            new CompanionNameRecognizer(CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true)!),
            reconciliation: new LifetimeNormalReconciliationAdapter(context),
            rareRowPipeline: new CompanionRareRowPipeline(), rowReview: review,
            specialReconciliation: new LifetimeNormalReconciliationAdapter(context, false, LootSource.Rare, 1),
            quantityBoundsResolver: (_, _) => new(1, 1), lootSourceResolver: _ => LootSource.Rare);
        var replay = new CompanionDiagnosticCounter(matcher.CatalogEntries,
            parsingContext: context, independentSpecial: true);
        using var frame = new Bitmap(calibration.ScreenWidth, calibration.ScreenHeight);
        var sample = 0;
        var confirmedReadings = 0;
        var strongSecondaryReadings = 0;

        var sequences = new List<int> { 4334, 4340, 4347, 4348, 4357 };
        if (secondArrival)
            // Scenery persists for 2.4 seconds before a genuinely new banner.
            sequences.AddRange([4357, 4357, 4340, 4347, 4348, 4357]);
        foreach (var sequence in sequences)
        {
            using var crop = new Bitmap(Path.Combine(AppContext.BaseDirectory,
                "fixtures", "rare-loot", $"twilight-necklace-{sequence}.png"));
            Assert.Equal(rareBounds.Size, crop.Size);
            using (var graphics = Graphics.FromImage(frame))
            {
                graphics.Clear(Color.Black);
                graphics.DrawImageUnscaled(crop, rareBounds.Location);
            }
            for (var repeat = 0; repeat < 4; repeat++)
            {
                var at = DateTimeOffset.UnixEpoch.AddMilliseconds(sample++ * 200);
                var result = await analyzer.AnalyzeAsync(frame, at, isHdr: true, isToneMapped: true, CancellationToken.None);
                replay.ProcessFrame(at, result.Observations, true);
                if (sequence is 4340 or 4347 or 4348)
                {
                    var observation = Assert.Single(result.Observations);
                    Assert.Null(observation.RejectionReason);
                    Assert.Equal(Necklace, observation.ItemName);
                    Assert.Equal(1, observation.Quantity);
                    var diagnostic = Assert.Single(result.RowReviews);
                    Assert.Contains(diagnostic.Outcome, new[] { "rare-primary-selected", "rare-secondary-selected" });
                    Assert.Equal(2, diagnostic.Readings.Count);
                    strongSecondaryReadings += diagnostic.Readings.Count(read => read.ItemName == Necklace &&
                        read.Confidence >= BackgroundLootRowReview.MinimumReadingConfidence);
                    confirmedReadings++;
                }
                else
                {
                    var observation = Assert.Single(result.Observations);
                    Assert.Null(observation.ItemName);
                    Assert.Equal(LootObservation.RareOcrNoMatchReason, observation.RejectionReason);
                }
            }
        }
        var completedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(sample * 200);
        var completed = analyzer.CompleteSession(completedAt).LootProjection!;
        var replayed = replay.CompleteSession(completedAt).LootProjection!;
        var expectedDrops = secondArrival ? 2 : 1;
        Assert.Equal(expectedDrops * 12, confirmedReadings);
        Assert.True(strongSecondaryReadings > 0, "The cropped banner must also be readable by the real Paddle engine.");
        Assert.Equal(expectedDrops, Assert.Single(completed.Totals).Value);
        Assert.Equal(expectedDrops, completed.Totals[Necklace]);
        Assert.Equal(expectedDrops, completed.ConfirmedDropCount);
        Assert.Equal(completed.Totals, replayed.Totals);
    }

    private sealed class EmptyNormalRows : ICompanionNormalRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) => new BlankRow(y);
        public void Dispose() { }
    }

    private sealed class BlankRow(int y) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => true;
        public int RecognizedTextWidth => 0;
        public int TemplateQuantity => -1;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => 1;
        public Mat? NameImage => null;
        public void Dispose() { }
    }
}
