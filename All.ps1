#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, packages, and deploys EasyEDA-Loader in a single step.

.DESCRIPTION
    Runs Build.ps1, Package.ps1, and Deploy.ps1 in sequence, forwarding the
    appropriate parameters to each script.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release).
    Forwarded to Build.ps1 and Package.ps1.

.PARAMETER SkipPlugin
    Skip building and deploying the plugin. Forwarded to all three scripts.

.PARAMETER IncludeStandalone
    Also build and package the Standalone viewer app.
    Forwarded to Build.ps1 and Package.ps1 only. Not included by default.

.PARAMETER SkipRegistry
    Skip updating ExtensionsRegistry.xml.
    Forwarded to Deploy.ps1 only.

.PARAMETER Force
    Deploy even if Altium Designer is currently running.
    Forwarded to Deploy.ps1 only.

.EXAMPLE
    .\All.ps1
    .\All.ps1 -Configuration Debug
    .\All.ps1 -Force
    .\All.ps1 -Configuration Debug -IncludeStandalone
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipPlugin,
    [switch]$IncludeStandalone,
    [switch]$SkipRegistry,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Shared.ps1')

# Build separate splats - each script only receives the params it declares.
$buildArgs = @{ Configuration = $Configuration }
if ($SkipPlugin)        { $buildArgs['SkipPlugin']        = $true }
if ($IncludeStandalone) { $buildArgs['IncludeStandalone'] = $true }

$packageArgs = @{ Configuration = $Configuration }
if ($SkipPlugin)        { $packageArgs['SkipPlugin']        = $true }
if ($IncludeStandalone) { $packageArgs['IncludeStandalone'] = $true }

$deployArgs = @{}
if ($Force)            { $deployArgs['Force']            = $true }
if ($SkipPlugin)       { $deployArgs['SkipPlugin']       = $true }
if ($SkipRegistry)     { $deployArgs['SkipRegistry']     = $true }

# ---------------------------------------------------------------------------

Write-Header "STEP 1 / 3 - Build"
& "$PSScriptRoot\Build.ps1" @buildArgs

Write-Header "STEP 2 / 3 - Package"
& "$PSScriptRoot\Package.ps1" @packageArgs

Write-Header "STEP 3 / 3 - Deploy"
& "$PSScriptRoot\Deploy.ps1" @deployArgs
