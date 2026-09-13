using System.Diagnostics;
using System.Text;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class WindowsOcrInstallationProcessTests
{
    [Fact]
    public void UsesTheSystemPowerShellWithOneEncodedCommandAndAnAdministratorPrompt()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "OCR O'Brien & $user", "installation.log");
        var startInfo = WindowsOcrInstallationProcess.CreateStartInfo(DismStartInfo(logPath));

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"), startInfo.FileName);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.System), startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.False(startInfo.RedirectStandardInput);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
        Assert.Empty(startInfo.Arguments);
        Assert.Equal(new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand" }, startInfo.ArgumentList.Take(3));
        Assert.Equal(4, startInfo.ArgumentList.Count);

        var script = DecodeScript(startInfo);
        Assert.Contains("$progressPath = '" + logPath.Replace("'", "''") + ".progress.txt'", script);
        Assert.Contains("\"/LogPath:" + logPath.Replace("'", "''") + "\"'", script);
        Assert.Contains("$mutexName = '" + WindowsOcrInstallationProcess.InstallationMutexName + "'", script);
        Assert.DoesNotContain("ExecutionPolicy", script, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("executable")]
    [InlineData("arguments")]
    [InlineData("extra-argument")]
    [InlineData("operation")]
    [InlineData("capability")]
    [InlineData("relative-log")]
    [InlineData("control-character")]
    public void RejectsCommandsOutsideTheFixedOcrInstallation(string modification)
    {
        var startInfo = DismStartInfo(Path.Combine(Path.GetTempPath(), "ocr.log"));
        switch (modification)
        {
            case "executable": startInfo.FileName = "dism.exe"; break;
            case "arguments": startInfo.Arguments = "/Online"; break;
            case "extra-argument": startInfo.ArgumentList.Add("/Source:C:\\other"); break;
            case "operation": startInfo.ArgumentList[1] = "/Remove-Capability"; break;
            case "capability": startInfo.ArgumentList[2] = "/CapabilityName:Language.OCR~~~fr-FR~0.0.1.0"; break;
            case "relative-log": startInfo.ArgumentList[5] = "/LogPath:ocr.log"; break;
            case "control-character": startInfo.ArgumentList[5] += "\r\nexit 0"; break;
        }

        Assert.Throws<ArgumentException>(() => WindowsOcrInstallationProcess.CreateStartInfo(startInfo));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3010, false)]
    [InlineData(unchecked((int)0x800F0954), false)]
    [InlineData(3010, true)]
    public async Task StreamsProgressAndAwaitsTheOriginalExitCodeEvenIfLoggingFails(int exitCode, bool failLogging)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ocr O'Brien & $test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, "install.log");
        var progressPath = logPath + ".progress.txt";
        var continuePath = Path.Combine(directory, "continue.txt");
        var startedPath = Path.Combine(directory, "started.txt");
        Process? process = null;
        try
        {
            // Exercise the production wrapper with a harmless console producer. Neither UAC nor DISM runs.
            var helperScript = $$"""
                [Console]::Out.Write('[==== 42.3% ]' + [char]13)
                [Console]::Out.Flush()
                [System.IO.File]::WriteAllText('{{startedPath.Replace("'", "''")}}', 'started')
                while (-not [System.IO.File]::Exists('{{continuePath.Replace("'", "''")}}')) { Start-Sleep -Milliseconds 50 }
                [Console]::Error.WriteLine(('x' * 100000) + 'stderr-complete')
                [Console]::Out.WriteLine('[========== 100.0% ]')
                [Console]::Out.Write('trailing-without-newline')
                exit {{exitCode}}
                """;
            var startInfo = SimulatedWrapperStartInfo(logPath, helperScript, failLogging: failLogging);
            process = Process.Start(startInfo);
            Assert.NotNull(process);
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string progress;
            do
            {
                await Task.Delay(25, timeout.Token);
                progress = ReadSharedFile(progressPath);
            } while ((!File.Exists(startedPath) || (!failLogging && !progress.Contains("42.3%", StringComparison.Ordinal)))
                && !process.HasExited);

            if (!failLogging) Assert.Contains("42.3%", progress);
            Assert.True(File.Exists(startedPath));
            if (failLogging) await Task.Delay(100, timeout.Token);
            Assert.False(process.HasExited);
            File.WriteAllText(continuePath, string.Empty);
            await process.WaitForExitAsync(timeout.Token);

            Assert.Equal(string.Empty, await errors);
            Assert.Equal(exitCode, process.ExitCode);
            progress = File.ReadAllText(progressPath);
            if (!failLogging)
            {
                Assert.Contains("100.0%", progress);
                Assert.Contains("stderr-complete", progress);
                Assert.Contains("trailing-without-newline", progress);
            }
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsASecondHelperUntilTheFirstProducerExits()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ocr-mutex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var firstStartedPath = Path.Combine(directory, "first-started.txt");
        var continuePath = Path.Combine(directory, "continue.txt");
        var secondStartedPath = Path.Combine(directory, "second-started.txt");
        var secondLogPath = Path.Combine(directory, "second.log");
        var mutexName = TestMutexName();
        var processes = new List<Process>();
        try
        {
            var firstProducer = $$"""
                [System.IO.File]::WriteAllText('{{firstStartedPath.Replace("'", "''")}}', 'started')
                while (-not [System.IO.File]::Exists('{{continuePath.Replace("'", "''")}}')) { Start-Sleep -Milliseconds 50 }
                exit 0
                """;
            var secondProducer = $$"""
                [System.IO.File]::WriteAllText('{{secondStartedPath.Replace("'", "''")}}', 'started')
                exit 0
                """;
            var first = Process.Start(SimulatedWrapperStartInfo(Path.Combine(directory, "first.log"), firstProducer, mutexName));
            Assert.NotNull(first);
            processes.Add(first);
            var firstErrors = first.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(firstStartedPath) && !first.HasExited)
                await Task.Delay(25, timeout.Token);
            Assert.True(File.Exists(firstStartedPath));
            Assert.False(first.HasExited);

            var second = Process.Start(SimulatedWrapperStartInfo(secondLogPath, secondProducer, mutexName));
            Assert.NotNull(second);
            processes.Add(second);
            var secondErrors = second.StandardError.ReadToEndAsync();
            await second.WaitForExitAsync(timeout.Token);

            Assert.Equal(string.Empty, await secondErrors);
            Assert.Equal(1618, second.ExitCode);
            Assert.False(File.Exists(secondStartedPath));
            Assert.Contains("Eine andere Grindcrest-OCR-Installation läuft noch", File.ReadAllText(secondLogPath + ".progress.txt"));
            Assert.False(first.HasExited);

            File.WriteAllText(continuePath, string.Empty);
            await first.WaitForExitAsync(timeout.Token);
            Assert.Equal(string.Empty, await firstErrors);
            Assert.Equal(0, first.ExitCode);

            var third = Process.Start(SimulatedWrapperStartInfo(Path.Combine(directory, "third.log"), secondProducer, mutexName));
            Assert.NotNull(third);
            processes.Add(third);
            var thirdErrors = third.StandardError.ReadToEndAsync();
            await third.WaitForExitAsync(timeout.Token);

            Assert.Equal(string.Empty, await thirdErrors);
            Assert.Equal(0, third.ExitCode);
            Assert.True(File.Exists(secondStartedPath));
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProcessStartInfo SimulatedWrapperStartInfo(string logPath, string producerScript,
        string? mutexName = null, bool failLogging = false)
    {
        var startInfo = WindowsOcrInstallationProcess.CreateStartInfo(DismStartInfo(logPath));
        var script = DecodeScript(startInfo);
        var helperArguments = "-NoProfile -NonInteractive -EncodedCommand "
            + Convert.ToBase64String(Encoding.Unicode.GetBytes(producerScript));
        var lines = script.Split('\n');
        var executableReplacements = 0;
        var argumentReplacements = 0;
        var mutexReplacements = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].TrimStart().StartsWith("$startInfo.FileName = ", StringComparison.Ordinal))
            {
                lines[index] = "$startInfo.FileName = '" + startInfo.FileName.Replace("'", "''") + "'";
                executableReplacements++;
            }
            if (lines[index].TrimStart().StartsWith("$startInfo.Arguments = ", StringComparison.Ordinal))
            {
                lines[index] = "$startInfo.Arguments = '" + helperArguments + "'";
                argumentReplacements++;
            }
            if (lines[index].TrimStart().StartsWith("$mutexName = ", StringComparison.Ordinal))
            {
                lines[index] = "$mutexName = '" + (mutexName ?? TestMutexName()).Replace("'", "''") + "'";
                mutexReplacements++;
            }
            if (failLogging && lines[index].Trim() == "$processStarted = $process.Start()")
                lines[index] += "\n$writer.Dispose()";
        }
        // Fail before launching anything if a wrapper change would leave real DISM or its mutex in place.
        Assert.Equal(1, executableReplacements);
        Assert.Equal(1, argumentReplacements);
        Assert.Equal(1, mutexReplacements);
        script = string.Join('\n', lines);
        Assert.DoesNotContain(WindowsOcrInstallationProcess.InstallationMutexName, script);
        startInfo.ArgumentList[3] = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        startInfo.UseShellExecute = false;
        startInfo.Verb = string.Empty;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardError = true;
        return startInfo;
    }

    private static string TestMutexName() => @"Local\Grindcrest.OcrTest." + Guid.NewGuid().ToString("N");

    private static ProcessStartInfo DismStartInfo(string logPath)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe"));
        foreach (var argument in new[]
        {
            "/Online", "/Add-Capability", "/CapabilityName:Language.OCR~~~en-US~0.0.1.0",
            "/NoRestart", "/English", "/LogPath:" + logPath
        }) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string DecodeScript(ProcessStartInfo startInfo) =>
        Encoding.Unicode.GetString(Convert.FromBase64String(startInfo.ArgumentList[3]));

    private static string ReadSharedFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
    }
}
