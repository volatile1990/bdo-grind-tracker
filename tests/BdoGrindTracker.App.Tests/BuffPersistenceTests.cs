using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffPersistenceTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 21, 12, 0, 20, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionStartBookingsRoundTripAndLegacyMissingMarkersRemainRenewals(bool legacy)
    {
        using var files = new Files();
        var snapshot = ExampleBuffs();
        snapshot = snapshot with { Consumptions = snapshot.Consumptions.Select(item => item with { IsSessionStart = true }).ToArray() };
        var history = JsonSerializer.SerializeToNode(HistoryEntry() with { Buffs = snapshot })!.AsObject();
        var current = JsonSerializer.SerializeToNode(Checkpoint() with { Buffs = snapshot })!.AsObject();
        if (legacy)
            foreach (var document in new[] { history, current })
                foreach (var item in document["Buffs"]!["Consumptions"]!.AsArray())
                    item!.AsObject().Remove(nameof(BuffConsumption.IsSessionStart));
        files.Write(history, current);

        foreach (var restored in new[] { Assert.Single(files.History.Load()).Buffs!, files.Current.Load()!.Buffs! })
        {
            Assert.Equal(!legacy, Assert.Single(restored.Consumptions).IsSessionStart);
            Assert.Equal(snapshot.ConsumedCost, restored.ConsumedCost);
            Assert.Equal(snapshot.KnownProratedCost, restored.KnownProratedCost);
        }
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    [Fact]
    public void HistoryAndCheckpointKeepConsumptionPricesAndUsageWithoutRestoringActiveTimers()
    {
        using var files = new Files();
        var snapshot = ExampleBuffs();
        files.History.Save([HistoryEntry() with { Buffs = snapshot }]);
        files.Current.Save(Checkpoint() with { Buffs = snapshot });

        var historical = Assert.IsType<BuffLedgerSnapshot>(Assert.Single(files.History.Load()).Buffs);
        var current = Assert.IsType<BuffLedgerSnapshot>(files.Current.Load()!.Buffs);
        foreach (var saved in new[] { historical, current })
        {
            Assert.Equal(snapshot.Consumptions, saved.Consumptions);
            Assert.Equal(snapshot.Usage, saved.Usage);
            Assert.Equal(snapshot.ConsumedCost, saved.ConsumedCost);
            Assert.Equal(snapshot.ProratedCost, saved.ProratedCost);
            Assert.True(saved.HasStalePrices);
            Assert.Empty(saved.Active);
        }
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    [Fact]
    public void AutomaticUnknownVariantGroupsSurviveBothStoresAlongsidePricedConsumptions()
    {
        using var files = new Files();
        var groups = AutomaticBuffCatalog.Default.GroupDefinitions;
        Assert.NotEmpty(groups);
        var priced = ExampleBuffs();
        var snapshot = new BuffLedgerSnapshot(
            priced.Consumptions.Concat(groups.Select(group => new BuffConsumption(
                group.Id, group.Name, null, ObservedAt, null))).ToArray(),
            priced.Usage.Concat(groups.Select(group => new BuffUsage(
                group.Id, group.Name, null, TimeSpan.FromSeconds(10), 0, TimeSpan.FromSeconds(10)))).ToArray(),
            priced.Active.Concat(groups.Select(group => new BuffActive(
                group.Id, group.Name, null, group.Duration, ObservedAt, null, false))).ToArray());
        files.History.Save([HistoryEntry() with { Buffs = snapshot }]);
        files.Current.Save(Checkpoint() with { Buffs = snapshot });

        foreach (var saved in new[] { Assert.Single(files.History.Load()).Buffs, files.Current.Load()!.Buffs })
        {
            Assert.NotNull(saved);
            Assert.Equal(snapshot.Consumptions, saved.Consumptions);
            Assert.Equal(snapshot.Usage.OrderBy(item => item.BuffId), saved.Usage.OrderBy(item => item.BuffId));
            Assert.Equal(priced.KnownConsumedCost, saved.KnownConsumedCost);
            Assert.Equal(priced.KnownProratedCost, saved.KnownProratedCost);
            Assert.Null(saved.ConsumedCost);
            Assert.Null(saved.ProratedCost);
            Assert.Empty(saved.Active);
        }
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    [Fact]
    public void UnknownPricesSurviveSavingAndCannotBecomeFreeBuffs()
    {
        using var files = new Files();
        var snapshot = ExampleBuffs(withPrice: false);
        files.History.Save([HistoryEntry() with { Buffs = snapshot }]);
        files.Current.Save(Checkpoint() with { Buffs = snapshot });
        foreach (var saved in new[] { Assert.Single(files.History.Load()).Buffs!, files.Current.Load()!.Buffs! })
        {
            Assert.Null(saved.ConsumedCost);
            Assert.Null(saved.ProratedCost);
            Assert.Null(Assert.Single(saved.Consumptions).Price);
            Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(saved.Usage).UnpricedDuration);
        }
    }

    [Theory]
    [InlineData("automatic-harmony-draught", "Harmony Draught (variant unknown)", 20)]
    [InlineData("automatic-cron-meal", "Cron-Mahlzeiten (variant unknown)", 120)]
    public void EarlierAutomaticFamiliesSurviveCatalogRefinementAlongsidePricedVariants(
        string legacyId, string legacyName, int durationMinutes)
    {
        using var files = new Files();
        var catalog = AutomaticBuffCatalog.Default;
        var legacy = Assert.Single(catalog.HistoricalGroupDefinitions, definition => definition.Id == legacyId);
        Assert.Equal(TimeSpan.FromMinutes(durationMinutes), legacy.Duration);
        Assert.Null(legacy.MarketItemId);
        Assert.Null(legacy.FixedUnitPrice);
        var normal = BuffPriceCatalog.Definitions.Single(definition => definition.Id == "harmony-draught");
        var price = new BuffPrice(1_200_000m, "eu", ObservedAt, false);
        var snapshot = new BuffLedgerSnapshot(
            [new(normal.Id, normal.Name, normal.MarketItemId, ObservedAt, price),
             new(legacyId, legacyName, null, ObservedAt, null)],
            [new(normal.Id, normal.Name, normal.MarketItemId, TimeSpan.FromSeconds(10), 10_000, TimeSpan.Zero),
             new(legacyId, legacyName, null, TimeSpan.FromSeconds(10), 0, TimeSpan.FromSeconds(10))],
            [new(legacyId, legacyName, null, legacy.Duration, ObservedAt, null, false)]);
        files.History.Save([HistoryEntry() with { Buffs = snapshot }]);
        files.Current.Save(Checkpoint() with { Buffs = snapshot });

        foreach (var saved in new[] { Assert.Single(files.History.Load()).Buffs, files.Current.Load()!.Buffs })
        {
            Assert.NotNull(saved);
            var resumed = new BuffLedger(BuffPriceCatalog.HistoryDefinitions.Concat(catalog.HistoricalGroupDefinitions));
            resumed.Restore(saved);
            Assert.Equal(snapshot.Consumptions, resumed.Snapshot.Consumptions);
            Assert.Equal(snapshot.Usage.OrderBy(item => item.BuffId), resumed.Snapshot.Usage.OrderBy(item => item.BuffId));
            Assert.Equal(1_200_000m, resumed.Snapshot.KnownConsumedCost);
            Assert.Null(resumed.Snapshot.ConsumedCost);
            Assert.Null(resumed.Snapshot.ProratedCost);
            Assert.Empty(resumed.Snapshot.Active);
        }
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    [Fact]
    public void TentNpcCostSurvivesBothStores()
    {
        using var files = new Files();
        var tent = BuffPriceCatalog.Definitions.Single(item => item.Id == "tent-adventurers-luck-v");
        var price = BuffPriceCatalog.GetPrice(tent, new LootPriceSnapshot("eu", []));
        var snapshot = new BuffLedgerSnapshot(
            [new(tent.Id, tent.Name, null, ObservedAt, price)], [], []);
        files.History.Save([HistoryEntry() with { Buffs = snapshot }]);
        files.Current.Save(Checkpoint() with { Buffs = snapshot });
        foreach (var stored in new[] { Assert.Single(files.History.Load()).Buffs!, files.Current.Load()!.Buffs! })
        {
            Assert.Equal(50_000_000m, stored.ConsumedCost);
            Assert.Equal(BuffPriceSource.FixedNpc, Assert.Single(stored.Consumptions).Price!.Source);
        }
    }

    [Fact]
    public void LegacySessionsWithoutBuffMetadataRemainReadableAndWritable()
    {
        using var files = new Files();
        var history = JsonSerializer.SerializeToNode(HistoryEntry())!.AsObject();
        var current = JsonSerializer.SerializeToNode(Checkpoint())!.AsObject();
        history.Remove(nameof(LootHistoryEntry.Buffs));
        current.Remove(nameof(CurrentSessionSnapshot.Buffs));
        files.Write(history, current);
        var savedHistory = Assert.Single(files.History.Load());
        var savedCurrent = Assert.IsType<CurrentSessionSnapshot>(files.Current.Load());
        Assert.Null(savedHistory.Buffs);
        Assert.Null(savedCurrent.Buffs);
        files.History.Save([savedHistory]);
        files.Current.Save(savedCurrent);
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("23")]
    [InlineData("\"invalid\"")]
    [InlineData("{}")]
    [InlineData("{\"Consumptions\":null,\"Usage\":[],\"Active\":[]}")]
    [InlineData("{\"Consumptions\":[null],\"Usage\":[],\"Active\":[]}")]
    [InlineData("{\"Consumptions\":[],\"Usage\":[null],\"Active\":[]}")]
    [InlineData("{\"Consumptions\":[],\"Usage\":[],\"Active\":[null]}")]
    public void MalformedOptionalMetadataNeverBlocksLootRecovery(string buffJson)
    {
        AssertInvalidMetadataPreservesLoot(JsonNode.Parse(buffJson));
    }

    [Theory]
    [InlineData("unknown-id")]
    [InlineData("negative-price")]
    [InlineData("invalid-price-region")]
    [InlineData("negative-duration")]
    [InlineData("negative-cost")]
    [InlineData("duplicate-usage")]
    [InlineData("invalid-market-item")]
    [InlineData("unpriced-exceeds-observed")]
    public void InvalidBuffValuesAreDiscardedWithoutDiscardingLoot(string mutation)
    {
        var metadata = JsonSerializer.SerializeToNode(ExampleBuffs())!.AsObject();
        var usage = metadata["Usage"]![0]!;
        var consumption = metadata["Consumptions"]![0]!;
        switch (mutation)
        {
            case "unknown-id": usage["BuffId"] = "not-in-catalog"; break;
            case "negative-price": consumption["Price"]!["UnitPrice"] = -1; break;
            case "invalid-price-region": consumption["Price"]!["Region"] = ""; break;
            case "negative-duration": usage["ObservedDuration"] = "-00:00:01"; break;
            case "negative-cost": usage["KnownProratedCost"] = -1; break;
            case "duplicate-usage": metadata["Usage"]!.AsArray().Add(usage.DeepClone()); break;
            case "invalid-market-item": consumption["MarketItemId"] = -1; break;
            case "unpriced-exceeds-observed": usage["UnpricedDuration"] = "00:10:00"; break;
        }
        AssertInvalidMetadataPreservesLoot(metadata);
    }

    [Fact]
    public void LootCorrectionsRetainEachHistoricalSessionsOwnBuffPrices()
    {
        using var files = new Files();
        var original = HistoryEntry() with { Buffs = ExampleBuffs() };
        var unpriced = HistoryEntry() with { Buffs = ExampleBuffs(withPrice: false) };
        files.History.Save([original, unpriced]);
        files.History.Save(files.History.Load().Select(entry => entry.SessionId == original.SessionId
            ? entry with { Totals = new() { ["Black Crystal Fragment"] = 99 } } : entry));
        var saved = files.History.Load().ToDictionary(entry => entry.SessionId);
        Assert.Equal(original.Buffs.ConsumedCost, saved[original.SessionId].Buffs!.ConsumedCost);
        Assert.Equal(original.Buffs.ProratedCost, saved[original.SessionId].Buffs!.ProratedCost);
        Assert.Null(saved[unpriced.SessionId].Buffs!.ConsumedCost);
        Assert.Equal(99, saved[original.SessionId].Totals["Black Crystal Fragment"]);
    }

    private static void AssertInvalidMetadataPreservesLoot(JsonNode? metadata)
    {
        using var files = new Files();
        var history = JsonSerializer.SerializeToNode(HistoryEntry())!.AsObject();
        var current = JsonSerializer.SerializeToNode(Checkpoint())!.AsObject();
        history[nameof(LootHistoryEntry.Buffs)] = metadata?.DeepClone();
        current[nameof(CurrentSessionSnapshot.Buffs)] = metadata?.DeepClone();
        files.Write(history, current);
        var savedHistory = Assert.Single(files.History.Load());
        var savedCurrent = Assert.IsType<CurrentSessionSnapshot>(files.Current.Load());
        Assert.Null(savedHistory.Buffs);
        Assert.Null(savedCurrent.Buffs);
        Assert.Equal(12, savedHistory.Totals["Black Crystal Fragment"]);
        Assert.Equal(12, savedCurrent.Totals["Black Crystal Fragment"]);
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
        files.History.Save([savedHistory]);
        files.Current.Save(savedCurrent);
    }

    private static BuffLedgerSnapshot ExampleBuffs(bool withPrice = true)
    {
        // Keep exercising historical records that are no longer selectable in the whitelist.
        var definition = BuffPriceCatalog.HistoryDefinitions.Single(item => item.Id == "frenzy-draught");
        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions);
        BuffPrice? Price(BuffDefinition _) => withPrice ? new(1_200_000m, "eu", ObservedAt, true) : null;
        ledger.Apply([new(definition.Id, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1))], ObservedAt.AddSeconds(-20), Price);
        ledger.Apply([new(definition.Id, definition.Duration, TimeSpan.FromSeconds(1))], ObservedAt.AddSeconds(-10), Price);
        return ledger.Apply([new(definition.Id, definition.Duration - TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1))], ObservedAt, Price);
    }

    private static LootHistoryEntry HistoryEntry() => new()
    {
        SessionId = Guid.NewGuid(), StartedAt = ObservedAt.AddMinutes(-1), UpdatedAt = ObservedAt,
        Duration = TimeSpan.FromMinutes(1), SpotId = LootSpotCatalog.HermesiaId,
        Totals = new() { ["Black Crystal Fragment"] = 12 },
        SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
    };

    private static CurrentSessionSnapshot Checkpoint() => new()
    {
        SessionId = Guid.NewGuid(), StartedAt = ObservedAt.AddMinutes(-1), UpdatedAt = ObservedAt,
        Duration = TimeSpan.FromMinutes(1), SpotId = LootSpotCatalog.HermesiaId, CharacterClassId = "warrior-awakening",
        SessionSubmitted = false, Totals = new() { ["Black Crystal Fragment"] = 12 }, ConfirmedEventCount = 1,
        ManualLootItems = [], GarmothLocallyModified = false, AgrisActiveDuration = TimeSpan.Zero,
        AgrisObservedDuration = TimeSpan.Zero, ExperienceGainedPercentagePoints = null,
        ExperienceObservedDuration = TimeSpan.Zero, ExperienceStartLevel = null, ExperienceEndLevel = null,
        GameLanguage = "en", MonitorDeviceName = null, RecordLoot = false,
    };

    private sealed class Files : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest.Buff.Tests", Guid.NewGuid().ToString("N"));
        private string HistoryPath => Path.Combine(directory, "history.json");
        private string CurrentPath => Path.Combine(directory, CurrentSessionStore.FileName);
        public LootHistoryStore History { get; }
        public CurrentSessionStore Current { get; }
        public Files()
        {
            Directory.CreateDirectory(directory);
            History = new(HistoryPath);
            Current = new(CurrentPath);
        }
        public void Write(JsonObject history, JsonObject current)
        {
            File.WriteAllText(HistoryPath, new JsonObject { ["Version"] = 1, ["Entries"] = new JsonArray(history) }.ToJsonString());
            File.WriteAllText(CurrentPath, new JsonObject { ["Version"] = 1, ["Session"] = current }.ToJsonString());
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
