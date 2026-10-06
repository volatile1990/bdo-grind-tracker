[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,
    [string] $CompilerPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') {
    throw 'Grindcrest.Native can only be built on Windows.'
}

function Resolve-NativeCompiler {
    if ($CompilerPath) {
        if (!(Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
            throw "Native compiler does not exist: $CompilerPath"
        }
        return (Resolve-Path -LiteralPath $CompilerPath).Path
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installations = @(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
        foreach ($installation in $installations) {
            $toolRoot = Join-Path $installation 'VC\Tools\MSVC'
            if (!(Test-Path -LiteralPath $toolRoot -PathType Container)) { continue }
            $versions = Get-ChildItem -LiteralPath $toolRoot -Directory | Sort-Object { [version] $_.Name } -Descending
            foreach ($version in $versions) {
                $candidate = Join-Path $version.FullName 'bin\Hostx64\x64\cl.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
            }
        }
    }

    $command = Get-Command cl.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw 'Install the Visual Studio C++ x64 build tools, set NativePixelConverterCompiler to cl.exe, or use -p:NativeKernelsEnabled=false for a managed-only development build.'
}

$compiler = Resolve-NativeCompiler
$compilerDirectory = Split-Path -Parent $compiler
$linker = Join-Path $compilerDirectory 'link.exe'
if (!(Test-Path -LiteralPath $linker -PathType Leaf)) {
    throw "MSVC linker is missing next to the compiler: $linker"
}
$toolDirectory = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $compilerDirectory))
$includeDirectory = Join-Path $toolDirectory 'include'
if (!(Test-Path -LiteralPath $includeDirectory -PathType Container)) {
    throw "MSVC headers are missing: $includeDirectory"
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repositoryRoot 'native\Grindcrest.Native.cpp'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$object = Join-Path $outputRoot 'Grindcrest.Native.obj'
$library = Join-Path $outputRoot 'Grindcrest.Native.lib'
$dll = Join-Path $outputRoot 'Grindcrest.Native.dll'

# Only compiler headers are needed; this DLL has no CRT or Windows SDK imports.
# All kernels are optimized even when the managed app is built in Debug mode.
& $compiler /nologo /c /O2 /Oi /Ot /GS- /GR- /EHs-c- /W4 /WX /std:c++17 /Brepro "/I$includeDirectory" "/Fo$object" $source
if ($LASTEXITCODE -ne 0) { throw "Native compilation failed with exit code $LASTEXITCODE." }
& $linker /nologo /DLL /NOENTRY /NODEFAULTLIB /INCREMENTAL:NO /MACHINE:X64 /DYNAMICBASE /NXCOMPAT /Brepro /OPT:REF /OPT:ICF "/OUT:$dll" "/IMPLIB:$library" $object
if ($LASTEXITCODE -ne 0) { throw "Native linking failed with exit code $LASTEXITCODE." }
Write-Host "Built native pixel kernels: $dll"
