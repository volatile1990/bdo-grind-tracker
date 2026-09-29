using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task ShutdownExcludesOnlyTheLastSegmentsIdleAndKeepsEarnedLoot()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 10));
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        fixture.ResumeClocks();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 20));
        fixture.Time.Advance(TimeSpan.FromMinutes(2));

        await fixture.Service.ShutdownAsync();

        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(TimeSpan.FromMinutes(4), saved.Duration);
        Assert.Equal(30, saved.Totals["Black Crystal Fragment"]);
        Assert.Equal(TimeSpan.FromMinutes(4), new CurrentSessionStore(
            Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load()!.Duration);
        Assert.False(fixture.Service.State.ShutdownFailed);
    }

    [Theory]
    [InlineData("dark-energy-floodlands", "dark-energy-floodlands-orbita")]
    [InlineData("dehkia-ash-forest-unspecified", "dehkia-ii-ash-forest")]
    [InlineData("winter-tree-fossil-unspecified", "winter-tree-fossil-280")]
    public async Task HistoricalUnspecifiedSpotCanBeResolvedAndUploadedWithoutChangingCurrentSession(
        string familyId, string variantId)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await ProcessVariantFrame(fixture, familyId);
        var completedId = fixture.Service.State.SessionId;
        var expectedLoot = fixture.Service.State.Loot.Totals;
        fixture.Analyzer.CompletionResult = Analysis() with { SpotId = familyId };
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        var currentId = fixture.Service.State.SessionId;

        var selection = await fixture.Service.SelectSpotVariantAsync(completedId, variantId);

        Assert.True(selection.Succeeded, selection.Error);
        Assert.Equal(currentId, fixture.Service.State.SessionId);
        Assert.False(fixture.Service.State.HasSession);
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(variantId, saved.SpotId);
        Assert.Equal(expectedLoot, saved.Totals);
        Assert.True(GarmothUploadPreview.ForHistory(saved, fixture.Service.Prices,
            fixture.Service.Preferences.Tax).IsReady);
        var upload = await fixture.Service.UploadHistoryAsync(completedId);
        Assert.True(upload.Succeeded, upload.Error);
        Assert.Single(fixture.Requests);
        Assert.True(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        Assert.False((await fixture.Service.SelectSpotVariantAsync(completedId,
            LootSpotCatalog.VariantsFor(familyId).FirstOrDefault(spot => spot.Id != variantId)?.Id ?? variantId)).Succeeded);
    }

    [Fact]
    public async Task HistoricalSpotResolutionRejectsOtherFamiliesAndRollsBackOnWriteFailure()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await ProcessVariantFrame(fixture, "dark-energy-floodlands");
        var id = fixture.Service.State.SessionId;
        fixture.Analyzer.CompletionResult = Analysis() with { SpotId = "dark-energy-floodlands" };
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.False((await fixture.Service.SelectSpotVariantAsync(id, LootSpotCatalog.HermesiaId)).Succeeded);
        var blockedPath = Path.Combine(fixture.DirectoryPath, "loot-history-v1.json.tmp");
        Directory.CreateDirectory(blockedPath);
        try
        {
            Assert.False((await fixture.Service.SelectSpotVariantAsync(id, "dark-energy-floodlands-orbita")).Succeeded);
            Assert.Equal("dark-energy-floodlands", Assert.Single(fixture.Service.History).SpotId);
            Assert.Equal("dark-energy-floodlands", Assert.Single(fixture.HistoryStore.Load()).SpotId);
        }
        finally { Directory.Delete(blockedPath); }
        Assert.True((await fixture.Service.SelectSpotVariantAsync(id, "dark-energy-floodlands-orbita")).Succeeded);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("journal-block")]
    [InlineData("journal-error")]
    public async Task HistoricalSpotResolutionHonorsAutomaticAndRestartUploadGuards(string guard)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await ProcessVariantFrame(fixture, "dark-energy-floodlands");
        var id = fixture.Service.State.SessionId;
        fixture.Analyzer.CompletionResult = Analysis() with { SpotId = "dark-energy-floodlands" };
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        var field = guard == "pending" ? "_pendingAutomaticGarmothUploads" : "_garmothRestartBlocks";
        if (guard == "journal-error") SetField(fixture.Service, "_garmothPersistenceError", "Uploadjournal nicht lesbar.");
        else
        {
            var ids = (HashSet<Guid>)fixture.Service.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fixture.Service)!;
            ids.Add(id);
        }
        try
        {
            Assert.False((await fixture.Service.SelectSpotVariantAsync(id, "dark-energy-floodlands-orbita")).Succeeded);
            Assert.Equal("dark-energy-floodlands", Assert.Single(fixture.HistoryStore.Load()).SpotId);
        }
        finally
        {
            if (guard == "journal-error") SetField(fixture.Service, "_garmothPersistenceError", null!);
            else
                ((HashSet<Guid>)fixture.Service.GetType().GetField(field,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fixture.Service)!).Remove(id);
        }
    }
}
