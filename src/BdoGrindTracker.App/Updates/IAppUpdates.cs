namespace BdoGrindTracker.App.Updates;

internal enum UpdatePhase { Disabled, StoreManaged, Idle, Checking, Available, Downloading, ReadyToRestart, Restarting, Installed, Error }

internal sealed record UpdateState(
    bool Enabled,
    string InstalledVersion,
    string? AvailableVersion,
    UpdatePhase Phase,
    int DownloadPercent,
    string Message)
{
    public bool UsesStore { get; init; }
    public bool IsDownloadIndeterminate { get; init; }
    public ulong? DownloadedBytes { get; init; }
    public ulong? TotalDownloadBytes { get; init; }
    public DateTimeOffset? OperationStartedAt { get; init; }
    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Restarting;
    public bool CanDownload => Enabled && Phase == UpdatePhase.Available;
    public bool IsReady => Phase == UpdatePhase.ReadyToRestart;
}

internal interface IAppUpdates
{
    UpdateState State { get; }
    event Action? Changed;
    Task CheckAsync();
    Task DownloadAsync();
    Task RequestRestartAsync();
}

internal sealed class DisabledAppUpdates(string version, string message, UpdatePhase phase = UpdatePhase.Disabled) : IAppUpdates
{
    public UpdateState State { get; } = new(false, version, null, phase, 0, message);
    public event Action? Changed { add { } remove { } }
    public Task CheckAsync() => Task.CompletedTask;
    public Task DownloadAsync() => Task.CompletedTask;
    public Task RequestRestartAsync() => Task.CompletedTask;
}
