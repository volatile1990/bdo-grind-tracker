using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LootHistoryStoreTests
{
    [Fact]
    public void SaveIsAtomicAndLoadKeepsNewestVersionOfEachSession()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "loot-history-v1.json");
        var store = new LootHistoryStore(path);
        var sharedId = Guid.NewGuid();

        store.Save([
            CreateEntry(sharedId, DateTimeOffset.Parse("2026-09-01T20:00:00+02:00"), 10),
            CreateEntry(Guid.NewGuid(), DateTimeOffset.Parse("2026-09-03T20:00:00+02:00"), 30),
            CreateEntry(sharedId, DateTimeOffset.Parse("2026-09-04T20:00:00+02:00"), 40)
        ]);

        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(sharedId, loaded[0].SessionId);
        Assert.Equal(40, loaded[0].Totals["Branch of Abundance"]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void InvalidOrUnsupportedHistoryIsIgnoredSafely()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "loot-history-v1.json");
        File.WriteAllText(path, "{ this is not valid json }");
        var store = new LootHistoryStore(path);

        Assert.Empty(store.Load());

        store.Save([
            CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 10) with { SpotId = "unknown" },
            CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 0)
        ]);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void HistoryIsBoundedToNewestFiveHundredSessions()
    {
        using var directory = new TemporaryDirectory();
        var store = new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json"));
        var origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var entries = Enumerable.Range(0, LootHistoryStore.MaximumEntries + 12)
            .Select(index => CreateEntry(Guid.NewGuid(), origin.AddHours(index), index + 1))
            .ToArray();

        store.Save(entries);
        var loaded = store.Load();

        Assert.Equal(LootHistoryStore.MaximumEntries, loaded.Count);
        Assert.Equal(origin.AddHours(entries.Length - 1), loaded[0].UpdatedAt);
        Assert.Equal(origin.AddHours(12), loaded[^1].UpdatedAt);
    }

    private static LootHistoryEntry CreateEntry(Guid id, DateTimeOffset updatedAt, long trash) => new()
    {
        SessionId = id,
        StartedAt = updatedAt.AddHours(-1),
        UpdatedAt = updatedAt,
        Duration = TimeSpan.FromHours(1),
        SpotId = LootSpotCatalog.AphrodonId,
        CharacterClass = "Maegu · Awakening",
        Totals = new Dictionary<string, long> { ["Branch of Abundance"] = trash },
        SilverBeforeTax = trash * 155_127m,
        SilverAfterTax = trash * 155_127m,
        SilverIsComplete = true
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
