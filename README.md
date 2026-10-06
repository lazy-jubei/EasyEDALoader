# EasyEDALoader for Altium

Import EasyEDA/LCSC symbols, footprints and 3D models into Altium Designer.

Fork of [expired6978/EasyEDALoader](https://github.com/expired6978/EasyEDALoader), tested in **Altium Designer 17.1 and 26.10.1** under Wine.

Changes in this fork:

- AD26 (.NET 8/x64) and AD17 (.NET Framework 4.8/x86) builds.
- Fixed symbol parsing, pin layout, 3D model offsets and download handling.
- Fixed dialog ownership, deployment and schematic placement with undo/redo.
- Added AD17 search using Altium's native suppliers.

Build and test details: [AD26](AD26.md) · [AD17](AD17.md).

AD17 includes [Manufacturer Part Search](ManufacturerPartSearch.md), also available in [Altium-17-MPS](https://github.com/lazy-jubei/Altium-17-MPS).

## Usage

With a schematic active, open **EasyEDA Loader**, search for an LCSC part number such as `C2040`, tick the part, then choose **Add to Library**. Libraries are saved in `Documents/AltiumEE`. Disable **Place in schematic** to import without placing a component in the active sheet.

## Build and install

Prebuilt [AD17 and AD26 downloads](https://github.com/lazy-jubei/EasyEDALoader/releases/latest) are available. Extract the matching archive and run `Deploy.ps1 -AltiumVersion 17` or `Deploy.ps1 -AltiumVersion 26`; on macOS, use the included `tools/deploy-wine.py` with `--altium-version 17` or `26`.

Requires .NET SDK 8 or later and the matching Altium installation. SDK and DevExpress libraries are not bundled. Commands below default to AD26; [AD17 build instructions](AD17.md) use `AltiumVersion 17`.

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
