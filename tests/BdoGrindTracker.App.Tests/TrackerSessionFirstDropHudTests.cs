using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task FirstDropReplacesWaitingHudSamplesWithoutCountingTheirExperienceGain()
    {
        var agrisStatus = AgrisStatus.Inactive;
        ExperienceReading? experience = new(61, 10m);
        var agrisDetector = new SessionAgrisDetector(() => new(agrisStatus));
        var experienceReader = new SessionExperienceReader(() => experience);
        var agris = new AgrisMonitor(agrisDetector, TimeSpan.FromSeconds(2));
        var experienceMonitor = new ExperienceMonitor(experienceReader, TimeSpan.FromSeconds(2));
        await using var fixture = new Fixture(autoUpload: false, agrisMonitor: agris,
            experienceMonitor: experienceMonitor, lootScrollVisible: _ => true);
        BeginAgrisSession(fixture);
        fixture.Clock.Pause();
        fixture.Clock.Start(waitForFirstDrop: true);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        var now = DateTimeOffset.UtcNow;

        await ProcessHudFrame(now.AddSeconds(-6), hasDrop: false);

        Assert.True(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Equal(AgrisStatus.Inactive, fixture.Service.State.Agris.Status);
        Assert.Equal(10m, fixture.Service.State.Experience.Percent);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.AgrisObservedDuration);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.ExperienceObservedDuration);
        Assert.Null(fixture.Service.State.ExperienceGainedPercentagePoints);

        agrisStatus = AgrisStatus.Active;
        experience = new(61, 20m);
        // The first drop arrives before either monitor's next regular sample.
        // Its frame must replace both waiting snapshots immediately.
        await ProcessHudFrame(now.AddSeconds(-5), hasDrop: true);

        Assert.False(fixture.Service.State.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
        Assert.Equal(2, agrisDetector.Calls);
        Assert.Equal(2, experienceReader.Calls);
        Assert.Equal(AgrisStatus.Active, fixture.Service.State.Agris.Status);
        Assert.Equal(20m, fixture.Service.State.Experience.Percent);
        Assert.Null(fixture.Service.State.ExperienceGainedPercentagePoints);
        Assert.Equal(TimeSpan.Zero, fixture.Service.State.ExperienceObservedDuration);

        // Replay two later HUD readings within the first ten grind seconds.
        // The large XP change during waiting must not enter their measured gain.
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        experience = new(61, 20.1m);
        await ProcessHudFrame(now.AddSeconds(-3), hasDrop: false);
        experience = new(61, 20.2m);
        await ProcessHudFrame(now.AddSeconds(-1), hasDrop: true);

        Assert.Equal(TimeSpan.FromSeconds(10), fixture.Service.State.Elapsed);
        Assert.Equal(.1m, fixture.Service.State.ExperienceGainedPercentagePoints);
        Assert.InRange(fixture.Service.State.ExperienceObservedDuration.TotalSeconds, 1, 2);
        Assert.Equal(AgrisStatus.Active, fixture.Service.State.Agris.Status);
        Assert.Equal(4, agrisDetector.Calls);
        Assert.Equal(4, experienceReader.Calls);
        Assert.Equal(2, fixture.Service.State.Loot.TotalQuantity);

        async Task ProcessHudFrame(DateTimeOffset capturedAt, bool hasDrop)
        {
            fixture.Analyzer.NextResult = hasDrop ? Analysis(("Black Crystal Fragment", 1)) : Analysis();
            using var frame = new Bitmap(2, 2);
            await fixture.Service.ProcessFrameAsync(frame,
                new CapturedFrameMetadata(1, capturedAt) { CanObserveHud = true }, CancellationToken.None);
            await Task.WhenAll(agris.CurrentAnalysis, experienceMonitor.CurrentAnalysis)
                .WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Service.RefreshPendingState();
        }
    }
}
