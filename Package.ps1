#Requires -Version 5.1
<#
.SYNOPSIS
    Stages EasyEDA-Loader build output into the dist\ folder.

.DESCRIPTION
    Copies the plugin DLL/INS/RCS and third-party dependencies into a clean
    dist\ directory tree ready for Deploy.ps1.

    Altium SDK and DevExpress DLLs are excluded - they are provided by
    Altium Designer at runtime (same pattern as AltiumMCP).

.PARAMETER Configuration
    Build configuration whose output to package: Debug or Release (default: Release).

.PARAMETER SkipPlugin
    Skip packaging the plugin files.

.PARAMETER IncludeStandalone
    Include the Standalone app (AD26 only).

.EXAMPLE
    .\Package.ps1
    .\Package.ps1 -Configuration Debug
    .\Package.ps1 -AltiumVersion 17
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('17','26')][string]$AltiumVersion = '26',

    [switch]$SkipPlugin,
    [switch]$IncludeStandalone
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Shared.ps1')

$PluginDir     = Join-Path $PSScriptRoot 'EasyEDA-Loader'
$StandaloneDir = Join-Path $PSScriptRoot 'Standalone'
$PluginBin     = Join-Path $PluginDir "bin\$Configuration"
$StandaloneBin = Join-Path $StandaloneDir "bin\$Configuration\net8.0-windows\win-x64"
$DistDir       = Join-Path $PSScriptRoot 'dist'
if ($AltiumVersion -eq '17') {
    if ($IncludeStandalone) { throw 'The standalone preview app supports AD26 only.' }
    $PluginBin = Join-Path $PluginDir "bin/ad17/$Configuration"
    $DistDir = Join-Path $PSScriptRoot 'dist-ad17'
}

Write-Header "Packaging AD$AltiumVersion into $DistDir"

# --- Validate build outputs exist -------------------------------------------

if (-not $SkipPlugin -and -not (Test-Path (Join-Path $PluginBin 'EasyEDA-Loader.dll'))) {
    Write-Fail "Plugin DLL not found in $PluginBin"
    Write-Fail "Run .\Build.ps1 first."
    exit 1
}
if (-not $SkipPlugin) { Assert-AltiumTarget $PluginBin $AltiumVersion -RequireManifest }

if ($IncludeStandalone -and -not (Test-Path $StandaloneBin)) {
    Write-Fail "Standalone build output not found: $StandaloneBin"
    Write-Fail "Run .\Build.ps1 -IncludeStandalone first."
    exit 1
}

# --- (Re)create dist ---------------------------------------------------------

if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# --- Copy plugin files -------------------------------------------------------

if (-not $SkipPlugin) {
    # Altium SDK and DevExpress DLLs are provided by Altium Designer at runtime.
    # Only the plugin DLL, manifests, config, and third-party dependencies are bundled.
    $excludePattern = '^(Altium\.|DevExpress\.)'

    $pluginFiles = Get-ChildItem $PluginBin -File | Where-Object {
        $_.Name -eq 'EasyEDA-Loader.dll' -or
        $_.Name -eq 'EasyEDA-Loader.Ins' -or
        $_.Name -eq 'EasyEDA-Loader.rcs' -or
        $_.Name -eq 'EasyEDA-Loader.dll.config' -or
        $_.Name -eq 'EasyEDA-Loader.deps.json' -or
        $_.Name -eq 'EasyEDA-Loader.target.json' -or
        ($_.Extension -eq '.dll' -and $_.Name -notmatch $excludePattern -and $_.Name -ne 'EasyEDA-Loader.dll') -or
        ($_.Extension -eq '.xml' -and $_.Name -like 'Newtonsoft.*')
    }

    foreach ($f in $pluginFiles) {
        Copy-Item $f.FullName -Destination (Join-Path $DistDir $f.Name) -Force
        Write-Ok "Plugin:  $($f.Name)"
    }

    # PDB is useful in Debug builds, include optionally in Release too
    $pdb = Join-Path $PluginBin 'EasyEDA-Loader.pdb'
    if (Test-Path $pdb) {
        Copy-Item $pdb -Destination (Join-Path $DistDir 'EasyEDA-Loader.pdb') -Force
        Write-Info "PDB:     EasyEDA-Loader.pdb (debug symbols)"
    }
}

# --- Copy Standalone files (opt-in) ------------------------------------------

if ($IncludeStandalone) {
    $StandaloneDistDir = Join-Path $DistDir 'standalone'
    New-Item -ItemType Directory -Path $StandaloneDistDir -Force | Out-Null

    $standaloneFiles = Get-ChildItem $StandaloneBin -File -Recurse
    foreach ($f in $standaloneFiles) {
        $relativePath = $f.FullName.Substring($StandaloneBin.Length).TrimStart([char[]]'\/')
        $destination = Join-Path $StandaloneDistDir $relativePath
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item $f.FullName -Destination $destination -Force
    }
    Write-Ok "Standalone: $($standaloneFiles.Count) file(s) -> dist\standalone\"
}

# --- Summary -----------------------------------------------------------------

Write-Header "Packaging complete"
Write-Host ""
Write-Host "  Dist : $DistDir" -ForegroundColor White
Write-Host ""
Write-Info "Run .\Deploy.ps1 -AltiumVersion $AltiumVersion to install into Altium Designer."
Write-Host ""

# Ship every deployment dependency beside dist/, including the shared helper.
$releaseDir = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$archive = Join-Path $releaseDir "EasyEDALoader-ad$AltiumVersion.zip"
$archiveFiles = @($DistDir, (Join-Path $PSScriptRoot 'Deploy.ps1'), (Join-Path $PSScriptRoot '_Shared.ps1'), (Join-Path $PSScriptRoot 'LICENSE'))
if ($AltiumVersion -eq '17') {
    $archiveFiles += Join-Path $PSScriptRoot 'AD17.md'
    $archiveFiles += Join-Path $PSScriptRoot 'ManufacturerPartSearch.md'
}
Compress-Archive -Path $archiveFiles -DestinationPath $archive -Force
Write-Ok "Release archive: $archive"
