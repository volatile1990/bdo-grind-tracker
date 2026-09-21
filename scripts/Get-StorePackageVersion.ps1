#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Store submissions use stable three-part app versions and a reserved zero
# revision. Keep the same components valid for the application assembly.
if ($Version -cnotmatch '^[1-9][0-9]*\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
    throw 'Store packages require a stable three-part version with a nonzero major component, for example 1.7.3.'
}
foreach ($part in $Version.Split('.')) {
    $component = 0L
    if (-not [long]::TryParse($part, [ref]$component) -or $component -gt 65534) {
        throw 'Each version component must fit a .NET assembly version (0 through 65534).'
    }
}
"$Version.0"
