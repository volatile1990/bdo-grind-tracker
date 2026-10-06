using System.Reflection;
using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class LootHistorySerializationCacheTests
{
    [Fact]
    public void WarmSavesReuseSerializationAndKeepTheExistingJsonAndBackup()
    {
        using var fixture = new Fixture();
        var entries = new[] { Entry(), Entry() };
        fixture.Store.Save(entries);
        var first = File.ReadAllBytes(fixture.Path);
        Assert.Equal(Oracle(entries), first);
        Assert.Equal(2, fixture.Store.SerializedEntryCount);

        fixture.Store.Save(entries);
        Assert.Equal(2, fixture.Store.SerializedEntryCount);
        Assert.Equal(first, File.ReadAllBytes(fixture.Path));
        Assert.Equal(first, File.ReadAllBytes(fixture.Path + ".bak"));

        entries[0] = entries[0] with { UpdatedAt = entries[0].UpdatedAt.AddSeconds(15), Duration = TimeSpan.FromMinutes(61) };
        fixture.Store.Save(entries);
        Assert.Equal(3, fixture.Store.SerializedEntryCount);
        Assert.Equal(Oracle(entries), File.ReadAllBytes(fixture.Path));
        Assert.Equal(first, File.ReadAllBytes(fixture.Path + ".bak"));
    }

    [Theory]
    [InlineData("totals")]
    [InlineData("drops")]
    [InlineData("pauses")]
    [InlineData("rotation-events")]
    [InlineData("rotation-sections")]
    [InlineData("timeline")]
    [InlineData("manual")]
    [InlineData("guards")]
    [InlineData("buff-consumption")]
    [InlineData("buff-usage")]
    [InlineData("buff-active")]
    [InlineData("offset")]
    public void InPlaceCollectionAndMetadataChangesCannotReuseStaleJson(string mutation)
    {
        using var fixture = new Fixture();
        var entry = Entry();
        fixture.Store.Save([entry]);
        switch (mutation)
        {
            case "totals": entry.Totals["Branch of Abundance"] = 25; break;
            case "drops": ((SessionDropSample[])entry.DropHistory!)[0] = new(TimeSpan.FromMinutes(2), "Branch of Abundance", 10); break;
            case "pauses": ((SessionPause[])entry.Pauses!)[0] = entry.Pauses![0] with { EndedAt = entry.StartedAt.AddMinutes(3) }; break;
            case "rotation-events": ((RotationEvent[])entry.Rotations[0].Run.Events)[0] = new("agris", "Agris", 20) { Inferred = true }; break;
            case "rotation-sections": ((RotationSection[])entry.Rotations[0].Run.Sections)[0] = new("wave-1", 1, 20); break;
            case "timeline": ((RotationTimelineEntry[])entry.RotationTimeline)[0] = entry.RotationTimeline[0] with { Detail = "revised", Quantity = 10 }; break;
            case "manual": entry.ManualLootItems[0] = "Caphras Stone"; break;
            case "guards": entry.GarmothPendingCorrectionIntervals[0] = Guid.NewGuid(); break;
            case "buff-consumption": ((BuffConsumption[])entry.Buffs!.Consumptions)[0] = entry.Buffs.Consumptions[0] with { Price = new(20, "na", entry.UpdatedAt, false) }; break;
            case "buff-usage": ((BuffUsage[])entry.Buffs!.Usage)[0] = entry.Buffs.Usage[0] with { KnownProratedCost = 15 }; break;
            case "buff-active": ((BuffActive[])entry.Buffs!.Active)[0] = entry.Buffs.Active[0] with { Remaining = TimeSpan.FromMinutes(29) }; break;
            case "offset": entry = entry with { UpdatedAt = entry.UpdatedAt.ToOffset(TimeSpan.FromHours(2)) }; break;
        }
        fixture.Store.Save([entry]);
        Assert.Equal(2, fixture.Store.SerializedEntryCount);
        Assert.Equal(Oracle([entry]), File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public void DictionaryComparerChangesAndDecimalScalePreserveUncachedJson()
    {
        using var fixture = new Fixture();
        var entry = Entry() with
        {
            Totals = new(StringComparer.OrdinalIgnoreCase) { ["Caphras Stone"] = 1 },
            ManualLootItems = ["caphras stone"], SilverAfterTax = 1.0m,
        };
        fixture.Store.Save([entry]);
        entry = entry with { Totals = new(entry.Totals, StringComparer.Ordinal) };
        fixture.Store.Save([entry]);
        Assert.Equal(Oracle([entry]), File.ReadAllBytes(fixture.Path));
        Assert.Empty(Assert.Single(fixture.Store.Load()).ManualLootItems);
        entry = entry with { SilverAfterTax = 1.00m };
        fixture.Store.Save([entry]);
        Assert.Equal(3, fixture.Store.SerializedEntryCount);
        Assert.Equal(Oracle([entry]), File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public void ReorderedTotalsAndRemovedInvalidSamplesStillMatchUncachedNormalization()
    {
        using var fixture = new Fixture();
        var entry = Entry();
        fixture.Store.Save([entry]);
        var quantity = entry.Totals["Branch of Abundance"];
        entry.Totals.Remove("Branch of Abundance");
        entry.Totals.Add("Branch of Abundance", quantity);
        ((SessionDropSample[])entry.DropHistory!)[0] = new(TimeSpan.FromHours(2), "Branch of Abundance", 10);
        fixture.Store.Save([entry]);
        Assert.Equal(2, fixture.Store.SerializedEntryCount);
        Assert.Equal(Oracle([entry]), File.ReadAllBytes(fixture.Path));
        Assert.Empty(Assert.Single(fixture.Store.Load()).DropHistory!);
    }

    [Fact]
    public void DeletedAndDuplicateSessionsCannotSurviveInTheCacheOrFile()
    {
        using var fixture = new Fixture();
        var entry = Entry();
        fixture.Store.Save([entry]);
        var older = entry with { UpdatedAt = entry.UpdatedAt.AddDays(-1), Totals = new() { ["Branch of Abundance"] = 1 } };
        fixture.Store.Save([older, entry]);
        Assert.Equal(Oracle([older, entry]), File.ReadAllBytes(fixture.Path));
        Assert.Equal(1, fixture.Store.CachedEntryCount);
        fixture.Store.Save([]);
        Assert.Empty(fixture.Store.Load());
        Assert.Equal(0, fixture.Store.CachedEntryCount);
        Assert.Equal(0, fixture.Store.CachedBytes);
    }

    [Fact]
    public void CacheBudgetDoesNotLimitHistoryAndCanBeReleased()
    {
        using var fixture = new Fixture();
        var entries = Enumerable.Range(0, 40).Select(_ => Entry() with { CharacterClass = new string('x', 100_000) }).ToArray();
        fixture.Store.Save(entries);
        Assert.Equal(entries.Length, fixture.Store.Load().Count);
        Assert.InRange(fixture.Store.CachedBytes, 1, LootHistoryStore.MaximumCacheBytes);
        Assert.InRange(fixture.Store.CachedEntryCount, 1, LootHistoryStore.MaximumCachedEntries);
        var oversized = Entry() with { CharacterClass = new string('x', 600_000) };
        fixture.Store.Save([oversized]);
        Assert.Equal(oversized.CharacterClass, Assert.Single(fixture.Store.Load()).CharacterClass);
        Assert.Equal(0, fixture.Store.CachedEntryCount);
        fixture.Store.Save([Entry()]);
        fixture.Store.ClearCache();
        Assert.Equal(0, fixture.Store.CachedBytes);
        Assert.Equal(0, fixture.Store.CachedEntryCount);
    }

    [Fact]
    public void MixedCachedStreamedAndEscapedOversizedEntriesKeepIdenticalDocumentBytes()
    {
        using var fixture = new Fixture();
        var entries = new[]
        {
            Entry() with { CharacterClass = "Maegu \u00E4" },
            Entry() with { CharacterClass = new string('\u00E4', 90_000) },
            Entry() with { CharacterClass = new string('x', 600_000) },
        };
        fixture.Store.Save(entries);
        Assert.Equal(1, fixture.Store.CachedEntryCount);
        Assert.Equal(Oracle(entries), File.ReadAllBytes(fixture.Path));
        fixture.Store.Save(entries);
        Assert.Equal(5, fixture.Store.SerializedEntryCount);
        Assert.Equal(Oracle(entries), File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public void OversizedNestedStringsStreamWithoutCacheAdmission()
    {
        using var fixture = new Fixture();
        var entry = Entry();
        entry = entry with
        {
            Rotations = [entry.Rotations[0] with
            {
                Run = entry.Rotations[0].Run with
                {
                    Events = [new RotationEvent("hog", new string('x', 600_000), 10)],
                },
            }],
        };
        fixture.Store.Save([entry]);
        Assert.Equal(0, fixture.Store.CachedEntryCount);
        Assert.Equal(Oracle([entry]), File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public void FailedCachedReplacementKeepsDiskDataAndSuccessfulRetryUsesTheNewValues()
    {
        using var fixture = new Fixture();
        var entry = Entry();
        fixture.Store.Save([entry]);
        var previous = File.ReadAllBytes(fixture.Path);
        entry.Totals["Branch of Abundance"] = 33;
        using (var locked = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => fixture.Store.Save([entry]));
        Assert.Equal(previous, File.ReadAllBytes(fixture.Path));
        Assert.False(File.Exists(fixture.Path + ".tmp"));
        fixture.Store.Save([entry]);
        Assert.Equal(33, Assert.Single(fixture.Store.Load()).Totals["Branch of Abundance"]);
        Assert.Equal(previous, File.ReadAllBytes(fixture.Path + ".bak"));
    }

    private static byte[] Oracle(IReadOnlyList<LootHistoryEntry> entries)
    {
        var normalize = typeof(LootHistoryStore).GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Static)!;
        var normalized = (IReadOnlyList<LootHistoryEntry>)normalize.Invoke(null, [entries])!;
        return JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Entries = normalized }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static LootHistoryEntry Entry()
    {
        var at = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var price = new BuffPrice(10, "eu", at, false);
        return new()
        {
            SessionId = Guid.NewGuid(), StartedAt = at, UpdatedAt = at.AddHours(1), Duration = TimeSpan.FromHours(1),
            SpotId = LootSpotCatalog.AphrodonId, CharacterClass = "Maegu",
            Totals = new() { ["Branch of Abundance"] = 20, ["Caphras Stone"] = 1 },
            SilverBeforeTax = 20, SilverAfterTax = 18, SilverIsComplete = true,
            DropHistory = new SessionDropSample[] { new(TimeSpan.FromMinutes(1), "Branch of Abundance", 20) },
            Pauses = new SessionPause[] { new(TimeSpan.FromMinutes(2), at.AddMinutes(2), at.AddMinutes(4), SessionPause.Manual) },
            Rotations = new SessionRotation[] { new(LootSpotCatalog.AphrodonId, at, new(60,
                new RotationEvent[] { new("hog", "wave", 10) }) { Sections = new RotationSection[] { new("wave-1", 0, 10) }, RecordedAt = at }) },
            RotationTimeline = new RotationTimelineEntry[] { new(Guid.NewGuid(), at, LootSpotCatalog.AphrodonId, "loot", "drop", "original") { RecordedAt = at } },
            ManualLootItems = ["Branch of Abundance"], GarmothPendingCorrectionIntervals = [Guid.NewGuid()],
            Buffs = new(new BuffConsumption[] { new("buff", "buff", null, at, price) },
                new BuffUsage[] { new("buff", "buff", null, TimeSpan.FromMinutes(1), 10, TimeSpan.Zero) },
                new BuffActive[] { new("buff", "buff", null, TimeSpan.FromMinutes(30), at, price, false) }),
        };
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public LootHistoryStore Store { get; }
        public Fixture()
        {
            Path = System.IO.Path.Combine(_directory, "history.json");
            Store = new(Path);
        }
        public void Dispose()
        {
            Store.ClearCache();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
