using System.ComponentModel;
using System.Diagnostics;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class WindowsOcrLanguageInstallerTests
{
    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("EN-us", "en-US")]
    [InlineData("de-DE", "de-DE")]
    [InlineData("DE-de", "de-DE")]
    public async Task RunsOnlyTheSelectedCapabilityWithUacWithoutAutomaticRestart(string requested, string canonical)
    {
        ProcessStartInfo? invocation = null;
        var installer = new WindowsOcrLanguageInstaller(startInfo =>
        {
            invocation = startInfo;
            return Task.FromResult(0);
        });

        var result = await installer.InstallAsync(requested);

        Assert.Equal(WindowsOcrInstallStatus.Installed, result.Status);
        Assert.Null(result.Error);
        Assert.NotNull(invocation);
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        Assert.Equal(Path.Combine(systemDirectory, "dism.exe"), invocation.FileName);
        Assert.True(Path.IsPathFullyQualified(invocation.FileName));
        Assert.Equal(systemDirectory, invocation.WorkingDirectory);
        Assert.True(invocation.UseShellExecute);
        Assert.Equal("runas", invocation.Verb);
        Assert.Equal(ProcessWindowStyle.Hidden, invocation.WindowStyle);
        Assert.Equal(new[]
        {
            "/Online", "/Add-Capability", $"/CapabilityName:Language.OCR~~~{canonical}~0.0.1.0",
            "/NoRestart", "/Quiet"
        }, invocation.ArgumentList);
        Assert.Empty(invocation.Arguments);
        Assert.False(invocation.RedirectStandardOutput);
        Assert.False(invocation.RedirectStandardError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en")]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData(" en-US")]
    [InlineData("en-US ")]
    [InlineData("en-US\n")]
    [InlineData("en-US /Remove-Capability")]
    [InlineData("en-US&calc.exe")]
    [InlineData("en-US\" /Source:C:\\fake")]
    [InlineData("..\\en-US")]
    public async Task RejectsUnsupportedOrModifiedTagsBeforeLaunchingAnything(string? languageTag)
    {
        var calls = 0;
        var installer = new WindowsOcrLanguageInstaller(_ =>
        {
            calls++;
            return Task.FromResult(0);
        });

        var result = await installer.InstallAsync(languageTag!);

        Assert.Equal(WindowsOcrInstallStatus.Failed, result.Status);
        Assert.NotEmpty(result.Error!);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AwaitsTheProcessBeforeReportingSuccess()
    {
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new WindowsOcrLanguageInstaller(_ => processExit.Task);

        var pending = installer.InstallAsync("en-US");
        Assert.False(pending.IsCompleted);

        processExit.SetResult(0);
        Assert.Equal(WindowsOcrInstallStatus.Installed, (await pending).Status);
    }

    [Fact]
    public async Task ReportsRestartRequirementSeparatelyFromSuccess()
    {
        var installer = new WindowsOcrLanguageInstaller(_ => Task.FromResult(3010));

        var result = await installer.InstallAsync("de-DE");

        Assert.Equal(WindowsOcrInstallStatus.RestartRequired, result.Status);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DeclinedUacIsCancellationAndAllowsAnotherAttempt()
    {
        var attempts = 0;
        var installer = new WindowsOcrLanguageInstaller(_ => ++attempts == 1
            ? Task.FromException<int>(new Win32Exception(1223))
            : Task.FromResult(0));

        var cancelled = await installer.InstallAsync("en-US");
        var retry = await installer.InstallAsync("en-US");

        Assert.Equal(WindowsOcrInstallStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Error);
        Assert.Equal(WindowsOcrInstallStatus.Installed, retry.Status);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(2, "0x00000002")]
    [InlineData(5, "0x00000005")]
    [InlineData(1260, "0x000004EC")]
    public async Task LaunchFailuresIncludeTheNativeErrorCode(int error, string code)
    {
        var installer = new WindowsOcrLanguageInstaller(_ => throw new Win32Exception(error, "Testfehler"));

        var result = await installer.InstallAsync("de-DE");

        Assert.Equal(WindowsOcrInstallStatus.Failed, result.Status);
        Assert.Contains(code, result.Error);
        Assert.Contains("Testfehler", result.Error);
    }

    [Theory]
    [InlineData(87, "0x00000057", "Windows Update")]
    [InlineData(unchecked((int)0x800F0954), "0x800F0954", "Update-Richtlinien")]
    [InlineData(unchecked((int)0x800F081F), "0x800F081F", "Installationsdateien")]
    [InlineData(unchecked((int)0x800F0906), "0x800F0906", "Internetverbindung")]
    [InlineData(unchecked((int)0x80070005), "0x80070005", "Administratorkonto")]
    public async Task NonzeroExitIsAnActionableFailure(int exitCode, string code, string detail)
    {
        var installer = new WindowsOcrLanguageInstaller(_ => Task.FromResult(exitCode));

        var result = await installer.InstallAsync("en-US");

        Assert.Equal(WindowsOcrInstallStatus.Failed, result.Status);
        Assert.Contains(code, result.Error);
        Assert.Contains(detail, result.Error);
    }

    [Fact]
    public async Task UnexpectedProcessFailureDoesNotEscapeToTheUi()
    {
        var installer = new WindowsOcrLanguageInstaller(_ => Task.FromException<int>(
            new InvalidOperationException("Windows hat keinen Installationsprozess gestartet.")));

        var result = await installer.InstallAsync("en-US");

        Assert.Equal(WindowsOcrInstallStatus.Failed, result.Status);
        Assert.Contains("0x80131509", result.Error);
        Assert.Contains("keinen Installationsprozess", result.Error);
    }
}
