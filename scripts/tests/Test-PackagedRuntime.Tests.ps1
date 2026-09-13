#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$validator = Join-Path $PSScriptRoot '../Test-PackagedRuntime.ps1'
$valid = '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"9.0.20"},{"name":"Microsoft.WindowsDesktop.App","version":"9.0.20"},{"name":"Microsoft.AspNetCore.App","version":"9.0.20"}]}}'
& $validator -RuntimeConfigJson $valid
foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App', 'Microsoft.AspNetCore.App')) {
    $config = $valid | ConvertFrom-Json
    ($config.runtimeOptions.includedFrameworks | Where-Object name -CEQ $name).version = '9.0.10'
    try {
        & $validator -RuntimeConfigJson ($config | ConvertTo-Json -Depth 6)
        throw "Outdated $name was accepted."
    } catch {
        if ($_.Exception.Message -notlike "Packaged $name must use*") { throw }
    }
}
$missingDesktop = $valid | ConvertFrom-Json
$missingDesktop.runtimeOptions.includedFrameworks = @($missingDesktop.runtimeOptions.includedFrameworks |
    Where-Object name -CNE 'Microsoft.WindowsDesktop.App')
try {
    & $validator -RuntimeConfigJson ($missingDesktop | ConvertTo-Json -Depth 6)
    throw 'Missing desktop runtime was accepted.'
} catch {
    if ($_.Exception.Message -notlike 'Packaged Microsoft.WindowsDesktop.App must use*') { throw }
}
Write-Host 'Runtime validation: current package accepted; three outdated frameworks and missing desktop runtime rejected.'
