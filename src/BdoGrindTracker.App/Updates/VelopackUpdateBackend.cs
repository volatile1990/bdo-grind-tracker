using Velopack;
using Velopack.Locators;

namespace BdoGrindTracker.App.Updates;

internal sealed record UpdateCandidate(string Version, bool IsPrerelease, object? Payload = null);

internal interface IUpdateBackend
{
    bool IsInstalled { get; }
    string InstalledVersion { get; }
    UpdateCandidate? PendingUpdate { get; }
    Task<UpdateCandidate?> CheckAsync();
    Task DownloadAsync(UpdateCandidate update, Action<int> progress);
    void ScheduleApply(UpdateCandidate update);
}

internal sealed class VelopackUpdateBackend : IUpdateBackend
{
    public const string StableChannel = "win-x64-stable";
    public const string BetaChannel = "win-x64-beta";
    private readonly UpdateManager _manager;

    public VelopackUpdateBackend(string repositoryUrl, bool? beta = null)
    {
        _manager = new UpdateManager(new PaginatedGithubSource(repositoryUrl, beta ?? true), new UpdateOptions
        {
            ExplicitChannel = beta is null ? null : beta.Value ? BetaChannel : StableChannel,
            // Returning to stable waits for its next newer release. This avoids running
            // an older binary against session data written by a newer beta.
            AllowVersionDowngrade = false
        });
    }

    public bool IsInstalled => _manager.IsInstalled;
    public bool IsBetaChannel => VelopackLocator.Current.Channel == BetaChannel;
    public string InstalledVersion => _manager.CurrentVersion?.ToString() ?? AppUpdateService.ApplicationVersion;
    public UpdateCandidate? PendingUpdate => _manager.UpdatePendingRestart is { } pending ? Candidate(pending) : null;

    public async Task<UpdateCandidate?> CheckAsync()
    {
        var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return update is null ? null : new(update.TargetFullRelease.Version.ToString(),
            update.TargetFullRelease.Version.IsPrerelease, update);
    }

    public Task DownloadAsync(UpdateCandidate update, Action<int> progress)
    {
        var information = update.Payload as UpdateInfo
            ?? throw new InvalidOperationException("Das Update muss erneut geprüft werden.");
        return _manager.DownloadUpdatesAsync(information, progress);
    }

    public void ScheduleApply(UpdateCandidate update)
    {
        var asset = update.Payload switch
        {
            UpdateInfo information => information.TargetFullRelease,
            VelopackAsset pending => pending,
            _ => throw new InvalidOperationException("Das Updatepaket fehlt.")
        };
        // The host invokes this only after settings/session shutdown has completed.
        // Unlike ApplyUpdatesAndRestart, this does not call Environment.Exit.
        _manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true);
    }

    private static UpdateCandidate Candidate(VelopackAsset asset) => new(asset.Version.ToString(), asset.Version.IsPrerelease, asset);
}
