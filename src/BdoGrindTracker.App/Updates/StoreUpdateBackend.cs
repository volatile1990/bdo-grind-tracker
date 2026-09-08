using Windows.Services.Store;

namespace BdoGrindTracker.App.Updates;

internal enum StoreUpdateResult { Completed, Canceled, LowBattery, NetworkRequired, Failed }

internal interface IStoreUpdateBackend
{
    Task<bool> CheckAsync();
    Task<StoreUpdateResult> DownloadAsync(Action<int> progress);
    Task<StoreUpdateResult> InstallAsync(Action<int> progress);
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

    public Task<StoreUpdateResult> DownloadAsync(Action<int> progress) => TransferAsync(false, progress);
    public Task<StoreUpdateResult> InstallAsync(Action<int> progress) => TransferAsync(true, progress);

    private async Task<StoreUpdateResult> TransferAsync(bool install, Action<int> progress)
    {
        var result = StoreUpdateResult.Failed;
        await onUiThread(async () =>
        {
            if (_packages.Count == 0) return;
            if (install) RegisterApplicationRestart(null, 1 | 2 | 8); // Allow patch restarts, not crashes/hangs/reboots.
            try
            {
                var operation = install
                    ? Context.RequestDownloadAndInstallStorePackageUpdatesAsync(_packages)
                    : Context.RequestDownloadStorePackageUpdatesAsync(_packages);
                operation.Progress = (_, status) => progress((int)Math.Round(status.TotalDownloadProgress * 100));
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
