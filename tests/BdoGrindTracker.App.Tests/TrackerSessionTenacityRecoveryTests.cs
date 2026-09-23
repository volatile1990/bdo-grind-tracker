using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task TenacityFirstApplicationWithUnreadableTimerAndLaterRefreshBothReachSessionHistory()
    {
        const string tenacity = "perfume-of-tenacity";
        BuffFrameReading? reading = new([]);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-8));
        reading = new([]) { UnknownBuffIds = [tenacity] };
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-7));
        reading = new([new(tenacity, TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(1))]);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-6));
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-5));
        Assert.Equal(tenacity, Assert.Single(fixture.Service.State.Buffs!.Consumptions).BuffId);

        reading = new([new(tenacity, TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(1))]);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-4));
        reading = new([new(tenacity, TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(1))]);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        Assert.Equal(2, fixture.Service.State.Buffs!.Consumptions.Count);
        Assert.All(fixture.Service.State.Buffs.Consumptions, item => Assert.Equal(tenacity, item.BuffId));

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(2, Assert.Single(fixture.HistoryStore.Load()).Buffs!.Consumptions.Count);
    }
}
