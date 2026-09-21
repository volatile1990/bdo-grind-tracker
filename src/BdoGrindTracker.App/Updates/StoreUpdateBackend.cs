using Windows.Services.Store;

namespace BdoGrindTracker.App.Updates;

internal enum StoreUpdateResult { Completed, Canceled, LowBattery, NetworkRequired, Failed }
internal enum StoreUpdateStage { Pending, Downloading, Installing, Completed }

internal sealed record StoreUpdateProgress(int DownloadPercent, ulong? DownloadedBytes = null,
    ulong? TotalDownloadBytes = null, StoreUpdateStage Stage = StoreUpdateStage.Downloading);

internal interface IStoreUpdateBackend
{
    Task<bool> CheckAsync();
    Task<StoreUpdateResult> DownloadAsync(Action<StoreUpdateProgress> progress);
    Task<StoreUpdateResult> InstallAsync(Action<StoreUpdateProgress> progress);
}

/// <summary>Store owns package selection, download verification and installation.</summary>
internal sealed class StoreUpdateBackend(Func<nint> ownerWindow, Func<Func<Task>, Task> onUiThread) : IStoreUpdateBackend
{
    private StoreContext? _context;
    private IReadOnlyList<StorePackageUpdate> _packages = [];

    private StoreContext Context
    {
        get
        {
            var window = ownerWindow();
            if (window == 0) throw new InvalidOperationException("The Store needs a live owner window.");
            _context ??= StoreContext.GetDefault();
            WinRT.Interop.InitializeWithWindow.Initialize(_context, window);
            return _context;
        }
    }

    public async Task<bool> CheckAsync()
    {
        await onUiThread(async () =>
        {
            _packages = [];
            _packages = await Context.GetAppAndOptionalStorePackageUpdatesAsync();
        });
        // StorePackageUpdate.Package describes the installed package, not the
        // target version. Do not present that version as the available update.
        return _packages.Count > 0;
    }

    public Task<StoreUpdateResult> DownloadAsync(Action<StoreUpdateProgress> progress) => TransferAsync(false, progress);
    public Task<StoreUpdateResult> InstallAsync(Action<StoreUpdateProgress> progress) => TransferAsync(true, progress);

    private async Task<StoreUpdateResult> TransferAsync(bool install, Action<StoreUpdateProgress> progress)
    {
        var result = StoreUpdateResult.Failed;
        await onUiThread(async () =>
        {
            if (_packages.Count == 0) return;
            if (install) RegisterApplicationRestart(null, 1 | 2 | 8); // Allow patch restarts, not crashes/hangs/reboots.
            try
            {
                var context = Context;
                var tracker = new StoreUpdateProgressTracker(_packages.Select(package => package.Package.Id.FamilyName));
                // The user already chose the update in Grindcrest. Avoid asking
                // again when Store settings and the current network permit it.
                // Choose once: a failed silent request must not start a second
                // installation attempt or another prompt automatically.
                var operation = (install, context.CanSilentlyDownloadStorePackageUpdates) switch
                {
                    (true, true) => context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(_packages),
                    (false, true) => context.TrySilentDownloadStorePackageUpdatesAsync(_packages),
                    (true, false) => context.RequestDownloadAndInstallStorePackageUpdatesAsync(_packages),
                    (false, false) => context.RequestDownloadStorePackageUpdatesAsync(_packages),
                };
                operation.Progress = (_, status) =>
                {
                    // Store byte counts describe one package, while the percent
                    // describes the whole request. Forward byte/stage changes even
                    // when rounding leaves the displayed percent unchanged.
                    lock (tracker) progress(tracker.Update(status));
                };
                result = MapResult((await operation).OverallState);
            }
            finally
            {
                // Keep successful registration alive: Windows may shut us down
                // after the async operation returns. Never relaunch the old EXE ourselves.
                if (install && result != StoreUpdateResult.Completed) UnregisterApplicationRestart();
            }
        });
        return result;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, int flags);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern int UnregisterApplicationRestart();

    internal static StoreUpdateResult MapResult(StorePackageUpdateState state) => state switch
    {
        StorePackageUpdateState.Completed => StoreUpdateResult.Completed,
        StorePackageUpdateState.Canceled => StoreUpdateResult.Canceled,
        StorePackageUpdateState.ErrorLowBattery => StoreUpdateResult.LowBattery,
        StorePackageUpdateState.ErrorWiFiRecommended or StorePackageUpdateState.ErrorWiFiRequired => StoreUpdateResult.NetworkRequired,
        _ => StoreUpdateResult.Failed
    };
}

internal sealed class StoreUpdateProgressTracker(IEnumerable<string> packageFamilyNames)
{
    private readonly Dictionary<string, StorePackageUpdateStatus?> _packages = packageFamilyNames
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToDictionary(name => name, _ => (StorePackageUpdateStatus?)null, StringComparer.OrdinalIgnoreCase);

    internal StoreUpdateProgress Update(StorePackageUpdateStatus status)
    {
        var percent = double.IsFinite(status.TotalDownloadProgress)
            ? (int)Math.Round(Math.Clamp(status.TotalDownloadProgress, 0, 1) * 100)
            : 0;
        var family = status.PackageFamilyName;
        if (string.IsNullOrEmpty(family))
        {
            // Without a family name, bytes cannot be attributed safely across
            // multiple packages. A single requested package is unambiguous.
            if (_packages.Count != 1)
                return new(percent, Stage: StageFor(status.PackageUpdateState));
            family = _packages.Keys.Single();
        }
        if (status.PackageUpdateState is StorePackageUpdateState.Deploying or StorePackageUpdateState.Completed &&
            status.PackageBytesDownloaded == 0 && status.PackageDownloadSizeInBytes == 0 &&
            _packages.TryGetValue(family, out var previous) && previous is { } last)
        {
            // A stage-only notification must not discard the bytes already
            // observed for this package. Do not infer completion from its size.
            status.PackageBytesDownloaded = last.PackageBytesDownloaded;
            status.PackageDownloadSizeInBytes = last.PackageDownloadSizeInBytes;
        }
        _packages[family] = status;

        ulong downloaded = 0, total = 0;
        var hasDownloadSize = false;
        var hasAllSizes = true;
        var allCompleted = true;
        var downloading = false;
        var installing = false;
        foreach (var package in _packages.Values)
        {
            if (package is not { } current)
            {
                hasAllSizes = allCompleted = false;
                continue;
            }
            downloaded += current.PackageBytesDownloaded;
            total += current.PackageDownloadSizeInBytes;
            hasDownloadSize |= current.PackageDownloadSizeInBytes > 0 || current.PackageBytesDownloaded > 0;
            hasAllSizes &= current.PackageDownloadSizeInBytes > 0;
            allCompleted &= current.PackageUpdateState == StorePackageUpdateState.Completed;
            downloading |= current.PackageUpdateState == StorePackageUpdateState.Downloading;
            installing |= current.PackageUpdateState == StorePackageUpdateState.Deploying;
        }
        // Size estimates can change during a download. Wait for all requested
        // packages before presenting their sum as the overall target size.
        var stage = downloading ? StoreUpdateStage.Downloading
            : installing ? StoreUpdateStage.Installing
            : allCompleted ? StoreUpdateStage.Completed
            : StoreUpdateStage.Pending;
        return new(percent, hasDownloadSize ? downloaded : null, hasAllSizes ? total : null, stage);
    }

    private static StoreUpdateStage StageFor(StorePackageUpdateState state) => state switch
    {
        StorePackageUpdateState.Downloading => StoreUpdateStage.Downloading,
        StorePackageUpdateState.Deploying => StoreUpdateStage.Installing,
        // One unidentified package completing cannot complete the whole request.
        _ => StoreUpdateStage.Pending
    };
}
