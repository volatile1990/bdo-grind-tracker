#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $ExpectedCommit,
    [string] $Directory,
    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$metadata = & (Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1') -Version $Version
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedCommit must be a full commit SHA.' }
if (-not $env:GH_TOKEN) { throw 'GH_TOKEN is required to publish or check release availability.' }

$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$releaseUri = "https://api.github.com/repos/$Repository/releases/tags/$($metadata.Tag)"
$response = Invoke-WebRequest -Uri $releaseUri -Headers $headers -SkipHttpErrorCheck
if ($response.StatusCode -eq 200) { throw "Release $($metadata.Tag) already exists, including drafts. Use a new version; releases are never overwritten." }
if ($response.StatusCode -ne 404) { throw "GitHub release lookup failed with HTTP $($response.StatusCode)." }
$tagCommit = & git rev-parse --verify --quiet "refs/tags/$($metadata.Tag)^{commit}"
if ($LASTEXITCODE -eq 0 -and $tagCommit.Trim() -ne $ExpectedCommit) {
    throw 'The release tag already points to a different commit.'
}
# A missing tag is valid for workflow_dispatch; gh creates it at ExpectedCommit.
$global:LASTEXITCODE = 0
if ($CheckOnly) {
    Write-Host "Release $($metadata.Tag) is available for commit $ExpectedCommit."
    return
}
if (-not $Directory) { throw 'Directory is required when publishing.' }
& (Join-Path $PSScriptRoot 'Test-Release.ps1') -Directory $Directory -Version $Version
$Directory = (Resolve-Path -LiteralPath $Directory).Path
$assets = @(Get-ChildItem -LiteralPath $Directory -File | ForEach-Object FullName)

function Invoke-GitHub([string[]] $Arguments) {
    & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed with exit code $LASTEXITCODE. Any draft created remains unpublished for inspection." }
}

$createArguments = @('release', 'create', $metadata.Tag, '--repo', $Repository,
    '--target', $ExpectedCommit, '--title', "Grindcrest $Version", '--draft')
$versionNotes = Join-Path $PSScriptRoot "../docs/release-notes/$Version.md"
if (Test-Path -LiteralPath $versionNotes -PathType Leaf) {
    $createArguments += @('--notes-file', (Resolve-Path -LiteralPath $versionNotes).Path)
} else {
    $createArguments += '--generate-notes'
}
if ($metadata.IsPrerelease) { $createArguments += '--prerelease' }
Invoke-GitHub $createArguments
Invoke-GitHub (@('release', 'upload', $metadata.Tag, '--repo', $Repository) + $assets)
$latestArgument = if ($metadata.IsPrerelease) { '--latest=false' } else { '--latest=true' }
Invoke-GitHub -Arguments @('release', 'edit', $metadata.Tag, '--repo', $Repository, '--draft=false', $latestArgument)
Write-Host "Published https://github.com/$Repository/releases/tag/$($metadata.Tag)"
