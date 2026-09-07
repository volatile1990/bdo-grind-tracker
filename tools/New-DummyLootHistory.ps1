param(
    [string]$OutputPath = (Join-Path $env:LOCALAPPDATA 'BdoGrindTracker\loot-history-v1.json'),
    [string]$TemplatePath = (Join-Path $PSScriptRoot '..\artifacts\dummy-history\loot-history-v1.json')
)

$ErrorActionPreference = 'Stop'
$templatePathResolved = [IO.Path]::GetFullPath($TemplatePath)
$outputPathResolved = [IO.Path]::GetFullPath($OutputPath)
$template = Get-Content -LiteralPath $templatePathResolved -Raw | ConvertFrom-Json
$existingEntries = @()
if (Test-Path -LiteralPath $outputPathResolved) {
    $existing = Get-Content -LiteralPath $outputPathResolved -Raw | ConvertFrom-Json
    $existingEntries = @($existing.Entries | Where-Object {
        $_.CharacterClass -notmatch '(?i)demo'
    })
}

$multipliers = @(0.91, 0.96, 1.02, 1.08, 0.94, 1.12, 0.99, 1.05, 0.88, 1.15)
$classes = @(
    'Maegu · Awakening · Demo',
    'Dark Knight · Awakening · Demo',
    'Warrior · Succession · Demo',
    'Witch · Awakening · Demo',
    'Lahn · Succession · Demo',
    'Guardian · Awakening · Demo'
)
$dummyEntries = [Collections.Generic.List[object]]::new()
$spotGroups = @($template.Entries | Group-Object SpotId | Sort-Object Name)
$now = [DateTimeOffset]::Now

for ($spotIndex = 0; $spotIndex -lt $spotGroups.Count; $spotIndex++) {
    $group = $spotGroups[$spotIndex]
    $seeds = @($group.Group)
    for ($hourIndex = 0; $hourIndex -lt 10; $hourIndex++) {
        $seed = $seeds[$hourIndex % $seeds.Count]
        $factor = [decimal]$multipliers[$hourIndex]
        $totals = [ordered]@{}
        foreach ($property in $seed.Totals.PSObject.Properties) {
            $scaled = [Math]::Max(1L, [long][Math]::Round(([decimal]$property.Value) * $factor))
            $totals[$property.Name] = $scaled
        }
        $updatedAt = $now.AddHours(-1 * (($hourIndex * $spotGroups.Count) + $spotIndex + 1))
        $dummyEntries.Add([ordered]@{
            SessionId = [Guid]::NewGuid().ToString()
            StartedAt = $updatedAt.AddHours(-1).ToString('o')
            UpdatedAt = $updatedAt.ToString('o')
            Duration = '01:00:00'
            SpotId = $group.Name
            CharacterClass = $classes[($hourIndex + $spotIndex) % $classes.Count]
            Totals = $totals
            SilverBeforeTax = [Math]::Round(([decimal]$seed.SilverBeforeTax) * $factor)
            SilverAfterTax = [Math]::Round(([decimal]$seed.SilverAfterTax) * $factor)
            SilverIsComplete = [bool]$seed.SilverIsComplete
        })
    }
}

$allEntries = @($existingEntries) + @($dummyEntries)
$document = [ordered]@{ Version = 1; Entries = $allEntries }
$directory = Split-Path -Parent $outputPathResolved
[IO.Directory]::CreateDirectory($directory) | Out-Null
if (Test-Path -LiteralPath $outputPathResolved) {
    $backupPath = Join-Path $directory ('loot-history-v1.backup-{0}.json' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Copy-Item -LiteralPath $outputPathResolved -Destination $backupPath
}
$temporaryPath = $outputPathResolved + '.tmp'
$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
Move-Item -LiteralPath $temporaryPath -Destination $outputPathResolved -Force

[pscustomobject]@{
    OutputPath = $outputPathResolved
    PreservedRealHours = $existingEntries.Count
    DummyHours = $dummyEntries.Count
    Spots = $spotGroups.Count
} | Format-List
