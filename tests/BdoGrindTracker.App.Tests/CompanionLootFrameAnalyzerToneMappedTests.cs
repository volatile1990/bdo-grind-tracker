using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    [Fact]
    public async Task ToneMappedFrameReadsEveryVisibleRowWithoutQuantityTemplates()
    {
        var rows = new Rows(Enumerable.Range(1, 5).Select(index =>
            new Input(index * 50, $"Black Crystal Fragment x{10 + index}", -1)).ToArray());
        var pipeline = new ToneMappedTrackingRows(rows);
        var reconciliation = new Reconciliation();
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration(), Matcher(), pipeline,
            new Names(rows), reconciliation);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch,
            isHdr: false, isToneMapped: true, CancellationToken.None);

        Assert.Equal(new[] { 50, 100, 150, 200, 250 }, rows.OcrY);
        Assert.Equal(6, pipeline.ToneMappedFlags.Count);
        Assert.All(pipeline.ToneMappedFlags, Assert.True);
        Assert.All(rows.Hdr, Assert.False);
        Assert.Equal(5, result.OcrRowCount);
        Assert.Contains("+tone-mapped-normal-v1", result.VariantName);
        Assert.Equal(5, result.Observations.Count);
        Assert.All(result.Observations, observation => Assert.Null(observation.RejectionReason));
        Assert.Equal(new uint[] { 15, 14, 13, 12, 11 },
            reconciliation.Frames.Single().Select(entry => entry.Count));
        Assert.All(rows.Prepared, row => Assert.True(row.Disposed));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void UntoneMappedPipelineKeepsExistingSdrAndHdrPixels(bool isHdr, int fontType)
    {
        using var template = new Mat(7, 5, MatType.CV_8UC1, Scalar.Black);
        Cv2.Line(template, new OpenCvSharp.Point(1, 1), new OpenCvSharp.Point(3, 5), Scalar.White, 1);
        var templates = new[] { new CompanionQuantityTemplate(1, template, 0.999f) };
        using var originalRecognizer = new CompanionQuantityRecognizer(templates);
        using var pipeline = new CompanionNormalRowPipeline(new CompanionQuantityRecognizer(templates));
        var original = new CompanionNormalRowProcessor(originalRecognizer);
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(100, 100, 100));
        Cv2.Rectangle(source, new Rect(150, 20, 200, 30),
            isHdr ? Scalar.White : new Scalar(232, 232, 232), -1);

        using var expected = original.Process(source, 224, 1.49f, fontType, isHdr);
        using var actual = pipeline.Process(source, 224, 1.49f, fontType, isHdr, isToneMapped: false);

        Assert.False(expected.IsBlank);
        Assert.Equal(expected.IsBlank, actual.IsBlank);
        Assert.Equal(expected.RecognizedTextWidth, actual.RecognizedTextWidth);
        Assert.Equal(expected.TemplateQuantity, actual.TemplateQuantity);
        Assert.Equal(expected.LeftmostQuantityX, actual.LeftmostQuantityX);
        Assert.Equal(expected.NameScale, actual.NameScale);
        Assert.Equal(expected.NormalizedNameTop, actual.NormalizedNameTop);
        Assert.Equal(expected.QuantityScore, actual.QuantityScore);
        Assert.Equal(0d, Cv2.Norm(expected.NameImage!, actual.NameImage!, NormTypes.INF));
    }

    [Fact]
    public void ToneMappedPipelineSelectsFullWidthPreparationWithoutTemplateMatching()
    {
        using var template = new Mat(7, 5, MatType.CV_8UC1, Scalar.Black);
        template.Set(3, 2, (byte)255);
        using var recognizer = new CompanionQuantityRecognizer(
            new[] { new CompanionQuantityTemplate(1, template, 0.999f) });
        using var pipeline = new CompanionNormalRowPipeline(recognizer);
        using var source = new Mat(75, 573, MatType.CV_8UC3, new Scalar(210, 210, 210));
        Cv2.Rectangle(source, new Rect(150, 25, 250, 20), new Scalar(232, 232, 232), -1);
        // The tone-mapped branch must never consult the unreliable digit templates.
        recognizer.Dispose();

        using var expected = ToneMappedNormalRowProcessor.Process(source, 224);
        using var actual = pipeline.Process(source, 224, 1.49f, 2, false, isToneMapped: true);

        Assert.False(actual.IsBlank);
        Assert.Equal(-1, actual.TemplateQuantity);
        Assert.Equal(0f, actual.NormalizedNameTop);
        Assert.Equal(expected.RecognizedTextWidth, actual.RecognizedTextWidth);
        Assert.Equal(0d, Cv2.Norm(expected.NameImage!, actual.NameImage!, NormTypes.INF));
    }

    [Theory]
    [InlineData(1280, 720, .75f)]
    [InlineData(1920, 1080, 1f)]
    [InlineData(2560, 1440, 1.25f)]
    [InlineData(3440, 1440, 1f)]
    [InlineData(3840, 2160, 1.49f)]
    [InlineData(3840, 2160, 2f)]
    [InlineData(7680, 4320, 2.5f)]
    public void ToneMappedPreparationFollowsCalibratedRowsAcrossResolutionAndUiScale(
        int screenWidth, int screenHeight, float uiScale)
    {
        var calibration = new CompanionCalibration("", "", "",
            screenWidth / 2, screenHeight / 2, screenWidth, screenHeight,
            uiScale, CompanionFontType.StrongSword, 0, false);
        var panel = CompanionNormalLootGeometry.CalculatePanelBounds(calibration);
        var slots = CompanionNormalLootGeometry.CalculateSlotCrops(calibration);
        Assert.Equal(6, slots.Count);
        Assert.True(new Rectangle(0, 0, screenWidth, screenHeight).Contains(panel));

        foreach (var slot in slots)
        {
            Assert.True(panel.Contains(slot));
            using var band = new Mat(slot.Height, slot.Width, MatType.CV_8UC3, Scalar.All(180));
            // Glyph-shaped strokes at proportional positions, including the far
            // right suffix, must survive every independently rounded row crop.
            Cv2.Rectangle(band, new Rect(slot.Width / 8, slot.Height * 35 / 100,
                slot.Width / 16, Math.Max(1, slot.Height / 5)), Scalar.All(240), -1);
            Cv2.Rectangle(band, new Rect(slot.Width * 7 / 8, slot.Height * 35 / 100,
                slot.Width / 32, Math.Max(1, slot.Height / 5)), Scalar.All(240), -1);
            using var prepared = ToneMappedNormalRowProcessor.Process(band, slot.Top - panel.Top);

            Assert.Equal(slot.Top - panel.Top, prepared.Y);
            Assert.False(prepared.IsBlank);
            Assert.Equal(-1, prepared.TemplateQuantity);
            var normalizedWidth = (int)Math.Round(slot.Width * 100d / slot.Height, MidpointRounding.ToEven);
            Assert.Equal(normalizedWidth - 10, prepared.RecognizedTextWidth);
            using var rightSuffix = new Mat(prepared.NameImage!, new Rect(
                prepared.NameImage!.Width * 4 / 5, 0,
                prepared.NameImage.Width - prepared.NameImage.Width * 4 / 5, prepared.NameImage.Height));
            Assert.True(Cv2.CountNonZero(rightSuffix) > 0);
        }
    }

    private sealed class ToneMappedTrackingRows(Rows rows) : ICompanionNormalRowPipeline
    {
        public List<bool> ToneMappedFlags { get; } = [];
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            rows.Process(band, y, uiScale, fontType, isHdr);
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr,
            bool isToneMapped)
        {
            ToneMappedFlags.Add(isToneMapped);
            return rows.Process(band, y, uiScale, fontType, isHdr);
        }
        public void Dispose() => rows.Dispose();
    }
}
