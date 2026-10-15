# Subspace Logistics and Stellar Hyper Mass Requirements

## Purpose

Document the feature requirements so implementation can continue in a later
session. This is a requirements note, not an implementation plan or a claim
that every described feature is already present.

## Current project state

- The project currently registers Subspace Logistics (a single-level research),
  Subspace Efficiency (three levels, reducing station energy use) and Battery
  Sphere Engineering. The mod's items sit on their own **Subspace** tab in the
  recipe and item pickers.
- Instant transfers travel through a wormhole into subspace; "Subspace" names
  the mod's research, items and tab, and **Subspace Transport** names the
  per-station toggle.
- A Battery Sphere is a real Dyson Sphere at a star planned as a Battery Sphere:
  Battery Rockets build its nodes, frames and shells, and its generated power
  charges accumulators.
- Stellar Hyper Mass Inducer (1953), Stellar Hyper Mass Amplification (2951,
  50 levels, on the Upgrades tab; Subspace Efficiency is 2950 there too), the Hyper Mass Rocket (11998) and the Hyper Mass Injector
  project are implemented (2026-10-15). See "Decisions" below.
- Subspace Logistics, Subspace Efficiency and Battery Sphere Engineering each
  have their own technology icon, and their tree nodes no longer overlap.

## Implementation priority

Implement and stabilize **Battery Sphere** and **instant Subspace Logistics**
first. Defer the Stellar Hyper Mass Inducer research, rocket, sphere category,
and star-conversion feature until these existing features are working as
intended.

### Instant transport and station controls

- Subspace Transport must be explicitly toggleable at each participating
  Interstellar Logistics Station. Both the sending and receiving stations must
  have Subspace Transport enabled for an instant transfer.
- Consume exactly one **Subspace Warper** from the sending station for each
  successful instant shipment. Do not consume a module when a shipment fails
  or no items transfer.
- Make both vanilla **Space Warpers** and the mod's **Subspace Warpers**
  visible as distinct items in the Interstellar Logistics Station's inventory
  or logistics UI, so players can inspect and manage the old and new items.
  Subspace Warpers must remain usable as the instant-transfer fuel described
  above rather than being hidden or represented as Space Warpers.
- Allow the player to enable or disable Subspace Transport for a station while
  off-planet both in the Interstellar Logistics Station window and in
  **Logistics Overview**.
- In Logistics Overview, put the toggle in the selected station's details
  panel. It should identify the selected station and show its current Subspace
  Transport setting.

### Battery Sphere

- Keep Battery Sphere Rockets and their charging output separate from ordinary
  Dyson Sphere stars and from the planned Hyper Mass Injector project.
- Make the Battery Sphere rocket selector and project-status UI usable and
  understandable as part of this first implementation phase.
- The Battery Rocket recipe is one Small Carrier Rocket plus one Energy
  Exchanger. Preserve it, accumulator charging, station-exchange behavior, and
  saved project/buffer state unless subsequent requirements explicitly change
  them.

## Requested science-tree work

- Give each new technology its own distinctive icon, designed to fit the
  existing Dyson Sphere Program science-icon style and to represent that
  technology's subject. Done for the current technologies; still required for
  Stellar Hyper Mass Inducer.
- Add a visible research node named **Stellar Hyper Mass Inducer** for the new
  hyper-mass star-conversion feature.
- Keep the Battery Sphere technology and project separate from the new
  hyper-mass feature.

## Stellar Hyper Mass Inducer feature

### Research

- Introduce a technology named **Stellar Hyper Mass Inducer**.
- Research should be priced roughly on par with **Vein Utilization**.
- Technology level should govern the star's resulting luminosity: the initial
  target is approximately **2.5**, increasing by approximately **0.1 for each
  subsequent research level**.
- Research must enable the player-facing hyper-mass operation and its
  technology-dependent result.

### Rocket and sphere project

- Add a new Dyson Sphere rocket for the hyper-mass project.
- Its recipe should use some of the mod's warp modules together with typical
  Dyson Sphere rocket components.
- Add a **Hyper Mass Injector** Dyson Sphere category/project, distinct from
  both the vanilla Dyson Sphere and the existing Battery Sphere project.
- When creating a sphere/project, the player should be able to choose which
  category it belongs to, following the kind of project selection flow used by
  More Mega Structures.
- The hyper-mass project must use its own rocket/project tracking and must not
  be conflated with Battery Sphere rockets or ordinary Dyson Sphere rockets.

### Star conversion

- Hyper-mass project rockets bring matter through Subspace Logistics from
  other galaxies and feed it into the selected star.
- Once the hyper-mass sphere reaches **10 GW** of power, expose an operation in
  the sphere-construction UI named **Hyperinject Mass into the Star**.
- Before the 10 GW threshold is reached, that operation should be visible but
  unavailable; at or above the threshold, it should become selectable.
- Activating the operation converts the star into a much larger and brighter
  star. Its luminosity target is based on Stellar Hyper Mass Inducer research
  level (see the progression above).
- As the star grows, its appearance should progress from blue hypergiant
  coloration toward violet hypergiant coloration, starting blue and becoming
  more violet at larger sizes.
- The conversion should be attached to the Hyper Mass Injector project and
  should not alter or consume the player's Battery Sphere project.

## Decisions (2026-10-15)

- Activation threshold is **1 MW** for now (`HyperMassManager.ActivationWatts`),
  to be raised to 10 GW after testing. It is measured as the
  sphere's generated power, the same statistic the Battery Sphere status shows.
- Amplification has **50 levels of +0.02 L** each (`HyperMassContent.LuminosityPerLevel`
  and `AmplificationMaximumLevel`), so 50 upgrades take a star from 2.5 L to
  the 3.5 L maximum (`HyperMassContent.MaximumLuminosity`), which is also the
  violet end of the blue-to-violet range.
- Luminosity is the absolute value the star detail panel shows (`dysonLumino`):
  2.5 L from the inducer, +0.02 L per Amplification level (max 3.5 L). A star
  that is already brighter keeps its own luminosity.
- Research cost copies the last Vein Utilization tech (found by its
  mining-cost unlock function); Amplification follows its cost curve.
- Rocket recipe: 1 Small Carrier Rocket + 2 Subspace Warpers, 30 s.
- Hyperinjection asks for confirmation, is permanent, and destroys the
  injector sphere (an empty one is left in its place). It can be repeated
  after more Amplification research.
- Mass follows luminosity (L = M^0.7), temperature follows mass (StarGen's
  formula), radius grows 3-6x on top of the mass scaling but stops at 60% of the
  innermost planet's orbit. The star becomes a giant (O-class) star. Planet solar
  luminosity is recalculated. Colour goes from blue (2.5 L) to violet (3.5 L).
- Only Hyper Mass Rockets build at a Hyper Mass Injector star.

## Open questions for implementation

Superseded by the decisions above for the Stellar Hyper Mass feature; kept for
reference.

These points were not settled in the request and should be resolved before
locking down implementation details:

- What exact Vein Utilization level/cost curve should this technology copy or
  approximate, and what should its maximum research level be?
- What should the exact hyper-mass rocket recipe, production time, and rocket
  count contribution be?
- Does “10 GW” mean the Hyper Mass Injector project's generated power, its
  current output, or another sphere statistic?
- Is star conversion a one-time, irreversible action, and should it require
  confirmation?
- How should the “other galaxies” matter source work in the base game, where
  galaxies outside the current cluster may not be simulated or accessible?
- Is luminosity **2.5** an absolute game value or a multiplier relative to the
  star's original luminosity? How should the “much larger” radius/mass change
  map to game data?
- How should the visual transition from blue to violet be mapped to star size
  or research level?
- What should happen to existing Dyson Sphere structures, power production,
  planetary orbits, and saves when a star is converted?
- Which sphere-creation UI/API should be used, and what compatibility is
  required with More Mega Structures?
- Which vanilla science colors/shapes should guide the custom icon artwork?
