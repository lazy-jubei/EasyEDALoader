#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$AltiumInstallDir = $env:ALTIUM_INSTALL_DIR,
    [string]$DevExpressVersion = '25.2',
    [switch]$SkipPlugin,
    [switch]$IncludeStandalone
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Shared.ps1')

$dotnetCommand = Get-Command 'dotnet' -ErrorAction SilentlyContinue
if (-not $dotnetCommand) { throw 'Install .NET SDK 8 or later and add dotnet to PATH.' }
if (-not $AltiumInstallDir) { $AltiumInstallDir = 'C:\Program Files\Altium\AD26' }
$buildArgs = @('-c', $Configuration, '--nologo', '-v', 'minimal',
    "-p:AltiumInstallDir=$AltiumInstallDir", "-p:DevExpressVersion=$DevExpressVersion")
$PluginDir = Join-Path $PSScriptRoot 'EasyEDA-Loader'
$PluginBin = Join-Path $PluginDir "bin/$Configuration"
if (-not $SkipPlugin) {
    Write-Header "Building for Altium 26 ($Configuration)"
    Invoke-Cmd $dotnetCommand.Source (@('build', (Join-Path $PluginDir 'EasyEDA-Loader.csproj')) + $buildArgs)
} elseif (-not (Test-Path (Join-Path $PluginBin 'EasyEDA-Loader.dll'))) {
    throw "Plugin DLL not found in $PluginBin. Build it before using -SkipPlugin."
}
if ($IncludeStandalone) {
    Invoke-Cmd $dotnetCommand.Source (@('build', (Join-Path $PSScriptRoot 'Standalone/Standalone.csproj')) + $buildArgs)
}
Write-Ok "Build complete: $PluginBin"
