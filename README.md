# EasyEDALoader for Altium 26

Import EasyEDA/LCSC symbols, footprints and 3D models into Altium Designer.

This local fork of [expired6978/EasyEDALoader](https://github.com/expired6978/EasyEDALoader) targets **Altium 26.10.1**, using its .NET 8 runtime and DevExpress 25.2 libraries. Compatibility changes and testing are recorded in [AD26.md](AD26.md).

## Usage

With a schematic active, open **EasyEDA Loader**, search for an LCSC part number such as `C2040`, tick the part, then choose **Add to Library**. Libraries are saved in `Documents/AltiumEE`. Disable **Place in schematic** to import without placing a component in the active sheet.

## Build and install

Requires .NET SDK 8 or later and an installed copy of Altium 26. SDK and DevExpress references are read directly from the installation; they are not bundled with the plugin.

On Windows, use PowerShell 5.1 or later:

```powershell
.\All.ps1 -AltiumInstallDir 'C:\Program Files\Altium\AD26'
```

For the existing macOS Wine installation, with PowerShell 7 (`pwsh`) and Python 3 available:

```sh
./tools/build-macos.sh
pwsh -NoProfile -File Package.ps1
python3 tools/deploy-wine.py
```

`ALTIUM_INSTALL_DIR` overrides the installation path on macOS. When multiple installations exist, select the extension directory using `-ExtensionsRoot` on Windows or `--extensions-root` on macOS.

Restart Altium after installation. Deployment keeps backups of the previous extension and registry. `release/EasyEDALoader-ad26.zip` contains the plugin and deployment scripts; extract it and run `Deploy.ps1` on Windows.

The scripts can also run separately: `Build.ps1`, `Package.ps1`, then `Deploy.ps1`. Add `-IncludeStandalone` to the build and package steps for the standalone WPF preview app.

Based on the original author's work, with conversion references from [easyeda2kicad.py](https://github.com/uPesy/easyeda2kicad.py).
