using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindGoalTests
{
    [Fact]
    public void GoalsPersistAndCanBeRemovedWithoutChangingOtherDays()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        var monday = new DateOnly(2026,9,14);
        try
        {
            var store = new GrindGoalStore(path);
            store.Set([monday, monday.AddDays(1)], 2_000_000_000);
            var reloaded = new GrindGoalStore(path); reloaded.Load();
            Assert.Equal(2_000_000_000, reloaded.Goals[monday]);
            reloaded.Set([monday], null);
            var final = new GrindGoalStore(path); final.Load();
            Assert.False(final.Goals.ContainsKey(monday));
            Assert.Equal(2_000_000_000, final.Goals[monday.AddDays(1)]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DailyNetUsesLocalStartDateAndLatestVersionOfEachSession()
    {
        var start = new DateTimeOffset(2026,9,14,23,45,0,TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026,9,14)));
        var first = new LootHistoryEntry { SessionId = Guid.NewGuid(), StartedAt = start, UpdatedAt = start,
            Duration = TimeSpan.FromHours(2), SpotId = LootSpotCatalog.HermesiaId,
            Totals = new Dictionary<string, long> { ["rare"] = 1, ["favorite"] = 2, ["ordinary"] = 100, ["empty"] = 0 },
            SilverBeforeTax = 900, SilverAfterTax = 100, SilverIsComplete = true };
        var revised = first with { UpdatedAt = start.AddHours(2), SilverAfterTax = 250 };
        var second = first with { SessionId = Guid.NewGuid(), SilverAfterTax = 50 };
        var nextDay = first with { SessionId = Guid.NewGuid(), StartedAt = start.AddDays(1), SilverAfterTax = 80 };
        var totals = GrindGoalStore.DailyNet([first,revised,second,nextDay]);
        Assert.Equal(300, totals[new DateOnly(2026,9,14)]);
        Assert.Equal(80, totals[new DateOnly(2026,9,15)]);
        var drops = GrindGoalStore.DailyDrops([first,revised,second,nextDay], name => name != "ordinary");
        var today = drops[new DateOnly(2026,9,14)].ToDictionary();
        Assert.Equal(2, today["rare"]);
        Assert.Equal(4, today["favorite"]);
        Assert.Equal(2, today.Count);
        Assert.Equal(1, drops[new DateOnly(2026,9,15)].Single(d => d.Key == "rare").Value);
    }

    [Fact]
    public void InvalidFileCannotBeOverwrittenByEditingGoals()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        try { File.WriteAllText(path,"broken"); var store = new GrindGoalStore(path); store.Load();
            Assert.NotNull(store.Error);
            Assert.Throws<IOException>(() => store.Set([new DateOnly(2026,9,14)],100));
            Assert.Equal("broken",File.ReadAllText(path)); }
        finally { File.Delete(path); }
    }
}
