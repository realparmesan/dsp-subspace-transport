# Playtest bug log — 2026-10-13

Found while playtesting v0.5.3. Each entry is written so it can be worked on
independently. Where an earlier note was later changed during testing, only the
final decision is recorded here.

New icons for these fixes are in `assets/icons/` (all 80×80 RGBA PNG):

| File | Used for |
| --- | --- |
| `wormhole_logistics_tech.png` | Wormhole Logistics tech icon (white glyph, vanilla tech-icon format) |
| `wormhole_efficiency_tech.png` | New Wormhole Efficiency tech icon (variant of the Wormhole Logistics glyph; see #10) |
| `battery_sphere_engineering_tech.png` | Battery Sphere Engineering tech icon (white glyph) |
| `subspace_warper.png` | Subspace Warper item (violet recolour of the vanilla Space Warper) |
| `battery_rocket.png` | Battery Rocket item (charcoal recolour of the Small Carrier Rocket) |
| `battery_rocket_alt_green.png` | Alternative Battery Rocket icon with green glow, if charcoal is too dark in slots |
| `stellar_hyper_mass_inducer_tech.png` | Future Stellar Hyper Mass Inducer tech icon (see REQUIREMENTS.md) |
| `hyper_mass_rocket.png` | Future hyper-mass rocket item (crimson recolour of the Small Carrier Rocket) |

**Loading note:** `scripts/package.sh` only ships the DLL, `manifest.json`,
`README.md` and `icon.png`, so the icons won't reach players as loose files.
Embed them (`<EmbeddedResource Include="assets/icons/*.png" />` in the csproj),
load them with `Texture2D.LoadImage`, build a `Sprite`, and assign it to each
proto's icon after the protos are registered. Currently the protos reuse
vanilla `IconPath`s (`HyperdriveModuleContent.cs`, `BatterySphereContent.cs`,
`SubspaceLogisticsTechnology.cs`), which is why they look identical to vanilla.

---

## 1. Tech tree icons are reused from vanilla

Wormhole Logistics and Battery Sphere Engineering both reuse the vanilla
technology icon. Use `wormhole_logistics_tech.png` and
`battery_sphere_engineering_tech.png`.

## 2. Rename "Hyperdrive Module" to "Subspace Warper" everywhere

The recipe unlocked by Wormhole Logistics shows its raw string key
(`SubspaceTransport_HyperdriveModule` / `…Recipe`) instead of a readable name,
so the localisation strings aren't registered or resolved
(`HyperdriveModuleContent.cs`). Check `BatterySphereContent.cs` for the same
problem.
Rename the item to **Subspace Warper** everywhere: item name, recipe name,
description, tech unlock text, station UI and code/README references. Give it
`subspace_warper.png`, because it currently looks identical to the vanilla
Space Warper.

## 3. Rename the battery sphere rocket to "Battery Rocket"

Replace the long name (from `SubspaceTransport_BatterySphereRocket`) with
**Battery Rocket**, and use `battery_rocket.png`. Its current placement in the
replicator is fine until #9 moves it.

## 4. Wormhole Transport row in Interstellar Station settings

In an **Interstellar** Logistics Station's Station Settings panel, add a
**Wormhole Transport** row with an enable/disable checkbox. Do not add it to
Planetary Logistics Stations.

## 5. Move the existing wormhole toggle into Station Settings

The interstellar station window already has a wormhole on/off button at the top
(`StationToggleUI.cs`). Remove that standalone button and drive the same
setting from the Station Settings row in #4, so all station toggles live in
one place. Don't keep both.

## 6. Dedicated Subspace Warper slot in the Interstellar Station

The station shows hard-coded slots for logistics drones, interstellar logistics
vessels and Space Warpers. Add a **Subspace Warper** slot:

- On a new row beneath those three; make the panel taller to fit it.
- A dedicated slot outside the five main storage rows, like the Space Warper
  slot.
- Shows the current count.
- Fills automatically when Subspace Warpers are belted into the station, or
  taken from a storage row, the same way Space Warpers are, since wormhole
  transport consumes them.

## Resolution notes

#7 and #8 were implemented **without** a More Megastructures dependency (decided 2026-10-14): the
Dyson Sphere editor has the mod's own *Megastructure type* selector (Dyson Sphere / Battery Sphere,
with Stellar Hyper Mass to follow), and the new items sit on the mod's own *Wormhole Logistics* tab
(Battery Rocket row 1, Subspace Warper row 2). More Megastructures stays an optional, compatible mod: tech IDs moved
to 1950/1951 (it uses 1918-1931 and ties recipes to 1919), and a star can't be both a Battery Sphere
and a More Megastructures structure.

## 7. Can't choose a megastructure type in the Dyson Sphere editor (Y)

Pressing Y opens the editor and some "battery sphere project" text appears,
but there's no way to select the battery sphere or switch between sphere
types. Fix:

- Add a **hard dependency** on the More Megastructures plugin
  (`[BepInDependency]` plus manifest).
- Register entries in its **Plan Megastructure** side tab, with **Battery
  Sphere** first. The Stellar Hyper Mass sphere will be the second entry later.
- Let the player switch sphere types there instead of overloading the vanilla
  editor.

## 8. Move the new items onto the More Megastructures tab

To keep new content separate from vanilla:

- **Battery Rocket:** first row, directly after the Crystal Reconstructed
  Carrier Rocket.
- **Subspace Warper:** second row.

## 9. Remove the Subspace Warper from the replicator

It's currently listed under *Buildings* in the Icarus replicator. Remove it
from the replicator completely; it should only appear on the More
Megastructures tab (#8). This replaces earlier notes asking for it to sit
under the replicator's *Items* category or below the Space Warper recipes.

## 10. Wormhole Logistics becomes a single-level tech that branches

Remove the multiple levels from Wormhole Logistics (`MaximumLevel = 4` in
`SubspaceLogisticsTechnology.cs`). Researching it unlocks two new techs:

- **Battery Sphere Engineering**: prerequisite is Wormhole Logistics. Its
  research cost matches **Artificial Star**: 432,000 hashes, 1,200 each of
  Electromagnetic, Energy, Structure and Information Matrix. Copy the real
  values from the vanilla Artificial Star tech rather than hard-coding these.
- **Wormhole Efficiency** (new): prerequisite is Wormhole Logistics. Use
  `wormhole_efficiency_tech.png`. It should take over the energy-cost
  reduction that the old extra levels provided (`GetEnergyMultiplier`).

Also raise Wormhole Logistics' own research cost to roughly the **Dyson Sphere
Stress System** tech (research cost only, not the recipe cost). Take the exact
figures from the game data. Lay out the tree so Battery Sphere Engineering and
Wormhole Efficiency both branch off Wormhole Logistics without overlapping
(Battery Sphere Engineering currently overlaps it slightly; see
REQUIREMENTS.md).

## 11. Control panel: Subspace Warper rows

In the Control Panel (not the individual station window; #6 covers that):

- **Left side:** the list currently has one row each for Logistics Drones,
  Interstellar Logistics Vessels and Space Warpers. Add a fourth row for
  **Subspace Warpers**.
- **Right side:** the compact station summary mirrors the station window with
  three icons. Add a row below them for Subspace Warpers, as in the station
  window.

## 12. Control panel: Wormhole Transport toggle

Add the Wormhole Transport enable/disable checkbox to the Control Panel's
settings tab on the right, directly under the **outgoing integration count**
row of Station Settings. This is the same place as in the Interstellar Station
window. It must read and write the same per-station setting as #4 and #5
(`StationSettings.cs`), not a second copy. Not yet confirmed whether #4 or #5
are implemented at all, so verify first.

## 13. Wormhole transport does not move any items

Test setup: two Interstellar Logistics Stations.

- Station A: Local and Remote **provider** for the item.
- Station B: Local and Remote **receiver** for the item.
- Both have Wormhole Transport enabled and hold Subspace Warpers and the
  items' components. Per the design, only the sending station needs Subspace
  Warpers. The receiver doesn't consume any, and it isn't.

No items move between the stations at all, so neither the wormhole path nor
normal vessel flow appears to be firing. Investigate `InstantTransportPatches.cs`
and `StationSettings.cs`. Check:

- whether the toggle value is actually read at transfer time;
- whether the patch targets and conditions are ever reached (add logging);
- whether the Subspace Warper item is found in the station's dedicated slot
  (#6), since the module is currently stored under its old Hyperdrive Module
  identity;
- whether the stations pair up for an instant transfer at all (both ends must
  have Wormhole Transport enabled, per REQUIREMENTS.md).
