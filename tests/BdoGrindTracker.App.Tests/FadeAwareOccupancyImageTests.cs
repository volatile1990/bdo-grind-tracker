using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;

namespace BdoGrindTracker.App.Tests;

public sealed class FadeAwareOccupancyImageTests
{
    private static readonly DrawingRectangle[] RecordedSlots =
    [new(60, 373, 451, 74), new(60, 298, 451, 75), new(60, 224, 451, 75),
        new(60, 149, 451, 75), new(60, 75, 451, 75), new(60, 0, 451, 75)];

    [Fact]
    public void OriginalThreeToFourRowArrivalRetainsUnreadablePreviousAndCurrentPhysicalRows()
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var templateFrame = Fixture(1385);
        using var previousFrame = Fixture(1386);
        using var arrivalFrame = Fixture(1387);
        var templateRows = OriginalReadableRows();
        var previousRows = OriginalReadableRows();
        previousRows[2] = new(LootSource.Normal, 2, "Elion Heltnet x", null, null, 0, 0, null, "native-catalog-miss")
        {
            NativeY = 224
        };
        var arrivalRows = OriginalReadableRows();

        // Exact original OCR fields and timestamps. Previously derived occupancy
        // is omitted so every assertion depends on the original PNGs afresh.
        var initial = tracker.Observe(templateFrame, RecordedSlots, templateRows,
            DateTimeOffset.Parse("2026-09-13T18:07:26.6999341+00:00"), 1.49f);
        Assert.Equal(templateRows, initial);
        var previous = tracker.Observe(previousFrame, RecordedSlots, previousRows,
            DateTimeOffset.Parse("2026-09-13T18:07:26.8874376+00:00"), 1.49f);

        Assert.Equal(3, previous.Count);
        var previousUnknown = Assert.Single(previous, row => row.Slot == 2);
        Assert.Equal(previousRows[2], previousUnknown with { OccupancyEvidence = null });
        Assert.NotNull(previousUnknown.OccupancyEvidence);
        Assert.Contains(previousUnknown.OccupancyEvidence.Matches,
            match => match.PreviousSlot == 2 && match.Correlation >= .98);
        Assert.Null(previousUnknown.ItemName);
        Assert.Null(previousUnknown.Quantity);

        var result = tracker.Observe(arrivalFrame, RecordedSlots, arrivalRows,
            DateTimeOffset.Parse("2026-09-13T18:07:27.1082768+00:00"), 1.49f);

        Assert.Equal(4, result.Count);
        Assert.Equal(arrivalRows, result.Take(3).Select(row => row with { OccupancyEvidence = null }).ToArray());
        Assert.All(result.Take(3), row =>
        {
            Assert.Equal("Elion Follower's Helmet", row.ItemName);
            Assert.Equal(4, row.Quantity);
        });
        var fourth = Assert.Single(result, row => row.Slot == 3);
        Assert.Equal(new LootObservation(LootSource.Normal, 3, "", null, null, 0, 0, null, "visual-occupancy-only")
        {
            NativeY = 149
        }, fourth with { OccupancyEvidence = null });
        Assert.NotNull(fourth.OccupancyEvidence);
        Assert.Contains(fourth.OccupancyEvidence.Matches,
            match => match.PreviousSlot == 1 && match.Correlation >= .90);
        Assert.All(fourth.OccupancyEvidence.Matches, match => Assert.InRange(match.Correlation, .90, 1));
        Assert.All(result, row =>
        {
            Assert.Null(row.AppearanceEvidence);
            Assert.Null(row.FadeEvidence);
        });
        Assert.DoesNotContain(result, row => row.Slot > 3);
    }

    private static LootObservation[] OriginalReadableRows() =>
    [
        new(LootSource.Normal, 0, "Elion Followerts Helmet x 4", "Elion Follower's Helmet", 4,
            .9565217383205891, 0, null, null) { NativeY = 373, QuantityBounds = new(2, 1000) },
        new(LootSource.Normal, 1, "Elion Follower(s Helmet x 4", "Elion Follower's Helmet", 4,
            1, 0, null, null) { NativeY = 298, QuantityBounds = new(2, 1000) },
        new(LootSource.Normal, 2, "Elion Followerts Helmet x 4", "Elion Follower's Helmet", 4,
            .9565217383205891, 0, null, null) { NativeY = 224, QuantityBounds = new(2, 1000) }
    ];

    private static Mat Fixture(int sequence) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "normal-appearance", $"occupancy-unreadable-{sequence:000000}.png"));
}
