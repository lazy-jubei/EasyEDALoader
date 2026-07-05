#Requires -Version 5.1
<#
.SYNOPSIS
    Deploys EasyEDA-Loader from the dist\ folder into Altium Designer and
    registers the extension. This script is self-contained and can be shipped
    as part of a release archive alongside the dist\ folder.

.DESCRIPTION
    Copies plugin and manifest files from dist\ (located next to this script)
    into the Altium Designer extensions directory, then ensures the extension
    is registered in ExtensionsRegistry.xml.

    The Altium installation is discovered automatically - no hardcoded GUIDs.

.PARAMETER Force
    Deploy even if Altium Designer is currently running. Altium must be
    restarted for the updated plugin to take effect.

.PARAMETER SkipPlugin
    Skip deploying the plugin files.

.PARAMETER SkipRegistry
    Skip updating ExtensionsRegistry.xml.

.EXAMPLE
    .\Deploy.ps1
    .\Deploy.ps1 -Force
    .\Deploy.ps1 -SkipRegistry
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipPlugin,
    [switch]$SkipRegistry
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Shared.ps1')

# --- Constants ---------------------------------------------------------------

$PluginHrid = 'EasyEDA-Loader'
$PluginGuid = '8035C261-E5FE-403B-A9B5-9ABFFB6E0EF5'
$PluginVerGuid = '7042BC82-F870-462D-86AF-B158AC75C490'

# --- Helpers -----------------------------------------------------------------

# Ensures EasyEDA-Loader is present in ExtensionsRegistry.xml, inserting an
# entry if missing or refreshing the Path if the entry already exists.
function Update-ExtensionsRegistry([string]$RegistryPath, [string]$DeployDir) {
    [xml]$xml = Get-Content $RegistryPath -Encoding UTF8

    $existing = $xml.Extensions.Item | Where-Object { $_.HRID -eq $PluginHrid }
    if ($existing) {
        $existing.Path = $DeployDir
        Write-Ok "Registry: $PluginHrid already registered - path refreshed."
    } else {
        $item = $xml.CreateElement('Item')
        $item.SetAttribute('HRID', $PluginHrid)
        $item.SetAttribute('Guid', $PluginGuid)

        $oleDate = ([datetime]::Today - [datetime]'1899-12-30').TotalDays.ToString('F7')
        $fields = [ordered]@{
            Path             = $DeployDir
            Status           = '0'
            VaultGuid        = ''
            CreatedBy        = 'Altium, Inc.'
            CategoryGuid     = '793A1F67-0B22-4E01-A5DE-3176A1E8C60D'
            CategoryName     = ''
            ReadMe           = ''
            Help             = ''
            Requirements     = ''
            Title            = $PluginHrid
            ShortDescription = 'EasyEDA-Loader'
            LongDescription  = 'Loads EasyEDA components into Altium Designer'
            SmallImage       = ''
            LargeImage       = ''
            Version          = '1.0.0.0'
            VersionGuid      = $PluginVerGuid
            ReleasedDate     = $oleDate
            ReleaseNotes     = ''
            DateInstalled    = $oleDate
        }
        foreach ($key in $fields.Keys) {
            $el = $xml.CreateElement($key)
            $el.InnerText = $fields[$key]
            $item.AppendChild($el) | Out-Null
        }

        $pv = $xml.CreateElement('PlatformVersions')
        foreach ($name in @('DXP', 'EDP', 'MaxDXP', 'MaxEDP')) {
            $el = $xml.CreateElement($name)
            $el.SetAttribute('BuildNumber', $(if ($name -like 'Max*') { '0.0.0.0' } else { '1.0.16.41' }))
            $pv.AppendChild($el) | Out-Null
        }
        $item.AppendChild($pv) | Out-Null

        $xml.Extensions.AppendChild($item) | Out-Null
        Write-Ok "Registry: $PluginHrid entry added."
    }

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = '  '
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)  # UTF-8, no BOM
    $writer = [System.Xml.XmlWriter]::Create($RegistryPath, $settings)
    try   { $xml.Save($writer) }
    finally { $writer.Flush(); $writer.Close() }
}

# --- Paths -------------------------------------------------------------------

$DistDir = Join-Path $PSScriptRoot 'dist'

# --- Pre-flight checks -------------------------------------------------------

Write-Header "Pre-flight checks"

$altiumProcs = Get-Process -Name 'X2' -ErrorAction SilentlyContinue
if ($altiumProcs -and -not $Force) {
    Write-Warn "Altium Designer is currently running."
    Write-Warn "The deployed plugin will only take effect after restarting Altium."
    $answer = Read-Host "  Continue anyway? [y/N]"
    if ($answer -notmatch '^[Yy]') { Write-Info "Aborted."; exit 0 }
} elseif ($altiumProcs) {
    Write-Warn "Altium Designer is running - restart it after deployment to load the new plugin."
} else {
    Write-Ok "Altium Designer is not running."
}

if (-not $SkipPlugin -and -not (Test-Path (Join-Path $DistDir 'EasyEDA-Loader.dll'))) {
    Write-Fail "dist\EasyEDA-Loader.dll not found next to this script."
    Write-Fail "Ensure the dist\ folder is present alongside Deploy.ps1."
    exit 1
}

# --- Find Altium extensions root ---------------------------------------------

$ExtRoot = Find-AltiumExtRoot
if (-not $ExtRoot) {
    Write-Fail "No Altium Designer Extensions folder found under C:\ProgramData\Altium\"
    Write-Fail "Verify that Altium Designer is installed."
    exit 1
}
Write-Ok "Altium extensions: $ExtRoot"

$DeployDir = Join-Path $ExtRoot $PluginHrid

# --- Deploy files ------------------------------------------------------------

Write-Header "Deploying to $DeployDir"

New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null

if (-not $SkipPlugin) {
    $pluginFiles = Get-ChildItem $DistDir -File
    foreach ($f in $pluginFiles) {
        Copy-Item $f.FullName -Destination (Join-Path $DeployDir $f.Name) -Force
        Write-Ok "Plugin:  $($f.Name)"
    }
}

# --- Update ExtensionsRegistry.xml -------------------------------------------

if (-not $SkipRegistry) {
    Write-Header "Updating ExtensionsRegistry.xml"

    $registryPath = Join-Path $ExtRoot 'ExtensionsRegistry.xml'
    if (Test-Path $registryPath) {
        Update-ExtensionsRegistry -RegistryPath $registryPath -DeployDir $DeployDir
    } else {
        Write-Warn "ExtensionsRegistry.xml not found at $registryPath - skipping registry update."
    }
}

# --- Summary -----------------------------------------------------------------

Write-Header "Deployment complete"
Write-Host ""
Write-Host "  Plugin : $DeployDir\EasyEDA-Loader.dll" -ForegroundColor White
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Gray
Write-Host "    1. (Re)start Altium Designer." -ForegroundColor Gray
Write-Host "    2. Open a schematic or PCB and look for 'EasyEDA Loader' in the menu." -ForegroundColor Gray
Write-Host ""
