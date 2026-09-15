using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

[Collection("Timing-sensitive integration")]
public sealed class RecordedQuantityConfidenceTests
{
    private const string Trash = "Scorched Belt Ornament";
    private static readonly DropQuantityBounds Bounds = new(2, 1000);

    [Theory]
    [InlineData("aresion-010320-y200.png")]
    [InlineData("aresion-010324-y200.png")]
    public async Task NativeBackgroundStrokeCannotReplaceRecordedTenWithOneHundredAndOne(string fixture)
    {
        using var band = LoadBand(fixture);
        using var engine = new RecordingRecognizer();
        using var review = Reviewer(engine);
        var baseline = Baseline(10, slot: 1, nativeY: 200);
        var input = Input(baseline, templateQuantity: 10, templateScore: .84152997f);

        var result = await review.ReviewAsync(band, input, CancellationToken.None);

        Assert.Equal("quantity-without-complete-text", result.Diagnostics!.Reason);
        Assert.Equal(0, result.Diagnostics.Errors);
        Assert.Equal(2, engine.Reads.Count);
        Assert.All(engine.Reads, reading =>
        {
            // The pinned real model reproduces the misleading whole-line consensus.
            Assert.EndsWith("x 101", reading.Text);
            Assert.InRange(reading.Confidence, .95f, 1f);
            Assert.Equal(reading.Text.Length, reading.CharacterConfidences.Count);
            Assert.True(reading.CharacterConfidences[^1] < .95f,
                "The appended scenery digit must remain distinguishable from the confident text.");
        });
        Assert.Same(baseline, result.Observation);
        Assert.Equal(10, result.Observation!.Quantity);
    }

    [Fact]
    public async Task NativeThirtyCanReplaceAnIncorrectSmallerTemplateAmount()
    {
        using var band = LoadBand("aresion-001355-y250.png");
        using var engine = new RecordingRecognizer();
        using var review = Reviewer(engine);
        // Deliberately wrong baseline tests replacement, not protection of an already correct 30.
        var result = await review.ReviewAsync(band, Input(Baseline(3), 3, .80f), CancellationToken.None);

        Assert.Equal(0, result.Diagnostics!.Errors);
        Assert.Equal(2, engine.Reads.Count);
        Assert.All(engine.Reads, reading => AssertConfidentAmount(reading, 30));
        Assert.Equal(Trash, result.Observation!.ItemName);
        Assert.Equal(30, result.Observation.Quantity);
    }

    [Fact]
    public async Task NativeMissingSixStillRecoversWhenItsDigitScoreIsBelowReplacementThreshold()
    {
        using var band = LoadBand("aresion-000655-y250.png");
        using var engine = new RecordingRecognizer();
        using var review = Reviewer(engine);
        var baseline = Baseline(null) with
        {
            RawText = "Scorched Belt Ornameht",
            NameConfidence = .9545454531908035,
        };
        var result = await review.ReviewAsync(band, Input(baseline, -1, 0), CancellationToken.None);

        Assert.Equal("missing-quantity", result.Diagnostics!.Reason);
        Assert.Equal(0, result.Diagnostics.Errors);
        Assert.Equal(2, engine.Reads.Count);
        Assert.All(engine.Reads, reading =>
        {
            Assert.EndsWith("x 6", reading.Text);
            Assert.InRange(reading.Confidence, .95f, 1f);
        });
        Assert.Contains(engine.Reads, reading => MinimumDigitConfidence(reading) < .95f);
        Assert.Equal(6, result.Observation!.Quantity);
    }

    [Theory]
    [InlineData(101, 10)]
    [InlineData(104, 10)]
    [InlineData(338, 33)]
    public async Task SyntheticConfidentLargeAmountCanReplaceAnIncorrectShorterAmount(int quantity, int baselineQuantity)
    {
        // Synthetic GDI text is a permissive control, not evidence of an actual recorded drop.
        using var band = RenderSyntheticBand($"{Trash} x {quantity}");
        using var engine = new RecordingRecognizer();
        using var review = Reviewer(engine);
        var result = await review.ReviewAsync(band,
            Input(Baseline(baselineQuantity), baselineQuantity, .80f), CancellationToken.None);

        Assert.Equal(0, result.Diagnostics!.Errors);
        Assert.Equal(2, engine.Reads.Count);
        Assert.All(engine.Reads, reading => AssertConfidentAmount(reading, quantity));
        Assert.Equal(quantity, result.Observation!.Quantity);
    }

    private static void AssertConfidentAmount(SecondaryLootOcrResult reading, int quantity)
    {
        var suffix = Regex.Match(reading.Text, @"[xX×]\s*([0-9]+)\s*\.?$");
        Assert.True(suffix.Success, reading.Text);
        Assert.Equal(quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), suffix.Groups[1].Value);
        Assert.InRange(reading.Confidence, .95f, 1f);
        Assert.InRange(MinimumDigitConfidence(reading), BackgroundLootRowReview.MinimumQuantityReplacementConfidence, 1f);
    }

    private static float MinimumDigitConfidence(SecondaryLootOcrResult reading)
    {
        Assert.Equal(reading.Text.Length, reading.CharacterConfidences.Count);
        var suffix = Regex.Match(reading.Text, @"[xX×]\s*([0-9]+)\s*\.?$");
        Assert.True(suffix.Success, reading.Text);
        return reading.CharacterConfidences.Skip(suffix.Groups[1].Index).Take(suffix.Groups[1].Length).Min();
    }

    private static LootObservation Baseline(int? quantity, int slot = 0, int nativeY = 250) =>
        new(LootSource.Normal, slot, Trash, Trash, quantity, 1, 0, null, null)
        { NativeY = nativeY, QuantityBounds = Bounds };

    private static LootRowReviewInput Input(LootObservation baseline, int templateQuantity, float templateScore) =>
        new(baseline, LootSource.Normal, baseline.Slot, baseline.NativeY!.Value,
            templateQuantity, templateScore, _ => Bounds, name => name == Trash) { UiScale = 1 };

    private static BackgroundLootRowReview Reviewer(RecordingRecognizer engine) =>
        new(new CompanionItemMatcher([Trash]), _ => engine, workerCount: 1);

    private static Mat LoadBand(string fixture)
    {
        var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "quantity-confidence", fixture));
        Assert.False(image.Empty());
        Assert.Equal(new OpenCvSharp.Size(385, 50), image.Size());
        return image;
    }

    private static Mat RenderSyntheticBand(string text)
    {
        using var bitmap = new Bitmap(385, 50, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.Clear(Color.FromArgb(42, 42, 42));
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(text, font, Brushes.White, 10, 12, StringFormat.GenericTypographic);
        graphics.Flush();
        return CompanionFrameDecoder.Decode(bitmap);
    }

    private sealed class RecordingRecognizer : ISecondaryLootOcrRecognizer
    {
        // Load the native model before the review's bounded row timer starts.
        private readonly PaddleLootOcrRecognizer engine = PaddleLootOcrRecognizer.Create("en-US");
        public List<SecondaryLootOcrResult> Reads { get; } = [];
        public string BackendName => engine.BackendName;
        public string LanguageTag => engine.LanguageTag;
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        {
            var read = engine.Recognize(image, cancellationToken);
            Reads.Add(read);
            return read;
        }
        public void Dispose() => engine.Dispose();
    }
}
