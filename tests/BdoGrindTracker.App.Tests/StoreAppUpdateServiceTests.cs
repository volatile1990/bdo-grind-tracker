using BdoGrindTracker.App.Updates;
using Windows.Services.Store;

namespace BdoGrindTracker.App.Tests;

public sealed class StoreAppUpdateServiceTests
{
    [Fact]
    public async Task UpdateFlowsThroughDownloadAndDurablePreparationBeforeInstallation()
    {
        var backend = new Backend();
        var steps = new List<string>();
        backend.Installing = () => steps.Add("install");
        var service = Create(backend, prepare: async install =>
        {
            steps.Add("save");
            await install();
            return true;
        });
        await service.CheckAsync();
        Assert.True(service.State.UsesStore);
        Assert.True(service.State.CanDownload);
        Assert.Null(service.State.AvailableVersion); // Store does not expose the target version here.
        await service.DownloadAsync();
        Assert.True(service.State.IsReady);
        Assert.Equal(100, service.State.DownloadPercent);
        await service.CheckAsync();
        Assert.Equal(1, backend.Checks); // Do not discard the staged packages.
        await service.RequestRestartAsync();
        Assert.Equal(["save", "install"], steps);
        Assert.Equal(UpdatePhase.Installed, service.State.Phase);
        await service.RequestRestartAsync();
        Assert.Equal(1, backend.Installs);
    }

    [Fact]
    public async Task TrackingMayContinueDuringDownloadButBlocksInstall()
    {
        var blocked = true;
        var backend = new Backend();
        var prepares = 0;
        var service = Create(backend, () => blocked, async install => { prepares++; await install(); return true; });
        await service.CheckAsync();
        await service.DownloadAsync();
        await service.RequestRestartAsync();
        Assert.Equal(1, backend.Downloads);
        Assert.Equal(0, prepares);
        Assert.True(service.State.IsReady);
        blocked = false;
        await service.RequestRestartAsync();
        Assert.Equal(1, backend.Installs);
    }

    [Fact]
    public async Task FailedSaveNeverCallsStoreInstallerAndCanBeRetried()
    {
        var fail = true;
        var backend = new Backend();
        var service = Create(backend, prepare: async install =>
        {
            if (fail) throw new IOException("disk full");
            await install();
            return true;
        });
        await service.CheckAsync();
        await service.DownloadAsync();
        await service.RequestRestartAsync();
        Assert.True(service.State.IsReady);
        Assert.Equal(0, backend.Installs);
        fail = false;
        await service.RequestRestartAsync();
        Assert.Equal(UpdatePhase.Installed, service.State.Phase);
    }

    [Fact]
    public async Task HostRecheckCanRefuseInstallationAfterSessionStateChanges()
    {
        var backend = new Backend();
        var service = Create(backend, prepare: _ => Task.FromResult(false));
        await service.CheckAsync();
        await service.DownloadAsync();
        await service.RequestRestartAsync();
        Assert.Equal(0, backend.Installs);
        Assert.True(service.State.IsReady);
    }

    [Theory]
    [InlineData((int)StoreUpdateResult.Canceled)]
    [InlineData((int)StoreUpdateResult.LowBattery)]
    [InlineData((int)StoreUpdateResult.NetworkRequired)]
    [InlineData((int)StoreUpdateResult.Failed)]
    public async Task IncompleteDownloadRemainsAvailableForAnotherAttempt(int result)
    {
        var backend = new Backend { DownloadResult = (StoreUpdateResult)result };
        var service = Create(backend);
        await service.CheckAsync();
        await service.DownloadAsync();
        Assert.True(service.State.CanDownload);
        Assert.False(service.State.IsReady);
        Assert.Equal(0, backend.Installs);
        backend.DownloadResult = StoreUpdateResult.Completed;
        await service.DownloadAsync();
        Assert.True(service.State.IsReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledInstallationLeavesUpdateRetryable(bool throws)
    {
        var backend = new Backend { InstallResult = StoreUpdateResult.Canceled, ThrowInstall = throws };
        var service = Create(backend);
        await service.CheckAsync();
        await service.DownloadAsync();
        await service.RequestRestartAsync();
        Assert.True(service.State.IsReady);
        Assert.False(service.State.IsBusy);
        backend.InstallResult = StoreUpdateResult.Completed;
        backend.ThrowInstall = false;
        await service.RequestRestartAsync();
        Assert.Equal(UpdatePhase.Installed, service.State.Phase);
    }

    [Fact]
    public async Task OfflineCheckDoesNotPermitDownloadAndCanRecover()
    {
        var backend = new Backend { ThrowCheck = true };
        var service = Create(backend);
        service.Changed += () => throw new ObjectDisposedException("UI");
        await service.CheckAsync();
        Assert.Equal(UpdatePhase.Error, service.State.Phase);
        Assert.False(service.State.CanDownload);
        await service.DownloadAsync();
        Assert.Equal(0, backend.Downloads);
        backend.ThrowCheck = false;
        backend.Available = false;
        await service.CheckAsync();
        Assert.Equal(UpdatePhase.Idle, service.State.Phase);
        await service.SetBetaAsync(true);
        Assert.False(service.State.IsBeta);
    }

    [Fact]
    public async Task OverlappingClicksCannotStartDuplicateOperations()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { DownloadGate = gate.Task };
        var service = Create(backend);
        await service.CheckAsync();
        var download = service.DownloadAsync();
        await service.DownloadAsync();
        await service.CheckAsync();
        await service.RequestRestartAsync();
        Assert.Equal(1, backend.Downloads);
        Assert.Equal(1, backend.Checks);
        Assert.Equal(0, backend.Installs);
        gate.SetResult();
        await download;
        Assert.True(service.State.IsReady);
    }

    [Fact]
    public async Task AvailableUpdateInstallsWithOneClickAfterDurableSaveWithoutSeparateDownload()
    {
        var steps = new List<string>();
        var backend = new Backend { Installing = () => steps.Add("install") };
        var service = Create(backend, prepare: async install =>
        {
            steps.Add("save");
            await install();
            await install(); // Host retries cannot start another Store operation.
            return true;
        });
        await service.CheckAsync();
        await service.RequestRestartAsync();
        Assert.Equal(["save", "install"], steps);
        Assert.Equal(0, backend.Downloads);
        Assert.Equal(1, backend.Installs);
        Assert.Equal(UpdatePhase.Installed, service.State.Phase);
        Assert.Equal(100, service.State.DownloadPercent);
    }

    [Fact]
    public async Task DirectInstallCannotInterruptTrackingOrRunWithoutSuccessfulCheck()
    {
        var blocked = true;
        var backend = new Backend();
        var service = Create(backend, () => blocked);
        await service.RequestRestartAsync();
        Assert.Equal(0, backend.Installs);
        await service.CheckAsync();
        await service.RequestRestartAsync();
        Assert.Equal(UpdatePhase.Available, service.State.Phase);
        Assert.Equal(0, backend.Installs);
        blocked = false;
        await service.RequestRestartAsync();
        Assert.Equal(1, backend.Installs);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("host-refused")]
    [InlineData("installer")]
    [InlineData("canceled")]
    public async Task FailedDirectInstallDoesNotClaimStagedPackagesAndCanRetry(string failure)
    {
        var fail = true;
        var backend = new Backend { ThrowInstall = failure == "installer",
            InstallResult = failure == "canceled" ? StoreUpdateResult.Canceled : StoreUpdateResult.Completed };
        var service = Create(backend, prepare: async install =>
        {
            if (fail && failure == "save") throw new IOException("disk full");
            if (fail && failure == "host-refused") return false;
            await install();
            return true;
        });
        await service.CheckAsync();
        await service.RequestRestartAsync();
        Assert.Equal(UpdatePhase.Available, service.State.Phase);
        Assert.Equal(0, service.State.DownloadPercent);
        Assert.False(service.State.IsReady);
        if (failure is "save" or "host-refused") Assert.Equal(0, backend.Installs);
        fail = false;
        backend.ThrowInstall = false;
        backend.InstallResult = StoreUpdateResult.Completed;
        await service.RequestRestartAsync();
        Assert.Equal(UpdatePhase.Installed, service.State.Phase);
    }

    [Fact]
    public async Task DirectInstallSerializesRepeatedClicksAndChecksWhileStoreOwnsTheOperation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { InstallGate = gate.Task };
        var service = Create(backend);
        await service.CheckAsync();
        var install = service.RequestRestartAsync();
        await service.RequestRestartAsync();
        await service.CheckAsync();
        await service.DownloadAsync();
        Assert.Equal(UpdatePhase.Restarting, service.State.Phase);
        Assert.Equal(1, backend.Installs);
        Assert.Equal(1, backend.Checks);
        Assert.Equal(0, backend.Downloads);
        gate.SetResult();
        await install;
        Assert.Equal(UpdatePhase.Installed, service.State.Phase);
    }

    [Fact]
    public void NonCompletedWindowsStatesNeverCountAsSuccess()
    {
        foreach (var state in Enum.GetValues<StorePackageUpdateState>())
            Assert.Equal(state == StorePackageUpdateState.Completed,
                StoreUpdateBackend.MapResult(state) == StoreUpdateResult.Completed);
    }

    private static StoreAppUpdateService Create(Backend backend, Func<bool>? blocked = null,
        Func<Func<Task>, Task<bool>>? prepare = null) => new(backend, blocked ?? (() => false),
        prepare ?? (async install => { await install(); return true; }), "1.0.1");

    private sealed class Backend : IStoreUpdateBackend
    {
        public bool Available = true, ThrowCheck, ThrowInstall;
        public int Checks, Downloads, Installs;
        public StoreUpdateResult DownloadResult = StoreUpdateResult.Completed, InstallResult = StoreUpdateResult.Completed;
        public Task DownloadGate = Task.CompletedTask;
        public Task InstallGate = Task.CompletedTask;
        public Action? Installing;
        public Task<bool> CheckAsync()
        {
            Checks++;
            if (ThrowCheck) throw new IOException("offline");
            return Task.FromResult(Available);
        }
        public async Task<StoreUpdateResult> DownloadAsync(Action<int> progress)
        {
            Downloads++;
            progress(40);
            await DownloadGate;
            progress(100);
            return DownloadResult;
        }
        public async Task<StoreUpdateResult> InstallAsync(Action<int> progress)
        {
            Installs++;
            Installing?.Invoke();
            if (ThrowInstall) throw new IOException("Store unavailable");
            await InstallGate;
            return InstallResult;
        }
    }
}
