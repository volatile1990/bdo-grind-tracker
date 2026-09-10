#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutputDirectory,
    [string] $WindowsSdkBin,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Windows is required to build an MSIX package.' }
$metadata = & (Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1') -Version $Version -Channel stable
if ($metadata.IsPrerelease -or [int]$Version.Split('.')[0] -eq 0) {
    throw 'Store packages require a stable version with a nonzero major component.'
}
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspaceRoot "artifacts/store/$Version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count) {
    throw 'The output directory must be empty. Existing Store packages are never overwritten.'
}
if (-not $WindowsSdkBin) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $WindowsSdkBin = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.19041.0' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64' } |
        Where-Object { (Test-Path -LiteralPath (Join-Path $_ 'makeappx.exe')) -and
            (Test-Path -LiteralPath (Join-Path $_ 'makepri.exe')) } | Select-Object -First 1
}
if (-not $WindowsSdkBin) { throw 'Install Windows SDK 10.0.19041.0 or newer, including MakeAppx and MakePri.' }
$makeAppx = Join-Path $WindowsSdkBin 'makeappx.exe'
$makePri = Join-Path $WindowsSdkBin 'makepri.exe'
if (-not (Test-Path -LiteralPath $makeAppx -PathType Leaf)) { throw 'MakeAppx.exe was not found.' }
if (-not (Test-Path -LiteralPath $makePri -PathType Leaf)) { throw 'MakePri.exe was not found.' }

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

$workDirectory = Join-Path $workspaceRoot "artifacts/store-work/$Version-$([guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $workDirectory 'publish'
$packageVersion = "$Version.0"
$packageName = "Grindcrest-$packageVersion-x64.msix"
[IO.Directory]::CreateDirectory($workDirectory) | Out-Null
Push-Location $workspaceRoot
try {
    $versionProperties = @("-p:Version=$Version", "-p:InformationalVersion=$Version",
        '-p:IncludeSourceRevisionInInformationalVersion=false')
    if (-not $SkipTests) {
        Invoke-DotNet (@('test', 'BdoGrindTracker.slnx', '-c', 'Release', '--nologo',
            '--logger', 'trx', '--results-directory', (Join-Path $workDirectory 'test-results')) + $versionProperties)
    }
    Invoke-DotNet (@('publish', 'src/BdoGrindTracker.App/BdoGrindTracker.App.csproj', '-c', 'Release',
        '-r', 'win-x64', '--self-contained', 'true', '-o', $publishDirectory, '--nologo',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false') + $versionProperties)
    Invoke-DotNet @('run', '--project', 'tools/BrandAssets', '-c', 'Release', '--', '--msix',
        'data/branding/grindcrest-logo.png', (Join-Path $publishDirectory 'Assets'))
    [xml] $manifest = Get-Content -LiteralPath (Join-Path $workspaceRoot 'packaging/msix/AppxManifest.xml') -Raw
    $manifest.Package.Identity.SetAttribute('Version', $packageVersion)
    $manifest.Save((Join-Path $publishDirectory 'AppxManifest.xml'))

    # The shell resolves target-size/unplated logos through the package resource index.
    # Copying transparent PNGs alone still permits an accent-colored icon backplate.
    $resourceDirectory = Join-Path $workDirectory 'shell-resources'
    [IO.Directory]::CreateDirectory($resourceDirectory) | Out-Null
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'Assets') -Destination $resourceDirectory -Recurse
    $priLog = Join-Path $workDirectory 'makepri.log'
    & $makePri new /pr $resourceDirectory /cf (Join-Path $workspaceRoot 'packaging/msix/priconfig.xml') `
        /mn (Join-Path $publishDirectory 'AppxManifest.xml') `
        /of (Join-Path $publishDirectory 'resources.pri') /o *> $priLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $priLog -Tail 25 | Write-Host
        throw "MakePri failed with exit code $LASTEXITCODE. Full log: $priLog"
    }

    # Partner Center accepts unsigned MSIX submissions and signs them after certification.
    # Do not run Velopack, inject an installer, or embed a private signing key in this package.
    $packLog = Join-Path $workDirectory 'makeappx.log'
    & $makeAppx pack /d $publishDirectory /p (Join-Path $workDirectory $packageName) /h SHA256 /o *> $packLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $packLog -Tail 25 | Write-Host
        throw "MakeAppx failed with exit code $LASTEXITCODE. Full log: $packLog"
    }
    & (Join-Path $PSScriptRoot 'Test-StorePackage.ps1') -PackagePath (Join-Path $workDirectory $packageName) -Version $Version -WindowsSdkBin $WindowsSdkBin
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    Copy-Item -LiteralPath (Join-Path $workDirectory $packageName) -Destination $OutputDirectory
    Copy-Item -LiteralPath $packLog -Destination $OutputDirectory
    Copy-Item -LiteralPath $priLog -Destination $OutputDirectory
    $hash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $packageName) -Algorithm SHA256).Hash
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'), "$hash  $packageName`n")
    Write-Host "Store submission package ready: $(Join-Path $OutputDirectory $packageName)"
    Write-Host "Unpacked build for inspection: $publishDirectory"
    Write-Host 'Local build only; Microsoft certification and Store publication are separate steps.'
} finally {
    Pop-Location
}
