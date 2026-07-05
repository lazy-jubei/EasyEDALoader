#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the EasyEDA-Loader plugin and Standalone app.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release).

.PARAMETER SkipPlugin
    Skip building the plugin.

.PARAMETER SkipStandalone
    Skip building the Standalone app.

.EXAMPLE
    .\Build.ps1
    .\Build.ps1 -Configuration Debug
    .\Build.ps1 -IncludeStandalone
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipPlugin,
    [switch]$IncludeStandalone
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Shared.ps1')

$PluginDir     = Join-Path $PSScriptRoot 'EasyEDA-Loader'
$StandaloneDir = Join-Path $PSScriptRoot 'Standalone'
$PluginBin     = Join-Path $PluginDir "bin\$Configuration"

# --- Pre-flight checks -------------------------------------------------------

Write-Header "Pre-flight checks"

if (-not $SkipPlugin) {
    $MSBuild = Find-MSBuild
    if (-not $MSBuild) {
        Write-Fail "MSBuild not found. Install Visual Studio or Build Tools, or use -SkipPlugin."
        exit 1
    }
    Write-Ok "MSBuild: $MSBuild"
}

if ($IncludeStandalone) {
    $dotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Fail "dotnet CLI not found. Install .NET SDK, or omit -IncludeStandalone."
        exit 1
    }
    Write-Ok "dotnet:  $($dotnet.Source)"
}

# --- Build plugin ------------------------------------------------------------

if (-not $SkipPlugin) {
    Write-Header "Building EasyEDA-Loader plugin ($Configuration)"

    # Restore NuGet packages first
    Invoke-Cmd $MSBuild @(
        (Join-Path $PluginDir 'EasyEDA-Loader.csproj')
        '/t:Restore'
        '/v:minimal'
        '/nologo'
    )

    Invoke-Cmd $MSBuild @(
        (Join-Path $PluginDir 'EasyEDA-Loader.csproj')
        "/p:Configuration=$Configuration"
        '/p:Platform=AnyCPU'
        '/t:Build'
        '/v:minimal'
        '/nologo'
    )
    Write-Ok "Plugin built -> $PluginBin"
} else {
    Write-Header "Plugin build skipped"
    Write-Info "Using existing output in $PluginBin"
    if (-not (Test-Path (Join-Path $PluginBin 'EasyEDA-Loader.dll'))) {
        Write-Fail "Plugin DLL not found in $PluginBin - build the project first."
        exit 1
    }
}

# --- Build Standalone (opt-in) ------------------------------------------------

if ($IncludeStandalone) {
    Write-Header "Building Standalone ($Configuration)"

    Invoke-Cmd 'dotnet' @(
        'build'
        (Join-Path $StandaloneDir 'Standalone.csproj')
        '-c', $Configuration
        '--nologo'
        '-v', 'minimal'
    )
    Write-Ok "Standalone built"
}

# --- Summary -----------------------------------------------------------------

Write-Header "Build complete"
Write-Host ""
Write-Host "  Plugin : $PluginBin" -ForegroundColor White
Write-Host ""
Write-Info "Run .\Package.ps1 to stage files into the dist\ folder."
Write-Host ""
