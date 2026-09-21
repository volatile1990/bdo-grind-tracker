#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$versionReader = Join-Path $PSScriptRoot '../Get-StorePackageVersion.ps1'
$validator = Join-Path $PSScriptRoot '../Test-StorePackage.ps1'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

foreach ($version in @('1.0.0', '1.7.3', '12.345.678', '65534.65534.65534')) {
    $actual = & $versionReader -Version $version
    if ($actual -cne "$version.0") { throw "Incorrect Store package version for $version`: $actual" }
}
$invalidVersions = @('0.10.0', '1.7.3-beta.1', '1.7.3+build', 'v1.7.3', '1.7', '1.7.3.0',
    '01.7.3', '1.07.3', '1.7.03', '65535.0.0', '1.65535.0', '1.0.65535', '999999999999999999999999.0.0',
    ' 1.7.3', "1.7.3`n")
foreach ($version in $invalidVersions) {
    $rejected = $false
    try { $null = & $versionReader -Version $version }
    catch {
        if ($_.Exception.Message -notlike 'Store packages require*' -and
            $_.Exception.Message -notlike 'Each version component must fit*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Invalid Store version was accepted: $version" }
}

# Minimal archives reach the payload guard without depending on a real MSIX,
# generated icons, native SDK tools or an application build. The complete valid
# package and manifest checks remain in Test-StorePackage.Tests.ps1.
$required = @('AppxManifest.xml', 'AppxBlockMap.xml', '[Content_Types].xml', 'resources.pri',
    'Grindcrest.exe', 'BdoGrindTracker.dll', 'BdoGrindTracker.runtimeconfig.json', 'System.Private.CoreLib.dll',
    'wwwroot/index.html', 'data/items.en.txt', 'THIRD_PARTY_NOTICES.md', 'Assets/StoreLogo.png',
    'Assets/Square44x44Logo.png', 'Assets/Square150x150Logo.png', 'runtimes/win-x64/native/OpenCvSharpExtern.dll',
    'WebView2Loader.dll', 'wwwroot/assets/icons/test.png')
$required += Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'licenses') -File |
    ForEach-Object { "licenses/$($_.Name)" }
$forbidden = @('Velopack.dll', 'lib/VELOPACK.DLL', 'lib/Velopack.Core.dll', 'Velopack.exe',
    'Update.exe', 'Setup.exe', 'Grindcrest-Setup.exe', 'releases.win-x64-stable.json', 'signing.pfx', 'keys/private.key')
$expectedFailure = 'Store packages must not contain external updaters, installers, feeds, Velopack components or private signing keys.'
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) "Grindcrest-StoreValidation-$([guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$testPackage = Join-Path $testDirectory 'forbidden-payload.msix'
try {
    foreach ($name in $forbidden) {
        $archive = [IO.Compression.ZipFile]::Open($testPackage, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entryName in $required + $name) {
                $writer = [IO.StreamWriter]::new($archive.CreateEntry($entryName).Open())
                try { $writer.Write('synthetic validation fixture') } finally { $writer.Dispose() }
            }
        } finally { $archive.Dispose() }
        $rejected = $false
        try { & $validator -PackagePath $testPackage -Version '1.7.3' }
        catch {
            if ($_.Exception.Message -cne $expectedFailure) { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "Forbidden Store payload was accepted: $name" }
        Remove-Item -LiteralPath $testPackage
    }
} finally {
    if (Test-Path -LiteralPath $testPackage) { Remove-Item -LiteralPath $testPackage }
    Remove-Item -LiteralPath $testDirectory
}
Write-Host "Store validation passed: four valid versions, $($invalidVersions.Count) rejected versions and $($forbidden.Count) rejected payloads."
