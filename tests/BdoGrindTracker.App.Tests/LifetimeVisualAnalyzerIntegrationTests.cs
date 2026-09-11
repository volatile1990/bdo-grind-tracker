using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeVisualAnalyzerIntegrationTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task AnalyzerAddsOccupancyAfterAcceptedPolicyAndResetsItsOwnImageHistory()
    {
        using var v2 = Analyzer(false);
        using var v3 = Analyzer(true);
        using var frame = GlyphFrame();
        var original = await v2.AnalyzeAsync(frame, Start, CancellationToken.None);
        var first = await v3.AnalyzeAsync(frame, Start, CancellationToken.None);
        var initial = Assert.Single(first.Observations);
        Assert.Equal(Helmet, initial.ItemName);
        Assert.Equal(4, initial.Quantity);
        Assert.Null(initial.OccupancyEvidence);
        Assert.Equal(original.Observations, first.Observations);

        var second = await v3.AnalyzeAsync(frame, Start.AddMilliseconds(200), CancellationToken.None);
        var confirmed = Assert.Single(second.Observations);
        Assert.NotNull(confirmed.OccupancyEvidence);
        Assert.Contains(confirmed.OccupancyEvidence.Matches, match => match.PreviousSlot == 0 && match.Correlation >= .9);
        Assert.Null(confirmed.AppearanceEvidence);
        Assert.Equal(initial, confirmed with { OccupancyEvidence = null });
        Assert.Equal(4, second.LootProjection!.Totals[Helmet]);
        var historical = await v2.AnalyzeAsync(frame, Start.AddMilliseconds(200), CancellationToken.None);
        Assert.Null(Assert.Single(historical.Observations).OccupancyEvidence);

        var changedRepresentation = await v3.AnalyzeAsync(frame, Start.AddMilliseconds(400), true, false, CancellationToken.None);
        Assert.Null(Assert.Single(changedRepresentation.Observations).OccupancyEvidence);
        v3.Reset();
        var restarted = await v3.AnalyzeAsync(frame, Start.AddMilliseconds(600), true, false, CancellationToken.None);
        Assert.Null(Assert.Single(restarted.Observations).OccupancyEvidence);
        Assert.Equal("lifetime-v3", restarted.VariantName.Split('+').Single(marker => marker.StartsWith("lifetime-")));
    }

    private static CompanionCalibration Calibration() => new("profile", "gamevariable.xml", "GameOption.txt",
        400, 300, 800, 600, 1, CompanionFontType.StrongSword, 0, false);

    private static CompanionLootFrameAnalyzer Analyzer(bool visual) => new(Calibration(),
        new CompanionItemMatcher([Helmet]), new Rows(), new Names(),
        new LifetimeNormalReconciliationAdapter(new LifetimeParsingContext(0, [new(Helmet, [])]), visual),
        quantityBoundsResolver: (_, _) => new(1, 1000));

    private static Bitmap GlyphFrame()
    {
        var bitmap = new Bitmap(800, 600);
        var slot = CompanionNormalLootGeometry.CalculateSlotCrops(Calibration())
            .OrderByDescending(bounds => bounds.Top).First();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        using var font = new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString(Helmet + " x 4", font, Brushes.White, slot.Left + 10, slot.Top + 12);
        return bitmap;
    }

    private sealed class Rows : ICompanionNormalRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) => new Row(y);
        public void Dispose() { }
    }

    private sealed class Row(int y) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => y != 250;
        public int RecognizedTextWidth => 300;
        public int TemplateQuantity => IsBlank ? -1 : 4;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 1;
        public float NameScale => 1;
        public Mat? NameImage { get; } = y == 250 ? new Mat(1, 1, MatType.CV_8UC1, Scalar.White) : null;
        public void Dispose() => NameImage?.Dispose();
    }

    private sealed class Names : ICompanionNameRecognizer
    {
        public string BackendName => "integration-test";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) =>
            new(Helmet + " x 4", new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
    }
}
