using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    private const string ProjectionPublicationItem = "Black Crystal Fragment";

    [Fact]
    public async Task ProjectionPublicationWaitsTwoSecondsWithoutDelayingDropActivity()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var start = fixture.Time.GetUtcNow();

        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 1, 4, 1, start);
        Assert.Equal(0, fixture.Service.State.Loot.TotalQuantity);

        var secondArrival = start.AddSeconds(1);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(1), 2, 8, 2, secondArrival);
        Assert.Equal(0, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(TimeSpan.Zero, fixture.Activity.IdleDuration);

        // Identical raw revisions still advance the publication window. Their
        // delayed quantities must not move the actual arrival/activity clock.
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(1), 2, 8, 2, secondArrival);
        Assert.Equal(4, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(TimeSpan.FromSeconds(1), fixture.Activity.IdleDuration);

        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(1), 2, 8, 2, secondArrival);
        Assert.Equal(8, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(TimeSpan.FromSeconds(2), fixture.Activity.IdleDuration);
    }

    [Fact]
    public async Task PauseFlushesExactProjectionAndResumeCanPublishBeyondTheSameRawRevision()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var start = fixture.Time.GetUtcNow();
        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 1, 4, 1, start);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(1), 2, 8, 2, start.AddSeconds(1));
        Assert.Equal(0, fixture.Service.State.Loot.TotalQuantity);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(8, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(8, Assert.Single(fixture.HistoryStore.Load()).Totals[ProjectionPublicationItem]);

        // Resume only the fixture clocks, with desktop capture remaining off.
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        fixture.ResumeClocks();
        SetField(fixture.Service, "_captureSegmentCompleted", false);
        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 2, 8, 2, start.AddSeconds(1));
        Assert.Equal(8, fixture.Service.State.Loot.TotalQuantity);

        var resumedArrival = fixture.Time.GetUtcNow().AddMilliseconds(200);
        await ProcessProjectionAfter(fixture, TimeSpan.FromMilliseconds(200), 3, 12, 3, resumedArrival);
        Assert.Equal(8, fixture.Service.State.Loot.TotalQuantity);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 3, 12, 3, resumedArrival);
        Assert.Equal(12, fixture.Service.State.Loot.TotalQuantity);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(12, Assert.Single(fixture.HistoryStore.Load()).Totals[ProjectionPublicationItem]);
    }

    [Fact]
    public async Task ManualRelativeCorrectionSurvivesPendingProjectionRelease()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var start = fixture.Time.GetUtcNow();
        await ProcessProjectionAfter(fixture, TimeSpan.Zero, 1, 8, 2, start);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 1, 8, 2, start);
        Assert.Equal(8, fixture.Service.State.Loot.TotalQuantity);

        var arrival = fixture.Time.GetUtcNow().AddMilliseconds(200);
        await ProcessProjectionAfter(fixture, TimeSpan.FromMilliseconds(200), 2, 12, 3, arrival);
        Assert.Equal(8, fixture.Service.State.Loot.TotalQuantity);
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            ProjectionPublicationItem, 10, 8)).Succeeded);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);

        // Editing 8 to 10 is a relative +2 correction. The four automatic
        // quantities still waiting for publication must remain independent.
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 2, 12, 3, arrival);
        Assert.Equal(14, fixture.Service.State.Loot.TotalQuantity);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(14, Assert.Single(fixture.HistoryStore.Load()).Totals[ProjectionPublicationItem]);
    }

    private static async Task ProcessProjectionAfter(Fixture fixture, TimeSpan elapsed,
        long revision, long quantity, int dropCount, DateTimeOffset latestArrival)
    {
        fixture.Time.Advance(elapsed);
        var projection = new LootTotalsProjection(revision,
            new Dictionary<string, long> { [ProjectionPublicationItem] = quantity }, dropCount, latestArrival);
        var analysis = new FrameAnalysisResult([], [], 1, "synthetic-publication-test", 0, 0, 0, 0, null)
        {
            SpotId = LootSpotCatalog.HermesiaId,
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
