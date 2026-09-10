using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    [Fact]
    public async Task MovingNormalLogMidBatchPreservesCountedDropsAndAdoptsAllCrops()
    {
        var initial = Calibration();
        var moved = initial with { LootAnchorX = 480, LootAnchorY = 360 };
        var current = initial;
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17", 17),
            new Input(200, "BON Origin Shard x1", 1));
        using var analyzer = new CompanionLootFrameAnalyzer(initial, Matcher(), rows, new Names(rows),
            reconciliation: new CompanionReconciliationAdapter(trackRows: true),
            captureGuard: new LootPanelCaptureGuard(initial, () => current));
        var controlRows = new Rows(rows.Values);
        using var control = new CompanionLootFrameAnalyzer(initial, Matcher(), controlRows, new Names(controlRows),
            reconciliation: new CompanionReconciliationAdapter(trackRows: true));
        using var frame = new Bitmap(800, 600);
        var events = new List<LootEventView>();
        var expectedEvents = new List<LootEventView>();

        for (var sample = 0; sample < 20; sample++)
        {
            // The first ten frames have already booked these drops. Move halfway
            // through the following batch while those same rows remain visible.
            if (sample == 15) current = moved;
            var result = await analyzer.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(sample * 450), CancellationToken.None);
            var unchanged = await control.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(sample * 450), CancellationToken.None);
            events.AddRange(result.NewEvents);
            expectedEvents.AddRange(unchanged.NewEvents);
            Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
            var expected = sample < 15 ? initial : moved;
            Assert.Equal(CompanionNormalLootGeometry.CalculatePanelBounds(expected), result.PanelRegion);
            Assert.Equal(CompanionNormalLootGeometry.CalculateSlotCrops(expected), result.SlotRegions);
            Assert.Equal([0, 1], result.Observations.Select(row => row.Slot));
            Assert.Equal([250, 200], result.Observations.Select(row => row.NativeY));
        }
        events.AddRange(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(10)).NewEvents);
        expectedEvents.AddRange(control.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(10)).NewEvents);

        // Preserve the native cyclic counter's output exactly; moving the panel
        // must neither restart its identities nor discard an unfinished batch.
        Assert.NotEmpty(events);
        Assert.Equal(expectedEvents.Select(EventValue), events.Select(EventValue));
        Assert.True(analyzer.IsAvailable);
    }

    [Fact]
    public async Task NewDropDuringPositionReloadIsCountedOnceAlongsideTheExistingRow()
    {
        var initial = Calibration();
        var current = initial;
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17", 17));
        using var analyzer = new CompanionLootFrameAnalyzer(initial, Matcher(), rows, new Names(rows),
            reconciliation: new CompanionReconciliationAdapter(trackRows: true),
            captureGuard: new LootPanelCaptureGuard(initial, () => current));
        using var frame = new Bitmap(800, 600);
        var events = new List<LootEventView>();

        await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        events.AddRange(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddMilliseconds(100)).NewEvents);

        current = initial with { LootAnchorX = 480, LootAnchorY = 360 };
        rows.Values = [new(250, "BON Origin Shard x1", 1),
            new(200, "Black Crystal Fragment x17", 17)];
        for (var sample = 0; sample < 2; sample++)
        {
            var result = await analyzer.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(2250 + sample * 450), CancellationToken.None);
            events.AddRange(result.NewEvents);
            Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
        }
        events.AddRange(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(3)).NewEvents);

        Assert.Equal(17, Assert.Single(events, entry => entry.ItemName == "Black Crystal Fragment").Quantity);
        Assert.Equal(1, Assert.Single(events, entry => entry.ItemName == "BON Origin Shard").Quantity);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task MovingRareLogSeparatelyPreservesItsRecentDropAndNormalGeometry()
    {
        var initial = Calibration() with
        {
            HasRareLootAnchor = true,
            RareLootAnchorX = 500,
            RareLootAnchorY = 300,
        };
        var moved = initial with { RareLootAnchorX = 440, RareLootAnchorY = 350 };
        var current = initial;
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17", 17))
        {
            RareText = "BON Origin Shard x1",
        };
        using var analyzer = new CompanionLootFrameAnalyzer(initial, Matcher(), rows, new Names(rows),
            reconciliation: new CompanionReconciliationAdapter(trackRows: true),
            rareRowPipeline: new RareRows(),
            captureGuard: new LootPanelCaptureGuard(initial, () => current));
        var controlRows = new Rows(rows.Values) { RareText = rows.RareText };
        using var control = new CompanionLootFrameAnalyzer(initial, Matcher(), controlRows, new Names(controlRows),
            reconciliation: new CompanionReconciliationAdapter(trackRows: true), rareRowPipeline: new RareRows());
        using var frame = new Bitmap(800, 600);
        var events = new List<LootEventView>();
        var expectedEvents = new List<LootEventView>();

        for (var sample = 0; sample < 20; sample++)
        {
            if (sample == 15) current = moved;
            var result = await analyzer.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(sample * 450), CancellationToken.None);
            var unchanged = await control.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(sample * 450), CancellationToken.None);
            events.AddRange(result.NewEvents);
            expectedEvents.AddRange(unchanged.NewEvents);
            Assert.Equal(CompanionNormalLootGeometry.CalculatePanelBounds(initial), result.PanelRegion);
            Assert.Equal(CompanionNormalLootGeometry.CalculateSlotCrops(initial), result.SlotRegions);
            var expected = sample < 15 ? initial : moved;
            Assert.Equal(CompanionNormalLootGeometry.CalculateRarePanelBounds(expected), result.RarePanelRegion);
            Assert.Equal(CompanionNormalLootGeometry.CalculateRareBandCrop(expected), result.RareBandRegion);
            Assert.Equal(LootSpotCatalog.HermesiaId, result.SpotId);
        }
        events.AddRange(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(10)).NewEvents);
        expectedEvents.AddRange(control.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(10)).NewEvents);

        Assert.Equal(expectedEvents.Select(EventValue), events.Select(EventValue));
        Assert.Equal(1, Assert.Single(events, entry => entry.ItemName == "BON Origin Shard").Quantity);
    }

    [Fact]
    public async Task CaptureSetupForcesImmediatePositionReloadWithoutDiscardingTheSpotOrPendingDrop()
    {
        var initial = Calibration();
        var current = initial;
        var reads = 0;
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17", 17));
        using var analyzer = new CompanionLootFrameAnalyzer(initial, Matcher(), rows, new Names(rows),
            reconciliation: new CompanionReconciliationAdapter(trackRows: true),
            captureGuard: new LootPanelCaptureGuard(initial, () => { reads++; return current; }));
        using var frame = new Bitmap(800, 600);
        var now = DateTimeOffset.UtcNow;
        await analyzer.AnalyzeAsync(frame, now, CancellationToken.None);
        Assert.Equal(1, reads);

        current = initial with { LootAnchorX = 480, LootAnchorY = 360 };
        analyzer.ValidateCaptureSetup(frame.Size);

        Assert.Equal(2, reads);
        var completed = analyzer.CompleteSession(now);
        Assert.Equal(CompanionNormalLootGeometry.CalculatePanelBounds(current), completed.PanelRegion);
        Assert.Equal(CompanionNormalLootGeometry.CalculateSlotCrops(current), completed.SlotRegions);
        Assert.Equal(17, Assert.Single(completed.NewEvents).Quantity);
        rows.Values = [new(250, "Elion Follower's Helmet x18", 18)];
        var afterMove = await analyzer.AnalyzeAsync(frame, now.AddMilliseconds(450), CancellationToken.None);
        Assert.Equal(LootSpotCatalog.HermesiaId, afterMove.SpotId);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason, Assert.Single(afterMove.Observations).RejectionReason);
        Assert.Empty(analyzer.CompleteSession(now.AddSeconds(1)).NewEvents);
        Assert.True(analyzer.IsAvailable);
    }

    [Fact]
    public async Task PositionReloadReadsPixelsFromEveryNewSlotInsteadOfTheOldImageRegion()
    {
        var initial = Calibration();
        var moved = initial with { LootAnchorX = 480, LootAnchorY = 360 };
        var current = initial;
        var expectedColor = Color.Red;
        var rows = new Rows(new Input(250, "Black Crystal Fragment x17", 17));
        var checkedRows = new PositionCheckedRows(rows, () => expectedColor);
        using var analyzer = new CompanionLootFrameAnalyzer(initial, Matcher(), checkedRows, new Names(rows),
            captureGuard: new LootPanelCaptureGuard(initial, () => current));
        using var frame = new Bitmap(800, 600);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.Black);
            Mark(initial, Color.Red);
            Mark(moved, Color.Lime);
            void Mark(CompanionCalibration calibration, Color color)
            {
                using var brush = new SolidBrush(color);
                foreach (var slot in CompanionNormalLootGeometry.CalculateSlotCrops(calibration))
                    graphics.FillRectangle(brush, slot.X + 1, slot.Y + 1, 3, 3);
            }
        }

        await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        current = moved;
        expectedColor = Color.Lime;
        await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch.AddSeconds(2.25), CancellationToken.None);

        Assert.Equal(12, checkedRows.Calls);
    }

    private static (string Item, int Quantity, int Revision, int? Total) EventValue(LootEventView entry) =>
        (entry.ItemName, entry.Quantity, entry.Revision, entry.TotalDropQuantity);

    private sealed class PositionCheckedRows(Rows rows, Func<Color> expectedColor) : ICompanionNormalRowPipeline
    {
        public int Calls { get; private set; }
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr)
        {
            var expected = expectedColor();
            Assert.Equal(new Vec3b(expected.B, expected.G, expected.R), band.At<Vec3b>(2, 2));
            Calls++;
            return rows.Process(band, y, uiScale, fontType, isHdr);
        }
        public void Dispose() => rows.Dispose();
    }
}
