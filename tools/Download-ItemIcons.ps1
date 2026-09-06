[CmdletBinding()]
param(
    [string]$ItemsFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'data/items.en.txt'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'data/icons'),
    [string]$CatalogFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'data/icons/catalog.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$imageMagick = Get-Command magick -ErrorAction Stop
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function ConvertTo-IconSlug {
    param([Parameter(Mandatory)][string]$Name)

    $slug = $Name.ToLowerInvariant() -replace "['’]", ''
    $slug = $slug -replace '[^a-z0-9]+', '-'
    return $slug.Trim('-')
}

$itemNames = Get-Content -LiteralPath $ItemsFile |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) }
$catalogItems = [System.Collections.Generic.List[object]]::new()

foreach ($itemName in $itemNames) {
    $query = [Uri]::EscapeDataString($itemName)
    $lookupUri = "https://bdocodex.com/ac.php?l=us&term=$query"
    $json = (Invoke-WebRequest -UseBasicParsing -Uri $lookupUri).Content.TrimStart([char]0xFEFF)
    $matches = @($json | ConvertFrom-Json | Where-Object {
        $_.object_type -eq 'Item' -and $_.name -ceq $itemName
    })

    if ($matches.Count -eq 0) {
        throw "Expected an exact BDO Codex item match for '$itemName', found none."
    }

    # Time-limited event items can be reissued under a new numeric ID while retaining
    # the exact same visible name and icon. That is one visual class for this tracker,
    # not an ambiguous reference. Different icons under one name remain a hard error.
    $distinctIcons = @($matches | Group-Object -Property icon)
    if ($distinctIcons.Count -ne 1) {
        throw "Exact BDO Codex matches for '$itemName' use $($distinctIcons.Count) different icons."
    }

    $match = $matches | Sort-Object { [long]$_.value } | Select-Object -First 1
    $sourceIconUri = "https://bdocodex.com/items/$($match.icon)"
    $pageUri = "https://bdocodex.com$($match.link)"
    $outputFile = Join-Path $OutputDirectory "$(ConvertTo-IconSlug $itemName).png"
    $temporaryFile = New-TemporaryFile

    try {
        Invoke-WebRequest -UseBasicParsing -Uri $sourceIconUri -OutFile $temporaryFile.FullName
        & $imageMagick.Source $temporaryFile.FullName -strip $outputFile

        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputFile)) {
            throw "ImageMagick could not convert the icon for '$itemName'."
        }
    }
    finally {
        Remove-Item -LiteralPath $temporaryFile.FullName -Force -ErrorAction SilentlyContinue
    }

    $dimensions = (& $imageMagick.Source identify -format '%w %h' $outputFile).Trim()
    if ($LASTEXITCODE -ne 0 -or $dimensions -notmatch '^(?<width>\d+) (?<height>\d+)$') {
        throw "ImageMagick could not inspect the converted icon for '$itemName'."
    }

    $sha256 = (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $catalogItems.Add([ordered]@{
        itemId = [string]$match.value
        name = $itemName
        file = [IO.Path]::GetFileName($outputFile)
        page = $pageUri
        sourceIcon = $sourceIconUri
        sha256 = $sha256
        width = [int]$Matches.width
        height = [int]$Matches.height
    })

    [pscustomobject]@{
        Item = $itemName
        ItemId = $match.value
        Page = $pageUri
        SourceIcon = $sourceIconUri
        File = $outputFile
    }
}

$catalogDirectory = Split-Path -Parent $CatalogFile
New-Item -ItemType Directory -Force -Path $catalogDirectory | Out-Null
$catalog = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    items = @($catalogItems | Sort-Object { $_.name })
}
$json = $catalog | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($CatalogFile),
    $json + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
