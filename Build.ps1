#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('17','26')][string]$AltiumVersion = '26',
    [string]$AltiumInstallDir = $env:ALTIUM_INSTALL_DIR,
    [string]$DevExpressVersion,
    [switch]$SkipPlugin,
    [switch]$IncludeStandalone
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Shared.ps1')

$dotnetCommand = Get-Command 'dotnet' -ErrorAction SilentlyContinue
if (-not $dotnetCommand) { throw 'Install .NET SDK 8 or later and add dotnet to PATH.' }
if ($AltiumVersion -eq '17' -and $IncludeStandalone) { throw 'The standalone preview app supports AD26 only.' }
if (-not $AltiumInstallDir) {
    $AltiumInstallDir = if ($AltiumVersion -eq '17') { 'C:\Program Files (x86)\Altium\AD17' } else { 'C:\Program Files\Altium\AD26' }
}
if (-not $DevExpressVersion) { $DevExpressVersion = if ($AltiumVersion -eq '17') { '15.2' } else { '25.2' } }
$buildArgs = @('-c', $Configuration, '--nologo', '-v', 'minimal',
    "-p:AltiumVersion=$AltiumVersion", "-p:AltiumInstallDir=$AltiumInstallDir", "-p:DevExpressVersion=$DevExpressVersion")
$PluginDir = Join-Path $PSScriptRoot 'EasyEDA-Loader'
$PluginBin = Join-Path $PluginDir "bin/$Configuration"
if ($AltiumVersion -eq '17') { $PluginBin = Join-Path $PluginDir "bin/ad17/$Configuration" }
if (-not $SkipPlugin) {
    Write-Header "Building for Altium $AltiumVersion ($Configuration)"
    Invoke-Cmd $dotnetCommand.Source (@('build', (Join-Path $PluginDir 'EasyEDA-Loader.csproj')) + $buildArgs)
} elseif (-not (Test-Path (Join-Path $PluginBin 'EasyEDA-Loader.dll'))) {
    throw "Plugin DLL not found in $PluginBin. Build it before using -SkipPlugin."
}
if ($IncludeStandalone) {
    Invoke-Cmd $dotnetCommand.Source (@('build', (Join-Path $PSScriptRoot 'Standalone/Standalone.csproj')) + $buildArgs)
}
Write-Ok "Build complete: $PluginBin"
