#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $Version,
    [string] $WindowsSdkBin
)

# Exercise the release validator against an actual locally built package and
# damaged copies. These archives are never signed, installed or submitted.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$validator = Join-Path $PSScriptRoot '../Test-StorePackage.ps1'
$sourcePackage = (Resolve-Path -LiteralPath $PackagePath).Path
$sourceHash = (Get-FileHash -LiteralPath $sourcePackage -Algorithm SHA256).Hash
& $validator -PackagePath $sourcePackage -Version $Version -WindowsSdkBin $WindowsSdkBin

$testDirectory = Join-Path ([IO.Path]::GetTempPath()) "Grindcrest-CapabilityTests-$([guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$testPackage = Join-Path $testDirectory 'invalid-capabilities.msix'
$expectedFailure = 'Expected exactly rescap:runFullTrust and uap11:graphicsCaptureWithoutBorder capabilities.'
$cases = @('missing-borderless', 'wrong-namespace', 'duplicate-borderless', 'unexpected-capability')
try {
    foreach ($case in $cases) {
        Copy-Item -LiteralPath $sourcePackage -Destination $testPackage
        $archive = [IO.Compression.ZipFile]::Open($testPackage, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.GetEntry('AppxManifest.xml')
            $reader = [IO.StreamReader]::new($entry.Open())
            try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $capabilities = $manifest.Package.Capabilities
            $borderless = $capabilities.SelectSingleNode('*[@Name="graphicsCaptureWithoutBorder"]')
            switch ($case) {
                'missing-borderless' { $null = $capabilities.RemoveChild($borderless) }
                'wrong-namespace' {
                    $replacement = $manifest.CreateElement('rescap', 'Capability',
                        'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')
                    $replacement.SetAttribute('Name', 'graphicsCaptureWithoutBorder')
                    $null = $capabilities.ReplaceChild($replacement, $borderless)
                }
                'duplicate-borderless' { $null = $capabilities.AppendChild($borderless.CloneNode($true)) }
                'unexpected-capability' {
                    $extra = $manifest.CreateElement('Capability',
                        'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
                    $extra.SetAttribute('Name', 'internetClient')
                    $null = $capabilities.AppendChild($extra)
                }
            }
            $entry.Delete()
            $writer = [IO.StreamWriter]::new($archive.CreateEntry('AppxManifest.xml').Open())
            try { $manifest.Save($writer) } finally { $writer.Dispose() }
        } finally { $archive.Dispose() }

        try {
            & $validator -PackagePath $testPackage -Version $Version -WindowsSdkBin $WindowsSdkBin
            throw "The validator accepted damaged package: $case"
        } catch {
            if ($_.Exception.Message -cne $expectedFailure) { throw }
        }
        Remove-Item -LiteralPath $testPackage
        Write-Host "Rejected $case."
    }
} finally {
    if (Test-Path -LiteralPath $testPackage) { Remove-Item -LiteralPath $testPackage }
    Remove-Item -LiteralPath $testDirectory
    if ((Get-FileHash -LiteralPath $sourcePackage -Algorithm SHA256).Hash -cne $sourceHash) {
        throw 'The original package changed during capability validation.'
    }
}
Write-Host 'Store package capability regression checks passed: valid package and four rejected mutations.'
