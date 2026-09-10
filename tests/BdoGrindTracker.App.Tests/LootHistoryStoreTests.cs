using System.Text.Json;
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

    [Theory]
    [InlineData("{ this is not valid json }")]
    [InlineData("{\"Version\":2,\"Entries\":[]}")]
    [InlineData("{\"Version\":1,\"Entries\":null}")]
    [InlineData("{}")]
    [InlineData("{\"Entries\":[]}")]
    [InlineData("{\"Version\":1}")]
    public void InvalidOrUnsupportedHistoryCannotBeOverwrittenUntilExplicitlyRepaired(string original)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "loot-history-v1.json");
        File.WriteAllText(path, original);
        var store = new LootHistoryStore(path);
        var replacement = CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 10);

        Assert.Empty(store.Load());
        Assert.NotNull(store.LoadError);
        Assert.Contains(path, store.LoadError);
        var failure = Assert.Throws<IOException>(() => store.Save([replacement]));
        Assert.Equal(store.LoadError, failure.Message);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));

        // Only an explicit successful reload of repaired data unlocks writes.
        File.WriteAllText(path, "{\"Version\":1,\"Entries\":[]}");
        Assert.Throws<IOException>(() => store.Save([replacement]));
        Assert.Empty(store.Load());
        Assert.Null(store.LoadError);
        store.Save([replacement]);
        Assert.Equal(replacement.SessionId, Assert.Single(store.Load()).SessionId);
    }

    [Fact]
    public void TemporarySharingViolationPreservesHistoryAndRecoversAfterReload()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "loot-history-v1.json");
        var entry = CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 10);
        new LootHistoryStore(path).Save([entry]);
        var original = File.ReadAllText(path);
        var reader = new LootHistoryStore(path);

        using (var fileLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(reader.Load());
            Assert.NotNull(reader.LoadError);
            Assert.Throws<IOException>(() => reader.Save([]));
        }

        Assert.Equal(original, File.ReadAllText(path));
        Assert.Throws<IOException>(() => reader.Save([]));
        var restored = Assert.Single(reader.Load());
        Assert.Null(reader.LoadError);
        Assert.Equal(entry.SessionId, restored.SessionId);
        Assert.Equal(10, restored.Totals["Branch of Abundance"]);

        var updated = CreateEntry(entry.SessionId, entry.UpdatedAt.AddMinutes(1), 20);
        reader.Save([updated]);
        Assert.Equal(20, Assert.Single(new LootHistoryStore(path).Load()).Totals["Branch of Abundance"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullTotalsAndCaseCollidingItemNamesBlockDestructiveOverwrite(bool nullTotals)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "loot-history-v1.json");
        var entry = CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 10);
        var invalidEntry = entry with
        {
            Totals = nullTotals ? null! : new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["Branch of Abundance"] = 10,
                ["branch of abundance"] = 20
            }
        };
        var original = JsonSerializer.Serialize(new { Version = 1, Entries = new[] { invalidEntry } });
        File.WriteAllText(path, original);
        var store = new LootHistoryStore(path);

        Assert.Empty(store.Load());
        Assert.NotNull(store.LoadError);
        Assert.Throws<IOException>(() => store.Save([entry]));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void AtomicSaveRetainsExactlyThePreviousVersionAsBackup()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "loot-history-v1.json");
        var store = new LootHistoryStore(path);
        var entry = CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 10);
        store.Save([entry]);
        var firstVersion = File.ReadAllText(path);

        store.Save([CreateEntry(entry.SessionId, entry.UpdatedAt.AddMinutes(1), 20)]);

        Assert.Equal(firstVersion, File.ReadAllText(path + ".bak"));
        Assert.Equal(10, Assert.Single(new LootHistoryStore(path + ".bak").Load()).Totals["Branch of Abundance"]);
        Assert.Equal(20, Assert.Single(new LootHistoryStore(path).Load()).Totals["Branch of Abundance"]);
        var secondVersion = File.ReadAllText(path);

        store.Save([CreateEntry(entry.SessionId, entry.UpdatedAt.AddMinutes(2), 30)]);

        Assert.Equal(secondVersion, File.ReadAllText(path + ".bak"));
        Assert.Equal(20, Assert.Single(new LootHistoryStore(path + ".bak").Load()).Totals["Branch of Abundance"]);
        Assert.Equal(30, Assert.Single(new LootHistoryStore(path).Load()).Totals["Branch of Abundance"]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SavingUnsupportedSpotsAndNegativeOnlyTotalsProducesAValidEmptyHistory()
    {
        using var directory = new TemporaryDirectory();
        var store = new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json"));

        store.Save([
            CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 10) with { SpotId = "unknown" },
            CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, -1)
        ]);

        Assert.Empty(store.Load());
        Assert.Null(store.LoadError);
    }

    [Fact]
    public void AnExplicitZeroCorrectionRemainsEditableAfterRestart()
    {
        using var directory = new TemporaryDirectory();
        var store = new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json"));
        var entry = CreateEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, 0) with { GarmothUploadBlocked = true };
        store.Save([entry]);
        var saved = Assert.Single(store.Load());
        Assert.Equal(0, saved.Totals["Branch of Abundance"]);
        Assert.Equal(entry.SessionId, saved.SessionId);
        Assert.True(saved.GarmothUploadBlocked);
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

    [Fact]
    public void GarmothUploadGuardSurvivesRestart()
    {
        using var directory = new TemporaryDirectory();
        var store = new LootHistoryStore(Path.Combine(directory.Path, "loot-history-v1.json"));
        var uploadedAt = DateTimeOffset.Parse("2026-09-07T18:30:00+02:00");
        var entry = CreateEntry(Guid.NewGuid(), uploadedAt, 18_400) with
        {
            GarmothUploadBlocked = true,
            GarmothUploadedAt = uploadedAt
        };

        store.Save([entry]);
        var loaded = Assert.Single(store.Load());

        Assert.True(loaded.GarmothUploadBlocked);
        Assert.Equal(uploadedAt, loaded.GarmothUploadedAt);
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
