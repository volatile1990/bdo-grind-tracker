using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData("dark-energy-floodlands", "dark-energy-floodlands-orbita")]
    [InlineData("dehkia-ash-forest-unspecified", "dehkia-ii-ash-forest")]
    [InlineData("winter-tree-fossil-unspecified", "winter-tree-fossil-280")]
    public async Task SpotVariantSurvivesIncomingFramesCompletionAndRestore(string familyId, string selectedId)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await ProcessVariantFrame(fixture, familyId);
        var id = fixture.Service.State.SessionId;
        var totals = fixture.Service.State.Loot.TotalQuantity;
        Assert.True(fixture.Service.State.CanSelectSpotVariant);

        Assert.True((await fixture.Service.SelectSpotVariantAsync(id, selectedId)).Succeeded);
        Assert.Equal(selectedId, Assert.Single(fixture.HistoryStore.Load()).SpotId);
        var store = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName));
        Assert.Equal(selectedId, store.Load()!.SpotId);

        await ProcessVariantFrame(fixture, familyId);
        Assert.Equal(selectedId, fixture.Service.State.SpotId);
        Assert.Equal(totals * 2, fixture.Service.State.Loot.TotalQuantity);
        fixture.Analyzer.CompletionResult = Analysis() with { SpotId = familyId };
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(selectedId, fixture.Service.State.SpotId);

        await using var restored = new Fixture(autoUpload: false, restoredSession: store.Load());
        Assert.Equal(id, restored.Service.State.SessionId);
        Assert.Equal(selectedId, restored.Service.State.SpotId);
        restored.ResumeClocks();
        await ProcessVariantFrame(restored, familyId);
        Assert.Equal(selectedId, restored.Service.State.SpotId);
        Assert.Equal(totals * 3, restored.Service.State.Loot.TotalQuantity);
    }

    [Fact]
    public async Task SpotVariantRejectsStaleSessionAndUnrelatedSpots()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await ProcessVariantFrame(fixture, "dark-energy-floodlands");
        Assert.False((await fixture.Service.SelectSpotVariantAsync(Guid.NewGuid(), "dark-energy-floodlands-orbita")).Succeeded);
        Assert.False((await fixture.Service.SelectSpotVariantAsync(fixture.Service.State.SessionId, LootSpotCatalog.HermesiaId)).Succeeded);
        Assert.Equal("dark-energy-floodlands", fixture.Service.State.SpotId);
        Assert.Empty(fixture.HistoryStore.Load());
    }

    [Theory]
    [InlineData("loot-history-v1.json")]
    [InlineData("current-session-v1.json")]
    public async Task SpotVariantPersistenceFailureRestoresOriginalSpotAndHistory(string blockedFile)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await ProcessVariantFrame(fixture, "dark-energy-floodlands");
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        var blockedPath = Path.Combine(fixture.DirectoryPath, blockedFile + ".tmp");
        Directory.CreateDirectory(blockedPath);
        try
        {
            Assert.False((await fixture.Service.SelectSpotVariantAsync(fixture.Service.State.SessionId, "dark-energy-floodlands-orbita")).Succeeded);
            Assert.Equal("dark-energy-floodlands", fixture.Service.State.SpotId);
            Assert.Equal("dark-energy-floodlands", Assert.Single(fixture.Service.History).SpotId);
            Assert.Equal("dark-energy-floodlands", Assert.Single(fixture.HistoryStore.Load()).SpotId);
            Assert.Equal("dark-energy-floodlands", new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load()!.SpotId);
        }
        finally { Directory.Delete(blockedPath); }
        Assert.True((await fixture.Service.SelectSpotVariantAsync(fixture.Service.State.SessionId, "dark-energy-floodlands-orbita")).Succeeded);
        Assert.Equal("dark-energy-floodlands-orbita", fixture.Service.State.SpotId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CompletedOrUploadedSessionCannotChangeVariantAfterRestore(bool submitted, bool unknownUpload)
    {
        var snapshot = CurrentSessionStoreTests.Example() with
        {
            SpotId = "dark-energy-floodlands-orbita",
            SessionSubmitted = submitted,
            Uploads = new GarmothUploadState
            {
                IsBlocked = unknownUpload,
                TransmittedTotals = !submitted && !unknownUpload ? new() { ["Black Stone"] = 1 } : [],
            },
        };
        await using var fixture = new Fixture(autoUpload: false, restoredSession: snapshot);
        Assert.False(fixture.Service.State.CanSelectSpotVariant);
        Assert.False((await fixture.Service.SelectSpotVariantAsync(snapshot.SessionId, "dark-energy-floodlands-zephyros")).Succeeded);
        Assert.Equal(snapshot.SpotId, fixture.Service.State.SpotId);
    }

    [Fact]
    public async Task SpotVariantIsLockedDuringAnUpload()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await ProcessVariantFrame(fixture, "dark-energy-floodlands");
        SetField(fixture.Service, "_garmothUploadInProgress", true);
        fixture.Service.RefreshPendingState();
        try
        {
            Assert.False(fixture.Service.State.CanSelectSpotVariant);
            Assert.False((await fixture.Service.SelectSpotVariantAsync(fixture.Service.State.SessionId, "dark-energy-floodlands-orbita")).Succeeded);
            Assert.Equal("dark-energy-floodlands", fixture.Service.State.SpotId);
        }
        finally { SetField(fixture.Service, "_garmothUploadInProgress", false); }
    }

    [Theory]
    [InlineData("dark-energy-floodlands")]
    [InlineData("dehkia-ash-forest-unspecified")]
    [InlineData("winter-tree-fossil-unspecified")]
    public async Task ResolvedSpotVariantIsUsedWhenTheSessionIsCompleted(string familyId)
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        fixture.Time.Advance(TimeSpan.FromMinutes(59));
        await ProcessVariantFrame(fixture, familyId);
        await fixture.Service.TickAsync();
        Assert.Empty(fixture.Requests);
        Assert.False(fixture.Service.State.AutomaticSuspended);
        Assert.False(fixture.Service.State.UploadBlocked);
        Assert.True(fixture.Service.State.CanSelectSpotVariant);
        var variantId = LootSpotCatalog.VariantsFor(familyId)[0].Id;
        Assert.True((await fixture.Service.SelectSpotVariantAsync(fixture.Service.State.SessionId, variantId)).Succeeded);
        await fixture.Service.TickAsync();
        Assert.Empty(fixture.Requests);
        Assert.False(fixture.Service.State.AutomaticSuspended);
        Assert.Equal(variantId, fixture.Service.State.SpotId);
        await fixture.Service.PauseAsync();
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Single(fixture.Requests);
        Assert.Equal(variantId, Assert.Single(fixture.Service.History).SpotId);

    }

    private static async Task ProcessVariantFrame(Fixture fixture, string detectedSpotId)
    {
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        var trashName = LootSpotCatalog.VariantsFor(detectedSpotId)[0].PrimaryTrashItemName!;
        fixture.Analyzer.NextResult = Analysis((trashName, 10))
            with { SpotId = detectedSpotId };
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(fixture.Analyzer.Calls + 1, fixture.Time.GetUtcNow()), CancellationToken.None);
        fixture.Service.RefreshPendingState();
    }
}
