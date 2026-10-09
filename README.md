# Subspace Transport

A minimal BepInEx 5 plugin starter for Dyson Sphere Program. The plugin currently only logs that it has loaded; it does not change gameplay yet.

## Requirements

- .NET SDK 8 or newer
- Dyson Sphere Program installed
- BepInEx 5 installed for DSP (the Thunderstore package is declared as a dependency)
- `zip` command-line utility to create the package archive

## Build

Set `DSP_GAME_DIR` to the game install directory. If BepInEx is installed in a mod-manager profile rather than the game directory, set `DSP_BEPINEX_DIR` to that profile's `BepInEx` folder (the folder containing `core/`).

```sh
export DSP_GAME_DIR="/path/to/steamapps/common/Dyson Sphere Program"
export DSP_BEPINEX_DIR="$DSP_GAME_DIR/BepInEx" # Change this if using a mod-manager profile
dotnet build SubspaceTransport.csproj --configuration Release
```

The output DLL is `bin/Release/net472/SubspaceTransport.dll`. To use it in a local mod-manager profile, copy it into that profile's `BepInEx/plugins/` folder.

## Create a Thunderstore package

With `DSP_GAME_DIR` (and, if needed, `DSP_BEPINEX_DIR`) set, run:

```sh
bash scripts/package.sh
```

This creates `dist/SubspaceTransport-0.1.0.zip`, with the manifest, readme, icon, and plugin DLL in Thunderstore's expected layout.

## Project files

- `Plugin.cs` — BepInEx entry point and startup log
- `SubspaceTransport.csproj` — .NET target and references to the local DSP/BepInEx assemblies
- `manifest.json` — Thunderstore package metadata and loader dependency
- `scripts/package.sh` — builds and creates a Thunderstore-ready zip
