#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $repo '_Shared.ps1')

$project = Join-Path $PSScriptRoot 'Setup.csproj'
[xml]$configuration = Get-Content -LiteralPath $project -Raw
$version = [string]$configuration.Project.PropertyGroup.Version
$assemblyVersion = [version]("$version.0")
$payloadFiles = @('EasyEDA-Loader.dll', 'EasyEDA-Loader.Ins', 'EasyEDA-Loader.rcs', 'Newtonsoft.Json.dll', 'EasyEDA-Loader.target.json')
$optionalFiles = @('EasyEDA-Loader.dll.config', 'Newtonsoft.Json.xml')
$stage = Join-Path ([IO.Path]::GetTempPath()) ('EasyEDALoader-setup-' + [guid]::NewGuid().ToString('N'))

try {
    $payload = Join-Path $stage 'payload'
    foreach ($target in '17', '26') {
        $directory = if ($target -eq '17') { 'dist-ad17' } else { 'dist' }
        $source = Join-Path $repo $directory
        Assert-AltiumTarget $source $target -RequireManifest
        $required = if ($target -eq '26') { $payloadFiles + 'EasyEDA-Loader.deps.json' } else { $payloadFiles }
        foreach ($name in $required) {
            if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
                throw "Missing AD$target payload file $name. Build and package AD$target first."
            }
        }
        $actual = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $source 'EasyEDA-Loader.dll')).Version
        $ins = Get-Content -LiteralPath (Join-Path $source 'EasyEDA-Loader.Ins') -Raw -Encoding UTF8
        if ($actual -ne $assemblyVersion -or $ins -notmatch ("(?m)^\s*Version\s*=\s*'Version " + [regex]::Escape($assemblyVersion.ToString()) + "'\s*$")) {
            throw "AD$target payload must be rebuilt and packaged at version $assemblyVersion."
        }

        $destination = Join-Path $payload $directory
        New-Item -ItemType Directory -Path $destination | Out-Null
        foreach ($name in $required + $optionalFiles) {
            $file = Join-Path $source $name
            if (Test-Path -LiteralPath $file -PathType Leaf) { Copy-Item -LiteralPath $file -Destination $destination }
        }
        foreach ($name in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
            Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $destination
        }
    }

    $archive = Join-Path $stage 'payload.zip'
    Compress-Archive -LiteralPath @((Join-Path $payload 'dist-ad17'), (Join-Path $payload 'dist')) -DestinationPath $archive
    $build = Join-Path $stage 'build'
    & $Dotnet build $project --configuration Release --output $build "-p:PayloadZip=$archive"
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed (exit $LASTEXITCODE)." }

    $release = Join-Path $repo 'release'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    $output = Join-Path $release "EasyEDALoader-Setup-$version.exe"
    Copy-Item -LiteralPath (Join-Path $build 'EasyEDALoader-Setup.exe') -Destination $output -Force
    Get-Item -LiteralPath $output
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
