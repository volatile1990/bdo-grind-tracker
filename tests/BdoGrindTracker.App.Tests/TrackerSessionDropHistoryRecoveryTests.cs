using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionRestoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewerMatchingHistoryReplacesCheckpointDropTimesIncludingAnExplicitEmptyTimeline(bool empty)
    {
        using var directory = new TestDirectory();
        var checkpoint = CurrentSessionStoreTests.Example() with
        {
            DropHistory = [new(TimeSpan.FromSeconds(10), Item, 2)],
        };
        SessionDropSample[] expected = empty ? [] :
        [
            new(TimeSpan.FromSeconds(30), Item, 3),
            new(TimeSpan.FromSeconds(140), Item, 5),
        ];
        var newer = HistoryWithDropTimes(checkpoint, expected) with
        {
            Duration = TimeSpan.FromMinutes(3),
            Totals = new() { [Item] = 35 },
        };
        var unrelated = newer with
        {
            SessionId = Guid.NewGuid(),
            UpdatedAt = newer.UpdatedAt.AddHours(1),
            DropHistory = [new(TimeSpan.FromSeconds(150), Item, 9)],
        };
        new CurrentSessionStore(directory.CurrentPath).Save(checkpoint);
        new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json")).Save([newer, unrelated]);

        await using var fixture = new Fixture(directory.Path);

        Assert.Equal(checkpoint.SessionId, fixture.Service.State.SessionId);
        Assert.Equal(newer.Duration, fixture.Service.State.Elapsed);
        Assert.Equal(35, fixture.Service.State.Loot.Totals[Item]);
        Assert.Equal(expected, fixture.Service.State.DropHistory);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(0, fixture.Captures);

        fixture.Time.Advance(TimeSpan.FromHours(1));
        fixture.Service.RefreshPendingState();
        Assert.Equal(expected, fixture.Service.State.DropHistory);
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        var saved = new CurrentSessionStore(directory.CurrentPath).Load()!;
        Assert.Equal(newer.Duration, saved.Duration);
        Assert.Equal(35, saved.Totals[Item]);
        Assert.Equal(expected, saved.DropHistory);
        Assert.Equal(expected, fixture.History.Load().Single(entry => entry.SessionId == checkpoint.SessionId).DropHistory);
        Assert.Equal(unrelated.DropHistory,
            fixture.History.Load().Single(entry => entry.SessionId == unrelated.SessionId).DropHistory);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(35)]
    public async Task NewerLegacyHistoryKeepsCheckpointTimesOnlyForItemsWithUnchangedQuantities(long changedQuantity)
    {
        using var directory = new TestDirectory();
        const string unchanged = "Black Stone";
        const string removed = "Caphras Stone";
        const string added = "Memory Fragment";
        var preserved = new SessionDropSample(TimeSpan.FromSeconds(40), unchanged, 2);
        var checkpoint = CurrentSessionStoreTests.Example() with
        {
            Totals = new() { [Item] = 27, [unchanged] = 2, [removed] = 1 },
            DropHistory =
            [
                new(TimeSpan.FromSeconds(10), Item, 3),
                preserved,
                new(TimeSpan.FromSeconds(50), removed, 1),
            ],
        };
        var newer = HistoryWithDropTimes(checkpoint, null) with
        {
            Duration = TimeSpan.FromMinutes(3),
            // Matching is case insensitive even when an older writer used different casing.
            Totals = new() { [Item] = changedQuantity, [unchanged.ToLowerInvariant()] = 2, [added] = 4 },
        };
        new CurrentSessionStore(directory.CurrentPath).Save(checkpoint);
        new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json")).Save([newer]);

        await using var fixture = new Fixture(directory.Path);

        Assert.Equal(preserved, Assert.Single(fixture.Service.State.DropHistory));
        Assert.Equal(changedQuantity, fixture.Service.State.Loot.Totals[Item]);
        Assert.Equal(4, fixture.Service.State.Loot.Totals[added]);
        Assert.False(fixture.Service.State.Loot.Totals.ContainsKey(removed));
        fixture.Service.RefreshPendingState();
        Assert.Equal(preserved, Assert.Single(fixture.Service.State.DropHistory));
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        var saved = new CurrentSessionStore(directory.CurrentPath).Load()!;
        Assert.Equal(preserved, Assert.Single(saved.DropHistory!));
        Assert.Equal(changedQuantity, saved.Totals[Item]);
        Assert.Equal(preserved, Assert.Single(Assert.Single(fixture.History.Load()).DropHistory!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewerUnrelatedHistoryNeverSuppliesDropTimesToTheRestoredSession(bool checkpointHasTimes)
    {
        using var directory = new TestDirectory();
        SessionDropSample[] expected = checkpointHasTimes
            ? [new(TimeSpan.FromSeconds(20), Item, 2)] : [];
        var checkpoint = CurrentSessionStoreTests.Example() with
        {
            DropHistory = checkpointHasTimes ? expected : null,
        };
        var unrelated = HistoryWithDropTimes(checkpoint, [new(TimeSpan.FromSeconds(90), Item, 7)]) with
        {
            SessionId = Guid.NewGuid(),
            Totals = new() { [Item] = 99 },
        };
        new CurrentSessionStore(directory.CurrentPath).Save(checkpoint);
        new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json")).Save([unrelated]);

        await using var fixture = new Fixture(directory.Path);

        Assert.Equal(checkpoint.SessionId, fixture.Service.State.SessionId);
        Assert.Equal(checkpoint.Duration, fixture.Service.State.Elapsed);
        Assert.Equal(checkpoint.Totals[Item], fixture.Service.State.Loot.Totals[Item]);
        Assert.Equal(expected, fixture.Service.State.DropHistory);
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);

        Assert.Equal(expected, new CurrentSessionStore(directory.CurrentPath).Load()!.DropHistory);
        Assert.Equal(expected, fixture.History.Load().Single(entry => entry.SessionId == checkpoint.SessionId).DropHistory);
        var retainedUnrelated = fixture.History.Load().Single(entry => entry.SessionId == unrelated.SessionId);
        Assert.Equal(unrelated.DropHistory, retainedUnrelated.DropHistory);
        Assert.Equal(99, retainedUnrelated.Totals[Item]);
    }

    private static LootHistoryEntry HistoryWithDropTimes(CurrentSessionSnapshot checkpoint,
        IReadOnlyList<SessionDropSample>? drops) => new()
    {
        SessionId = checkpoint.SessionId,
        StartedAt = checkpoint.StartedAt!.Value,
        UpdatedAt = checkpoint.UpdatedAt.AddMinutes(1),
        Duration = checkpoint.Duration,
        SpotId = checkpoint.SpotId!,
        Totals = new(checkpoint.Totals),
        DropHistory = drops,
        SilverBeforeTax = 0,
        SilverAfterTax = 0,
        SilverIsComplete = false,
    };
}
