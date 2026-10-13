# Wormhole Logistics

A BepInEx 5 plugin for Dyson Sphere Program. Research Wormhole Logistics to transfer cargo instantly between Interstellar Logistics Stations without using Logistics Vessels.

Wormhole Logistics appears on the main technology page after Interstellar Logistics System; Battery Sphere Engineering follows it. The research has four levels. Transfers retain the normal vessel cargo capacity and station energy settings. Station energy use starts at 8x the vanilla trip cost, halves at each level, and reaches the vanilla cost at level 4. Instant transfer is disabled by default and must be enabled separately at both endpoint stations with the **Wormhole Transport** checkbox in the Interstellar Logistics Station's Station Settings tab. Station choices are saved with each game.

Instant shipments consume one Subspace Warper from the station initiating the transfer, instead of Space Warpers. Subspace Warpers are assembled from 4 Space Warpers and 2 Graviton Lenses in an assembler (they are not craftable in the replicator). Each Interstellar Logistics Station has a dedicated Subspace Warper slot beneath its drone, vessel, and Space Warper slots, holding up to 100. Like the Space Warper slot, it fills from belts, from sorters, from a storage row containing Subspace Warpers, or by clicking it with Subspace Warpers in hand; clicking it with an empty hand takes them out. Subspace Warpers are excluded from instant cargo transfer to prevent them being shipped by the system that consumes them.

The mod's items (Battery Rocket and Subspace Warper) are on their own **Wormhole Logistics** tab in the recipe and item pickers.

## Battery Sphere

Battery Sphere Engineering unlocks Battery Rockets. Each rocket uses a Small Carrier Rocket, 4 Energy Exchangers, and 2 Subspace Warpers. In the Dyson Sphere editor (Y), use the **Megastructure type** selector to plan a Battery Sphere at a star; the star's Dyson Sphere must have no structures. Use the rocket toggle in a vertical-launch silo's window to load Battery Rockets; the silo must be empty and idle before switching. Battery Rockets only launch at stars with a Battery Sphere planned. They are captured by a separate, mod-managed sphere project for that star and do not add nodes or shells to the star's ordinary Dyson Sphere. The editor shows the project's rocket count and output. Switching a star back to a Dyson Sphere pauses its Battery Sphere output and keeps its rockets for when it is planned again.

Each completed project rocket contributes 1 MW of dedicated charging output. Accumulators store 270 MJ each, so one rocket produces one charged accumulator every 270 seconds while empty accumulators are available. Output from planned Battery Sphere projects across the cluster feeds a shared, bounded buffer; it does not consume ordinary Dyson Sphere output or interact with Ray Receivers.

To exchange batteries, enable Wormhole Transport at participating Interstellar Logistics Stations and keep Subspace Warpers in their Subspace Warper slot. Configure an empty Accumulator slot as remote Supply and a full Accumulator slot as remote Demand. Any enabled station in the cluster can contribute or request batteries. Each empty-accumulator intake batch and each charged-accumulator delivery batch (up to 1,000 items) consumes one Subspace Warper and station energy at that station, with the energy multiplier following Wormhole Logistics research. Project progress and the accumulator buffer are saved with the game.

Wormhole Logistics targets vanilla Interstellar Logistics Station components only. It has no direct integration with Galactic Scale, Orbital Ring, or their mod-added structures and private/shared storage.

More Megastructures is optional and compatible. The two mods use separate tech IDs (this mod uses 1950 and 1951), separate replicator tabs, and separate structure selectors in the Dyson Sphere editor. A star can hold either a Battery Sphere or a More Megastructures structure; each selector asks you to switch the star back to a Dyson Sphere before changing it to the other mod's type.

**Save compatibility:** 0.6.0 changes the research IDs, so Wormhole Logistics and Battery Sphere Engineering must be researched again in saves from earlier versions.

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

This creates `dist/SubspaceTransport-0.6.0.zip`, with the manifest, readme, icon, and plugin DLL in Thunderstore's expected layout.

## Project files

- `Plugin.cs` — BepInEx entry point and patch lifecycle
- `SubspaceLogisticsTechnology.cs` — registers the four-level research path
- `SubspaceWarperContent.cs` — registers the Subspace Warper and its recipe
- `IconLoader.cs` — loads the embedded icons in `assets/icons/` for mod items, recipes, techs, and the tab
- `BatterySphereContent.cs` — registers the Battery Sphere rocket and research unlock
- `BatterySphereManager.cs` — tracks independent per-star projects and accumulator exchange
- `BatterySphereEditorUI.cs` — megastructure type selector and project status in the Dyson Sphere editor
- `BatterySphereSiloUI.cs` — adds the silo rocket-type selector
- `InstantTransportPatches.cs` — station dispatch, energy cost, and cargo transfer
- `StationSettings.cs` — per-station Wormhole Transport setting and Subspace Warper slot, saved per game
- `SubspaceWarperSlotPatches.cs` — fills and empties the Subspace Warper slot (belts, sorters, storage rows, dismantling)
- `MoreMegaStructureCompat.cs` — optional More Megastructures interop, found by reflection
- `StationWindowUI.cs` — Wormhole Transport settings row and Subspace Warper slot in the station window
- `SubspaceTransport.csproj` — .NET target and references to the local DSP, BepInEx, LDBTool, CommonAPI, and DSP Mod Save assemblies
- `manifest.json` — Thunderstore package metadata and loader dependencies
- `scripts/package.sh` — builds and creates a Thunderstore-ready zip
