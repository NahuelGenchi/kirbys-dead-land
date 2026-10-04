---
kind: game
title: KSSU ROM extraction and Linux runtime bridge investigation
game: Dead Cells
games_also: [Kirby Super Star Ultra]
game_version: 'Steam Linux build 23762174, v35'
platform: linux
engine: haxe
route: loader-api
tools: [Universal Modder, DCCM 35.13.6, .NET SDK 10.0.401, Python, Pillow, melon-rs]
anti_cheat: 'None detected; single-player local development'
status: in-progress
agents:
- Codex
humans: []
date: '2026-10-03'
links:
- https://github.com/rehan-remade/universal-modder
- https://github.com/dead-cells-core-modding/core/releases/tag/v35.13.6
- https://github.com/benhall-7/melon-kssu
tags: [linux, hashlink, rom-conversion, gameplay-port]
---
# KSSU ROM extraction and Linux runtime bridge investigation

A new Kirby port requires an original gameplay controller, not only costume
assets. This project decoded ROM sprites, converted them into verified host asset
formats and compiled a DCCM adapter. No playable Kirby character was observed in
Dead Cells; the adapter remains dormant pending the original controller.

## Setup
Owned Steam Linux Dead Cells build 23762174 uses a loose `hlboot.dat` and `res.pak`.
DCCM 35.13.6 supplies Linux x64 runtime and a C# API through .NET 10. Official SDK
10.0.401 and the released MDK were installed in a private development directory.
The user supplied the USA revision 0 KSSU NDS ROM. Its source files, extracted
assets and game bytecode stayed outside the source repository.

## Route and why
Use DCCM hooks for controller/weapon integration and per-mod asset packs to avoid
overwriting the user's existing Sonic archive mod. The essential controller can
come from a validated port or from the original game through emulation, but neither
the host-world collision bridge nor a complete original gameplay provider exists
in this project yet. Merely changing `gravity` and `runSpd` is not source fidelity.

## How the game works (what we had to learn)
- The host uses HashLink. `um scan` reported unknown native; inspect actual files.
- Released DCCM proxy APIs expose `Hero.fixedUpdate`, `Weapon.tryToUse`,
  `Inventory.equip`, and level lifecycle callbacks. Compilation validates their
  signatures, not the installed game's live compatibility.
- A physical shield needs its `InventItem.permanentId`; item kind alone cannot
  distinguish two copies of the same shield.
- NDS sprite `.b` archives contain groups, pose-pointer tables, palettes, tile
  block pointers and extended OAM parts. The right and left facing OAM attributes
  are separate. Alternate palette groups are not extra animation frames.
- Ability body groups can pair with cap/weapon groups. A cap-only bank is not a
  complete Kirby. Graphic pose indices do not encode frame timing or state logic.

## Build steps
Read the local README for exact commands: extract the user's ROM, build pose
resources, verify them against decoded originals, build the runtime, and stage it
in a disjoint lab. Source contains converters and new code only. No live-game
loader installation or desktop input occurred; existing player saves were untouched.

## Verification
Decoder synthetic and actual-ROM structure tests passed. Base Kirby has 491 fully
decoded normal-color poses. All 61 sprite archives were decoded, giving 20,224
group-indexed poses with separate left/right facing and 630 body/cap compositions.
Unknown palette parts are explicitly recorded instead of guessed.

Atlas checks compare visible RGBA pixels and original pivots to individual decoded
PNGs. Independent verification opens the generated PAK through released
`GameRes.Core.Pak.PakFile` and compares every payload hash/checksum with the build
manifest. The hook adapter compiles without warnings/errors. The separate rule
test runner passes 26 assertions. These checks do not establish live gameplay.

## Gotchas
1. **A pose sheet is mistaken for an animation.** Its frame numbers give graphics
   references only. Recover or observe original timing and state selection.
2. **Incomplete weapons appear in composed frames.** Some OAM parts use shared
   palettes outside the archive. Preserve unknown metadata and exclude incomplete
   frames from usable exports.
3. **Long weapon effects clip in diagnostic sheets.** Some poses exceed a
   128-pixel cell. Size cells from actual bounds and compose using union bounds.
4. **DCCM unpack exits successfully but leaves empty files.** Its unpack command
   calls asynchronous writes without awaiting them. Verify using the synchronous
   archive reader; exit 0 alone is not proof of complete export.
5. **Rebuilding keeps obsolete atlas pages.** Build in fresh staging, validate
   ownership and replace only the previously generated output.
6. **Two shields share a kind string.** Preserve the physical instance ID when
   limiting copied Kirby to the shield held before transformation.
7. **Framework availability looks like finished integration.** Signatures can
   compile while gameplay is absent. Do not call an inert adapter a playable mod.

## Assets
Character artwork came from the user's ROM; no generated substitute art was used.
Host palette encoding is lossless. Flat normal maps are renderer support data,
not original NDS graphics. No game files or assets were published.

## Cost and time
One local development session; no paid image-generation service used.

## Open questions
Original physics, complete ability action graphs, shared palettes, animation
timing, collision mapping, enemy lifecycle, host sprite rendering, persistent
state and full in-game verification remain work. The project includes original
ROM headless oracle research to help resolve these facts.
