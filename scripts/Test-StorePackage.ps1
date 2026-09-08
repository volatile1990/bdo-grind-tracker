#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$null = & (Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1') -Version $Version -Channel stable
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml] $expected = Get-Content -LiteralPath (Join-Path $workspaceRoot 'packaging/msix/AppxManifest.xml') -Raw
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('AppxManifest.xml', 'AppxBlockMap.xml', '[Content_Types].xml',
            'Grindcrest.exe', 'BdoGrindTracker.dll', 'BdoGrindTracker.runtimeconfig.json',
            'System.Private.CoreLib.dll', 'wwwroot/index.html', 'data/items.en.txt', 'THIRD_PARTY_NOTICES.md',
            'Assets/StoreLogo.png', 'Assets/Square44x44Logo.png', 'Assets/Square150x150Logo.png')) {
        if ($entries -notcontains $required) { throw "Missing Store package content: $required" }
    }
    foreach ($license in Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'licenses') -File) {
        if ($entries -notcontains "licenses/$($license.Name)") { throw "Missing third-party license: $($license.Name)" }
    }
    foreach ($pattern in @('*OpenCvSharpExtern.dll', '*WebView2Loader.dll', 'wwwroot/assets/icons/*.png')) {
        if (-not ($entries | Where-Object { $_ -like $pattern })) { throw "Missing runtime or UI content: $pattern" }
    }
    if ($entries | Where-Object { $_ -match '(?i)(^|/)(Update\.exe|.*-Setup\.exe|releases\..*\.json)$|\.(pfx|key)$' }) {
        throw 'Store packages must not contain a GitHub updater/installer/feed or private signing keys.'
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    foreach ($attribute in @('Name', 'Publisher', 'ProcessorArchitecture')) {
        if ($manifest.Package.Identity.GetAttribute($attribute) -cne $expected.Package.Identity.GetAttribute($attribute)) {
            throw "Store identity mismatch: $attribute"
        }
    }
    if ($manifest.Package.Identity.Version -cne "$Version.0") { throw 'Store package version does not match the requested version.' }
    if ($manifest.Package.Properties.PublisherDisplayName -cne $expected.Package.Properties.PublisherDisplayName) {
        throw 'Store PublisherDisplayName mismatch.'
    }
    $application = $manifest.Package.Applications.Application
    if ($application.Executable -cne 'Grindcrest.exe' -or
        $application.GetAttribute('TrustLevel', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -ne 'mediumIL' -or
        $application.GetAttribute('RuntimeBehavior', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -ne 'packagedClassicApp') {
        throw 'Expected the packaged full-trust desktop launcher.'
    }
    $capabilities = @($manifest.Package.Capabilities.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
    if ($capabilities.Count -ne 1 -or $capabilities[0].Name -cne 'runFullTrust') {
        throw 'Expected only the runFullTrust restricted capability.'
    }
    Add-Type -AssemblyName System.Drawing
    foreach ($logo in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
        $stream = $archive.GetEntry("Assets/$($logo[0])").Open()
        try {
            $bitmap = [Drawing.Bitmap]::new($stream)
            try {
                if ($bitmap.Width -ne $logo[1] -or $bitmap.Height -ne $logo[1]) { throw "Wrong logo dimensions: $($logo[0])" }
            } finally { $bitmap.Dispose() }
        } finally { $stream.Dispose() }
    }
} finally {
    $archive.Dispose()
}
Write-Host "Verified Store identity, version $Version.0, runtime dependencies, logos and payload."
