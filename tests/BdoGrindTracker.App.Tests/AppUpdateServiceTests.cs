using BdoGrindTracker.App.Updates;

namespace BdoGrindTracker.App.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task PreviewModeNeverCallsSessionOrRestartCallbacks()
    {
        var updates = AppUpdateService.Create(false, () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), _ => throw new InvalidOperationException());

        await updates.CheckAsync();
        await updates.DownloadAsync();
        await updates.SetBetaAsync(true);
        await updates.RequestRestartAsync();

        Assert.False(updates.State.Enabled);
        Assert.Equal(UpdatePhase.Disabled, updates.State.Phase);
    }

    [Fact]
    public async Task UninstalledBuildDoesNotContactTheReleaseSource()
    {
        using var fixture = new Fixture();
        fixture.Backend.IsInstalled = false;
        var service = fixture.Create();

        await service.CheckAsync();
        await service.DownloadAsync();
        await service.SetBetaAsync(true);

        Assert.Equal(0, fixture.Backend.CheckCount);
        Assert.Equal(0, fixture.Backend.DownloadCount);
        Assert.False(service.State.Enabled);
    }

    [Fact]
    public async Task UnavailableGitHubDoesNotBreakTrackingAndCheckCanBeRetried()
    {
        using var fixture = new Fixture();
        fixture.Backend.CheckFailure = new HttpRequestException("offline");
        var service = fixture.Create();

        await service.CheckAsync();
        Assert.Equal(UpdatePhase.Error, service.State.Phase);

        fixture.Backend.CheckFailure = null;
        await service.CheckAsync();

        Assert.Equal(UpdatePhase.Available, service.State.Phase);
        Assert.Equal("1.1.0", service.State.AvailableVersion);
    }

    [Fact]
    public async Task StableChannelRejectsPrereleaseEvenIfFeedIsMislabelled()
    {
        using var fixture = new Fixture();
        fixture.Backend.Available = new("1.1.0-beta.1", true);
        var service = fixture.Create();

        await service.CheckAsync();

        Assert.Equal(UpdatePhase.Idle, service.State.Phase);
        Assert.Null(service.State.AvailableVersion);
        await service.DownloadAsync();
        Assert.Equal(0, fixture.Backend.DownloadCount);
    }

    [Fact]
    public async Task BetaOptInPersistsAndIncludesPrereleases()
    {
        using var fixture = new Fixture();
        fixture.Backend.Available = new("1.1.0-beta.1", true);
        var channels = new List<bool>();
        var service = fixture.Create(beta => { channels.Add(beta); return fixture.Backend; });

        await service.SetBetaAsync(true);

        Assert.True(service.State.IsBeta);
        Assert.Equal(UpdatePhase.Available, service.State.Phase);
        Assert.Equal(new[] { false, true }, channels);
        Assert.True(fixture.Create().State.IsBeta);
    }

    [Fact]
    public async Task FailedDownloadCannotTriggerAnInstallerAndCanBeRetried()
    {
        using var fixture = new Fixture();
        fixture.Backend.DownloadFailure = new IOException("incomplete package");
        var service = fixture.Create();
        await service.CheckAsync();

        await service.DownloadAsync();
        await service.RequestRestartAsync();

        Assert.Equal(UpdatePhase.Error, service.State.Phase);
        Assert.True(service.State.CanDownload);
        Assert.Equal(0, fixture.RestartRequests);
        Assert.Equal(0, fixture.Backend.ScheduleCount);
        Assert.Null(fixture.Store.Load().PendingVersion);

        fixture.Backend.DownloadFailure = null;
        await service.DownloadAsync();
        Assert.Equal(UpdatePhase.ReadyToRestart, service.State.Phase);
    }

    [Fact]
    public async Task DownloadOnlyBecomesReadyAndApplyIsScheduledAfterHostSaves()
    {
        using var fixture = new Fixture();
        var saved = false;
        var steps = new List<string>();
        fixture.Backend.OnApply = () =>
        {
            Assert.True(saved);
            steps.Add("apply");
        };
        fixture.Restart = async schedule =>
        {
            await Task.Yield();
            saved = true;
            steps.Add("saved");
            schedule();
            steps.Add("close");
            return true;
        };
        var service = fixture.Create();
        var progress = new List<int>();
        service.Changed += () => progress.Add(service.State.DownloadPercent);
        await service.CheckAsync();
        await service.DownloadAsync();

        Assert.Equal(UpdatePhase.ReadyToRestart, service.State.Phase);
        Assert.Contains(42, progress);
        Assert.Equal(100, service.State.DownloadPercent);
        Assert.Equal(0, fixture.Backend.ScheduleCount);
        Assert.False(saved);

        await service.RequestRestartAsync();
        await service.RequestRestartAsync();

        Assert.Equal(new[] { "saved", "apply", "close" }, steps);
        Assert.Equal(1, fixture.Backend.ScheduleCount);
        Assert.Equal(UpdatePhase.Restarting, service.State.Phase);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RunningOrBusySessionBlocksRestart(bool running, bool busy)
    {
        using var fixture = new Fixture { Running = running, Busy = busy };
        var service = fixture.Create();
        await service.CheckAsync();
        await service.DownloadAsync();

        await service.RequestRestartAsync();

        Assert.Equal(UpdatePhase.ReadyToRestart, service.State.Phase);
        Assert.Equal(0, fixture.RestartRequests);
        Assert.Equal(0, fixture.Backend.ScheduleCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrDeclinedHostShutdownNeverSchedulesApply(bool throws)
    {
        using var fixture = new Fixture();
        fixture.Restart = _ => throws ? throw new IOException("save failed") : Task.FromResult(false);
        var service = fixture.Create();
        await service.CheckAsync();
        await service.DownloadAsync();

        await service.RequestRestartAsync();

        Assert.Equal(0, fixture.Backend.ScheduleCount);
        Assert.Equal(UpdatePhase.ReadyToRestart, service.State.Phase);
    }

    [Fact]
    public async Task DownloadSurvivesAppRestartWithoutAnyNetworkCallOrAutomaticApply()
    {
        using var fixture = new Fixture();
        var first = fixture.Create();
        await first.CheckAsync();
        await first.DownloadAsync();
        fixture.Backend.PendingUpdate = fixture.Backend.Available;
        fixture.Backend.CheckFailure = new HttpRequestException("offline");

        var reopened = fixture.Create();
        await reopened.CheckAsync();

        Assert.Equal(UpdatePhase.ReadyToRestart, reopened.State.Phase);
        Assert.Equal("1.1.0", reopened.State.AvailableVersion);
        Assert.Equal(1, fixture.Backend.CheckCount);
        Assert.Equal(0, fixture.Backend.ScheduleCount);
    }

    [Fact]
    public async Task SwitchingBackToStableCannotInstallPreviouslyDownloadedBeta()
    {
        using var fixture = new Fixture();
        fixture.Backend.Available = new("1.1.0-beta.1", true);
        var service = fixture.Create();
        await service.SetBetaAsync(true);
        await service.DownloadAsync();
        fixture.Backend.PendingUpdate = fixture.Backend.Available;

        await service.SetBetaAsync(false);
        await service.RequestRestartAsync();
        var reopened = fixture.Create();
        await reopened.RequestRestartAsync();

        Assert.False(reopened.State.IsBeta);
        Assert.False(reopened.State.IsReady);
        Assert.Null(fixture.Store.Load().PendingVersion);
        Assert.Equal(0, fixture.Backend.ScheduleCount);
    }

    [Fact]
    public async Task ChannelCannotChangeWhileAReleaseCheckIsInProgress()
    {
        using var fixture = new Fixture();
        var response = new TaskCompletionSource<UpdateCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Backend.CheckTask = response.Task;
        var service = fixture.Create();
        var checking = service.CheckAsync();

        await service.SetBetaAsync(true);
        await service.CheckAsync();
        await service.DownloadAsync();

        Assert.Equal(UpdatePhase.Checking, service.State.Phase);
        Assert.False(service.State.IsBeta);
        Assert.Equal(1, fixture.Backend.CheckCount);
        Assert.Equal(0, fixture.Backend.DownloadCount);

        response.SetResult(fixture.Backend.Available);
        await checking;
        Assert.Equal(UpdatePhase.Available, service.State.Phase);
    }

    [Fact]
    public async Task DisposedUiSubscriberCannotTurnSuccessfulDownloadIntoFailure()
    {
        using var fixture = new Fixture();
        var service = fixture.Create();
        service.Changed += () => throw new ObjectDisposedException("view");

        await service.CheckAsync();
        await service.DownloadAsync();

        Assert.Equal(UpdatePhase.ReadyToRestart, service.State.Phase);
    }

    [Fact]
    public void CorruptedUpdatePreferencesUsePackagedChannelAndLeaveTrackerSettingsUntouched()
    {
        using var fixture = new Fixture();
        var trackerSettings = Path.Combine(fixture.DirectoryPath, "settings.json");
        const string original = "{\"SpotId\":\"hermesia\",\"AutoPauseMinutes\":9}";
        File.WriteAllText(trackerSettings, original);
        File.WriteAllText(fixture.SettingsPath, "broken json");

        Assert.True(fixture.Store.Load(defaultBeta: true).IsBeta);
        fixture.Store.Save(new(true));

        Assert.Equal(original, File.ReadAllText(trackerSettings));
    }

    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.UpdateTests", Guid.NewGuid().ToString("N"));
        public string SettingsPath => Path.Combine(DirectoryPath, "update-settings.json");
        public UpdatePreferencesStore Store { get; }
        public FakeBackend Backend { get; } = new();
        public bool Running { get; init; }
        public bool Busy { get; init; }
        public int RestartRequests { get; private set; }
        public Func<Action, Task<bool>> Restart { get; set; } = schedule => { schedule(); return Task.FromResult(true); };

        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            Store = new(SettingsPath);
        }

        public AppUpdateService Create(Func<bool, IUpdateBackend>? backendFactory = null) => new(
            backendFactory ?? (_ => Backend), Store, () => Running, () => Busy,
            schedule => { RestartRequests++; return Restart(schedule); });

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class FakeBackend : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;
        public string InstalledVersion => "1.0.0";
        public UpdateCandidate? PendingUpdate { get; set; }
        public UpdateCandidate? Available { get; set; } = new("1.1.0", false);
        public Exception? CheckFailure { get; set; }
        public Exception? DownloadFailure { get; set; }
        public Task<UpdateCandidate?>? CheckTask { get; set; }
        public Action? OnApply { get; set; }
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int ScheduleCount { get; private set; }

        public Task<UpdateCandidate?> CheckAsync()
        {
            CheckCount++;
            if (CheckFailure is not null) throw CheckFailure;
            return CheckTask ?? Task.FromResult(Available);
        }

        public Task DownloadAsync(UpdateCandidate update, Action<int> progress)
        {
            DownloadCount++;
            progress(42);
            if (DownloadFailure is not null) throw DownloadFailure;
            progress(100);
            return Task.CompletedTask;
        }

        public void ScheduleApply(UpdateCandidate update)
        {
            ScheduleCount++;
            OnApply?.Invoke();
        }
    }
}
