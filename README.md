# Wormhole Logistics

A BepInEx 5 plugin for Dyson Sphere Program. Research Wormhole Logistics to transfer cargo instantly between Interstellar Logistics Stations without using Logistics Vessels.

The research has four levels. Transfers retain the normal vessel cargo capacity and station energy settings. Station energy use starts at 8x the vanilla trip cost, halves at each level, and reaches the vanilla cost at level 4. Instant transfer is disabled by default and must be enabled separately at both endpoint stations with the Wormhole toggle in the station window. The same control is available when opening a remote station from the starmap. Station choices are saved with each game.

Instant shipments consume one Hyperdrive Module from the station initiating the transfer, instead of Space Warpers. Modules are assembled from 4 Space Warpers and 2 Graviton Lenses. The module is excluded from instant cargo transfer to prevent it being shipped by the system that consumes it.

## Battery Sphere

Battery Sphere Engineering unlocks Battery Sphere Rockets. Each rocket uses a Small Carrier Rocket, 4 Energy Exchangers, and 2 Hyperdrive Modules. Use the Battery Sphere toggle in a vertical-launch silo's window to choose its rocket type; the silo must be empty and idle before switching. Launch them while in the target star system. The rockets are captured by a separate, mod-managed sphere project for that star; they do not add nodes or shells to the star's ordinary Dyson Sphere. The Dyson Sphere editor displays project rocket count and output.

Each completed project rocket contributes 1 MW of dedicated charging output. Accumulators store 270 MJ each, so one rocket produces one charged accumulator every 270 seconds while empty accumulators are available. Output from Battery Sphere projects across the cluster feeds a shared, bounded buffer; it does not consume ordinary Dyson Sphere output or interact with Ray Receivers.

To exchange batteries, enable Wormhole Logistics at participating Interstellar Logistics Stations and keep Hyperdrive Modules in their storage. Configure an empty Accumulator slot as remote Supply and a full Accumulator slot as remote Demand. Any enabled station in the cluster can contribute or request batteries. Each empty-accumulator intake batch and each charged-accumulator delivery batch (up to 1,000 items) consumes one Hyperdrive Module and station energy at that station, with the energy multiplier following Wormhole Logistics research. Project progress and the accumulator buffer are saved with the game.

Wormhole Logistics targets vanilla Interstellar Logistics Station components only. It has no direct integration with Galactic Scale, More Mega Structures, Orbital Ring, or their mod-added structures and private/shared storage.

## Requirements

- .NET SDK 8 or newer
- Dyson Sphere Program installed
- BepInEx 5, LDBTool, CommonAPI, and DSP Mod Save installed for DSP (declared as Thunderstore dependencies)
- `zip` command-line utility to create the package archive

## Build

Set `DSP_GAME_DIR` to the game install directory. The build detects both `DSPGAME_Data` and `DSP_Data`. If BepInEx is installed in a mod-manager profile rather than the game directory, set `DSP_BEPINEX_DIR` to that profile's `BepInEx` folder (the folder containing `core/`). If LDBTool is installed in a nonstandard location, set `DSP_LDBTOOL_DIR` to the folder containing `LDBTool.dll`.

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

This creates `dist/SubspaceTransport-0.5.2.zip`, with the manifest, readme, icon, and plugin DLL in Thunderstore's expected layout.

## Project files

- `Plugin.cs` — BepInEx entry point and patch lifecycle
- `SubspaceLogisticsTechnology.cs` — registers the four-level research path
- `HyperdriveModuleContent.cs` — registers the Hyperdrive Module and its recipe
- `BatterySphereContent.cs` — registers the Battery Sphere rocket and research unlock
- `BatterySphereManager.cs` — tracks independent per-star projects and accumulator exchange
- `BatterySphereSiloUI.cs` — adds the silo rocket-type selector
- `InstantTransportPatches.cs` — station dispatch, energy cost, and cargo transfer
- `StationSettings.cs` — per-station opt-in state saved per game
- `StationToggleUI.cs` — station window toggle for local and remote station views
- `SubspaceTransport.csproj` — .NET target and references to the local DSP, BepInEx, LDBTool, CommonAPI, and DSP Mod Save assemblies
- `manifest.json` — Thunderstore package metadata and loader dependencies
- `scripts/package.sh` — builds and creates a Thunderstore-ready zip
