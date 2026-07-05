# Motivations / Inspirations

Sourcing parts from JLCPCB can be a bit of a pain when you don't have the footprint, the symbol, or the model. 

If you're a user of KiCAD you're in luck as there's a nice project [easyeda2kicad.py](https://github.com/uPesy/easyeda2kicad.py) which will offline convert and pull models for you, and there's another script which integrates this script into KiCAD.

If you're an Altium user, you're stuck either running this script and importing via KiCAD import, or exporting the Altium files from EasyEDA and copying them manually into a footprint.

I used easyeda2kicad as a reference for this project as well as AtliumLibraryLoader script for reference on Altium APIs to manipulate adding parts to libraries

# Usage

Using the extension is pretty straight forward once it is installed, there will be a new Menu option `EasyEDA Loader` while a SchDoc is active, this will open a Modal Dialog which prompts the LCSC Part number e.g. "C2040". Tick which part you want to add after searching and press Add to Library

![Dialog](/Assets/Loader.PNG)

Hitting OK will automatically create `EasyEDA.pcblib` and `EasyEDA.schlib` if they don't already exist in `Documents/AltiumEE`, create the footprint, download the 3d model, create the symbol, add part info, map the footprint to the symbol, then place the component into the active schematic at the bottom left.

# Comparisons

Left EasyEDA, Right Altium after import

## Symbol

![Comparison of EasyEDA Symbol](/Assets/Compare-Symbol.png)

## Footprint

![Comparison of EasyEDA Footprint](/Assets/Compare-Footprint.png)

## 3D Model

![Comparison of EasyEDA 3D](/Assets/Compare-3D.png)

## Part Info

![Comparison of EasyEDA Part Info](/Assets/PartInfo-EEL.PNG)

# Building

You shouldn't need anything special to build, just .NET 4.8, Language v8.0, and probably assembly references to Altium's internal libraries.

The following Assembly references were made and can be found in

```
C:\Program Files\Altium\AD24\System
C:\Program Files\Altium\AD24\System\DotNet\DevExpress.Wpf
```

```
Altium.Controls
Altium.Controls.Skins
Altium.SDK
Altium.SDK.Interfaces
DevExpress.Data.v22.1
DevExpress.Mvvm.v22.1
DevExpress.Printing.v22.1.Core
DevExpress.Utils.v22.1
DevExpress.Xpf.Core.v22.1
DevExpress.Xpf.Grid.v22.1.Core
DevExpress.Xpf.Grid.v22.1
```

# Standalone

The standalone version is a simple WPF app that draws the primitives to a Canvas and was mainly used to validate without having to repeatedly re-launch Altium. Unfortunately doesn't load the step file, but will load the raw obj model, you can also use it to manually save the step or obj.

# Installation

There are two ways to install EasyEDA-Loader.

## Option 1: Install from a Release Archive (Recommended)

Download the latest release archive from the [Releases](https://github.com/your-org/EasyEDALoader/releases) page. The archive contains a ready-to-deploy `dist\` folder alongside `Deploy.ps1`.

1. Extract the archive to any location.
2. Open a PowerShell terminal in that directory.
3. Run the deploy script:

```powershell
.\Deploy.ps1
```

The script will:
- Automatically discover your Altium Designer installation (no hardcoded GUIDs).
- Copy plugin files into the correct `Extensions\EasyEDA-Loader` folder.
- Register the extension in `ExtensionsRegistry.xml`.
- Prompt if Altium Designer is currently running (use `-Force` to skip the prompt).

```powershell
.\Deploy.ps1 -Force      # Deploy even if Altium is running
.\Deploy.ps1 -SkipRegistry  # Copy files only, skip registry update
```

## Option 2: Build & Deploy from Source

If you have the source code, use the build script suite to build, package, and deploy in one step.

### Script Overview

| Script | Description |
|--------|-------------|
| `_Shared.ps1` | Shared helper functions (dot-sourced by other scripts, do not run directly) |
| `Build.ps1` | Builds the plugin (via MSBuild) and optionally the Standalone app (via dotnet) |
| `Package.ps1` | Stages built artifacts into `dist\`, excluding Altium SDK and DevExpress DLLs (provided at runtime) |
| `Deploy.ps1` | Copies `dist\` into the Altium Extensions folder and registers in `ExtensionsRegistry.xml` |
| `All.ps1` | Orchestrates Build, Package, and Deploy in sequence |

### Prerequisites

- Visual Studio 2022 or later with MSBuild (for plugin build)
- .NET SDK 6.0+ (for Standalone build, if using `-IncludeStandalone`)
- Altium Designer 24 installed on the target machine
- PowerShell 5.1+ (run as Administrator when deploying)

### Quick Start

```powershell
cd path\to\EasyEDALoader

# Build, package, and deploy in one command:
.\All.ps1

# Debug build:
.\All.ps1 -Configuration Debug

# Deploy even if Altium Designer is currently running:
.\All.ps1 -Force

# Also build the Standalone viewer app:
.\All.ps1 -IncludeStandalone
```

### Running Steps Individually

```powershell
# Step 1: Build the plugin (and optionally Standalone)
.\Build.ps1
.\Build.ps1 -Configuration Debug
.\Build.ps1 -IncludeStandalone

# Step 2: Stage files into dist\
.\Package.ps1
.\Package.ps1 -IncludeStandalone

# Step 3: Deploy into Altium Designer
.\Deploy.ps1
.\Deploy.ps1 -Force
.\Deploy.ps1 -SkipRegistry
```

### DLL Bundling Strategy

The build scripts follow the same deployment pattern as the AltiumMCP extension. Only third-party and polyfill DLLs that Altium Designer does **not** ship are bundled alongside the plugin:

**Bundled with the plugin:**
| DLL | Purpose |
|-----|---------|
| `EasyEDA-Loader.dll` | The plugin |
| `EasyEDA-Loader.Ins` | Server manifest |
| `EasyEDA-Loader.rcs` | Menu resources |
| `EasyEDA-Loader.dll.config` | Assembly binding redirects |
| `Microsoft.Bcl.AsyncInterfaces.dll` | Async/await polyfill |
| `Newtonsoft.Json.dll` | JSON serialization |
| `System.Buffers.dll` | Span\<T\> / Memory\<T\> |
| `System.IO.Pipelines.dll` | Stream pipe processing |
| `System.Memory.dll` | Memory\<T\> support |
| `System.Numerics.Vectors.dll` | SIMD Vector\<T\> |
| `System.Runtime.CompilerServices.Unsafe.dll` | Unsafe ref operations |
| `System.Threading.Tasks.Extensions.dll` | ValueTask support |
| `System.ValueTuple.dll` | ValueTuple support |

**NOT bundled (provided by Altium Designer at runtime):**
`Altium.SDK.dll`, `Altium.SDK.Interfaces.dll`, `Altium.Controls.dll`, `Altium.Controls.Skins.dll`, and all `DevExpress.*.dll` (v22.1).

## Known Issues
The 3D model is not places *quite* right, something is still different from the reported translation and the actual. See [EeFootprint3dModel](/EasyEDA-Loader/FootprintShapes/EeFootprint3dModel.cs) for more information and how and where it retrieves model info from.
