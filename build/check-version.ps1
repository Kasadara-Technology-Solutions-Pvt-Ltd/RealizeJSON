#Requires -Version 7.0
<#
.SYNOPSIS
    Verifies that the version is identical in Directory.Build.props, the VSIX manifest and the package registration,
    and (optionally) matches a release tag such as v0.1.0. Prints the version.
#>
[CmdletBinding()]
param([string]$Tag)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot

$props = [xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))
$propsVersion = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]

$manifest = [xml](Get-Content -Raw (Join-Path $root 'src/RealizeJSON.VisualStudio/source.extension.vsixmanifest'))
$manifestVersion = $manifest.PackageManifest.Metadata.Identity.Version

$package = Get-Content -Raw (Join-Path $root 'src/RealizeJSON.VisualStudio/RealizeJSONPackage.cs')
$match = [regex]::Match($package, 'InstalledProductRegistration\([^)]*"(?<v>\d+\.\d+\.\d+)"\)')
if (-not $match.Success) { throw 'InstalledProductRegistration version not found.' }
$packageVersion = $match.Groups['v'].Value

$versions = @{ 'Directory.Build.props' = $propsVersion; 'source.extension.vsixmanifest' = $manifestVersion; 'RealizeJSONPackage.cs' = $packageVersion }
if (@($versions.Values | Select-Object -Unique).Count -ne 1) {
    $versions.GetEnumerator() | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }
    throw 'Version mismatch between project files.'
}

if ($Tag) {
    $expected = $Tag -replace '^v', ''
    $core = ($expected -split '-')[0]
    if ($core -ne $propsVersion) { throw "Tag '$Tag' does not match project version '$propsVersion'." }
}

Write-Output $propsVersion
