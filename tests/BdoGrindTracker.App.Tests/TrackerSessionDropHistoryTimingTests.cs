using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task DelayedUiPublicationKeepsRareDropWhenAutomaticPauseTrimsIdleTime()
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { AutoPauseMinutes = 1 });
        fixture.Begin();
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        fixture.Analyzer.NextResult = Analysis(("Pure Black Stone", 1));
        using var frame = new Bitmap(2, 2);
        await fixture.Service.ProcessFrameAsync(frame,
            new CapturedFrameMetadata(1, fixture.Time.GetUtcNow()), CancellationToken.None);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(400));
        fixture.Service.RefreshPendingState();
        var observed = Assert.Single(fixture.Service.State.DropHistory);
        Assert.Equal(TimeSpan.FromSeconds(10), observed.Elapsed);

        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.State.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(10), fixture.Service.State.Elapsed);
        Assert.Equal(observed, Assert.Single(fixture.Service.State.DropHistory));
        var saved = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load();
        Assert.NotNull(saved);
        Assert.Equal(observed, Assert.Single(saved.DropHistory!));
    }
}
