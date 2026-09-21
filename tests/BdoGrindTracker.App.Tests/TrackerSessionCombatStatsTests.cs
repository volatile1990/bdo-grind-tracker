using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task ConfirmedHudStatsReachLiveAndHistoryButNeverLeakIntoTheNextSession()
    {
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => new(2374, 826, CombatStatsCategory.Edania)));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor, lootScrollVisible: _ => true);
        BeginCombatStatsSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-10));
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Null(fixture.Service.State.SessionCombatStats);
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-5));
        var confirmed = fixture.Service.State.CombatStats;
        Assert.Equal(2374, confirmed.Ap);
        Assert.Equal(826, confirmed.Dp);
        Assert.Equal(CombatStatsCategory.Edania, confirmed.Category);
        Assert.Equal(CombatStatsCategory.Edania, fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Equal(confirmed, fixture.Service.State.SessionCombatStats);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Equal(confirmed, fixture.Service.State.SessionCombatStats);
        Assert.Equal(confirmed, Assert.Single(fixture.HistoryStore.Load()).CombatStats);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Null(fixture.Service.State.SessionCombatStats);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Equal(confirmed, Assert.Single(fixture.HistoryStore.Load()).CombatStats);
    }

    [Fact]
    public async Task UnreadableOrHiddenHudKeepsOnlyTheLastConfirmedSessionObservation()
    {
        CombatStatsReading? reading = new(1700, 800, CombatStatsCategory.General);
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor, lootScrollVisible: _ => true);
        BeginCombatStatsSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-5));
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-4));
        var previous = fixture.Service.State.SessionCombatStats;
        Assert.NotNull(previous);
        reading = null;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-3));
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Equal(previous, fixture.Service.State.SessionCombatStats);
        reading = new(2300, 820, CombatStatsCategory.Edania);
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-2));
        Assert.Equal(previous, fixture.Service.State.SessionCombatStats);
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-1), visible: false);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        await ProcessCombatStatsFrame(fixture, monitor, now);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Equal(previous, fixture.Service.State.SessionCombatStats);
        Assert.True(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task ChangingCategoryNeedsConfirmationAndSavesTheNewSessionValue()
    {
        CombatStatsReading reading = new(1700, 800, CombatStatsCategory.General);
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor, lootScrollVisible: _ => true);
        BeginCombatStatsSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-4));
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-3));
        reading = new(2380, 826, CombatStatsCategory.Edania);
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-2));
        Assert.Equal(CombatStatsCategory.General, fixture.Service.State.SessionCombatStats!.Category);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-1));
        Assert.Equal(CombatStatsCategory.Edania, fixture.Service.State.CombatStats.Category);
        Assert.Equal(CombatStatsCategory.Edania, fixture.Service.State.ObservedCombatStatsCategory);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var saved = Assert.Single(fixture.HistoryStore.Load()).CombatStats;
        Assert.Equal(2380, saved!.Ap);
        Assert.Equal(CombatStatsCategory.Edania, saved.Category);
    }

    [Fact]
    public async Task NewCaptureCanReplaceRestoredStatsAfterTheSystemClockMovesBackward()
    {
        var saved = CurrentSessionStoreTests.Example() with
        {
            CombatStats = new(1700, 800, CombatStatsCategory.General, DateTimeOffset.UtcNow.AddDays(1)),
        };
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => new(2374, 826, CombatStatsCategory.Edania)),
            TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor,
            lootScrollVisible: _ => true, restoredSession: saved);
        Assert.Equal(saved.CombatStats, fixture.Service.State.SessionCombatStats);
        fixture.ResumeClocks();
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
        var now = DateTimeOffset.UtcNow;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-2));
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-1));
        Assert.Equal(CombatStatsCategory.Edania, fixture.Service.State.SessionCombatStats!.Category);
        Assert.Equal(2374, fixture.Service.State.SessionCombatStats.Ap);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(2374, Assert.Single(fixture.HistoryStore.Load()).CombatStats!.Ap);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Demihuman)]
    [InlineData(CombatStatsCategory.Kamasylvian)]
    public async Task UnrelatedColoredStatsAreNotAttachedToAnEdaniaSession(CombatStatsCategory category)
    {
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => new(1900, 810, category)),
            TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor, lootScrollVisible: _ => true);
        BeginCombatStatsSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-2));
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-1));

        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Null(fixture.Service.State.SessionCombatStats);
        Assert.Equal(category, fixture.Service.State.ObservedCombatStatsCategory);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Null(Assert.Single(fixture.HistoryStore.Load()).CombatStats);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Demihuman)]
    [InlineData(CombatStatsCategory.Kamasylvian)]
    public async Task UnrelatedColoredStatsKeepThePreviousGeneralObservation(CombatStatsCategory category)
    {
        CombatStatsReading reading = new(1700, 800, CombatStatsCategory.General);
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor, lootScrollVisible: _ => true);
        BeginCombatStatsSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-4));
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-3));
        var previous = fixture.Service.State.SessionCombatStats;
        Assert.NotNull(previous);
        reading = new(1900, 810, category);
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-2));
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-1));

        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Equal(previous, fixture.Service.State.SessionCombatStats);
        Assert.Equal(category, fixture.Service.State.ObservedCombatStatsCategory);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Equal(previous, Assert.Single(fixture.HistoryStore.Load()).CombatStats);
    }

    [Theory]
    [InlineData(CombatStatsCategory.General, true)]
    [InlineData(CombatStatsCategory.Edania, false)]
    public async Task DetectedSpotChangeRechecksPreviouslyConfirmedStats(CombatStatsCategory category, bool remainsApplicable)
    {
        CombatStatsReading? reading = new(2374, 826, category);
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor, lootScrollVisible: _ => true);
        BeginCombatStatsSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-3));
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-2));
        var previous = fixture.Service.State.SessionCombatStats;
        Assert.NotNull(previous);
        reading = null;
        await ProcessCombatStatsFrame(fixture, monitor, now.AddSeconds(-1), spotId: "tungrad-ruins");

        Assert.Equal("tungrad-ruins", fixture.Service.State.SpotId);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Equal(remainsApplicable ? previous : null, fixture.Service.State.SessionCombatStats);
    }

    [Fact]
    public async Task OptionalHudReaderFailureDoesNotBlockLootOrTracking()
    {
        var monitor = new CombatStatsMonitor(new SessionCombatStatsReader(() => throw new IOException("Unreadable HUD")));
        await using var fixture = new Fixture(autoUpload: false, combatStatsMonitor: monitor, lootScrollVisible: _ => true);
        BeginCombatStatsSession(fixture);
        await ProcessCombatStatsFrame(fixture, monitor, DateTimeOffset.UtcNow);
        Assert.False(fixture.Service.State.CombatStats.IsKnown);
        Assert.Null(fixture.Service.State.ObservedCombatStatsCategory);
        Assert.Null(fixture.Service.State.SessionCombatStats);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.CanPause);
        Assert.False(fixture.Service.State.IsError);
        Assert.Null(fixture.Service.State.TrackingBlockedReason);
    }

    private static void BeginCombatStatsSession(Fixture fixture)
    {
        fixture.Begin();
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
    }

    private static async Task ProcessCombatStatsFrame(Fixture fixture, CombatStatsMonitor monitor,
        DateTimeOffset capturedAt, bool visible = true, string spotId = LootSpotCatalog.HermesiaId)
    {
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10)) with { SpotId = spotId };
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(1, capturedAt) { CanObserveHud = visible }, CancellationToken.None);
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Service.RefreshPendingState();
    }

    private sealed class SessionCombatStatsReader(Func<CombatStatsReading?> read) : ICombatStatsFrameReader
    {
        public CombatStatsReading? Read(Bitmap frame, CancellationToken cancellationToken) => read();
        public void Dispose() { }
    }
}
