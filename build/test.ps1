#Requires -Version 7.0
<#
.SYNOPSIS
    Runs all automated tests and writes TRX results to artifacts/test-results.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root 'artifacts/test-results'
New-Item -ItemType Directory -Force -Path $results | Out-Null

$arguments = @('test', (Join-Path $root 'RealizeJSON.sln'), '--configuration', $Configuration, '--logger', 'trx', '--results-directory', $results)
if ($NoBuild) { $arguments += '--no-build' }

dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
Write-Host "All tests passed. Results: $results" -ForegroundColor Green
