#Requires -Version 7.2
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$tests = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../tests/ui') -Filter '*.test.cjs' -File |
    Sort-Object Name | ForEach-Object FullName)
if (-not $tests.Count) { throw 'No UI interaction tests were found.' }
& node --test @tests
if ($LASTEXITCODE -ne 0) { throw "UI interaction tests failed with exit code $LASTEXITCODE." }
