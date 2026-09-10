using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class ExperienceHistoryPersistenceTests
{
    [Theory]
    [InlineData("0.123")]
    [InlineData("-1.234")]
    [InlineData("0")]
    [InlineData("120.456")]
    public void SignedExperiencePointsAndObservationMetadataSurviveSaveAndLoad(string value)
    {
        using var folder = new Folder();
        var store = new LootHistoryStore(folder.HistoryPath);
        var entry = Entry() with
        {
            ExperienceGainedPercentagePoints = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            ExperienceObservedDuration = TimeSpan.FromSeconds(44.875),
            ExperienceStartLevel = 61,
            ExperienceEndLevel = 62,
        };

        store.Save([entry]);
        var restored = Assert.Single(store.Load());

        Assert.Equal(entry.ExperienceGainedPercentagePoints, restored.ExperienceGainedPercentagePoints);
        Assert.Equal(entry.ExperienceObservedDuration, restored.ExperienceObservedDuration);
        Assert.Equal(61, restored.ExperienceStartLevel);
        Assert.Equal(62, restored.ExperienceEndLevel);
        Assert.Equal(entry.Duration, restored.Duration);
    }

    [Fact]
    public void LegacyHistoryWithoutExperienceFieldsRemainsUnknownAndWritable()
    {
        using var folder = new Folder();
        var entry = JsonSerializer.SerializeToNode(Entry())!.AsObject();
        foreach (var name in new[] { nameof(LootHistoryEntry.ExperienceGainedPercentagePoints),
                     nameof(LootHistoryEntry.ExperienceObservedDuration), nameof(LootHistoryEntry.ExperienceStartLevel),
                     nameof(LootHistoryEntry.ExperienceEndLevel) })
            entry.Remove(name);
        File.WriteAllText(folder.HistoryPath, new JsonObject { ["Version"] = 1, ["Entries"] = new JsonArray(entry) }.ToJsonString());
        var store = new LootHistoryStore(folder.HistoryPath);

        var restored = Assert.Single(store.Load());

        Assert.Null(store.LoadError);
        Assert.Null(restored.ExperienceGainedPercentagePoints);
        Assert.Null(restored.ExperienceObservedDuration);
        Assert.Null(restored.ExperienceStartLevel);
        Assert.Null(restored.ExperienceEndLevel);
        store.Save([restored with { CharacterClass = "Maegu · Awakening" }]);
        Assert.Null(Assert.Single(store.Load()).ExperienceObservedDuration);
    }

    [Fact]
    public void NewUnobservedProgressAndMeasuredZeroStayDistinctFromLegacy()
    {
        using var folder = new Folder();
        var store = new LootHistoryStore(folder.HistoryPath);
        var legacy = Entry();
        var unobserved = Entry() with { ExperienceObservedDuration = TimeSpan.Zero };
        var zero = Entry() with
        {
            ExperienceGainedPercentagePoints = 0,
            ExperienceObservedDuration = TimeSpan.FromSeconds(30),
            ExperienceStartLevel = 62, ExperienceEndLevel = 62,
        };

        store.Save([legacy, unobserved, zero]);
        var restored = store.Load().ToDictionary(entry => entry.SessionId);

        Assert.Null(restored[legacy.SessionId].ExperienceObservedDuration);
        Assert.Null(restored[unobserved.SessionId].ExperienceGainedPercentagePoints);
        Assert.Equal(TimeSpan.Zero, restored[unobserved.SessionId].ExperienceObservedDuration);
        Assert.Equal(0, restored[zero.SessionId].ExperienceGainedPercentagePoints);
        Assert.Equal(TimeSpan.FromSeconds(30), restored[zero.SessionId].ExperienceObservedDuration);
    }

    [Theory]
    [InlineData(-1, 62, 62)]
    [InlineData(0, 62, 62)]
    [InlineData(30, 0, 62)]
    [InlineData(30, 62, 101)]
    [InlineData(30, null, 62)]
    [InlineData(null, 62, 62)]
    public void InvalidOrIncompleteObservationsDoNotBecomeMeasuredExperience(int? observedSeconds, int? startLevel, int? endLevel)
    {
        using var folder = new Folder();
        var store = new LootHistoryStore(folder.HistoryPath);
        var entry = Entry() with
        {
            ExperienceGainedPercentagePoints = 1m,
            ExperienceObservedDuration = observedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            ExperienceStartLevel = startLevel, ExperienceEndLevel = endLevel,
        };

        store.Save([entry]);
        var restored = Assert.Single(store.Load());

        Assert.Null(restored.ExperienceGainedPercentagePoints);
        Assert.Null(restored.ExperienceStartLevel);
        Assert.Null(restored.ExperienceEndLevel);
        Assert.Equal(observedSeconds is null ? null : TimeSpan.Zero, restored.ExperienceObservedDuration);
    }

    [Fact]
    public void ObservedExperienceTimeCannotExceedTheSavedActiveDuration()
    {
        using var folder = new Folder();
        var store = new LootHistoryStore(folder.HistoryPath);
        store.Save([Entry() with
        {
            ExperienceGainedPercentagePoints = -.125m,
            ExperienceObservedDuration = TimeSpan.FromMinutes(2),
            ExperienceStartLevel = 62, ExperienceEndLevel = 62,
        }]);

        var restored = Assert.Single(store.Load());

        Assert.Equal(restored.Duration, restored.ExperienceObservedDuration);
        Assert.Equal(-.125m, restored.ExperienceGainedPercentagePoints);
    }

    private static LootHistoryEntry Entry() => new()
    {
        SessionId = Guid.NewGuid(), StartedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1), Duration = TimeSpan.FromMinutes(1),
        SpotId = LootSpotCatalog.HermesiaId, Totals = new() { ["Black Crystal Fragment"] = 10 },
        SilverBeforeTax = 1_605_390, SilverAfterTax = 1_605_390, SilverIsComplete = true,
    };

    private sealed class Folder : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "Grindcrest.ExperienceHistory.Tests", Guid.NewGuid().ToString("N"));
        internal string HistoryPath => Path.Combine(_path, "loot-history-v1.json");
        internal Folder() => Directory.CreateDirectory(_path);
        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task ExperienceHistoryRetainsSignedProgressAcrossCorrectionsAndNewSessions()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 5));
        SeedExperienceProgress(fixture.Service);
        var id = fixture.Service.State.SessionId;

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        AssertExperience(Assert.Single(fixture.HistoryStore.Load()));
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(id, "Black Crystal Fragment", 6, 5)).Succeeded);
        AssertExperience(Assert.Single(fixture.HistoryStore.Load()));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.True((await fixture.Service.UpdateHistoryLootAsync(id,
            new Dictionary<string, long> { ["Black Crystal Fragment"] = 7 }, "Maegu · Awakening")).Succeeded);
        AssertExperience(Assert.Single(fixture.HistoryStore.Load()));
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(id, "Black Crystal Fragment", 8, 7)).Succeeded);
        AssertExperience(Assert.Single(fixture.HistoryStore.Load()));

        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 2));
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        var next = fixture.HistoryStore.Load().Single(entry => entry.SessionId != id);
        Assert.Null(next.ExperienceGainedPercentagePoints);
        Assert.Equal(TimeSpan.Zero, next.ExperienceObservedDuration);
        Assert.Null(next.ExperienceStartLevel);
        Assert.Null(next.ExperienceEndLevel);
    }

    [Fact]
    public async Task ExperienceHistoryLeavesTheGarmothRequestContractUnchanged()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 5));
        SeedExperienceProgress(fixture.Service);

        Assert.True((await fixture.Service.UploadAsync()).Succeeded);

        var payload = Assert.Single(fixture.Requests);
        AssertPayload(payload, 2, 5);
        Assert.Equal(new[] { "class_id", "drops", "global", "grindspot_id", "hourly", "minutes", "note", "spec", "total" },
            payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        AssertExperience(Assert.Single(fixture.HistoryStore.Load()));
    }

    private static void SeedExperienceProgress(TrackerSessionService service)
    {
        var field = typeof(TrackerSessionService).GetField("_experienceSessionTracker", BindingFlags.NonPublic | BindingFlags.Instance);
        var tracker = Assert.IsType<ExperienceSessionTracker>(field!.GetValue(service));
        tracker.Reset();
        var first = DateTimeOffset.UtcNow.AddSeconds(-60);
        tracker.Update(TimeSpan.FromSeconds(30), new(62, 10.125m, first), true, first);
        tracker.Update(TimeSpan.FromSeconds(90), new(62, 10m, first.AddSeconds(60)), true, first.AddSeconds(60));
    }

    private static void AssertExperience(LootHistoryEntry entry)
    {
        Assert.Equal(-.125m, entry.ExperienceGainedPercentagePoints);
        Assert.Equal(TimeSpan.FromMinutes(1), entry.ExperienceObservedDuration);
        Assert.Equal(62, entry.ExperienceStartLevel);
        Assert.Equal(62, entry.ExperienceEndLevel);
    }
}
