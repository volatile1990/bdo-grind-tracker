using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    private static readonly BuffDefinition SessionBuff = BuffPriceCatalog.Definitions.Single(item => item.Id == "harmony-draught");

    [Fact]
    public async Task TwoBuffScansExposeActiveBaselineAndObservedCostWithoutInventingConsumption()
    {
        BuffFrameReading? reading = BuffReading(300);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        Assert.Empty(Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs).Active);
        reading = BuffReading(299);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
        var confirmed = Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs);
        Assert.True(Assert.Single(confirmed.Active).IsBaseline);
        Assert.Empty(confirmed.Consumptions);
        Assert.Equal(1000m, confirmed.ProratedCost);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(confirmed.Usage).ObservedDuration);
        // Rebuilding the frontend without a new capture must not bill the same sample again.
        fixture.Service.RefreshPendingState();
        fixture.Service.RefreshPendingState();
        Assert.Equal(1000m, fixture.Service.State.Buffs!.ProratedCost);
    }

    [Fact]
    public async Task BuffPauseResumePersistsCostsWithoutReplayingConsumptionAndNewSessionClearsThem()
    {
        BuffFrameReading? reading = BuffReading(60);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-12));
        reading = BuffReading(1200);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-11));
        reading = BuffReading(1199);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-10));
        Assert.Single(fixture.Service.State.Buffs!.Consumptions);
        Assert.Equal(1_200_000m, fixture.Service.State.Buffs.ConsumedCost);
        Assert.Equal(1000m, fixture.Service.State.Buffs.ProratedCost);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var paused = Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs);
        Assert.Empty(paused.Active);
        Assert.Equal(1000m, paused.ProratedCost);
        var historical = Assert.IsType<BuffLedgerSnapshot>(Assert.Single(fixture.HistoryStore.Load()).Buffs);
        var checkpoint = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load();
        Assert.Equal(paused.ConsumedCost, historical.ConsumedCost);
        Assert.Equal(paused.ProratedCost, checkpoint!.Buffs!.ProratedCost);
        Assert.Single(checkpoint.Buffs.Consumptions);

        fixture.ResumeClocks();
        reading = BuffReading(1180);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-4));
        reading = BuffReading(1179);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        var resumed = Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs);
        Assert.Single(resumed.Consumptions);
        Assert.Equal(2000m, resumed.ProratedCost);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(resumed.Usage).ObservedDuration);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.Buffs);
        Assert.False(fixture.Service.State.HasSession);
        Assert.Equal(2000m, Assert.Single(fixture.HistoryStore.Load()).Buffs!.ProratedCost);
        Assert.Null(new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load());
    }

    [Fact]
    public async Task RestoredBuffHistoryStartsPausedAndNewObservationsExcludeOfflineTime()
    {
        var now = DateTimeOffset.UtcNow;
        var ledger = new BuffLedger(BuffPriceCatalog.Definitions);
        BuffPrice? Price(BuffDefinition _) => new(1_200_000m, "eu", now.AddHours(-1), false);
        ledger.Apply([], now.AddHours(-1), Price);
        ledger.Apply(BuffReading(1200).Observations, now.AddHours(-1).AddSeconds(1), Price);
        var old = ledger.Apply(BuffReading(1199).Observations, now.AddHours(-1).AddSeconds(2), Price);
        var saved = CurrentSessionStoreTests.Example() with { Buffs = old };
        BuffFrameReading? reading = BuffReading(600);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor,
            lootScrollVisible: _ => true, restoredSession: saved);
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Empty(fixture.Service.State.Buffs!.Active);
        Assert.Single(fixture.Service.State.Buffs.Consumptions);
        Assert.Equal(1000m, fixture.Service.State.Buffs.ProratedCost);

        InstallBuffPrice(fixture);
        fixture.ResumeClocks();
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        reading = BuffReading(599);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
        Assert.Single(fixture.Service.State.Buffs!.Consumptions);
        Assert.Equal(2000m, fixture.Service.State.Buffs.ProratedCost);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(fixture.Service.State.Buffs.Usage).ObservedDuration);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(2000m, Assert.Single(fixture.HistoryStore.Load()).Buffs!.ProratedCost);
    }

    [Fact]
    public async Task UnreadableBuffFrameBreaksCostContinuityWithoutBlockingLoot()
    {
        BuffFrameReading? reading = BuffReading(600);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-7));
        reading = BuffReading(599);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-6));
        reading = null;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-5));
        Assert.Empty(fixture.Service.State.Buffs!.Active);
        Assert.Equal(1000m, fixture.Service.State.Buffs.ProratedCost);
        reading = BuffReading(596);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        reading = BuffReading(595);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
        Assert.Equal(2000m, fixture.Service.State.Buffs!.ProratedCost);
        Assert.Empty(fixture.Service.State.Buffs.Consumptions);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.IsError);
        Assert.True(fixture.Service.State.Loot.TotalQuantity > 0);
    }

    [Fact]
    public async Task OptionalBuffReaderFailureDoesNotBlockSessionOrLoot()
    {
        var monitor = new BuffMonitor(new SessionBuffReader(() => throw new IOException("Unreadable buff bar")));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        await ProcessBuffFrame(fixture, monitor, DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Null(fixture.Service.State.Buffs);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.IsError);
        Assert.Null(fixture.Service.State.TrackingBlockedReason);
    }

    private static void BeginBuffSession(Fixture fixture)
    {
        fixture.Begin();
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
        InstallBuffPrice(fixture);
    }

    private static void InstallBuffPrice(Fixture fixture) =>
        SetField(fixture.Service, "<Prices>k__BackingField", new LootPriceSnapshot("eu",
            [new(SessionBuff.Name, 1_200_000m, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UtcNow)]));

    private static BuffFrameReading BuffReading(int remainingSeconds) =>
        new([new(SessionBuff.Id, TimeSpan.FromSeconds(remainingSeconds), TimeSpan.FromSeconds(1))]);

    private static async Task ProcessBuffFrame(Fixture fixture, BuffMonitor monitor, DateTimeOffset capturedAt)
    {
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(1, capturedAt) { CanObserveHud = true }, CancellationToken.None);
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Service.RefreshPendingState();
    }

    private sealed class SessionBuffReader(Func<BuffFrameReading?> read) : IBuffFrameReader
    {
        public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken) => read();
        public void Dispose() { }
    }
}
