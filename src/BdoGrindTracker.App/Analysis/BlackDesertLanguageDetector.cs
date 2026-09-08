using BdoGrindTracker.Ocr;
using Microsoft.Win32;

namespace BdoGrindTracker.App.Analysis;

internal static class BlackDesertLanguageDetector
{
    public static GameLanguageDetection Detect() => BlackDesertLanguageReader.Read(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Black Desert", "GameOption.txt"),
        InstallationDirectories());

    private static IReadOnlyList<string> InstallationDirectories()
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
                        paths.Add(path);
                }
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or
                UnauthorizedAccessException or IOException) { }
        }
        return paths.ToArray();
    }
}
