#Requires -Version 7.0
<#
.SYNOPSIS
    Verifies the packaged VSIX: required manifest entries, icon, pkgdef, RealizeJSON assemblies, and the absence of
    Roslyn/Visual Studio assemblies that must come from the IDE.
#>
[CmdletBinding()]
param([string]$Path)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
if (-not $Path) {
    $Path = Get-ChildItem (Join-Path $root 'artifacts/vsix') -Filter '*.vsix' | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Path -or -not (Test-Path $Path)) { throw 'VSIX not found.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
try {
    $entries = $zip.Entries | ForEach-Object FullName
    $entries | Sort-Object | ForEach-Object { Write-Host "  $_" }

    $required = @('extension.vsixmanifest', 'manifest.json', 'catalog.json', 'RealizeJSON.VisualStudio.dll', 'RealizeJSON.VisualStudio.pkgdef',
        'RealizeJSON.Core.dll', 'RealizeJSON.CodeAnalysis.dll', 'RealizeJSON.Serialization.dll', 'RealizeJSON.Serialization.SystemTextJson.dll',
        'RealizeJSON.Serialization.NewtonsoftJson.dll', 'RealizeJSON.SampleData.dll', 'RealizeJSON.UI.dll', 'RealizeJSON.Diagnostics.dll',
        'Resources/RealizeJSON-128.png', 'LICENSE', 'NOTICE')
    $missing = $required | Where-Object { $entries -notcontains $_ }
    if ($missing) { throw "VSIX is missing: $($missing -join ', ')" }

    $forbidden = $entries | Where-Object { $_ -match '^(Microsoft\.CodeAnalysis|Microsoft\.VisualStudio|System\.Collections\.Immutable|System\.Reflection\.Metadata|Newtonsoft\.Json)' }
    if ($forbidden) { throw "VSIX contains host-provided assemblies: $($forbidden -join ', ')" }

    $manifestEntry = $zip.GetEntry('extension.vsixmanifest')
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    $manifest = $reader.ReadToEnd()
    $reader.Dispose()
    foreach ($needle in @('RealizeJSON - Live JSON Serialization Preview', 'Microsoft.VisualStudio.Community', '[18.0,)', 'RealizeJSON-128.png', '<License>LICENSE</License>', 'Publisher="Kasadara"')) {
        if (-not $manifest.Contains($needle)) { throw "Manifest is missing '$needle'." }
    }
    foreach ($needle in @('<Preview>', '<ProductArchitecture>arm64</ProductArchitecture>', 'Community" Version="[17.')) {
        if ($manifest.Contains($needle)) { throw "Manifest must not contain '$needle' (release targets validated hosts only)." }
    }

    Write-Host "VSIX verified: $Path" -ForegroundColor Green
}
finally {
    $zip.Dispose()
}
