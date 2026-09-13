using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Services;

internal interface IWindowsOcrLanguageInstaller
{
    Task<WindowsOcrInstallResult> InstallAsync(string languageTag, IProgress<WindowsOcrInstallProgress>? progress = null);
}

internal enum WindowsOcrInstallStatus
{
    Installed,
    RestartRequired,
    Cancelled,
    Failed
}

internal sealed record WindowsOcrInstallResult(WindowsOcrInstallStatus Status, string? Error = null, string? LogPath = null);

internal sealed record WindowsOcrInstallProgress(TimeSpan Elapsed, string LogPath, int? Percent = null)
{
    public string Message => Percent is { } percent
        ? $"Windows-Installation: {percent} % · Wartezeit {Elapsed:mm\\:ss}. " +
          (percent == 100 ? "Windows schließt den Vorgang ab; anschließend wird die Texterkennung geprüft. " : "") +
          $"Protokoll: {LogPath}"
        : Elapsed < TimeSpan.FromMinutes(2)
        ? $"Warte auf Windows · {(int)Elapsed.TotalSeconds} Sekunden. Windows meldet noch keinen Prozentwert. " +
          $"Bitte bestätige eine noch offene Administratorabfrage. Protokoll: {LogPath}"
        : $"Windows hat die Installation nach {(int)Elapsed.TotalMinutes} Minuten noch nicht abgeschlossen. " +
          "Der Download oder die Windows-Update-Verarbeitung kann länger dauern. " +
          $"Lass den Vorgang weiterlaufen; starte keine zweite Installation und den PC nicht neu. Protokoll: {LogPath}";
}

internal sealed class WindowsOcrLanguageInstaller : IWindowsOcrLanguageInstaller
{
    private readonly Func<ProcessStartInfo, Task<int>> _run;
    private readonly Func<string> _createLogPath;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, int?> _readPercentage;

    internal static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "DISM", "dism.log");

    public WindowsOcrLanguageInstaller()
        : this(WindowsOcrInstallationProcess.RunAsync, CreateLogPath)
    {
    }

    internal WindowsOcrLanguageInstaller(Func<ProcessStartInfo, Task<int>> run,
        Func<string>? createLogPath = null, TimeProvider? timeProvider = null, Func<string, int?>? readPercentage = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _createLogPath = createLogPath ?? (() => DefaultLogPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _readPercentage = readPercentage ?? ReadProgressPercentage;
    }

    public async Task<WindowsOcrInstallResult> InstallAsync(string languageTag, IProgress<WindowsOcrInstallProgress>? progress = null)
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
        startInfo.ArgumentList.Add("/English");

        string? logPath = null;
        try
        {
            logPath = _createLogPath();
            startInfo.ArgumentList.Add($"/LogPath:{logPath}");
            var started = _timeProvider.GetTimestamp();
            Report(progress, new(TimeSpan.Zero, logPath));
            var installation = _run(startInfo);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _timeProvider);
            var lastReport = TimeSpan.Zero;
            int? lastPercent = null;
            // A slow servicing operation is still running, not a failed attempt.
            // Keep awaiting its exit so a retry cannot start another elevated DISM.
            while (!installation.IsCompleted)
            {
                var tick = timer.WaitForNextTickAsync().AsTask();
                if (await Task.WhenAny(installation, tick).ConfigureAwait(false) == installation) break;
                await tick.ConfigureAwait(false);
                if (!installation.IsCompleted)
                {
                    var elapsed = _timeProvider.GetElapsedTime(started);
                    var percent = _readPercentage(logPath + ".progress.txt") ?? lastPercent;
                    if (percent != lastPercent || elapsed - lastReport >= TimeSpan.FromSeconds(30))
                    {
                        Report(progress, new(elapsed, logPath, percent));
                        lastReport = elapsed;
                        lastPercent = percent;
                    }
                }
            }
            var exitCode = await installation.ConfigureAwait(false);
            return exitCode switch
            {
                0 => new(WindowsOcrInstallStatus.Installed, LogPath: logPath),
                3010 => new(WindowsOcrInstallStatus.RestartRequired, LogPath: logPath),
                _ => new(WindowsOcrInstallStatus.Failed, WithLog(InstallationError(exitCode), logPath), logPath)
            };
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(WindowsOcrInstallStatus.Cancelled, LogPath: logPath);
        }
        catch (Win32Exception exception)
        {
            return new(WindowsOcrInstallStatus.Failed,
                WithLog($"Die Windows-Installation konnte nicht gestartet werden (Fehler {ErrorCode(exception.NativeErrorCode)}). {exception.Message}", logPath), logPath);
        }
        catch (Exception exception)
        {
            return new(WindowsOcrInstallStatus.Failed,
                WithLog($"Die Windows-Installation konnte nicht abgeschlossen werden (Fehler {ErrorCode(exception.HResult)}). {exception.Message}", logPath), logPath);
        }
    }

    private static string InstallationError(int exitCode)
    {
        var detail = unchecked((uint)exitCode) switch
        {
            1618 => "Ein anderer Grindcrest-OCR-Installationsprozess läuft noch. Warte auf seinen Abschluss oder prüfe die Texterkennung erneut.",
            0x80070005 or 5 or 740 => "Windows hat den Zugriff verweigert. Bestätige die Installation mit einem Administratorkonto.",
            0x800F081F => "Windows konnte die erforderlichen Installationsdateien nicht finden. Prüfe den Zugriff auf Windows Update.",
            _ => "Prüfe die Internetverbindung und den Zugriff auf Windows Update. Auf verwalteten PCs können Update-Richtlinien die Installation verhindern."
        };
        return $"Das Windows-OCR-Sprachpaket konnte nicht installiert werden (Fehler {ErrorCode(exitCode)}). {detail}";
    }

    private static string ErrorCode(int code) => $"0x{unchecked((uint)code):X8}";

    private static string WithLog(string message, string? logPath) => logPath is null
        ? message : $"{message} Windows-Installationsprotokoll: {logPath}";

    private static string CreateLogPath()
    {
        var directory = Path.Combine(AppDataPaths.Current.BaseDirectory, "logs", "ocr-installation");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"ocr-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
    }

    private static void Report(IProgress<WindowsOcrInstallProgress>? progress, WindowsOcrInstallProgress update)
    {
        // Display failures must never free the installation lock while DISM still runs.
        try { progress?.Report(update); }
        catch (Exception) { }
    }

    internal static int? ReadProgressPercentage(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 8192) stream.Seek(-8192, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return ParseProgressPercentage(reader.ReadToEnd());
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    internal static int? ParseProgressPercentage(string output)
    {
        int? percent = null;
        // /English produces invariant decimal percentages inside DISM's console bar.
        // Ignore error text containing percentages and incomplete writes.
        foreach (Match match in Regex.Matches(output,
            @"(?:^|[\r\n])[ \t]*\[[= \t]*(\d{1,3}(?:\.\d+)?)%[= \t]*\][ \t]*(?=[\r\n]|$)", RegexOptions.CultureInvariant))
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var value) && value is >= 0 and <= 100)
                percent = (int)value;
        }
        return percent;
    }
}
