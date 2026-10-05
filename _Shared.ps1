# Shared helpers - dot-source this file; do not run directly.

function Write-Header([string]$Text) {
    Write-Host ""
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "  $('-' * $Text.Length)" -ForegroundColor DarkGray
}
function Write-Ok([string]$Text)   { Write-Host "  [OK]   $Text" -ForegroundColor Green  }
function Write-Info([string]$Text) { Write-Host "  [ ]   $Text" -ForegroundColor Gray   }
function Write-Warn([string]$Text) { Write-Host "  [!!]  $Text" -ForegroundColor Yellow }
function Write-Fail([string]$Text) { Write-Host "  [ERR] $Text" -ForegroundColor Red    }

function Invoke-Cmd([string]$Exe, [string[]]$ArgList) {
    Write-Info "$([System.IO.Path]::GetFileName($Exe)) $($ArgList -join ' ')"
    $result = & $Exe @ArgList 2>&1
    if ($LASTEXITCODE -ne 0) {
        $result | Write-Host -ForegroundColor DarkGray
        throw "Command failed (exit $LASTEXITCODE)"
    }
    $result | Where-Object { $_ -match 'error|warning' } |
        ForEach-Object { Write-Host "     $_" -ForegroundColor DarkGray }
}

function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild `
                      -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
                  Select-Object -First 1
        if ($vsPath -and (Test-Path $vsPath)) { return $vsPath }
    }

    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    $inPath = Get-Command 'MSBuild.exe' -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }

    return $null
}

function Assert-AltiumTarget([string]$Directory, [string]$AltiumVersion, [switch]$RequireManifest) {
    $manifestPath = Join-Path $Directory 'EasyEDA-Loader.target.json'
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        if ($RequireManifest -or $AltiumVersion -eq '17') { throw "Missing target manifest in $Directory. Rebuild for AD$AltiumVersion." }
        return # Older AD26 packages predate the target manifest.
    }
    $target = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $framework = if ($AltiumVersion -eq '17') { 'net48' } else { 'net8.0-windows' }
    $architecture = if ($AltiumVersion -eq '17') { 'x86' } else { 'x64' }
    if ($target.altiumVersion -ne $AltiumVersion -or $target.framework -ne $framework -or $target.architecture -ne $architecture) {
        throw "Package target does not match AD$AltiumVersion ($framework/$architecture). Rebuild with the matching -AltiumVersion."
    }
}

# Refuse ambiguous installations rather than deploying to the most recently modified one.
function Find-AltiumExtRoot([string]$ExtensionsRoot) {
    if ($ExtensionsRoot) {
        if (-not (Test-Path (Join-Path $ExtensionsRoot 'ExtensionsRegistry.xml'))) {
            throw "No ExtensionsRegistry.xml in $ExtensionsRoot"
        }
        return (Resolve-Path $ExtensionsRoot).Path
    }
    $altiumBase = 'C:\ProgramData\Altium'
    if (-not (Test-Path $altiumBase)) { return $null }
    $candidates = @(Get-ChildItem $altiumBase -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^Altium Designer \{[0-9A-Fa-f\-]+\}$' } |
        ForEach-Object { Join-Path $_.FullName 'Extensions' } |
        Where-Object { Test-Path (Join-Path $_ 'ExtensionsRegistry.xml') })
    if ($candidates.Count -gt 1) { throw 'Multiple Altium installations found. Select one with -ExtensionsRoot.' }
    if ($candidates.Count -eq 0) { return $null }
    return $candidates[0]
}
