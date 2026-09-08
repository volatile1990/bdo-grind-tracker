namespace BdoGrindTracker.App.Updates;

internal enum UpdatePhase { Disabled, StoreManaged, Idle, Checking, Available, Downloading, ReadyToRestart, Restarting, Installed, Error }

internal sealed record UpdateState(
    bool Enabled,
    bool IsBeta,
    string InstalledVersion,
    string? AvailableVersion,
    UpdatePhase Phase,
    int DownloadPercent,
    string Message)
{
    public bool UsesStore { get; init; }
    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Restarting;
    public bool CanDownload => Enabled && (Phase == UpdatePhase.Available || AvailableVersion is not null && Phase == UpdatePhase.Error);
    public bool IsReady => Phase == UpdatePhase.ReadyToRestart;
}

internal interface IAppUpdates
{
    UpdateState State { get; }
    event Action? Changed;
    Task CheckAsync();
    Task DownloadAsync();
    Task SetBetaAsync(bool enabled);
    Task RequestRestartAsync();
}

internal sealed class DisabledAppUpdates(string version, string message, UpdatePhase phase = UpdatePhase.Disabled) : IAppUpdates
{
    public UpdateState State { get; } = new(false, false, version, null, phase, 0, message);
    public event Action? Changed { add { } remove { } }
    public Task CheckAsync() => Task.CompletedTask;
    public Task DownloadAsync() => Task.CompletedTask;
    public Task SetBetaAsync(bool enabled) => Task.CompletedTask;
    public Task RequestRestartAsync() => Task.CompletedTask;
}
