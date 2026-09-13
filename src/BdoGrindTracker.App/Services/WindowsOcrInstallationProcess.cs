using System.Diagnostics;
using System.Text;

namespace BdoGrindTracker.App.Services;

internal static class WindowsOcrInstallationProcess
{
    internal const string InstallationMutexName = @"Global\Grindcrest.WindowsOcrInstallation";

    internal static async Task<int> RunAsync(ProcessStartInfo dismStartInfo)
    {
        var startInfo = CreateStartInfo(dismStartInfo);
        // ShellExecute can wait for the administrator prompt. Keep that wait off the UI thread.
        using var process = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Windows hat keinen Installationsprozess gestartet.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateStartInfo(ProcessStartInfo dismStartInfo)
    {
        ArgumentNullException.ThrowIfNull(dismStartInfo);
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var dismPath = Path.Combine(systemDirectory, "dism.exe");
        var arguments = dismStartInfo.ArgumentList;
        // Keep the elevated command restricted even if a future caller bypasses the installer.
        if (!string.Equals(dismStartInfo.FileName, dismPath, StringComparison.OrdinalIgnoreCase)
            || dismStartInfo.Arguments.Length != 0
            || arguments.Count != 6
            || arguments[0] != "/Online"
            || arguments[1] != "/Add-Capability"
            || arguments[2] is not ("/CapabilityName:Language.OCR~~~en-US~0.0.1.0"
                or "/CapabilityName:Language.OCR~~~de-DE~0.0.1.0")
            || arguments[3] != "/NoRestart"
            || arguments[4] != "/English"
            || !arguments[5].StartsWith("/LogPath:", StringComparison.Ordinal))
        {
            throw new ArgumentException("Ungültiger Windows-OCR-Installationsbefehl.", nameof(dismStartInfo));
        }

        var logPath = arguments[5]["/LogPath:".Length..];
        if (!Path.IsPathFullyQualified(logPath) || logPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("Das Windows-Installationsprotokoll benötigt einen vollständigen Dateipfad.", nameof(dismStartInfo));

        var nativeArguments = string.Join(" ", arguments.Select(QuoteWindowsArgument));
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $progressPath = {{PowerShellLiteral(logPath + ".progress.txt")}}
            $writer = $null
            $process = $null
            $processStarted = $false
            $errorOutput = $null
            $exitCode = 1
            $installationMutex = $null
            $ownsInstallationMutex = $false
            $mutexName = {{PowerShellLiteral(InstallationMutexName)}}
            function Write-InstallationOutput([string]$text) {
                if ($null -ne $writer) {
                    try { $writer.WriteLine($text) } catch { }
                }
            }
            try {
                $writer = [System.IO.StreamWriter]::new($progressPath, $false, [System.Text.UTF8Encoding]::new($false))
                $writer.AutoFlush = $true
                # This helper owns the lock until DISM exits, including after Grindcrest closes.
                $installationMutex = [System.Threading.Mutex]::new($false, $mutexName)
                try { $ownsInstallationMutex = $installationMutex.WaitOne(0) }
                catch [System.Threading.AbandonedMutexException] { $ownsInstallationMutex = $true }
                if (-not $ownsInstallationMutex) {
                    Write-InstallationOutput 'Eine andere Grindcrest-OCR-Installation läuft noch. Bitte warte, bis Windows sie abgeschlossen hat.'
                    exit 1618
                }
                $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
                $startInfo.FileName = {{PowerShellLiteral(dismPath)}}
                $startInfo.Arguments = {{PowerShellLiteral(nativeArguments)}}
                $startInfo.WorkingDirectory = {{PowerShellLiteral(systemDirectory)}}
                $startInfo.UseShellExecute = $false
                $startInfo.CreateNoWindow = $true
                $startInfo.RedirectStandardOutput = $true
                $startInfo.RedirectStandardError = $true
                $process = [System.Diagnostics.Process]::new()
                $process.StartInfo = $startInfo
                $processStarted = $process.Start()
                if (-not $processStarted) { throw 'Windows hat DISM nicht gestartet.' }
                $errorOutput = $process.StandardError.ReadToEndAsync()
                $outputLine = [System.Text.StringBuilder]::new()
                while (($character = $process.StandardOutput.Read()) -ne -1) {
                    # ReadLine waits for another character after CR; DISM may pause there for a long time.
                    if ($character -eq 13 -or $character -eq 10) {
                        if ($outputLine.Length -gt 0) {
                            Write-InstallationOutput $outputLine.ToString()
                            $null = $outputLine.Clear()
                        }
                    }
                    else { $null = $outputLine.Append([char]$character) }
                }
                if ($outputLine.Length -gt 0) { Write-InstallationOutput $outputLine.ToString() }
            }
            catch {
                Write-InstallationOutput $_.Exception.ToString()
            }
            finally {
                if ($processStarted) {
                    # Always drain and wait, even after a progress-reader or writer failure.
                    $remainingOutput = $null
                    try { $remainingOutput = $process.StandardOutput.ReadToEndAsync() } catch { }
                    if ($null -eq $errorOutput) {
                        try { $errorOutput = $process.StandardError.ReadToEndAsync() } catch { }
                    }
                    $process.WaitForExit()
                    $exitCode = $process.ExitCode
                    if ($null -ne $remainingOutput) {
                        try { Write-InstallationOutput $remainingOutput.GetAwaiter().GetResult() } catch { }
                    }
                    if ($null -ne $errorOutput) {
                        try { Write-InstallationOutput $errorOutput.GetAwaiter().GetResult() } catch { }
                    }
                }
                if ($null -ne $writer) { try { $writer.Dispose() } catch { } }
                if ($null -ne $process) { try { $process.Dispose() } catch { } }
                if ($ownsInstallationMutex) { try { $installationMutex.ReleaseMutex() } catch { } }
                if ($null -ne $installationMutex) { try { $installationMutex.Dispose() } catch { } }
            }
            exit $exitCode
            """;

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = systemDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        return startInfo;
    }

    private static string PowerShellLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    private static string QuoteWindowsArgument(string value)
    {
        // Windows native argument parsing doubles backslashes before a quote or the closing quote.
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            result.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            result.Append(character);
            backslashes = 0;
        }
        result.Append('\\', backslashes * 2);
        return result.Append('"').ToString();
    }
}
