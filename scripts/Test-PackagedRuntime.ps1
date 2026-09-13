#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $RuntimeConfigJson)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[xml] $project = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/BdoGrindTracker.App/BdoGrindTracker.App.csproj') -Raw
$minimum = [version] $project.SelectSingleNode('//MinimumPackagedRuntimeVersion').InnerText
$config = $RuntimeConfigJson | ConvertFrom-Json
$frameworks = @($config.runtimeOptions.includedFrameworks)
foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App', 'Microsoft.AspNetCore.App')) {
    $matches = @($frameworks | Where-Object { $_.name -ceq $name })
    if ($matches.Count -ne 1 -or [version] $matches[0].version -lt $minimum -or
        ([version] $matches[0].version).Major -ne $minimum.Major) {
        throw "Packaged $name must use the supported .NET $($minimum.Major) runtime, at least $minimum."
    }
}
Write-Host "Verified packaged .NET runtimes against minimum $minimum."
