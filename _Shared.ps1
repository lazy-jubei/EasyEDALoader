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

# Returns the path to the Extensions folder of the most-recently-modified
# Altium Designer installation that has an Extensions subfolder, or $null.
function Find-AltiumExtRoot {
    $altiumBase = 'C:\ProgramData\Altium'
    if (-not (Test-Path $altiumBase)) { return $null }

    $candidates = Get-ChildItem $altiumBase -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^Altium Designer \{[0-9A-Fa-f\-]+\}$' } |
        Where-Object { Test-Path (Join-Path $_.FullName 'Extensions') } |
        Sort-Object LastWriteTime -Descending

    if (-not $candidates) { return $null }

    if (@($candidates).Count -gt 1) {
        Write-Warn "Multiple Altium Designer installations found with an Extensions folder:"
        $candidates | ForEach-Object { Write-Info "  $($_.Name)" }
        Write-Info "Using most recently modified: $($candidates[0].Name)"
    }

    return Join-Path $candidates[0].FullName 'Extensions'
}
