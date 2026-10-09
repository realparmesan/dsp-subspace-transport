# Subspace Transport

A BepInEx 5 plugin for Dyson Sphere Program. Research Subspace Logistics to transfer cargo instantly between Interstellar Logistics Stations without using Logistics Vessels.

The research has four levels. Transfers retain the normal vessel cargo capacity and station warper settings. Station energy use starts at 8x the vanilla trip cost, halves at each level, and reaches the vanilla cost at level 4. Instant transfer is disabled by default and must be enabled separately at both endpoint stations with the Subspace toggle in the station window. The same control is available when opening a remote station from the starmap. Station choices are saved with each game.

Subspace Transport targets vanilla Interstellar Logistics Station components only. It has no direct integration with Galactic Scale, More Mega Structure, Orbital Ring, or their mod-added structures and private/shared storage.

## Requirements

- .NET SDK 8 or newer
- Dyson Sphere Program installed
- BepInEx 5, LDBTool, and DSP Mod Save installed for DSP (declared as Thunderstore dependencies)
- `zip` command-line utility to create the package archive

## Build

Set `DSP_GAME_DIR` to the game install directory. If BepInEx is installed in a mod-manager profile rather than the game directory, set `DSP_BEPINEX_DIR` to that profile's `BepInEx` folder (the folder containing `core/`). If LDBTool is installed in a nonstandard location, set `DSP_LDBTOOL_DIR` to the folder containing `LDBTool.dll`.

```sh
export DSP_GAME_DIR="/path/to/steamapps/common/Dyson Sphere Program"
export DSP_BEPINEX_DIR="$DSP_GAME_DIR/BepInEx" # Change this if using a mod-manager profile
export DSP_LDBTOOL_DIR="$DSP_BEPINEX_DIR/plugins/LDBTool" # Change if LDBTool is in another folder
dotnet build SubspaceTransport.csproj --configuration Release
```

The output DLL is `bin/Release/net472/SubspaceTransport.dll`. To use it in a local mod-manager profile, copy it into that profile's `BepInEx/plugins/` folder.

## Create a Thunderstore package

With `DSP_GAME_DIR` (and, if needed, `DSP_BEPINEX_DIR`) set, run:

```sh
bash scripts/package.sh
```

This creates `dist/SubspaceTransport-0.3.0.zip`, with the manifest, readme, icon, and plugin DLL in Thunderstore's expected layout.

## Project files

- `Plugin.cs` — BepInEx entry point and patch lifecycle
- `SubspaceLogisticsTechnology.cs` — registers the repeatable research path
- `InstantTransportPatches.cs` — station dispatch, energy cost, and cargo transfer
- `StationSettings.cs` — per-station opt-in state saved per game
- `StationToggleUI.cs` — station window toggle for local and remote station views
- `SubspaceTransport.csproj` — .NET target and references to the local DSP, BepInEx, and LDBTool assemblies plus the DSP Mod Save API
- `manifest.json` — Thunderstore package metadata and loader dependencies
- `scripts/package.sh` — builds and creates a Thunderstore-ready zip
