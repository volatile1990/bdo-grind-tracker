#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [ValidateSet('', 'stable', 'beta')] [string] $Channel = '',
    [string] $OutputDirectory,
    [string] $PreviousReleaseDirectory,
    [string] $ReleaseNotes,
    [string] $UpdateRepositoryUrl = 'https://github.com/volatile1990/bdo-grind-tracker',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Windows is required to build the Grindcrest installer.' }
$metadata = & (Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1') -Version $Version -Channel $Channel
if ($UpdateRepositoryUrl -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') {
    throw 'UpdateRepositoryUrl must be the HTTPS URL of a public GitHub repository.'
}
$UpdateRepositoryUrl = $UpdateRepositoryUrl.TrimEnd('/')
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $workspaceRoot 'src/BdoGrindTracker.App/BdoGrindTracker.App.csproj'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspaceRoot "artifacts/releases/$Version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count) {
    throw 'The output directory is not empty. Use a new version or a new empty output directory; release files are never overwritten.'
}
if ($PreviousReleaseDirectory) {
    $PreviousReleaseDirectory = (Resolve-Path -LiteralPath $PreviousReleaseDirectory).Path
    if (-not (Test-Path -LiteralPath (Join-Path $PreviousReleaseDirectory "releases.$($metadata.VelopackChannel).json"))) {
        throw 'The previous release directory must contain the feed for the same update channel.'
    }
}
if (-not $ReleaseNotes) {
    $versionNotes = Join-Path $workspaceRoot "docs/release-notes/$Version.md"
    if (Test-Path -LiteralPath $versionNotes -PathType Leaf) { $ReleaseNotes = $versionNotes }
}
if ($ReleaseNotes) { $ReleaseNotes = (Resolve-Path -LiteralPath $ReleaseNotes).Path }

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

# No credential is passed as a command-line argument. Signing uses a certificate
# already installed in CurrentUser\My, or authenticated Azure signing metadata.
$signingArguments = @()
if ($env:GRINDCREST_SIGN_CERT_SHA1 -and $env:GRINDCREST_AZURE_SIGN_METADATA) {
    throw 'Choose either certificate-store signing or Azure signing metadata.'
}
if ($env:GRINDCREST_SIGN_CERT_SHA1) {
    if ($env:GRINDCREST_SIGN_CERT_SHA1 -notmatch '^[0-9A-Fa-f]{40}$') { throw 'The signing certificate thumbprint must have 40 hex characters.' }
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$env:GRINDCREST_SIGN_CERT_SHA1"
    if (-not $certificate.HasPrivateKey) { throw 'The signing certificate has no accessible private key.' }
    $timestampUrl = if ($env:GRINDCREST_SIGN_TIMESTAMP_URL) { $env:GRINDCREST_SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
    if ($timestampUrl -notmatch '^https?://[A-Za-z0-9./:_-]+$') { throw 'Invalid signing timestamp URL.' }
    $signingArguments = @('--signParams', "/sha1 $env:GRINDCREST_SIGN_CERT_SHA1 /fd SHA256 /tr $timestampUrl /td SHA256")
} elseif ($env:GRINDCREST_AZURE_SIGN_METADATA) {
    $signingArguments = @('--azureTrustedSignFile', (Resolve-Path -LiteralPath $env:GRINDCREST_AZURE_SIGN_METADATA).Path)
} else {
    Write-Warning 'No signing identity configured: this build produces an unsigned installer.'
}

$workDirectory = Join-Path $workspaceRoot "artifacts/release-work/$Version-$([guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $workDirectory 'publish'
$packageDirectory = Join-Path $workDirectory 'packages'
[IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
Push-Location $workspaceRoot
try {
    Invoke-DotNet -Arguments @('tool', 'restore')
    $versionProperties = @("-p:Version=$Version", "-p:InformationalVersion=$Version",
        '-p:IncludeSourceRevisionInInformationalVersion=false', "-p:UpdateRepositoryUrl=$UpdateRepositoryUrl")
    if (-not $SkipTests) {
        Invoke-DotNet (@('test', 'BdoGrindTracker.slnx', '--configuration', 'Release', '--nologo',
            '--logger', 'trx', '--results-directory', (Join-Path $workspaceRoot 'artifacts/test-results')) + $versionProperties)
    }
    Invoke-DotNet (@('publish', $projectPath, '--configuration', 'Release', '--runtime', 'win-x64',
        '--self-contained', 'true', '--output', $publishDirectory, '--nologo',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false') + $versionProperties)

    foreach ($required in @('Grindcrest.exe', 'BdoGrindTracker.dll', 'Velopack.dll', 'System.Private.CoreLib.dll',
            'wwwroot/index.html', 'data/items.en.txt', 'data/branding/grindcrest.ico', 'THIRD_PARTY_NOTICES.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $required) -PathType Leaf)) {
            throw "Required application content is missing: $required"
        }
    }
    foreach ($license in Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'licenses') -File) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory "licenses/$($license.Name)"))) {
            throw "Required third-party license is missing: $($license.Name)"
        }
    }
    # The complete previous channel feed and its packages let vpk compute deltas.
    # A first release requires none of these files and always gets a full package.
    if ($PreviousReleaseDirectory) {
        Get-ChildItem -LiteralPath $PreviousReleaseDirectory -File |
            Where-Object { $_.Extension -eq '.nupkg' -or $_.Name -eq "releases.$($metadata.VelopackChannel).json" } |
            Copy-Item -Destination $packageDirectory
    }
    $packArguments = @('tool', 'run', 'vpk', '--', 'pack', '--packId', 'Grindcrest',
        '--packVersion', $Version, '--packTitle', 'Grindcrest', '--packAuthors', 'Grindcrest',
        '--packDir', $publishDirectory, '--mainExe', 'Grindcrest.exe', '--runtime', 'win-x64',
        '--channel', $metadata.VelopackChannel, '--outputDir', $packageDirectory,
        '--icon', (Join-Path $workspaceRoot 'data/branding/grindcrest.ico'),
        '--framework', 'webview2,vcredist143-x64', '--shortcuts', 'Desktop,StartMenuRoot')
    if (-not $PreviousReleaseDirectory) { $packArguments += @('--delta', 'None') }
    if ($ReleaseNotes) { $packArguments += @('--releaseNotes', $ReleaseNotes) }
    Invoke-DotNet ($packArguments + $signingArguments)

    & (Join-Path $PSScriptRoot 'Test-Release.ps1') -Directory $packageDirectory -Version $Version -Channel $metadata.Channel
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    Get-ChildItem -LiteralPath $packageDirectory -File | Copy-Item -Destination $OutputDirectory
    Write-Host "Release $Version ($($metadata.VelopackChannel)) is ready: $OutputDirectory"
    Write-Host 'The build is local only. Publishing happens in the Release workflow.'
} finally {
    Pop-Location
}
