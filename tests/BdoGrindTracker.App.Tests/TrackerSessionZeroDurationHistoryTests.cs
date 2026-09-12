using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroDurationLootSurvivesStartingANewSession(bool firstDropArrivesDuringStop)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        if (firstDropArrivesDuringStop)
            fixture.Analyzer.CompletionResult = Analysis(("Black Crystal Fragment", 5));
        else
            await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 5));

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var id = fixture.Service.State.SessionId;
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Equal(5, fixture.Service.State.Loot.TotalQuantity);
        var saved = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(TimeSpan.Zero, saved.Duration);
        Assert.Equal(5, saved.Totals["Black Crystal Fragment"]);
        Assert.False(fixture.Service.State.CurrentGarmothUpload.IsReady);

        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.NotEqual(id, fixture.Service.State.SessionId);
        Assert.Empty(fixture.Service.State.Loot.Totals);
        Assert.Null(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load());
        var reloaded = Assert.Single(new LootHistoryStore(
            Path.Combine(fixture.DirectoryPath, "loot-history-v1.json")).Load());
        Assert.Equal(id, reloaded.SessionId);
        Assert.Equal(TimeSpan.Zero, reloaded.Duration);
        Assert.Equal(5, reloaded.Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task ZeroDurationSessionWithoutLootDoesNotCreateHistory()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Empty(fixture.HistoryStore.Load());
    }
}
