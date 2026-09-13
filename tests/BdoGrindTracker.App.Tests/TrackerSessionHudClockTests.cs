using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task HudFreshnessUsesCaptureClockAcrossWallClockJumpsAndStillExpires(int shiftDays)
    {
        var clock = new HudCaptureClock();
        var capture = new PassiveCaptureSession(_ => new Bitmap(2, 2), clock, TimeSpan.FromDays(1));
        var agris = new AgrisMonitor(new SessionAgrisDetector(() => new(AgrisStatus.Active)), TimeSpan.FromSeconds(1));
        var experience = new ExperienceMonitor(new SessionExperienceReader(() => new(61, .25m)), TimeSpan.FromSeconds(1));
        var remaining = TimeSpan.FromSeconds(1000);
        var scroll = new LootScrollMonitor(new SessionScrollDetector(() => new(LootScrollStatus.Active, 1)
        { RemainingTime = remaining, TimerResolution = TimeSpan.FromSeconds(1) }), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, suppliedCapture: capture,
            agrisMonitor: agris, experienceMonitor: experience, lootScrollMonitor: scroll, lootScrollVisible: _ => true);
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await WaitUntilAsync(() => fixture.Analyzer.Calls == 1);
        await capture.StopAsync(); // Keep service running; feed controlled frames on its capture time line.
        await Task.WhenAll(agris.CurrentAnalysis, experience.CurrentAnalysis, scroll.CurrentAnalysis);
        fixture.Service.RefreshPendingState();
        Assert.Equal(AgrisStatus.Active, fixture.Service.State.Agris.Status);
        Assert.True(fixture.Service.State.Experience.IsKnown);

        clock.UtcNow = clock.UtcNow.AddDays(shiftDays);
        clock.Advance(TimeSpan.FromSeconds(10));
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        remaining -= TimeSpan.FromSeconds(10);
        using (var frame = new Bitmap(2, 2))
            await fixture.Service.ProcessFrameAsync(frame,
                new CapturedFrameMetadata(2, capture.ObservationTime) { CanObserveHud = true }, CancellationToken.None);
        await Task.WhenAll(agris.CurrentAnalysis, experience.CurrentAnalysis, scroll.CurrentAnalysis);
        fixture.Service.RefreshPendingState();
        Assert.Equal(AgrisStatus.Active, fixture.Service.State.Agris.Status);
        Assert.True(fixture.Service.State.Experience.IsKnown);
        Assert.Equal(LootScrollStatus.Active, fixture.Service.State.LootScroll.Status);
        Assert.Equal(1, fixture.Service.State.LootScroll.Level);

        clock.Advance(TimeSpan.FromSeconds(151));
        fixture.Service.RefreshPendingState();
        Assert.Equal(AgrisStatus.Unknown, fixture.Service.State.Agris.Status);
        Assert.False(fixture.Service.State.Experience.IsKnown);
        Assert.Equal(LootScrollStatus.Unknown, fixture.Service.State.LootScroll.Status);
    }

    private sealed class HudCaptureClock : TimeProvider
    {
        private long _ticks;
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
}
