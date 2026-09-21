using System.Text.Json.Nodes;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    private const string PersistedRareDrop = "Vestige of Everlight";

    [Fact]
    public async Task ShutdownRestoresRareDropAgeWithoutOfflineTimeAndResumeAddsOnlyTheNewDrop()
    {
        await using var original = new Fixture(autoUpload: false);
        original.Begin();
        await original.ProcessAfter(TimeSpan.FromSeconds(30), (PersistedRareDrop, 1));
        await original.ProcessAfter(TimeSpan.FromSeconds(90), ("Black Crystal Fragment", 7));
        var expected = original.Service.State.DropHistory.ToArray();
        var sessionId = original.Service.State.SessionId;

        await original.Service.ShutdownAsync();

        Assert.False(original.Service.State.ShutdownFailed);
        var saved = LoadDropCheckpoint(original);
        Assert.Equal(TimeSpan.FromMinutes(2), saved.Duration);
        Assert.Equal(expected, saved.DropHistory);
        Assert.Equal(expected, Assert.Single(original.HistoryStore.Load()).DropHistory);

        await using var restored = new Fixture(autoUpload: false, restoredSession: saved);
        Assert.Equal(sessionId, restored.Service.State.SessionId);
        Assert.False(restored.Service.State.IsRunning);
        Assert.Equal(expected, restored.Service.State.DropHistory);
        var rare = Assert.Single(restored.Service.State.DropHistory, drop => drop.ItemName == PersistedRareDrop);
        Assert.Equal(TimeSpan.FromSeconds(30), rare.Elapsed);
        Assert.Equal("1m ago", LastDropPresentation.Create(rare.Elapsed, restored.Service.State.Elapsed, "en").Label);

        restored.Time.Advance(TimeSpan.FromHours(8));
        await restored.Service.TickAsync();

        Assert.Equal(TimeSpan.FromMinutes(2), restored.Service.State.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(90), restored.Service.State.Elapsed - rare.Elapsed);
        Assert.Equal(expected, restored.Service.State.DropHistory);
        Assert.Equal(0, restored.Captures);

        restored.ResumeClocks();
        await restored.ProcessAfter(TimeSpan.FromSeconds(15), (PersistedRareDrop, 2));
        var resumedDrops = restored.Service.State.DropHistory.ToArray();
        Assert.Equal(3, resumedDrops.Length);
        Assert.Equal(expected, resumedDrops.Take(2));
        Assert.Equal(new SessionDropSample(TimeSpan.FromSeconds(135), PersistedRareDrop, 2), resumedDrops[^1]);
        Assert.Equal(3, restored.Service.State.Loot.Totals[PersistedRareDrop]);
        Assert.Equal("Just now", LastDropPresentation.Create(resumedDrops[^1].Elapsed,
            restored.Service.State.Elapsed, "en").Label);

        Assert.True((await restored.Service.SaveSessionAsync()).Succeeded);
        restored.Service.RefreshPendingState();
        restored.Service.RefreshPendingState();
        Assert.Equal(resumedDrops, restored.Service.State.DropHistory);
        Assert.Equal(resumedDrops, LoadDropCheckpoint(restored).DropHistory);
        Assert.Equal(resumedDrops, Assert.Single(restored.HistoryStore.Load()).DropHistory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task ManualRareQuantityCorrectionPreservesDropTimeAcrossRestoreAndNewSessionClearsIt(int correctedQuantity)
    {
        await using var original = new Fixture(autoUpload: false);
        original.Begin();
        await original.ProcessAfter(TimeSpan.FromSeconds(20), (PersistedRareDrop, 1));
        await original.ProcessAfter(TimeSpan.FromSeconds(40), ("Black Crystal Fragment", 3));
        var expected = original.Service.State.DropHistory.ToArray();

        var correction = await original.Service.UpdateLootQuantityAsync(original.Service.State.SessionId,
            PersistedRareDrop, correctedQuantity, 1);

        Assert.True(correction.Succeeded, correction.Error);
        Assert.Equal(expected, original.Service.State.DropHistory);
        Assert.Equal(2, original.Service.State.Loot.ConfirmedEventCount);
        Assert.Equal(correctedQuantity, original.Service.State.Loot.Totals[PersistedRareDrop]);
        await original.Service.ShutdownAsync();
        Assert.False(original.Service.State.ShutdownFailed);
        var saved = LoadDropCheckpoint(original);
        Assert.Equal(expected, saved.DropHistory);
        Assert.Equal(expected, Assert.Single(original.HistoryStore.Load()).DropHistory);

        await using var restored = new Fixture(autoUpload: false, restoredSession: saved);
        Assert.Equal(expected, restored.Service.State.DropHistory);
        Assert.Equal(correctedQuantity, restored.Service.State.Loot.Totals[PersistedRareDrop]);
        Assert.Contains(PersistedRareDrop, restored.Service.State.ManualLootItems);
        Assert.True((await restored.Service.NewSessionAsync()).Succeeded);
        Assert.Empty(restored.Service.State.DropHistory);
        Assert.Empty(restored.Service.State.Loot.Totals);
        Assert.Null(new CurrentSessionStore(Path.Combine(restored.DirectoryPath, CurrentSessionStore.FileName)).Load());
        Assert.Equal(expected, Assert.Single(restored.HistoryStore.Load()).DropHistory);

        restored.Begin();
        await restored.ProcessAfter(TimeSpan.FromSeconds(7), (PersistedRareDrop, 1));
        Assert.Equal(new SessionDropSample(TimeSpan.FromSeconds(7), PersistedRareDrop, 1),
            Assert.Single(restored.Service.State.DropHistory));
        Assert.NotEqual(saved.SessionId, restored.Service.State.SessionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingOcrRareDropIsPersistedExactlyOnceBeforeTheNextUiPublish(bool shutdown)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(10), (PersistedRareDrop, 1));
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        fixture.Analyzer.NextResult = Analysis((PersistedRareDrop, 2));
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(2, fixture.Time.GetUtcNow()), CancellationToken.None);
        Assert.Single(fixture.Service.State.DropHistory);
        Assert.Equal(1, fixture.Service.State.Loot.ConfirmedEventCount);

        if (shutdown)
        {
            await fixture.Service.ShutdownAsync();
            Assert.False(fixture.Service.State.ShutdownFailed);
        }
        else
        {
            var result = await fixture.Service.SaveSessionAsync();
            Assert.True(result.Succeeded, result.Error);
            fixture.Service.RefreshPendingState();
            fixture.Service.RefreshPendingState();
        }

        SessionDropSample[] expected =
        [
            new(TimeSpan.FromSeconds(10), PersistedRareDrop, 1),
            new(TimeSpan.FromSeconds(20), PersistedRareDrop, 2),
        ];
        Assert.Equal(3, fixture.Service.State.Loot.Totals[PersistedRareDrop]);
        Assert.Equal(expected, fixture.Service.State.DropHistory);
        var saved = LoadDropCheckpoint(fixture);
        Assert.Equal(expected, saved.DropHistory);
        Assert.Equal(expected, Assert.Single(fixture.HistoryStore.Load()).DropHistory);

        await using var restored = new Fixture(autoUpload: false, restoredSession: saved);
        restored.Service.RefreshPendingState();
        Assert.Equal(expected, restored.Service.State.DropHistory);
        Assert.Equal(2, restored.Service.State.Loot.ConfirmedEventCount);
    }

    [Fact]
    public async Task LegacyCheckpointWithoutDropHistoryDoesNotInventRareDropTimes()
    {
        await using var source = new Fixture(autoUpload: false);
        var path = Path.Combine(source.DirectoryPath, CurrentSessionStore.FileName);
        new CurrentSessionStore(path).Save(CurrentSessionStoreTests.Example() with
        {
            Totals = new() { [PersistedRareDrop] = 3 },
        });
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(document["Session"]!.AsObject().Remove("DropHistory"));
        File.WriteAllText(path, document.ToJsonString());
        var legacy = new CurrentSessionStore(path).Load();
        Assert.NotNull(legacy);
        Assert.Null(legacy.DropHistory);

        await using var restored = new Fixture(autoUpload: false, restoredSession: legacy);
        Assert.Equal(3, restored.Service.State.Loot.Totals[PersistedRareDrop]);
        Assert.Empty(restored.Service.State.DropHistory);
        Assert.Equal("—", LastDropPresentation.Create(null, restored.Service.State.Elapsed, "en").Label);
        restored.Time.Advance(TimeSpan.FromHours(8));
        await restored.Service.TickAsync();
        Assert.True((await restored.Service.SaveSessionAsync()).Succeeded);
        Assert.Empty(restored.Service.State.DropHistory);
        Assert.Empty(LoadDropCheckpoint(restored).DropHistory!);

        restored.ResumeClocks();
        await restored.ProcessAfter(TimeSpan.FromSeconds(10), (PersistedRareDrop, 1));
        Assert.Equal(new SessionDropSample(TimeSpan.FromSeconds(130), PersistedRareDrop, 1),
            Assert.Single(restored.Service.State.DropHistory));
        Assert.Equal(4, restored.Service.State.Loot.Totals[PersistedRareDrop]);
    }

    private static CurrentSessionSnapshot LoadDropCheckpoint(Fixture fixture) =>
        Assert.IsType<CurrentSessionSnapshot>(new CurrentSessionStore(
            Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load());
}
