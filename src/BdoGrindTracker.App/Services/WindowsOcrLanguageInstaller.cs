using System.ComponentModel;
using System.Diagnostics;

namespace BdoGrindTracker.App.Services;

internal interface IWindowsOcrLanguageInstaller
{
    Task<WindowsOcrInstallResult> InstallAsync(string languageTag);
}

internal enum WindowsOcrInstallStatus
{
    Installed,
    RestartRequired,
    Cancelled,
    Failed
}

internal sealed record WindowsOcrInstallResult(WindowsOcrInstallStatus Status, string? Error = null);

internal sealed class WindowsOcrLanguageInstaller : IWindowsOcrLanguageInstaller
{
    private readonly Func<ProcessStartInfo, Task<int>> _run;

    public WindowsOcrLanguageInstaller()
        : this(RunAsync)
    {
    }

    internal WindowsOcrLanguageInstaller(Func<ProcessStartInfo, Task<int>> run)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public async Task<WindowsOcrInstallResult> InstallAsync(string languageTag)
    {
        // Only these exact capabilities may be passed to an elevated process.
        var capability = languageTag switch
        {
            string tag when string.Equals(tag, "en-US", StringComparison.OrdinalIgnoreCase)
                => "Language.OCR~~~en-US~0.0.1.0",
            string tag when string.Equals(tag, "de-DE", StringComparison.OrdinalIgnoreCase)
                => "Language.OCR~~~de-DE~0.0.1.0",
            _ => null
        };
        if (capability is null)
        {
            return new(WindowsOcrInstallStatus.Failed,
                "Die Installation unterstützt nur die Windows-OCR-Sprachen Deutsch (de-DE) und Englisch (en-US).");
        }

        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(systemDirectory, "dism.exe"),
            WorkingDirectory = systemDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("/Online");
        startInfo.ArgumentList.Add("/Add-Capability");
        startInfo.ArgumentList.Add($"/CapabilityName:{capability}");
        startInfo.ArgumentList.Add("/NoRestart");
        startInfo.ArgumentList.Add("/Quiet");

        try
        {
            var exitCode = await _run(startInfo).ConfigureAwait(false);
            return exitCode switch
            {
                0 => new(WindowsOcrInstallStatus.Installed),
                3010 => new(WindowsOcrInstallStatus.RestartRequired),
                _ => new(WindowsOcrInstallStatus.Failed, InstallationError(exitCode))
            };
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(WindowsOcrInstallStatus.Cancelled);
        }
        catch (Win32Exception exception)
        {
            return new(WindowsOcrInstallStatus.Failed,
                $"Die Windows-Installation konnte nicht gestartet werden (Fehler {ErrorCode(exception.NativeErrorCode)}). {exception.Message}");
        }
        catch (Exception exception)
        {
            return new(WindowsOcrInstallStatus.Failed,
                $"Die Windows-Installation konnte nicht abgeschlossen werden (Fehler {ErrorCode(exception.HResult)}). {exception.Message}");
        }
    }

    private static string InstallationError(int exitCode)
    {
        var detail = unchecked((uint)exitCode) switch
        {
            0x80070005 or 5 or 740 => "Windows hat den Zugriff verweigert. Bestätige die Installation mit einem Administratorkonto.",
            0x800F081F => "Windows konnte die erforderlichen Installationsdateien nicht finden. Prüfe den Zugriff auf Windows Update.",
            _ => "Prüfe die Internetverbindung und den Zugriff auf Windows Update. Auf verwalteten PCs können Update-Richtlinien die Installation verhindern."
        };
        return $"Das Windows-OCR-Sprachpaket konnte nicht installiert werden (Fehler {ErrorCode(exitCode)}). {detail}";
    }

    private static string ErrorCode(int code) => $"0x{unchecked((uint)code):X8}";

    private static async Task<int> RunAsync(ProcessStartInfo startInfo)
    {
        // ShellExecute may wait for UAC; keep that wait off the UI thread too.
        using var process = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Windows hat keinen Installationsprozess gestartet.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }
}
