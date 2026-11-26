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

# Microsoft.Testing.Platform mode (opted in via global.json "test.runner"); TRX via the xUnit v3 reporter.
$arguments = @('test', '--solution', (Join-Path $root 'RealizeJSON.sln'), '--configuration', $Configuration, '--results-directory', $results, '--report-xunit-trx')
if ($NoBuild) { $arguments += '--no-build' }

dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
Write-Host "All tests passed. Results: $results" -ForegroundColor Green
