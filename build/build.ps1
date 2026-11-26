#Requires -Version 7.0
<#
.SYNOPSIS
    Restores and builds the RealizeJSON solution.
.DESCRIPTION
    Builds all libraries, tests and the Visual Studio host assembly. On Linux/macOS the VSIX host compiles against the
    Visual Studio SDK reference assemblies but no VSIX is produced (use package.ps1 on Windows).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot

dotnet --version | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The .NET SDK is required (see global.json).' }

dotnet restore (Join-Path $root 'RealizeJSON.sln')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

dotnet build (Join-Path $root 'RealizeJSON.sln') --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

Write-Host "Build succeeded ($Configuration)." -ForegroundColor Green
