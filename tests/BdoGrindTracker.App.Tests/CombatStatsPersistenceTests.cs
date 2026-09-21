using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class CombatStatsPersistenceTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 21, 12, 0, 20, TimeSpan.Zero);

    [Fact]
    public void ObservationRoundTripsWithCaseSensitiveCamelCaseOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var stats = new CombatStatsState(2401, 841, CombatStatsCategory.Edania, ObservedAt);

        var json = JsonSerializer.Serialize(stats, options);

        Assert.Contains("\"observedAt\"", json);
        Assert.Equal(stats, JsonSerializer.Deserialize<CombatStatsState>(json, options));
    }

    [Theory]
    [InlineData(CombatStatsCategory.General, "hermesia")]
    [InlineData(CombatStatsCategory.Edania, "hermesia")]
    [InlineData(CombatStatsCategory.Demihuman, "tungrad-ruins")]
    [InlineData(CombatStatsCategory.Kamasylvian, "dehkia-ash-forest")]
    public void BothStoresRoundTripTheSameHudObservation(CombatStatsCategory category, string spotId)
    {
        using var files = new Files();
        var stats = new CombatStatsState(2401, 841, category, ObservedAt);
        files.History.Save([HistoryEntry() with { CombatStats = stats, SpotId = spotId }]);
        files.Current.Save(Checkpoint() with { CombatStats = stats, SpotId = spotId });

        Assert.True(stats.IsKnown);
        Assert.Equal(stats, Assert.Single(files.History.Load()).CombatStats);
        Assert.Equal(stats, files.Current.Load()!.CombatStats);
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    [Fact]
    public void LegacySessionsWithoutCombatStatsRemainUnknownAndWritable()
    {
        using var files = new Files();
        var history = JsonSerializer.SerializeToNode(HistoryEntry())!.AsObject();
        var current = JsonSerializer.SerializeToNode(Checkpoint())!.AsObject();
        history.Remove(nameof(LootHistoryEntry.CombatStats));
        current.Remove(nameof(CurrentSessionSnapshot.CombatStats));
        files.Write(history, current);

        var savedHistory = Assert.Single(files.History.Load());
        var savedCurrent = Assert.IsType<CurrentSessionSnapshot>(files.Current.Load());
        Assert.Null(savedHistory.CombatStats);
        Assert.Null(savedCurrent.CombatStats);
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);

        files.History.Save([savedHistory with { CharacterClass = "Warrior · Awakening" }]);
        files.Current.Save(savedCurrent with { ConfirmedEventCount = 2 });
        Assert.Null(Assert.Single(files.History.Load()).CombatStats);
        Assert.Null(files.Current.Load()!.CombatStats);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("23")]
    [InlineData("\"not an observation\"")]
    [InlineData("{}")]
    [InlineData("{\"Ap\":2401,\"Dp\":841,\"Category\":1}")]
    [InlineData("{\"Ap\":\"2401\",\"Dp\":841,\"Category\":1,\"ObservedAt\":\"2026-09-21T12:00:20Z\"}")]
    [InlineData("{\"Ap\":2401,\"Dp\":-1,\"Category\":1,\"ObservedAt\":\"2026-09-21T12:00:20Z\"}")]
    [InlineData("{\"Ap\":2401,\"Dp\":841,\"Category\":99,\"ObservedAt\":\"2026-09-21T12:00:20Z\"}")]
    [InlineData("{\"Ap\":2401,\"Dp\":841,\"Category\":{},\"ObservedAt\":\"2026-09-21T12:00:20Z\"}")]
    [InlineData("{\"Ap\":2401,\"Dp\":841,\"Category\":1,\"ObservedAt\":\"yesterday\"}")]
    [InlineData("{\"Ap\":10001,\"Dp\":841,\"Category\":1,\"ObservedAt\":\"2026-09-21T12:00:20Z\"}")]
    [InlineData("{\"Ap\":2401,\"Dp\":841,\"Category\":1,\"ObservedAt\":\"1970-01-01T00:00:00Z\"}")]
    public void MalformedOptionalStatsNeverBlockSessionRecoveryOrHistoryEdits(string combatStats)
    {
        using var files = new Files();
        var history = JsonSerializer.SerializeToNode(HistoryEntry())!.AsObject();
        var current = JsonSerializer.SerializeToNode(Checkpoint())!.AsObject();
        history[nameof(LootHistoryEntry.CombatStats)] = JsonNode.Parse(combatStats);
        current[nameof(CurrentSessionSnapshot.CombatStats)] = JsonNode.Parse(combatStats);
        files.Write(history, current);

        var savedHistory = Assert.Single(files.History.Load());
        var savedCurrent = Assert.IsType<CurrentSessionSnapshot>(files.Current.Load());
        Assert.Null(savedHistory.CombatStats);
        Assert.Null(savedCurrent.CombatStats);
        Assert.Equal(12, savedHistory.Totals["Black Crystal Fragment"]);
        Assert.Equal(12, savedCurrent.Totals["Black Crystal Fragment"]);
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
        files.History.Save([savedHistory]);
        files.Current.Save(savedCurrent);
    }

    [Fact]
    public void InvalidInMemoryObservationIsDiscardedWithoutDiscardingLoot()
    {
        using var files = new Files();
        var invalid = new CombatStatsState(2401, 841, (CombatStatsCategory)999, ObservedAt);
        files.History.Save([HistoryEntry() with { CombatStats = invalid }]);
        files.Current.Save(Checkpoint() with { CombatStats = invalid });

        Assert.Null(Assert.Single(files.History.Load()).CombatStats);
        Assert.Null(files.Current.Load()!.CombatStats);
        Assert.False(CombatStatsState.Unknown.IsKnown);
        Assert.False(new CombatStatsState(2401, 841, CombatStatsCategory.Edania).IsKnown);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Edania, "tungrad-ruins")]
    [InlineData(CombatStatsCategory.Demihuman, "hermesia")]
    [InlineData(CombatStatsCategory.Kamasylvian, "hermesia")]
    public void IncompatibleSavedObservationsAreRemovedWithoutBlockingRecoveryOrEdits(CombatStatsCategory category, string spotId)
    {
        using var files = new Files();
        var stats = new CombatStatsState(2401, 841, category, ObservedAt);
        files.Write(JsonSerializer.SerializeToNode(HistoryEntry() with { CombatStats = stats, SpotId = spotId })!.AsObject(),
            JsonSerializer.SerializeToNode(Checkpoint() with { CombatStats = stats, SpotId = spotId })!.AsObject());

        var history = Assert.Single(files.History.Load());
        var current = Assert.IsType<CurrentSessionSnapshot>(files.Current.Load());

        Assert.Null(history.CombatStats);
        Assert.Null(current.CombatStats);
        Assert.Equal(12, history.Totals["Black Crystal Fragment"]);
        Assert.Equal(12, current.Totals["Black Crystal Fragment"]);
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
        files.History.Save([history with { Totals = new() { ["Black Crystal Fragment"] = 13 } }]);
        files.Current.Save(current with { Totals = new() { ["Black Crystal Fragment"] = 13 } });
        Assert.Equal(13, Assert.Single(files.History.Load()).Totals["Black Crystal Fragment"]);
        Assert.Equal(13, files.Current.Load()!.Totals["Black Crystal Fragment"]);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Edania, "tungrad-ruins")]
    [InlineData(CombatStatsCategory.Demihuman, "hermesia")]
    [InlineData(CombatStatsCategory.Kamasylvian, "hermesia")]
    public void BothStoresDiscardIncompatibleStatsBeforeWritingButPreserveLoot(CombatStatsCategory category, string spotId)
    {
        using var files = new Files();
        var stats = new CombatStatsState(2401, 841, category, ObservedAt);
        files.History.Save([HistoryEntry() with { CombatStats = stats, SpotId = spotId }]);
        files.Current.Save(Checkpoint() with { CombatStats = stats, SpotId = spotId });

        var persisted = files.ReadSavedStats();
        Assert.Null(persisted.History);
        Assert.Null(persisted.Current);
        Assert.Equal(12, Assert.Single(files.History.Load()).Totals["Black Crystal Fragment"]);
        Assert.Equal(12, files.Current.Load()!.Totals["Black Crystal Fragment"]);
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    [Theory]
    [InlineData(CombatStatsCategory.General, true)]
    [InlineData(CombatStatsCategory.Edania, false)]
    [InlineData(CombatStatsCategory.Demihuman, false)]
    [InlineData(CombatStatsCategory.Kamasylvian, false)]
    public void CheckpointWithoutSelectedSpotKeepsOnlyGeneralStats(CombatStatsCategory category, bool keep)
    {
        using var files = new Files();
        var stats = new CombatStatsState(2401, 841, category, ObservedAt);
        files.Current.Save(Checkpoint() with { SpotId = null, CombatStats = stats });
        var loaded = Assert.IsType<CurrentSessionSnapshot>(files.Current.Load());
        Assert.Equal(keep ? stats : null, loaded.CombatStats);
        Assert.Null(loaded.SpotId);
        Assert.Equal(12, loaded.Totals["Black Crystal Fragment"]);
        Assert.Null(files.Current.LoadError);

        // Also exercise an older raw checkpoint written before category filtering.
        files.Write(JsonSerializer.SerializeToNode(HistoryEntry())!.AsObject(),
            JsonSerializer.SerializeToNode(Checkpoint() with { SpotId = null, CombatStats = stats })!.AsObject());
        loaded = Assert.IsType<CurrentSessionSnapshot>(files.Current.Load());
        Assert.Equal(keep ? stats : null, loaded.CombatStats);
        Assert.Equal(12, loaded.Totals["Black Crystal Fragment"]);
        Assert.Null(files.Current.LoadError);
    }

    [Fact]
    public void GeneralStatsDoNotMakeAnInvalidSpotIdAValidSession()
    {
        using var files = new Files();
        var stats = new CombatStatsState(2401, 841, CombatStatsCategory.General, ObservedAt);
        files.History.Save([HistoryEntry() with { SpotId = "unknown-spot", CombatStats = stats }]);
        Assert.Empty(files.History.Load());
        Assert.Null(files.History.LoadError);
        Assert.Throws<InvalidDataException>(() => files.Current.Save(Checkpoint() with { SpotId = "unknown-spot", CombatStats = stats }));
    }

    [Fact]
    public void HistoricalSessionsKeepTheirOwnStatsAcrossLootCorrections()
    {
        using var files = new Files();
        var first = HistoryEntry() with { CombatStats = new(1560, 740, CombatStatsCategory.General, ObservedAt) };
        var second = HistoryEntry() with { CombatStats = new(2401, 841, CombatStatsCategory.Edania, ObservedAt.AddHours(1)) };
        var legacy = HistoryEntry();
        files.History.Save([first, second, legacy]);
        var corrected = files.History.Load().Select(entry => entry.SessionId == first.SessionId
            ? entry with { Totals = new() { ["Black Crystal Fragment"] = 99 } } : entry);
        files.History.Save(corrected);

        var entries = files.History.Load().ToDictionary(entry => entry.SessionId);
        Assert.Equal(first.CombatStats, entries[first.SessionId].CombatStats);
        Assert.Equal(second.CombatStats, entries[second.SessionId].CombatStats);
        Assert.Null(entries[legacy.SessionId].CombatStats);
    }

    private static LootHistoryEntry HistoryEntry() => new()
    {
        SessionId = Guid.NewGuid(), StartedAt = ObservedAt.AddSeconds(-20),
        UpdatedAt = ObservedAt.AddSeconds(40), Duration = TimeSpan.FromMinutes(1),
        SpotId = LootSpotCatalog.HermesiaId, Totals = new() { ["Black Crystal Fragment"] = 12 },
        SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
    };

    private static CurrentSessionSnapshot Checkpoint() => new()
    {
        SessionId = Guid.NewGuid(), StartedAt = ObservedAt.AddSeconds(-20), UpdatedAt = ObservedAt.AddSeconds(40),
        Duration = TimeSpan.FromMinutes(1), SpotId = LootSpotCatalog.HermesiaId, CharacterClassId = "warrior-awakening",
        SessionSubmitted = false, Totals = new() { ["Black Crystal Fragment"] = 12 }, ConfirmedEventCount = 1,
        ManualLootItems = [], GarmothLocallyModified = false, AgrisActiveDuration = TimeSpan.Zero,
        AgrisObservedDuration = TimeSpan.Zero, ExperienceGainedPercentagePoints = null,
        ExperienceObservedDuration = TimeSpan.Zero, ExperienceStartLevel = null, ExperienceEndLevel = null,
        GameLanguage = "en", MonitorDeviceName = null, RecordLoot = false,
    };

    private sealed class Files : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Grindcrest.CombatStats.Tests", Guid.NewGuid().ToString("N"));
        private string HistoryPath => Path.Combine(_directory, "history.json");
        private string CurrentPath => Path.Combine(_directory, CurrentSessionStore.FileName);
        public LootHistoryStore History { get; }
        public CurrentSessionStore Current { get; }
        public Files()
        {
            Directory.CreateDirectory(_directory);
            History = new(HistoryPath);
            Current = new(CurrentPath);
        }

        public void Write(JsonObject history, JsonObject current)
        {
            File.WriteAllText(HistoryPath, new JsonObject { ["Version"] = 1, ["Entries"] = new JsonArray(history) }.ToJsonString());
            File.WriteAllText(CurrentPath, new JsonObject { ["Version"] = 1, ["Session"] = current }.ToJsonString());
        }

        public (JsonNode? History, JsonNode? Current) ReadSavedStats() =>
            (JsonNode.Parse(File.ReadAllText(HistoryPath))!["Entries"]![0]![nameof(LootHistoryEntry.CombatStats)],
                JsonNode.Parse(File.ReadAllText(CurrentPath))!["Session"]![nameof(CurrentSessionSnapshot.CombatStats)]);

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
