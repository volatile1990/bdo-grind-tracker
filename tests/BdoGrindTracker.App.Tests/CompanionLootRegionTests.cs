using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    [Fact]
    public async Task NormalAndRareRowsReceiveTheSamePixelsThroughRegionOnlyDecode()
    {
        var calibration = Calibration() with { HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300 };
        using var bitmap = new Bitmap(800, 600);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(47, 35, 78));
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 800, 600),
                Color.Orange, Color.Blue, 35);
            graphics.FillRectangle(brush, 0, 0, 800, 600);
        }
        using var full = CompanionFrameDecoder.Decode(bitmap);
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17"));
        var decoder = new RegionOnlyDecoder();
        var normal = new PixelNormalRows(full, calibration, rows);
        var rare = new PixelRareRows(full, calibration);
        using var analyzer = new CompanionLootFrameAnalyzer(calibration, Matcher(), normal, new Names(rows),
            frameDecoder: decoder, rareRowPipeline: rare);

        var result = await analyzer.AnalyzeAsync(bitmap, DateTimeOffset.UnixEpoch, CancellationToken.None);

        Assert.Equal(6, normal.Calls);
        Assert.Equal(1, rare.Calls);
        Assert.Equal(new[] { CompanionNormalLootGeometry.CalculatePanelBounds(calibration),
            CompanionNormalLootGeometry.CalculateRareBandCrop(calibration) }, decoder.Regions);
        Assert.Equal(2, result.Observations.Count);
    }

    private sealed class RegionOnlyDecoder : ICompanionBitmapDecoder
    {
        internal List<Rectangle> Regions { get; } = [];
        public Mat Decode(Bitmap bitmap) => throw new InvalidOperationException("Full-frame decode is unnecessary.");
        public Mat Decode(Bitmap bitmap, Rectangle region)
        {
            Regions.Add(region);
            return CompanionFrameDecoder.Decode(bitmap, region);
        }
    }

    private sealed class PixelNormalRows(Mat full, CompanionCalibration calibration, Rows rows) : ICompanionNormalRowPipeline
    {
        internal int Calls { get; private set; }
        public ICompanionPreparedRow Process(Mat band, int y, float scale, int font, bool hdr)
        {
            var panel = CompanionNormalLootGeometry.CalculatePanelBounds(calibration);
            var region = CompanionNormalLootGeometry.CalculateSlotCrops(calibration).Single(r => r.Y - panel.Y == y);
            using var expected = new Mat(full, new Rect(region.X, region.Y, region.Width, region.Height));
            Assert.Equal(0, Cv2.Norm(expected, band, NormTypes.INF));
            Calls++;
            return rows.Process(band, y, scale, font, hdr);
        }
        public void Dispose() => rows.Dispose();
    }

    private sealed class PixelRareRows(Mat full, CompanionCalibration calibration) : ICompanionRareRowPipeline
    {
        internal int Calls { get; private set; }
        public ICompanionPreparedRow Process(Mat band, int y, float scale, int font, bool hdr)
        {
            var region = CompanionNormalLootGeometry.CalculateRareBandCrop(calibration);
            using var expected = new Mat(full, new Rect(region.X, region.Y, region.Width, region.Height));
            Assert.Equal(0, Cv2.Norm(expected, band, NormTypes.INF));
            Assert.Equal(0, y);
            Calls++;
            return new Row(y, false, -1, 0, 9);
        }
        public void Dispose() { }
    }
}
