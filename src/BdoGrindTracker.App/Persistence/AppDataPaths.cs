using System.Runtime.InteropServices;
using System.Text;
using BdoGrindTracker.App.Updates;

namespace BdoGrindTracker.App.Persistence;

/// <summary>Package-owned data stays in LocalState, independently of MSIX file virtualization.</summary>
internal sealed class AppDataPaths
{
    private static readonly Lazy<AppDataPaths> Paths = new(() =>
    {
        var identity = AppUpdateRuntime.Current.PackageIdentity;
        var localAppData = identity == AppPackageIdentity.Packaged
            ? ReadPhysicalLocalAppData()
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Create(identity, localAppData, ReadPackageFamilyName);
    });

    public static AppDataPaths Current => Paths.Value;
    public string BaseDirectory { get; }
    public string LegacyDirectory { get; }
    public bool IsPackaged { get; }

    private AppDataPaths(string baseDirectory, string legacyDirectory, bool isPackaged)
        => (BaseDirectory, LegacyDirectory, IsPackaged) = (baseDirectory, legacyDirectory, isPackaged);

    internal static AppDataPaths Create(AppPackageIdentity identity, string localAppData, Func<string> readFamilyName)
    {
        var local = Path.GetFullPath(localAppData);
        var legacy = Path.Combine(local, "BdoGrindTracker");
        if (identity == AppPackageIdentity.Unpackaged) return new(legacy, legacy, false);
        if (identity != AppPackageIdentity.Packaged)
            throw new IOException("Der sichere Speicherort für Grindcrest-Daten konnte nicht ermittelt werden. Bitte Grindcrest erneut starten.");

        var family = readFamilyName();
        var separator = family.LastIndexOf('_');
        if (separator <= 0 || family.Length - separator - 1 != 13 ||
            family.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new IOException("Die Paketidentität für den Grindcrest-Datenordner ist ungültig.");
        return new(Path.Combine(local, "Packages", family, "LocalState"), legacy, true);
    }

    public static void PrepareForStartup(bool useUserData)
    {
        // Resolve neither data paths nor the old installation for previews or smoke tests.
        if (!useUserData) return;
        var paths = Current;
        if (paths.IsPackaged) LegacyDataImport.Import(paths.LegacyDirectory, paths.BaseDirectory);
    }

    internal delegate int KnownFolderPathResolver(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    internal static string ReadPhysicalLocalAppData(KnownFolderPathResolver? resolve = null)
    {
        var localAppDataId = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
        // Resolve the physical legacy root under MSIX; deriving Packages/<PFN>
        // from a redirected LocalAppData path would nest it in the wrong folder.
        // DONT_VERIFY avoids filesystem probing; never request KF_FLAG_CREATE.
        const uint flags = 0x00010000 | 0x00004000; // NO_PACKAGE_REDIRECTION | DONT_VERIFY.
        IntPtr path = IntPtr.Zero;
        try
        {
            if ((resolve ?? SHGetKnownFolderPath)(ref localAppDataId, flags, IntPtr.Zero, out path) < 0)
                throw new IOException("Der physische Windows-Datenordner konnte nicht ermittelt werden.");
            var value = Marshal.PtrToStringUni(path);
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
                throw new IOException("Der physische Windows-Datenordner ist ungültig.");
            return value;
        }
        finally
        {
            if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path);
        }
    }

    private static string ReadPackageFamilyName()
    {
        uint length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) != 122 || length is < 2 or > 256)
            throw new IOException("Der Microsoft-Store-Datenordner konnte nicht ermittelt werden.");
        var family = new StringBuilder((int)length);
        if (GetCurrentPackageFamilyName(ref length, family) != 0)
            throw new IOException("Der Microsoft-Store-Datenordner konnte nicht ermittelt werden.");
        return family.ToString();
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
