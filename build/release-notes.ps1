#Requires -Version 7.0
<#
.SYNOPSIS
    Extracts the CHANGELOG.md section for a version into a Markdown file used as GitHub release notes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutFile) { $OutFile = Join-Path $root 'artifacts/release-notes.md' }

$lines = Get-Content (Join-Path $root 'CHANGELOG.md')
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^## \[$([regex]::Escape($Version))\]") { $start = $i + 1; break }
}
if ($start -lt 0) { throw "CHANGELOG.md has no '## [$Version]' section." }

$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ' -or $lines[$i] -match '^\[[^\]]+\]: ') { $end = $i; break }
}

$body = ($lines[$start..($end - 1)] -join "`n").Trim()
if (-not $body) { throw "CHANGELOG.md section for $Version is empty." }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null
Set-Content -Path $OutFile -Value $body -Encoding utf8NoBOM
Write-Output $OutFile
