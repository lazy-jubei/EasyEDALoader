#!/bin/bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
wine_root="${ALTIUM_WINE_ROOT:-$HOME/AltiumWine}"
altium_dir="${ALTIUM_INSTALL_DIR:-$wine_root/prefix/drive_c/Program Files/Altium/AD26}"
dotnet_cli="${DOTNET_CLI:-}"
if [ -z "$dotnet_cli" ]; then
    dotnet_cli="$(command -v dotnet || true)"
    [ -n "$dotnet_cli" ] || dotnet_cli="$wine_root/tools/dotnet-sdk/dotnet"
fi
[ -x "$dotnet_cli" ] || { echo 'Install .NET SDK 8 or later, or set DOTNET_CLI to its dotnet executable.' >&2; exit 1; }
"$dotnet_cli" build "$repo_dir/EasyEDA-Loader/EasyEDA-Loader.csproj" -c Release --nologo \
    "-p:AltiumInstallDir=$altium_dir" "-p:DevExpressVersion=${DEVEXPRESS_VERSION:-25.2}"
