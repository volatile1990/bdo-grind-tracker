#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $Version,
    [string] $WindowsSdkBin
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$null = & (Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1') -Version $Version -Channel stable
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml] $expected = Get-Content -LiteralPath (Join-Path $workspaceRoot 'packaging/msix/AppxManifest.xml') -Raw
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('AppxManifest.xml', 'AppxBlockMap.xml', '[Content_Types].xml', 'resources.pri',
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
    $visuals = $application.SelectSingleNode('*[local-name()="VisualElements"]')
    if ($visuals.BackgroundColor -cne 'transparent' -or
        $visuals.Square44x44Logo -cne 'Assets\Square44x44Logo.png') {
        throw 'Expected transparent shell icons using Assets\Square44x44Logo.png.'
    }
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
    $targetSizes = @(16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256)
    $logos = @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))
    foreach ($size in $targetSizes) {
        foreach ($suffix in @('', '_altform-unplated', '_altform-lightunplated')) {
            $logos += ,@("Square44x44Logo.targetsize-$size$suffix.png", $size)
        }
    }
    foreach ($logo in $logos) {
        if ($entries -notcontains "Assets/$($logo[0])") { throw "Missing shell icon: $($logo[0])" }
        $stream = $archive.GetEntry("Assets/$($logo[0])").Open()
        try {
            $bitmap = [Drawing.Bitmap]::new($stream)
            try {
                if ($bitmap.Width -ne $logo[1] -or $bitmap.Height -ne $logo[1]) { throw "Wrong logo dimensions: $($logo[0])" }
                if (-not [Drawing.Image]::IsAlphaPixelFormat($bitmap.PixelFormat) -or
                    $bitmap.GetPixel(0, 0).A -ne 0 -or $bitmap.GetPixel($bitmap.Width - 1, 0).A -ne 0 -or
                    $bitmap.GetPixel(0, $bitmap.Height - 1).A -ne 0 -or
                    $bitmap.GetPixel($bitmap.Width - 1, $bitmap.Height - 1).A -ne 0) {
                    throw "Shell icon background is not transparent: $($logo[0])"
                }
                $visible = $false
                for ($y = 0; $y -lt $bitmap.Height -and -not $visible; $y++) {
                    for ($x = 0; $x -lt $bitmap.Width -and -not $visible; $x++) {
                        $visible = $bitmap.GetPixel($x, $y).A -gt 0
                    }
                }
                if (-not $visible) { throw "Shell icon has no visible pixels: $($logo[0])" }
            } finally { $bitmap.Dispose() }
        } finally { $stream.Dispose() }
    }

    if (-not $WindowsSdkBin) {
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
        $WindowsSdkBin = Get-ChildItem -LiteralPath $sdkRoot -Directory |
            Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.19041.0' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64' } |
            Where-Object { Test-Path -LiteralPath (Join-Path $_ 'makepri.exe') } | Select-Object -First 1
    }
    if (-not $WindowsSdkBin) { throw 'Windows SDK MakePri.exe is required to verify shell icon resources.' }
    $makePri = Join-Path $WindowsSdkBin 'makepri.exe'
    if (-not (Test-Path -LiteralPath $makePri -PathType Leaf)) { throw 'MakePri.exe was not found.' }
    $validationDirectory = Join-Path ([IO.Path]::GetTempPath()) "Grindcrest-PriCheck-$([guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($validationDirectory) | Out-Null
    $priPath = Join-Path $validationDirectory 'resources.pri'
    $dumpPath = Join-Path $validationDirectory 'resources.xml'
    $dumpLog = Join-Path $validationDirectory 'makepri.log'
    try {
        [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry('resources.pri'), $priPath)
        & $makePri dump /if $priPath /of $dumpPath /dt detailed /o *> $dumpLog
        if ($LASTEXITCODE -ne 0) { throw 'The packaged shell resource index could not be read by MakePri.' }
        [xml] $index = Get-Content -LiteralPath $dumpPath -Raw
        if ($index.PriInfo.ResourceMap.name -cne $manifest.Package.Identity.Name) {
            throw 'Shell resource index does not match the Store package identity.'
        }
        $icon = $index.SelectSingleNode('//NamedResource[@uri="ms-resource://' +
            $manifest.Package.Identity.Name + '/Files/Assets/Square44x44Logo.png"]')
        if ($null -eq $icon) { throw 'Shell icon resource does not match the manifest logo path.' }
        foreach ($size in $targetSizes) {
            foreach ($form in @('', 'unplated', 'lightunplated')) {
                $suffix = if ($form) { "_altform-$form" } else { '' }
                $path = "Assets\Square44x44Logo.targetsize-$size$suffix.png"
                $candidate = $icon.SelectSingleNode('Candidate[Value="' + $path + '"]')
                if ($null -eq $candidate -or $null -eq $candidate.SelectSingleNode(
                    'QualifierSet/Qualifier[@name="TargetSize" and @value="' + $size + '"]')) {
                    throw "Shell icon is missing its target-size resource mapping: $path"
                }
                if ($form -and $null -eq $candidate.SelectSingleNode(
                    'QualifierSet/Qualifier[@name="AlternateForm" and @value="' + $form.ToUpperInvariant() + '"]')) {
                    throw "Shell icon is missing its unplated resource mapping: $path"
                }
            }
        }
    } finally {
        foreach ($temporary in @($priPath, $dumpPath, $dumpLog)) {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
        Remove-Item -LiteralPath $validationDirectory
    }
} finally {
    $archive.Dispose()
}
Write-Host "Verified Store identity, version $Version.0, runtime dependencies, 48 transparent logos, shell resource mappings and payload."
