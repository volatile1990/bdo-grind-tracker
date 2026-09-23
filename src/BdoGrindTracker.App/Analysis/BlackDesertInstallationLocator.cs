using Microsoft.Win32;

namespace BdoGrindTracker.App.Analysis;

internal static class BlackDesertInstallationLocator
{
    public static IReadOnlyList<string> FindDirectories()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(name);
                    if (app?.GetValue("DisplayName") is not string displayName ||
                        !displayName.StartsWith("Black Desert", StringComparison.OrdinalIgnoreCase)) continue;
                    if (app.GetValue("InstallLocation") is string path && Path.IsPathFullyQualified(path))
                        paths.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
                }
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or
                UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException) { }
        }
        return paths.ToArray();
    }
}
