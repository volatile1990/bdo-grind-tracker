using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task ExperienceProgressReachesLiveStateAndHistoryAndResetsForTheNextSession()
    {
        ExperienceReading? next = new(61, .579m);
        var monitor = new ExperienceMonitor(new SessionExperienceReader(() => next));
        await using var fixture = new Fixture(autoUpload: false, experienceMonitor: monitor, lootScrollVisible: _ => true);
        BeginExperienceSession(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var now = DateTimeOffset.UtcNow;
        await ProcessExperienceFrame(fixture, monitor, now.AddMinutes(-2));
        Assert.Null(fixture.Service.State.ExperienceGainedPercentagePoints);
        next = new(61, .679m);
        await ProcessExperienceFrame(fixture, monitor, now.AddMinutes(-1));
        next = new(61, .629m);
        await ProcessExperienceFrame(fixture, monitor, now);
        var state = fixture.Service.State;
        Assert.Equal(.050m, state.ExperienceGainedPercentagePoints);
        Assert.Equal(61, state.ExperienceStartLevel);
        Assert.Equal(61, state.ExperienceEndLevel);
        Assert.Equal(.629m, state.Experience.Percent);
        Assert.InRange(state.ExperienceObservedDuration.TotalSeconds, 118, 120);
        Assert.Equal(30, state.Loot.TotalQuantity);
        Assert.True(state.IsRunning);
        Assert.False(state.IsError);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(ExperienceState.Unknown, fixture.Service.State.Experience);
        var history = Assert.Single(fixture.HistoryStore.Load());
        Assert.Equal(state.ExperienceGainedPercentagePoints, history.ExperienceGainedPercentagePoints);
        Assert.Equal(state.ExperienceObservedDuration, history.ExperienceObservedDuration);
        Assert.Equal(state.ExperienceStartLevel, history.ExperienceStartLevel);
        Assert.Equal(state.ExperienceEndLevel, history.ExperienceEndLevel);
        Assert.Equal(TimeSpan.FromMinutes(3), history.Duration);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.ExperienceGainedPercentagePoints);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.ExperienceObservedDuration);
        Assert.Equal(.050m, Assert.Single(fixture.HistoryStore.Load()).ExperienceGainedPercentagePoints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenOrUnreadableHudCannotBridgeExperienceEvenWhenTheUiSkipsTheGap(bool unreadable)
    {
        ExperienceReading? next = new(61, .100m);
        var monitor = new ExperienceMonitor(new SessionExperienceReader(() => next), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, experienceMonitor: monitor, lootScrollVisible: _ => true);
        BeginExperienceSession(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(3));
        var now = DateTimeOffset.UtcNow;
        await ProcessExperienceFrame(fixture, monitor, now.AddSeconds(-120));
        next = new(61, .200m);
        await ProcessExperienceFrame(fixture, monitor, now.AddSeconds(-90));
        var progress = fixture.Service.State.ExperienceGainedPercentagePoints;
        Assert.Equal(.100m, progress);
        next = null;
        await ProcessExperienceFrame(fixture, monitor, now.AddSeconds(-60), canObserveHud: unreadable, refresh: false);
        next = new(61, 1.200m);
        await ProcessExperienceFrame(fixture, monitor, now.AddSeconds(-30), refresh: false);
        fixture.Service.RefreshPendingState();
        Assert.Equal(progress, fixture.Service.State.ExperienceGainedPercentagePoints);
        next = new(61, 1.300m);
        await ProcessExperienceFrame(fixture, monitor, now);
        Assert.Equal(.200m, fixture.Service.State.ExperienceGainedPercentagePoints);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(50, fixture.Service.State.Loot.TotalQuantity);
    }

    [Fact]
    public async Task ExperienceOcrFailureNeverBlocksTrackingOrChangesLoot()
    {
        var reader = new SessionExperienceReader(() => throw new InvalidOperationException("Optional XP OCR failure"));
        var monitor = new ExperienceMonitor(reader);
        await using var fixture = new Fixture(autoUpload: false, experienceMonitor: monitor, lootScrollVisible: _ => true);
        BeginExperienceSession(fixture);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await ProcessExperienceFrame(fixture, monitor, DateTimeOffset.UtcNow);
        Assert.Equal(ExperienceState.Unknown, fixture.Service.State.Experience);
        Assert.Null(fixture.Service.State.ExperienceGainedPercentagePoints);
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.CanPause);
        Assert.False(fixture.Service.State.IsError);
        Assert.Null(fixture.Service.State.TrackingBlockedReason);
        await ProcessExperienceFrame(fixture, monitor, DateTimeOffset.UtcNow.AddMinutes(1), canObserveHud: false);
        Assert.Equal(1, reader.Calls);
    }

    private static void BeginExperienceSession(Fixture fixture)
    {
        fixture.Begin();
        SetField(fixture.Service, "_lastCaptureDesktopRegion", new Rectangle(0, 0, 1920, 1080));
    }

    private static async Task ProcessExperienceFrame(Fixture fixture, ExperienceMonitor monitor, DateTimeOffset capturedAt,
        bool canObserveHud = true, bool refresh = true)
    {
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame, new CapturedFrameMetadata(1, capturedAt) { CanObserveHud = canObserveHud }, CancellationToken.None);
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(3));
        if (refresh) fixture.Service.RefreshPendingState();
    }

    private sealed class SessionExperienceReader(Func<ExperienceReading?> read) : IExperienceFrameReader
    {
        public int Calls;
        public ExperienceReading? Read(Bitmap frame, CancellationToken cancellationToken) { Calls++; return read(); }
        public void Dispose() { }
    }
}
