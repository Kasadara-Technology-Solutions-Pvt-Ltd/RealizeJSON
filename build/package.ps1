#Requires -Version 7.0
<#
.SYNOPSIS
    Produces the RealizeJSON VSIX (Windows with Visual Studio 2022+ or Build Tools and the
    "Visual Studio extension development" workload).
.DESCRIPTION
    Locates MSBuild with vswhere, builds the VSIX host project in Release and copies the VSIX to artifacts/vsix.
    The VSIX build tasks (VSCT compiler, pkgdef generation, VSIX container) only run on Windows.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot

if (-not $IsWindows) {
    throw 'VSIX packaging requires Windows (VSSDK build tasks). Run build.ps1 and test.ps1 on other platforms.'
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found. Install Visual Studio 2022+ or Build Tools.' }

$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found.' }

$project = Join-Path $root 'src/RealizeJSON.VisualStudio/RealizeJSON.VisualStudio.csproj'
& $msbuild $project /restore /p:Configuration=$Configuration /p:DeployExtension=false /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'VSIX build failed.' }

$vsix = Get-ChildItem -Path (Join-Path $root 'src/RealizeJSON.VisualStudio/bin') -Filter '*.vsix' -Recurse |
    Where-Object { $_.FullName -match [regex]::Escape($Configuration) } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $vsix) { throw 'No VSIX was produced.' }

$output = Join-Path $root 'artifacts/vsix'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item $vsix.FullName -Destination $output -Force
$hash = (Get-FileHash (Join-Path $output $vsix.Name) -Algorithm SHA256).Hash
Write-Host "VSIX: $(Join-Path $output $vsix.Name)" -ForegroundColor Green
Write-Host "SHA256: $hash"
