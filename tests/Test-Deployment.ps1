$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('EasyEDA-deploy-test-' + [guid]::NewGuid().ToString('N'))
$extensions = Join-Path $testRoot 'Extensions'
New-Item -ItemType Directory -Path $extensions -Force | Out-Null
$registry = Join-Path $extensions 'ExtensionsRegistry.xml'
'<Extensions><Item HRID="KeepMe"><Path>C:\KeepMe</Path></Item></Extensions>' | Set-Content $registry
$cultureBefore = [System.Threading.Thread]::CurrentThread.CurrentCulture
try {
    [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('fr-FR')
    & (Join-Path $repo 'Deploy.ps1') -ExtensionsRoot $extensions -Force
    [xml]$xml = Get-Content $registry
    $item = $xml.SelectSingleNode('/Extensions/Item[@HRID="EasyEDA-Loader"]')
    if (-not $item -or $item.ReleasedDate -notmatch '^\d+\.\d+$') { throw 'Locale corrupted the release date.' }
    if (-not $xml.SelectSingleNode('/Extensions/Item[@HRID="KeepMe"]')) { throw 'Existing extension was lost.' }
    $stale = Join-Path $extensions 'EasyEDA-Loader/stale.dll'
    'old' | Set-Content $stale
    $item.Version = '1.0.1.0'
    $xml.Save($registry)
    & (Join-Path $repo 'Deploy.ps1') -ExtensionsRoot $extensions -Force
    [xml]$xml = Get-Content $registry
    if ($xml.SelectNodes('/Extensions/Item[@HRID="EasyEDA-Loader"]').Count -ne 1) { throw 'Duplicate registry entry.' }
    if ($xml.SelectSingleNode('/Extensions/Item[@HRID="EasyEDA-Loader"]').Version -ne '1.2.0.0') { throw 'Existing registry version was not updated.' }
    if (Test-Path $stale) { throw 'Stale dependencies were not removed.' }
    '<invalid' | Set-Content $registry
    $before = (Get-FileHash (Join-Path $extensions 'EasyEDA-Loader/EasyEDA-Loader.dll')).Hash
    $rejected = $false
    try { & (Join-Path $repo 'Deploy.ps1') -ExtensionsRoot $extensions -Force } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid registry accepted.' }
    if ((Get-FileHash (Join-Path $extensions 'EasyEDA-Loader/EasyEDA-Loader.dll')).Hash -ne $before) { throw 'Failed deploy overwrote the plugin.' }
    Write-Host 'PASS: PowerShell installation, update, invariant dates and invalid-registry checks.'
} finally {
    [System.Threading.Thread]::CurrentThread.CurrentCulture = $cultureBefore
    Remove-Item $testRoot -Recurse -Force
}
