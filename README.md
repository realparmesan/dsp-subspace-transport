# Wormhole Logistics

A BepInEx 5 plugin for Dyson Sphere Program. Research Subspace Logistics to transfer cargo instantly between Interstellar Logistics Stations without using Logistics Vessels.

Subspace Logistics appears on the main technology page after Interstellar Logistics System. It is a single research with the same cost as the first level of Dyson Sphere Stress System, and it leads to two further techs: Battery Sphere Engineering (same cost as Artificial Star) and Subspace Efficiency. Transfers retain the normal vessel cargo capacity and station energy settings. Station energy use starts at 8x the vanilla trip cost and halves with each of Subspace Efficiency's three levels, reaching the vanilla cost at level 3. Instant transfer is disabled by default and must be enabled separately at both endpoint stations with the **Subspace Transport** checkbox, either in the Interstellar Logistics Station's Station Settings tab or in the station's settings tab in the Control Panel. Station choices are saved with each game. Like vessels, wormhole transport only runs between stations on different planets; the game never pairs two stations on the same planet for remote supply and demand.

Instant shipments consume one Subspace Warper instead of Space Warpers. The sending station pays by default; if its Subspace Warper slot is empty, the receiving station pays instead, so a station can keep Subspace Warpers and pull cargo from senders that have none. Neither station needs Logistics Vessels, Space Warpers, or warp drive research for a wormhole transfer. The station's **Min. Load of Vessels** setting still applies: a station only dispatches once the supplying slot holds that share of a vessel's capacity, so wormhole cargo moves in batches. Subspace Warpers are assembled from 4 Space Warpers and 2 Graviton Lenses in an assembler (they are not craftable in the replicator). Each Interstellar Logistics Station has a dedicated Subspace Warper slot beneath its drone, vessel, and Space Warper slots, holding up to 100. The Control Panel shows the slot's count in the station list and the station inspector. Like the Space Warper slot, it fills from belts, from sorters, from a storage row containing Subspace Warpers, or by clicking it with Subspace Warpers in hand; clicking it with an empty hand takes them out. Subspace Warpers are excluded from instant cargo transfer to prevent them being shipped by the system that consumes them.

The mod's items (Battery Rocket and Subspace Warper) are on their own **Subspace** tab in the recipe and item pickers.

## Battery Sphere

Battery Sphere Engineering unlocks Battery Rockets. Each rocket uses a Small Carrier Rocket and an Energy Exchanger. In the Dyson Sphere editor (Y), use the **Megastructure type** selector to plan a Battery Sphere at a star; the star's Dyson Sphere must have no structures. Vertical-launch silos accept both Small Carrier Rockets and Battery Rockets; an empty silo follows whichever type is fed to it. Battery Rockets only launch at stars with a Battery Sphere planned, where they build the sphere's nodes, frames and shells like Carrier Rockets build a Dyson Sphere. The sphere's generated power charges accumulators instead of the grid, and the editor shows its output. A star can switch back to a Dyson Sphere once its sphere has no structures left.

Each completed project rocket contributes 1 MW of dedicated charging output. Accumulators store 270 MJ each, so one rocket produces one charged accumulator every 270 seconds while empty accumulators are available. Output from planned Battery Sphere projects across the cluster feeds a shared, bounded buffer; it does not consume ordinary Dyson Sphere output or interact with Ray Receivers.

To exchange batteries, enable Subspace Transport at participating Interstellar Logistics Stations and keep Subspace Warpers in their Subspace Warper slot. Configure an empty Accumulator slot as remote Supply and a full Accumulator slot as remote Demand. Any enabled station in the cluster can contribute or request batteries. Each empty-accumulator intake batch and each charged-accumulator delivery batch (up to 1,000 items) consumes one Subspace Warper and station energy at that station (the Battery Sphere has no station of its own, so a station receiving charged accumulators pays for their delivery), with the energy multiplier following Subspace Efficiency research. Project progress and the accumulator buffer are saved with the game.

Subspace Logistics targets vanilla Interstellar Logistics Station components only. It has no direct integration with Galactic Scale, Orbital Ring, or their mod-added structures and private/shared storage.

More Megastructures is optional and compatible. The two mods use separate tech IDs (this mod uses 1950-1952), separate replicator tabs, and separate structure selectors in the Dyson Sphere editor. A star can hold either a Battery Sphere or a More Megastructures structure; each selector asks you to switch the star back to a Dyson Sphere before changing it to the other mod's type.

**Save compatibility:** 0.6.0 changes the research IDs, so Subspace Logistics and Battery Sphere Engineering must be researched again in saves from earlier versions.

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
- `SubspaceLogisticsTechnology.cs` — registers the Subspace Logistics research
- `WormholeEfficiencyTechnology.cs` — registers the three-level Subspace Efficiency research and its energy multiplier
- `SubspaceWarperContent.cs` — registers the Subspace Warper and its recipe
- `IconLoader.cs` — loads the embedded icons in `assets/icons/` for mod items, recipes, techs, and the tab
- `BatterySphereContent.cs` — registers the Battery Sphere rocket and research unlock
- `BatterySphereManager.cs` — tracks independent per-star projects and accumulator exchange
- `BatterySphereEditorUI.cs` — megastructure type selector and project status in the Dyson Sphere editor
- `BatterySphereSiloFeed.cs` — lets silos accept both rocket types; the launch follows the star's sphere type
- `InstantTransportPatches.cs` — station dispatch, energy cost, and cargo transfer
- `StationSettings.cs` — per-station Subspace Transport setting and Subspace Warper slot, saved per game
- `SubspaceWarperSlotPatches.cs` — fills and empties the Subspace Warper slot (belts, sorters, storage rows, dismantling)
- `MoreMegaStructureCompat.cs` — optional More Megastructures interop, found by reflection
- `StationWindowUI.cs` — Subspace Transport settings row and Subspace Warper slot in the station window
- `ControlPanelUI.cs` — Subspace Warper rows and Subspace Transport checkbox in the Control Panel
- `SubspaceTransport.csproj` — .NET target and references to the local DSP, BepInEx, LDBTool, CommonAPI, and DSP Mod Save assemblies
- `manifest.json` — Thunderstore package metadata and loader dependencies
- `scripts/package.sh` — builds and creates a Thunderstore-ready zip
