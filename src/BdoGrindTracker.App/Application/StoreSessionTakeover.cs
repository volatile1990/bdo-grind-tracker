using System.ComponentModel;
using System.Diagnostics;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Services;

/// <summary>Launches a normal tracker against the source, while retaining the shared single-writer lock.</summary>
internal static class StoreSessionTakeover
{
    internal const string BusyReason = "Die Store-App läuft noch. Bitte dort im Tray-Menü „Beenden“ wählen und dann die Session übernehmen.";

    internal static TrackerCommandResult Start(string executable, string sourceDirectory, string mutexName,
        Action<ProcessStartInfo>? launch = null)
    {
        using var mutex = new Mutex(false, mutexName);
        bool available;
        try { available = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { available = true; }
        if (!available) return new(BusyReason);
        // The child acquires this lock itself before constructing any writable services.
        mutex.ReleaseMutex();

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!,
        };
        start.ArgumentList.Add(StoreSessionViewerLaunch.TakeoverArgument);
        start.Environment[AppDataPaths.DataDirectoryVariable] = Path.GetFullPath(sourceDirectory);
        try
        {
            if (launch is not null) launch(start);
            else
            {
                using var child = Process.Start(start) ?? throw new InvalidOperationException("Die lokale EXE konnte nicht gestartet werden.");
                // Keep the viewer available if the child hits its lock or startup error dialog.
                if (!child.WaitForInputIdle(15000) || child.HasExited || child.MainWindowTitle != AppBranding.WindowTitle)
                    return new("Die lokale Ausgabe konnte noch nicht geöffnet werden. Die Store-Ansicht bleibt verfügbar.");
            }
            return TrackerCommandResult.Success;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new("Die lokale EXE konnte nicht gestartet werden. Bitte die lokale Ausgabe erneut öffnen.");
        }
    }
}
