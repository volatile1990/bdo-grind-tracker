#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [string] $Version,
    [ValidateSet('', 'stable', 'beta')] [string] $Channel = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$metadata = & (Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1') -Version $Version -Channel $Channel
$Directory = (Resolve-Path -LiteralPath $Directory).Path
$feed = Get-Content -LiteralPath (Join-Path $Directory "releases.$($metadata.VelopackChannel).json") -Raw | ConvertFrom-Json
$fullPackages = @($feed.Assets | Where-Object { $_.Type -eq 'Full' -and $_.Version -eq $Version })
if ($fullPackages.Count -ne 1) { throw 'Expected exactly one full update package for the requested version.' }
$installers = @(Get-ChildItem -LiteralPath $Directory -Filter '*-Setup.exe' -File)
if ($installers.Count -ne 1) { throw 'Expected exactly one Windows installer.' }
foreach ($asset in $feed.Assets) {
    if ([IO.Path]::GetFileName($asset.FileName) -ne $asset.FileName -or $asset.FileName -match '[/\\]') {
        throw 'The update feed contains an invalid file name.'
    }
    $packagePath = Join-Path $Directory $asset.FileName
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Missing update package: $($asset.FileName)" }
    if ((Get-Item -LiteralPath $packagePath).Length -ne $asset.Size) { throw "Package size mismatch: $($asset.FileName)" }
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne $asset.SHA256) {
        throw "Package checksum mismatch: $($asset.FileName)"
    }
}

$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory $fullPackages[0].FileName))
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('Grindcrest.exe', 'BdoGrindTracker.dll', 'Velopack.dll', 'System.Private.CoreLib.dll',
            'wwwroot/index.html', 'data/items.en.txt', 'data/branding/grindcrest.ico', 'THIRD_PARTY_NOTICES.md',
            'licenses/Microsoft.Web.WebView2.txt', 'licenses/Blazor.WebView.WindowsForms.txt', 'licenses/Velopack.txt',
            'Microsoft.ML.OnnxRuntime.dll', 'onnxruntime.dll', 'onnxruntime_providers_shared.dll',
            'data/ocr/paddle-v6-small/inference.onnx', 'data/ocr/paddle-v6-small/characters.json',
            'licenses/PaddleOCR-Apache-2.0.txt', 'licenses/ONNXRuntime-MIT.txt', 'licenses/ONNXRuntime-ThirdPartyNotices.txt')) {
        if ($entries -notcontains "lib/app/$required") { throw "Required file missing from update package: $required" }
    }
    if (-not ($entries | Where-Object { $_ -like 'lib/app/wwwroot/assets/icons/*.png' })) {
        throw 'The packaged Blazor UI is missing the item icons.'
    }
    $nuspecEntry = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
    if ($nuspecEntry.Count -ne 1) { throw 'Expected one package manifest.' }
    $reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
    try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($nuspec.package.metadata.id -ne 'Grindcrest' -or $nuspec.package.metadata.version -ne $Version) {
        throw 'The package ID or version does not match the release.'
    }
} finally {
    $archive.Dispose()
}
Write-Host "Verified installer, feed, package checksums and application assets for $Version."
