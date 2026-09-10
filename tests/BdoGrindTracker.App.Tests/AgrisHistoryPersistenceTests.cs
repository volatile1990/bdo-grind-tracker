using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class AgrisHistoryPersistenceTests
{
    [Fact]
    public void AgrisDurationsKeepTheirPrecisionAcrossSaveAndLoad()
    {
        using var folder = new Folder();
        var store = new LootHistoryStore(folder.HistoryPath);
        var entry = Entry() with
        {
            AgrisActiveDuration = TimeSpan.FromSeconds(12.125),
            AgrisObservedDuration = TimeSpan.FromSeconds(44.875),
        };

        store.Save([entry]);
        var restored = Assert.Single(store.Load());

        Assert.Equal(entry.AgrisActiveDuration, restored.AgrisActiveDuration);
        Assert.Equal(entry.AgrisObservedDuration, restored.AgrisObservedDuration);
        Assert.Equal(entry.Duration, restored.Duration);
    }

    [Fact]
    public void LegacyHistoryWithoutAgrisFieldsStaysUnknownAndWritable()
    {
        using var folder = new Folder();
        var entry = JsonSerializer.SerializeToNode(Entry())!.AsObject();
        entry.Remove(nameof(LootHistoryEntry.AgrisActiveDuration));
        entry.Remove(nameof(LootHistoryEntry.AgrisObservedDuration));
        File.WriteAllText(folder.HistoryPath, new JsonObject
        {
            ["Version"] = 1,
            ["Entries"] = new JsonArray(entry),
        }.ToJsonString());
        var store = new LootHistoryStore(folder.HistoryPath);

        var restored = Assert.Single(store.Load());

        Assert.Null(store.LoadError);
        Assert.Null(restored.AgrisActiveDuration);
        Assert.Null(restored.AgrisObservedDuration);
        store.Save([restored with { CharacterClass = "Maegu · Awakening" }]);
        Assert.Null(Assert.Single(store.Load()).AgrisObservedDuration);
    }

    [Fact]
    public void NewUnobservedSessionsRemainDistinctFromLegacyAndKnownInactiveSessions()
    {
        using var folder = new Folder();
        var store = new LootHistoryStore(folder.HistoryPath);
        var legacy = Entry();
        var unobserved = Entry() with { AgrisActiveDuration = TimeSpan.Zero, AgrisObservedDuration = TimeSpan.Zero };
        var inactive = Entry() with { AgrisActiveDuration = TimeSpan.Zero, AgrisObservedDuration = TimeSpan.FromSeconds(30) };

        store.Save([legacy, unobserved, inactive]);
        var restored = store.Load().ToDictionary(entry => entry.SessionId);

        Assert.Null(restored[legacy.SessionId].AgrisActiveDuration);
        Assert.Null(restored[legacy.SessionId].AgrisObservedDuration);
        Assert.Equal(TimeSpan.Zero, restored[unobserved.SessionId].AgrisObservedDuration);
        Assert.Equal(TimeSpan.Zero, restored[inactive.SessionId].AgrisActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), restored[inactive.SessionId].AgrisObservedDuration);
    }

    [Theory]
    [InlineData(120, 90, 60, 60)]
    [InlineData(30, 10, 10, 10)]
    [InlineData(-3, 40, 0, 40)]
    [InlineData(5, -1, 0, 0)]
    [InlineData(null, 30, null, null)]
    [InlineData(30, null, null, null)]
    public void CorruptOrIncompleteDurationsCannotClaimMoreThanObservedSessionTime(int? active, int? observed,
        int? expectedActive, int? expectedObserved)
    {
        using var folder = new Folder();
        var store = new LootHistoryStore(folder.HistoryPath);
        var entry = Entry() with { AgrisActiveDuration = Seconds(active), AgrisObservedDuration = Seconds(observed) };

        store.Save([entry]);
        var restored = Assert.Single(store.Load());

        Assert.Equal(Seconds(expectedActive), restored.AgrisActiveDuration);
        Assert.Equal(Seconds(expectedObserved), restored.AgrisObservedDuration);
    }

    private static TimeSpan? Seconds(int? value) => value is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

    private static LootHistoryEntry Entry() => new()
    {
        SessionId = Guid.NewGuid(),
        StartedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
        Duration = TimeSpan.FromMinutes(1),
        SpotId = LootSpotCatalog.HermesiaId,
        Totals = new() { ["Black Crystal Fragment"] = 10 },
        SilverBeforeTax = 1_605_390,
        SilverAfterTax = 1_605_390,
        SilverIsComplete = true,
    };

    private sealed class Folder : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "Grindcrest.AgrisHistory.Tests", Guid.NewGuid().ToString("N"));
        internal string HistoryPath => Path.Combine(_path, "loot-history-v1.json");
        internal Folder() => Directory.CreateDirectory(_path);
        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task AgrisHistoryKeepsDurationsAcrossLiveAndHistoricalCorrections()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 5));
        SeedAgrisDuration(fixture.Service);
        var id = fixture.Service.State.SessionId;

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        AssertAgris(Assert.Single(fixture.HistoryStore.Load()));
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(id, "Black Crystal Fragment", 6, 5)).Succeeded);
        AssertAgris(Assert.Single(fixture.HistoryStore.Load()));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.True((await fixture.Service.UpdateHistoryLootAsync(id,
            new Dictionary<string, long> { ["Black Crystal Fragment"] = 7 }, "Maegu · Awakening")).Succeeded);
        AssertAgris(Assert.Single(fixture.HistoryStore.Load()));
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(id, "Black Crystal Fragment", 8, 7)).Succeeded);

        var saved = Assert.Single(fixture.HistoryStore.Load());
        AssertAgris(saved);
        Assert.Equal(8, saved.Totals["Black Crystal Fragment"]);

        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 2));
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        var next = fixture.HistoryStore.Load().Single(entry => entry.SessionId != id);
        Assert.Equal(TimeSpan.Zero, next.AgrisActiveDuration);
        Assert.Equal(TimeSpan.Zero, next.AgrisObservedDuration);
        AssertAgris(fixture.HistoryStore.Load().Single(entry => entry.SessionId == id));
    }

    [Fact]
    public async Task AgrisHistoryDoesNotChangeTheGarmothPayload()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 5));
        SeedAgrisDuration(fixture.Service);

        Assert.True((await fixture.Service.UploadAsync()).Succeeded);

        var payload = Assert.Single(fixture.Requests);
        AssertPayload(payload, 2, 5);
        Assert.Equal(new[] { "class_id", "drops", "global", "grindspot_id", "hourly", "minutes", "note", "spec", "total" },
            payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Agris", payload.GetProperty("note").GetString(), StringComparison.OrdinalIgnoreCase);
        AssertAgris(Assert.Single(fixture.HistoryStore.Load()));
    }

    private static void SeedAgrisDuration(TrackerSessionService service)
    {
        var field = typeof(TrackerSessionService).GetField("_agrisSessionTracker", BindingFlags.NonPublic | BindingFlags.Instance);
        var tracker = Assert.IsType<AgrisSessionTracker>(field!.GetValue(service));
        // Seed confirmed past observations; these tests exercise the real save,
        // correction, reset and upload paths independently of image detection.
        tracker.Reset();
        var first = DateTimeOffset.UtcNow.AddSeconds(-10);
        tracker.Update(TimeSpan.FromSeconds(20), new(AgrisStatus.Active, first), true, first);
        tracker.Update(TimeSpan.FromSeconds(25), new(AgrisStatus.Active, first.AddSeconds(5)), true, first.AddSeconds(5));
        tracker.Update(TimeSpan.FromSeconds(30), new(AgrisStatus.Inactive, first.AddSeconds(10)), true, first.AddSeconds(10));
    }

    private static void AssertAgris(LootHistoryEntry entry)
    {
        Assert.Equal(TimeSpan.FromSeconds(5), entry.AgrisActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), entry.AgrisObservedDuration);
    }
}
