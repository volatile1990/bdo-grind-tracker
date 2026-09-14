using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData("Black Stone", 1, 0)]
    [InlineData("Black Crystal Fragment", 5, 1)]
    public async Task PendingOcrDropAndManualCorrectionProduceOnlyObservedDropMarkers(
        string correctedItem, long correctedQuantity, long originalQuantity)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(10), ("Black Crystal Fragment", 1));
        Assert.Single(fixture.Service.State.DropHistory);

        // A second OCR drop is in the mailbox but has not reached the UI yet.
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 1));
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(2, fixture.Time.GetUtcNow()), CancellationToken.None);
        Assert.Equal(1, fixture.Service.State.Loot.ConfirmedEventCount);

        var result = await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            correctedItem, correctedQuantity, originalQuantity);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, fixture.Service.State.Loot.ConfirmedEventCount);
        Assert.Equal(correctedItem == "Black Stone" ? 1 : 6,
            fixture.Service.State.Loot.Totals[correctedItem]);
        Assert.Equal(2, fixture.Service.State.DropHistory.Count);
        Assert.All(fixture.Service.State.DropHistory, drop =>
        {
            Assert.Equal("Black Crystal Fragment", drop.ItemName);
            Assert.Equal(1, drop.Quantity);
        });
        fixture.Service.RefreshPendingState();
        Assert.Equal(2, fixture.Service.State.DropHistory.Count);
    }
}
