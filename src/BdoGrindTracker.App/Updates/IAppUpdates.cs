namespace BdoGrindTracker.App.Updates;

internal enum UpdatePhase { Disabled, Idle, Checking, Available, Downloading, ReadyToRestart, Restarting, Error }

internal sealed record UpdateState(
    bool Enabled,
    bool IsBeta,
    string InstalledVersion,
    string? AvailableVersion,
    UpdatePhase Phase,
    int DownloadPercent,
    string Message)
{
    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Restarting;
    public bool CanDownload => Enabled && AvailableVersion is not null && Phase is UpdatePhase.Available or UpdatePhase.Error;
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

internal sealed class DisabledAppUpdates(string version, string message) : IAppUpdates
{
    public UpdateState State { get; } = new(false, false, version, null, UpdatePhase.Disabled, 0, message);
    public event Action? Changed { add { } remove { } }
    public Task CheckAsync() => Task.CompletedTask;
    public Task DownloadAsync() => Task.CompletedTask;
    public Task SetBetaAsync(bool enabled) => Task.CompletedTask;
    public Task RequestRestartAsync() => Task.CompletedTask;
}
