#!/bin/bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
altium_version="${ALTIUM_VERSION:-26}"
case "$altium_version" in
    17) default_wine_root="$HOME/AltiumWine17"; install_subdir='Program Files (x86)'; default_devexpress='15.2' ;;
    26) default_wine_root="$HOME/AltiumWine"; install_subdir='Program Files'; default_devexpress='25.2' ;;
    *) echo 'ALTIUM_VERSION must be 17 or 26.' >&2; exit 1 ;;
esac
wine_root="${ALTIUM_WINE_ROOT:-$default_wine_root}"
altium_dir="${ALTIUM_INSTALL_DIR:-$wine_root/prefix/drive_c/$install_subdir/Altium/AD$altium_version}"
dotnet_cli="${DOTNET_CLI:-}"
if [ -z "$dotnet_cli" ]; then
    dotnet_cli="$(command -v dotnet || true)"
    [ -n "$dotnet_cli" ] || dotnet_cli="$wine_root/tools/dotnet-sdk/dotnet"
    [ -x "$dotnet_cli" ] || dotnet_cli="$HOME/AltiumWine/tools/dotnet-sdk/dotnet"
fi
[ -x "$dotnet_cli" ] || { echo 'Install .NET SDK 8 or later, or set DOTNET_CLI to its dotnet executable.' >&2; exit 1; }
"$dotnet_cli" build "$repo_dir/EasyEDA-Loader/EasyEDA-Loader.csproj" -c Release --nologo \
    "-p:AltiumVersion=$altium_version" "-p:AltiumInstallDir=$altium_dir" "-p:DevExpressVersion=${DEVEXPRESS_VERSION:-$default_devexpress}"
