using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.Updates;

internal enum AppPackageIdentity { Unpackaged, Packaged, Unknown }

/// <summary>Only an unpackaged process may initialize or use the GitHub updater.</summary>
internal sealed class AppUpdateRuntime(AppPackageIdentity packageIdentity)
{
    private static readonly Lazy<AppUpdateRuntime> Runtime = new(() => new(DetectPackageIdentity()));
    public static AppUpdateRuntime Current => Runtime.Value;
    public AppPackageIdentity PackageIdentity => packageIdentity;

    public void Bootstrap(Action initializeVelopack)
    {
        if (packageIdentity == AppPackageIdentity.Unpackaged) initializeVelopack();
    }

    public IAppUpdates CreateUpdates(bool enabled, Func<IAppUpdates> createUnpackagedUpdates,
        Func<IAppUpdates>? createStoreUpdates = null)
    {
        if (!enabled)
            return new DisabledAppUpdates(AppUpdateService.ApplicationVersion,
                "Updates sind in der Vorschau und bei Prüfungen deaktiviert.");
        if (packageIdentity == AppPackageIdentity.Packaged)
            return createStoreUpdates?.Invoke() ?? new DisabledAppUpdates(AppUpdateService.ApplicationVersion,
                "Diese Version wird über den Microsoft Store aktualisiert.", UpdatePhase.StoreManaged);
        if (packageIdentity == AppPackageIdentity.Unknown)
            return new DisabledAppUpdates(AppUpdateService.ApplicationVersion,
                "Die Installationsart konnte nicht erkannt werden. Grindcrest kann weiter verwendet werden; App-Updates sind vorübergehend deaktiviert.");
        // Keep backend creation, channel preferences and network access behind
        // the package check, including when a GitHub installation also exists.
        return createUnpackagedUpdates();
    }

    private static AppPackageIdentity DetectPackageIdentity()
    {
        if (!OperatingSystem.IsWindows()) return AppPackageIdentity.Unknown;
        try
        {
            uint length = 0;
            return ClassifyPackageIdentity(GetCurrentPackageFullName(ref length, IntPtr.Zero));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return AppPackageIdentity.Unknown;
        }
    }

    internal static AppPackageIdentity ClassifyPackageIdentity(int result) => result switch
    {
        122 => AppPackageIdentity.Packaged, // ERROR_INSUFFICIENT_BUFFER: an identity exists.
        15700 => AppPackageIdentity.Unpackaged, // APPMODEL_ERROR_NO_PACKAGE.
        _ => AppPackageIdentity.Unknown
    };

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
