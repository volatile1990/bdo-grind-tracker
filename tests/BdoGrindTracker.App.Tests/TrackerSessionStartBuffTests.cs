using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task StartBuffCountsOnceAcrossPauseAndOnlyANewGrindGetsAnotherStartBooking()
    {
        BuffFrameReading? reading = BuffReading(600);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-8));
        Assert.Empty(fixture.Service.State.Buffs!.Consumptions);
        reading = BuffReading(599);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-7));

        var first = Assert.Single(fixture.Service.State.Buffs!.Consumptions);
        Assert.True(first.IsSessionStart);
        Assert.Equal(1_200_000m, first.Cost);
        Assert.True(Assert.Single(fixture.Service.State.Buffs.Active).IsBaseline);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var checkpoint = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load()!;
        Assert.Equal(first, Assert.Single(checkpoint.Buffs!.Consumptions));
        Assert.Equal(first, Assert.Single(Assert.Single(fixture.HistoryStore.Load()).Buffs!.Consumptions));

        fixture.ResumeClocks();
        reading = BuffReading(596);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-4));
        reading = BuffReading(595);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        Assert.Equal(first, Assert.Single(fixture.Service.State.Buffs!.Consumptions));

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        BeginBuffSession(fixture);
        reading = BuffReading(594);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
        reading = BuffReading(593);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-1));
        var next = Assert.Single(fixture.Service.State.Buffs!.Consumptions);
        Assert.True(next.IsSessionStart);
        Assert.Equal(first.Cost, next.Cost);
        Assert.NotEqual(first.ConsumedAt, next.ConsumedAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RestartBooksTheFirstConfirmedBuffOnlyWhenNoPreviousConsumptionExists(
        bool hasBuffMetadata, bool hasStartBooking)
    {
        var now = DateTimeOffset.UtcNow;
        var initial = new BuffConsumption(SessionBuff.Id, SessionBuff.Name, SessionBuff.MarketItemId,
            now.AddMinutes(-2), new(1_200_000m, "eu", now.AddMinutes(-5), false)) { IsSessionStart = true };
        var saved = CurrentSessionStoreTests.Example() with
        {
            Buffs = hasStartBooking ? new([initial], [], []) : hasBuffMetadata ? BuffLedgerSnapshot.Empty : null,
        };
        BuffFrameReading? reading = BuffReading(600);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor,
            lootScrollVisible: _ => true, restoredSession: saved);
        InstallBuffPrice(fixture);
        fixture.ResumeClocks();
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        Assert.Equal(hasStartBooking ? 1 : 0, fixture.Service.State.Buffs!.Consumptions.Count);
        reading = BuffReading(599);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));

        var result = fixture.Service.State.Buffs!;
        Assert.Single(result.Active);
        if (hasStartBooking) Assert.Equal(initial, Assert.Single(result.Consumptions));
        else
        {
            var first = Assert.Single(result.Consumptions);
            Assert.True(first.IsSessionStart);
            Assert.Equal(SessionBuff.Id, first.BuffId);
            Assert.Equal(now.AddSeconds(-3), first.ConsumedAt);
            Assert.Equal(1_200_000m, first.Cost);
        }
    }

    [Fact]
    public async Task InitiallyUnreadableCronAndLaterBoonAreChargedOnceAcrossPauseAndRestore()
    {
        const string mealId = "simple-cron-meal", boonFamily = "automatic-tent-adventures-boon";
        var meal = BuffPriceCatalog.Definitions.Single(item => item.Id == mealId);
        BuffFrameReading ReadAll(int harmonySeconds, int mealMinutes, int boonMinutes) => new([
            .. BuffReading(harmonySeconds).Observations,
            new(mealId, TimeSpan.FromMinutes(mealMinutes), TimeSpan.FromMinutes(1)),
            new(boonFamily, TimeSpan.FromMinutes(boonMinutes),
                boonMinutes == 120 ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1)),
        ]);
        var now = DateTimeOffset.UtcNow;
        var prices = new LootPriceSnapshot("eu", [
            new(SessionBuff.Name, 1_200_000m, 0, LootPriceOrigin.LiveMarket, now),
            new(meal.Name, 450_000m, 0, LootPriceOrigin.LiveMarket, now),
        ]);
        BuffFrameReading? reading = BuffReading(600) with { UnknownBuffIds = [mealId] };
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        SetField(fixture.Service, "<Prices>k__BackingField", prices);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-12));
        Assert.Empty(fixture.Service.State.Buffs!.Consumptions);

        reading = ReadAll(599, 67, 120);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-11));
        Assert.Equal(SessionBuff.Id, Assert.Single(fixture.Service.State.Buffs!.Consumptions).BuffId);
        reading = ReadAll(598, 67, 120);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-10));
        var first = fixture.Service.State.Buffs!;
        Assert.Equal(3, first.Consumptions.Count);
        Assert.All(first.Consumptions, item => Assert.True(item.IsSessionStart));
        var cron = Assert.Single(first.Consumptions, item => item.BuffId == mealId);
        Assert.Equal(450_000m, cron.Cost);
        Assert.Equal(now.AddSeconds(-11), cron.ConsumedAt);
        var boon = Assert.Single(first.Consumptions, item => item.BuffId == "tent-adventures-boon-300");
        Assert.Equal(12_000_000m, boon.Cost);
        Assert.Equal(BuffPriceSource.FixedNpc, boon.Price!.Source);
        Assert.Equal(now.AddSeconds(-11), boon.ConsumedAt);
        Assert.Equal(13_650_000m, first.ConsumedCost);

        reading = ReadAll(597, 67, 120);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-9));
        fixture.Service.RefreshPendingState();
        Assert.Equal(first.Consumptions, fixture.Service.State.Buffs!.Consumptions);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        fixture.ResumeClocks();
        reading = ReadAll(594, 66, 119);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-6));
        reading = ReadAll(593, 66, 119);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-5));
        Assert.Equal(first.Consumptions, fixture.Service.State.Buffs!.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(3), fixture.Service.State.Buffs.Usage.Single(item => item.BuffId == mealId).ObservedDuration);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);

        var checkpoint = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load()!;
        Assert.Equal(first.Consumptions, checkpoint.Buffs!.Consumptions);
        Assert.Equal(first.Consumptions, Assert.Single(fixture.HistoryStore.Load()).Buffs!.Consumptions);
        BuffFrameReading? restartedReading = ReadAll(591, 66, 119);
        var restartedMonitor = new BuffMonitor(new SessionBuffReader(() => restartedReading), TimeSpan.FromSeconds(1));
        await using var restarted = new Fixture(autoUpload: false, buffMonitor: restartedMonitor,
            lootScrollVisible: _ => true, restoredSession: checkpoint);
        SetField(restarted.Service, "<Prices>k__BackingField", prices);
        restarted.ResumeClocks();
        SetField(restarted.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
        await ProcessBuffFrame(restarted, restartedMonitor, now.AddSeconds(-3));
        restartedReading = ReadAll(590, 66, 119);
        await ProcessBuffFrame(restarted, restartedMonitor, now.AddSeconds(-2));
        Assert.Equal(first.Consumptions, restarted.Service.State.Buffs!.Consumptions);
        Assert.Equal(3, restarted.Service.State.Buffs.Active.Count);
        Assert.Equal(first.ConsumedCost, restarted.Service.State.Buffs.ConsumedCost);
    }

    [Theory]
    [InlineData("tent-adventures-boon-60")]
    [InlineData("tent-adventures-boon-120")]
    [InlineData("tent-adventures-boon-300")]
    [InlineData("automatic-tent-adventures-boon")]
    public async Task RestoredDurationFamilyKeepsOriginalConsumptionWithoutAnotherInitialCharge(string previousId)
    {
        var definition = BuffPriceCatalog.HistoryDefinitions.Concat(AutomaticBuffCatalog.Default.HistoricalGroupDefinitions)
            .Single(item => item.Id == previousId);
        var now = DateTimeOffset.UtcNow;
        var previous = new BuffConsumption(definition.Id, definition.Name, definition.MarketItemId,
            now.AddHours(-1), BuffPriceCatalog.GetPrice(definition, new("eu", []))) { IsSessionStart = true };
        var saved = CurrentSessionStoreTests.Example() with { Buffs = new([previous], [], []) };
        var reading = new BuffFrameReading([new("automatic-tent-adventures-boon", TimeSpan.FromHours(2), TimeSpan.FromHours(1))]);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor,
            lootScrollVisible: _ => true, restoredSession: saved);
        fixture.ResumeClocks();
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));

        var result = fixture.Service.State.Buffs!;
        Assert.Equal("tent-adventures-boon-300", Assert.Single(result.Active).BuffId);
        Assert.Equal(previous, Assert.Single(result.Consumptions));
        Assert.Equal(previous.Cost, result.ConsumedCost);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(previous, Assert.Single(Assert.Single(fixture.HistoryStore.Load()).Buffs!.Consumptions));
    }
}
