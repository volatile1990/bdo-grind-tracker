using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task SuccessiveSessionsKeepTheirOwnBuffCountsAndBookedPricesAfterHistoryCorrections()
    {
        var immortal = BuffPriceCatalog.Definitions.Single(item => item.Id == "immortal-harmony-draught");
        var meal = BuffPriceCatalog.Definitions.Single(item => item.Id == "simple-cron-meal");
        BuffFrameReading? reading = BuffReading(60);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        var now = DateTimeOffset.UtcNow;
        BeginBuffSession(fixture);
        var firstId = fixture.Service.State.SessionId;
        SetHistoryBuffPrices(fixture, now.AddHours(-1), (SessionBuff, 1_200_000m));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-18));
        reading = BuffReading(1200);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-17));
        reading = BuffReading(1199);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-16));
        SetHistoryBuffPrices(fixture, now, (SessionBuff, 1_800_000m));
        reading = BuffReading(1200);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-15));
        var first = Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs);
        Assert.Equal(2, first.Consumptions.Count);
        Assert.All(first.Consumptions, item => Assert.Equal(SessionBuff.Id, item.BuffId));
        Assert.Equal(new decimal?[] { 1_200_000m, 1_800_000m }, first.Consumptions.Select(item => item.Cost));
        Assert.Equal(3_000_000m, first.ConsumedCost);
        Assert.DoesNotContain(first.Consumptions, item => item.IsSessionStart);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.Buffs);

        BeginBuffSession(fixture);
        var secondId = fixture.Service.State.SessionId;
        Assert.NotEqual(firstId, secondId);
        SetHistoryBuffPrices(fixture, now, (immortal, 7_000_000m), (meal, 600_000m));
        reading = HistoryBuffReading((immortal, 10), (meal, 10));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-12));
        reading = HistoryBuffReading((immortal, 1200), (meal, 7200));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-11));
        reading = HistoryBuffReading((immortal, 1199), (meal, 7199));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-10));
        SetHistoryBuffPrices(fixture, now.AddMinutes(1), (immortal, 9_000_000m), (meal, 900_000m));
        reading = HistoryBuffReading((immortal, 1198), (meal, 7200));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-9));
        var second = Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs);
        Assert.Equal(3, second.Consumptions.Count);
        Assert.Equal(7_000_000m, Assert.Single(second.Consumptions, item => item.BuffId == immortal.Id).Cost);
        Assert.Equal(new decimal?[] { 600_000m, 900_000m },
            second.Consumptions.Where(item => item.BuffId == meal.Id).Select(item => item.Cost));
        Assert.Equal(8_500_000m, second.ConsumedCost);
        Assert.DoesNotContain(second.Consumptions, item => item.IsSessionStart);
        Assert.DoesNotContain(second.Consumptions, item => item.BuffId == SessionBuff.Id);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);

        var beforeCorrections = ReloadBuffHistory(fixture);
        Assert.Equal(2, beforeCorrections.Count);
        AssertSavedHistoryBuffs(first, beforeCorrections[firstId].Buffs);
        AssertSavedHistoryBuffs(second, beforeCorrections[secondId].Buffs);

        SetHistoryBuffPrices(fixture, now.AddHours(1),
            (SessionBuff, 99_000_000m), (immortal, 98_000_000m), (meal, 97_000_000m));
        var correctedClass = CompanionCharacterClassCatalog.FindById("maegu-awakening")!.DisplayName;
        var firstCorrection = await fixture.Service.UpdateHistoryLootAsync(firstId,
            new Dictionary<string, long> { ["Black Crystal Fragment"] = 99 }, correctedClass);
        Assert.True(firstCorrection.Succeeded, firstCorrection.Error);
        var secondCorrection = await fixture.Service.UpdateLootQuantityAsync(secondId,
            "Black Crystal Fragment", 7, beforeCorrections[secondId].Totals["Black Crystal Fragment"]);
        Assert.True(secondCorrection.Succeeded, secondCorrection.Error);
        await fixture.Service.ShutdownAsync();
        Assert.False(fixture.Service.State.ShutdownFailed);

        var reloaded = ReloadBuffHistory(fixture);
        Assert.Equal(2, reloaded.Count);
        Assert.Equal(99, reloaded[firstId].Totals["Black Crystal Fragment"]);
        Assert.Equal(correctedClass, reloaded[firstId].CharacterClass);
        Assert.Equal(7, reloaded[secondId].Totals["Black Crystal Fragment"]);
        AssertSavedHistoryBuffs(first, reloaded[firstId].Buffs);
        AssertSavedHistoryBuffs(second, reloaded[secondId].Buffs);
    }

    [Fact]
    public async Task RestoredConsumptionWithMissingPriceRemainsUnpricedWhenCurrentMarketPriceBecomesAvailable()
    {
        var meal = BuffPriceCatalog.Definitions.Single(item => item.Id == "simple-cron-meal");
        BuffFrameReading? reading = HistoryBuffReading((meal, 600));
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var original = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(original);
        SetHistoryBuffPrices(original, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(original, monitor, now.AddSeconds(-5));
        reading = HistoryBuffReading((meal, 7200));
        await ProcessBuffFrame(original, monitor, now.AddSeconds(-4));
        reading = HistoryBuffReading((meal, 7199));
        await ProcessBuffFrame(original, monitor, now.AddSeconds(-3));
        var originalBuffs = Assert.IsType<BuffLedgerSnapshot>(original.Service.State.Buffs);
        Assert.Null(Assert.Single(originalBuffs.Consumptions).Price);
        Assert.Null(originalBuffs.ConsumedCost);
        Assert.Null(originalBuffs.ProratedCost);
        await original.Service.ShutdownAsync();
        Assert.False(original.Service.State.ShutdownFailed);
        AssertSavedHistoryBuffs(originalBuffs, Assert.Single(ReloadBuffHistory(original)).Value.Buffs);
        var checkpoint = new CurrentSessionStore(Path.Combine(original.DirectoryPath, CurrentSessionStore.FileName)).Load()!;

        await using var restored = new Fixture(autoUpload: false, restoredSession: checkpoint);
        SetHistoryBuffPrices(restored, now.AddDays(1), (meal, 9_000_000m));
        restored.Time.Advance(TimeSpan.FromHours(8));
        var saved = await restored.Service.SaveSessionAsync();
        Assert.True(saved.Succeeded, saved.Error);
        AssertSavedHistoryBuffs(originalBuffs, restored.Service.State.Buffs);
        await restored.Service.ShutdownAsync();
        Assert.False(restored.Service.State.ShutdownFailed);
        var history = Assert.Single(ReloadBuffHistory(restored)).Value;
        Assert.Equal(checkpoint.SessionId, history.SessionId);
        AssertSavedHistoryBuffs(originalBuffs, history.Buffs);
        Assert.Null(Assert.Single(history.Buffs!.Consumptions).Price);
        Assert.Null(history.Buffs.ConsumedCost);
    }

    private static BuffFrameReading HistoryBuffReading(params (BuffDefinition Definition, int Seconds)[] items) =>
        new(items.Select(item => new BuffObservation(item.Definition.Id,
            TimeSpan.FromSeconds(item.Seconds), TimeSpan.FromSeconds(1))).ToArray());

    private static void SetHistoryBuffPrices(Fixture fixture, DateTimeOffset fetchedAt,
        params (BuffDefinition Definition, decimal Price)[] prices) =>
        SetField(fixture.Service, "<Prices>k__BackingField", new LootPriceSnapshot("eu",
            prices.Select(item => new LootPriceQuote(item.Definition.Name, item.Price, 0,
                LootPriceOrigin.LiveMarket, fetchedAt))));

    private static IReadOnlyDictionary<Guid, LootHistoryEntry> ReloadBuffHistory(Fixture fixture)
    {
        var store = new LootHistoryStore(Path.Combine(fixture.DirectoryPath, "loot-history-v1.json"));
        var saved = store.Load();
        Assert.Null(store.LoadError);
        return saved.ToDictionary(item => item.SessionId);
    }

    private static void AssertSavedHistoryBuffs(BuffLedgerSnapshot expected, BuffLedgerSnapshot? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Consumptions, actual.Consumptions);
        Assert.Equal(expected.Usage, actual.Usage);
        Assert.Equal(expected.ConsumedCost, actual.ConsumedCost);
        Assert.Equal(expected.KnownConsumedCost, actual.KnownConsumedCost);
        Assert.Equal(expected.ProratedCost, actual.ProratedCost);
        Assert.Equal(expected.KnownProratedCost, actual.KnownProratedCost);
        Assert.Empty(actual.Active);
    }
}
