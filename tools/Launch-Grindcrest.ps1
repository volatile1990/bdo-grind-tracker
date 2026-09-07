$ErrorActionPreference = 'Stop'

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $workspaceRoot 'src\BdoGrindTracker.App\BdoGrindTracker.App.csproj'
$buildDirectory = Join-Path $workspaceRoot 'artifacts\desktop-latest'
$executablePath = Join-Path $buildDirectory 'BdoGrindTracker.exe'

function Show-LauncherError([string] $message) {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show(
        $message,
        'Grindcrest konnte nicht gestartet werden',
        [Windows.Forms.MessageBoxButtons]::OK,
        [Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}

try {
    $runningBuild = Get-Process -Name 'BdoGrindTracker' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $executablePath } |
        Select-Object -First 1
    if ($null -ne $runningBuild) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class GrindcrestWindow {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr handle, int command);
}
"@
        [GrindcrestWindow]::ShowWindowAsync($runningBuild.MainWindowHandle, 9) | Out-Null
        [GrindcrestWindow]::SetForegroundWindow($runningBuild.MainWindowHandle) | Out-Null
        exit 0
    }

    [IO.Directory]::CreateDirectory($buildDirectory) | Out-Null
    Push-Location $workspaceRoot
    try {
        & dotnet publish $projectPath --configuration Release --runtime win-x64 `
            --self-contained true --output $buildDirectory --nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw 'Der aktuelle eigenständige Windows-Build konnte nicht erstellt werden.'
        }
    }
    finally {
        Pop-Location
    }

    Start-Process -FilePath $executablePath -WorkingDirectory $buildDirectory
}
catch {
    Show-LauncherError ("{0}`n`nSchließe eine bereits laufende Grindcrest-Version und versuche es erneut." -f $_.Exception.Message)
    exit 1
}
