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
    [string]$ExtensionsRoot,
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

    $existing = $xml.SelectNodes('/Extensions/Item') | Where-Object { $_.HRID -eq $PluginHrid }
    if (@($existing).Count -gt 1) { throw 'Duplicate EasyEDA entries in the extension registry.' }
    if ($existing) {
        $existing.Path = $DeployDir
        foreach ($field in @{ Version = '1.1.0.0'; VersionGuid = $PluginVerGuid }.GetEnumerator()) {
            $el = $existing.SelectSingleNode($field.Key)
            if (-not $el) { $el = $xml.CreateElement($field.Key); $existing.AppendChild($el) | Out-Null }
            $el.InnerText = $field.Value
        }
        Write-Ok "Registry: $PluginHrid registration updated."
    } else {
        $item = $xml.CreateElement('Item')
        $item.SetAttribute('HRID', $PluginHrid)
        $item.SetAttribute('Guid', $PluginGuid)

        $oleDate = ([datetime]::Today - [datetime]'1899-12-30').TotalDays.ToString('F7', [System.Globalization.CultureInfo]::InvariantCulture)
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
            Version          = '1.1.0.0'
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
    $temporaryPath = $RegistryPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $backupPath = $RegistryPath + '.' + [guid]::NewGuid().ToString('N') + '.bak'
    $writer = [System.Xml.XmlWriter]::Create($temporaryPath, $settings)
    try   { $xml.Save($writer) }
    finally { $writer.Flush(); $writer.Close() }
    [System.IO.File]::Replace($temporaryPath, $RegistryPath, $backupPath)
    Write-Info "Registry backup: $backupPath"
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

if (-not $SkipPlugin) {
    foreach ($required in @('EasyEDA-Loader.dll', 'EasyEDA-Loader.Ins', 'EasyEDA-Loader.rcs', 'EasyEDA-Loader.deps.json', 'Newtonsoft.Json.dll')) {
        if (-not (Test-Path (Join-Path $DistDir $required) -PathType Leaf)) { throw "Missing built artifact: $required" }
    }
    if (Get-ChildItem $DistDir -File | Where-Object { $_.Name -match '^(Altium\.|DevExpress\.)' }) {
        throw 'The dist folder must not contain Altium SDK or DevExpress assemblies.'
    }
}

# --- Find Altium extensions root ---------------------------------------------

$ExtRoot = Find-AltiumExtRoot $ExtensionsRoot
if (-not $ExtRoot) {
    Write-Fail "No Altium Designer Extensions folder found under C:\ProgramData\Altium\"
    Write-Fail "Verify that Altium Designer is installed."
    exit 1
}
Write-Ok "Altium extensions: $ExtRoot"

$DeployDir = Join-Path $ExtRoot $PluginHrid
if (-not $SkipRegistry) {
    [xml]$validateRegistry = Get-Content (Join-Path $ExtRoot 'ExtensionsRegistry.xml') -Encoding UTF8
    if ($validateRegistry.DocumentElement.Name -ne 'Extensions') { throw 'Invalid extension registry root.' }
    if ($validateRegistry.SelectNodes('/Extensions/Item[@HRID="EasyEDA-Loader"]').Count -gt 1) {
        throw 'Duplicate EasyEDA entries in the extension registry.'
    }
}
# --- Deploy files ------------------------------------------------------------

Write-Header "Deploying to $DeployDir"
$stagingDir = $DeployDir + '.' + [guid]::NewGuid().ToString('N') + '.new'
$pluginBackup = $null
$installedNew = $false
try {
    if (-not $SkipPlugin) {
        New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
        Get-ChildItem $DistDir -File | ForEach-Object {
            Copy-Item $_.FullName -Destination (Join-Path $stagingDir $_.Name) -Force
        }
        if (Test-Path $DeployDir) {
            $pluginBackup = $DeployDir + '.' + [guid]::NewGuid().ToString('N') + '.bak'
            Move-Item $DeployDir $pluginBackup
            Write-Info "Plugin backup: $pluginBackup"
        }
        Move-Item $stagingDir $DeployDir
        $installedNew = $true
    }
    if (-not $SkipRegistry) {
        Update-ExtensionsRegistry -RegistryPath (Join-Path $ExtRoot 'ExtensionsRegistry.xml') -DeployDir $DeployDir
    }
} catch {
    if ($installedNew -and (Test-Path $DeployDir)) { Remove-Item $DeployDir -Recurse -Force }
    if ($pluginBackup -and (Test-Path $pluginBackup) -and -not (Test-Path $DeployDir)) {
        Move-Item $pluginBackup $DeployDir
    }
    throw
} finally {
    if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
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
