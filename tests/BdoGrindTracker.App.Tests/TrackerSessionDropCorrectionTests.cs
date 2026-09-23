using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    private const string CorrectedTimelineRing = "Twilight of the End - Ring";

    [Fact]
    public async Task ProjectedRingRetractionRemovesDuplicateMarkerAndPersistsOnlyTheRemainingDrop()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var metrics = new OverlayMetrics();
        var preferences = fixture.Service.Preferences with { FavoriteItems = [CorrectedTimelineRing] };
        var firstArrival = fixture.Time.GetUtcNow().AddSeconds(10);

        await ProcessRingProjectionAfter(fixture, TimeSpan.FromSeconds(10), 1, 1, firstArrival);
        Assert.Empty(fixture.Service.State.DropHistory);
        await ProcessRingProjectionAfter(fixture, LootProjectionBuffer.ConfirmationDelay, 1, 1, firstArrival);
        var originalDrop = Assert.Single(fixture.Service.State.DropHistory,
            drop => drop.ItemName == CorrectedTimelineRing);
        Assert.Single(metrics.Update(fixture.Service.State, preferences).DropMarkers);

        var secondArrival = fixture.Time.GetUtcNow().AddSeconds(10);
        await ProcessRingProjectionAfter(fixture, TimeSpan.FromSeconds(10), 2, 2, secondArrival);
        await ProcessRingProjectionAfter(fixture, LootProjectionBuffer.ConfirmationDelay, 2, 2, secondArrival);
        Assert.Equal(2, fixture.Service.State.Loot.Totals[CorrectedTimelineRing]);
        var duplicated = metrics.Update(fixture.Service.State, preferences);
        Assert.Equal(2, duplicated.DropMarkers.Count);
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        // The estimate is revised after both increases survived the publication
        // buffer. Inventory and the already-visible timeline must retract together.
        await ProcessRingProjectionAfter(fixture, TimeSpan.FromSeconds(1), 3, 1, secondArrival, 1);

        Assert.Equal(1, fixture.Service.State.Loot.Totals[CorrectedTimelineRing]);
        Assert.Equal(originalDrop, Assert.Single(fixture.Service.State.DropHistory,
            drop => drop.ItemName == CorrectedTimelineRing));
        var corrected = metrics.Update(fixture.Service.State, preferences);
        var marker = Assert.Single(corrected.DropMarkers);
        Assert.Equal(originalDrop.Elapsed, marker.Elapsed);
        Assert.Equal(1, marker.Item.Quantity);
        Assert.Equal(1, Assert.Single(corrected.Drops,
            item => item.CanonicalName == CorrectedTimelineRing).Quantity);
        Assert.Equal(2, duplicated.DropMarkers.Count);

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        var expected = fixture.Service.State.DropHistory.ToArray();
        var saved = LoadDropCheckpoint(fixture);
        Assert.Equal(expected, saved.DropHistory);
        Assert.Equal(expected, Assert.Single(fixture.HistoryStore.Load()).DropHistory);
        await using var restored = new Fixture(autoUpload: false, restoredSession: saved);
        Assert.Equal(expected, restored.Service.State.DropHistory);
        Assert.Single(new OverlayMetrics().Update(restored.Service.State, preferences).DropMarkers);
    }

    [Fact]
    public async Task ProjectedRingRetractionRecoveryAndRetractionLeaveNoGhostMarkersInSavedSession()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var metrics = new OverlayMetrics();
        var preferences = fixture.Service.Preferences with { FavoriteItems = [CorrectedTimelineRing] };
        var arrival = fixture.Time.GetUtcNow().AddSeconds(10);

        await ProcessRingProjectionAfter(fixture, TimeSpan.FromSeconds(10), 1, 1, arrival);
        await ProcessRingProjectionAfter(fixture, LootProjectionBuffer.ConfirmationDelay, 1, 1, arrival);
        Assert.Single(metrics.Update(fixture.Service.State, preferences).DropMarkers);
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        await ProcessRingProjectionAfter(fixture, TimeSpan.FromSeconds(1), 2, 0, arrival, 1);
        Assert.Empty(metrics.Update(fixture.Service.State, preferences).DropMarkers);
        Assert.False(fixture.Service.State.Loot.Totals.ContainsKey(CorrectedTimelineRing));

        // Recovery is a replacement estimate of the same arrival. It should
        // leave one current marker without retaining the earlier retracted one.
        await ProcessRingProjectionAfter(fixture, TimeSpan.FromSeconds(1), 3, 1, arrival, 2);
        await ProcessRingProjectionAfter(fixture, LootProjectionBuffer.ConfirmationDelay, 3, 1, arrival, 2);
        Assert.Equal(1, fixture.Service.State.Loot.Totals[CorrectedTimelineRing]);
        Assert.Single(metrics.Update(fixture.Service.State, preferences).DropMarkers);

        await ProcessRingProjectionAfter(fixture, TimeSpan.FromSeconds(1), 4, 0, arrival, 3);
        var corrected = metrics.Update(fixture.Service.State, preferences);
        Assert.Empty(corrected.DropMarkers);
        Assert.DoesNotContain(corrected.Drops, item => item.CanonicalName == CorrectedTimelineRing);
        Assert.DoesNotContain(fixture.Service.State.DropHistory, drop => drop.ItemName == CorrectedTimelineRing);

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        var expected = fixture.Service.State.DropHistory.ToArray();
        var saved = LoadDropCheckpoint(fixture);
        Assert.False(saved.Totals.ContainsKey(CorrectedTimelineRing));
        Assert.Equal(expected, saved.DropHistory);
        Assert.Equal(expected, Assert.Single(fixture.HistoryStore.Load()).DropHistory);
        await using var restored = new Fixture(autoUpload: false, restoredSession: saved);
        Assert.Equal(expected, restored.Service.State.DropHistory);
        Assert.Empty(new OverlayMetrics().Update(restored.Service.State, preferences).DropMarkers);
    }

    private static async Task ProcessRingProjectionAfter(Fixture fixture, TimeSpan elapsed,
        long revision, int ringQuantity, DateTimeOffset latestArrival, long correctionRevision = 0)
    {
        fixture.Time.Advance(elapsed);
        // Retain ordinary loot so even the fully retracted ring session has a
        // history entry, as in a real grind with an ongoing trash-loot baseline.
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 10 };
        if (ringQuantity > 0) totals[CorrectedTimelineRing] = ringQuantity;
        var projection = new LootTotalsProjection(revision, totals, 1 + ringQuantity, latestArrival)
        {
            QuantityCorrectionRevision = correctionRevision,
        };
        var analysis = Analysis() with
        {
            LootProjection = projection,
            TrackingResult = new([], []) { LootProjection = projection },
        };
        fixture.Analyzer.NextResult = analysis;
        fixture.Analyzer.CompletionResult = analysis;
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(fixture.Analyzer.Calls + 1, fixture.Time.GetUtcNow()), CancellationToken.None);
        fixture.Service.RefreshPendingState();
    }
}
