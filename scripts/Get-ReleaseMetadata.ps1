[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [ValidateSet('', 'stable', 'beta')] [string] $Channel = ''
)

$ErrorActionPreference = 'Stop'
# Three-part SemVer, with an optional prerelease. Build metadata is deliberately
# excluded so the tag, assembly informational version and package stay identical.
$semver = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?$'
if ($Version -cnotmatch $semver) {
    throw 'Version must be a three-part SemVer without a v prefix or build metadata, for example 0.10.0 or 0.10.0-beta.1.'
}
foreach ($part in ($Version.Split('-')[0].Split('.'))) {
    if ([long] $part -gt 65534) { throw 'Each version component must fit a .NET assembly version (0 through 65534).' }
}

$isPrerelease = $Version.Contains('-')
$inferredChannel = if ($isPrerelease) { 'beta' } else { 'stable' }
if ($Channel -and $Channel -ne $inferredChannel) {
    throw 'Stable versions must use the stable channel; prerelease versions must use the beta channel.'
}
[pscustomobject]@{
    Version = $Version
    Tag = "v$Version"
    Channel = $inferredChannel
    VelopackChannel = "win-x64-$inferredChannel"
    IsPrerelease = $isPrerelease
}
