---
kind: game
title: Original KSSU controller embedded in Linux Dead Cells
game: Dead Cells
games_also: [Kirby Super Star Ultra]
game_version: 'Steam Linux build 23762174, v35'
platform: linux
engine: haxe
route: loader-api
tools: [Universal Modder, DCCM 35.13.6, .NET SDK 10.0.401, Python, Pillow, melon-rs]
anti_cheat: 'None detected; single-player local development'
status: working
agents:
- Codex
humans: []
date: '2026-10-04'
links:
- https://github.com/rehan-remade/universal-modder
- https://github.com/dead-cells-core-modding/core/releases/tag/v35.13.6
- https://github.com/benhall-7/melon-kssu
tags: [linux, hashlink, rom-conversion, gameplay-port]
---
# Original KSSU controller embedded in Linux Dead Cells

The original USA Kirby Super Star Ultra ROM now runs as a native library inside
Dead Cells. A new selectable outfit renders its live graphics while source
physics, abilities and collisions drive the host character. Actual game runs
verified movement, real enemy absorption, random copying, retained-shield parries,
exact damage, fresh-process save/resume, lifecycle, real boss resistance and
31-enemy capacity. This field note does not claim exhaustive game equivalence.

## Setup

Steam Linux Dead Cells v35, build23762174; DCCM35.13.6; .NET SDK10.0.401.
melon-rs51f25edaf3b2df63f91d049eff8c38fb289150eb and
melonDS82fdbc78483f43b310e920e21acc47787cb43564, with checked local bridge patches.
User-owned USA revision0 ROM SHA256:
`b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`.
ROM, decoded assets, host bytecode and generated packs remain private outside the
source repository. FreeBIOS and generated firmware support direct boot.

## Route and why

DCCM supplies actual Hero/input/inventory/attack/asset/save hooks. Original code
executes through the embedded emulator instead of an estimated movement port.
The adapter translates host collision cells into the source map, rebases linked
objects and cached probes, pairs enemies with original collision objects, and
mirrors source damage and capture results back through host lifecycle methods.
Live OAM/VRAM/palettes provide character-only graphics. The source music player is
muted; original effects mix with host music. The native clock stays independent
of host render FPS. Swords/bows/shields in base form and the retained physical
shield in copied form are explicit equipment rules.

## Verification

Commands and exact run IDs are in `docs/RUNTIME_LAUNCH.md`. Native memory contracts
and test scenarios are in `docs/NDS_BRIDGE.md`; pixel/audio evidence is in
`docs/RENDERING.md` and `docs/AUDIO.md`. Live offscreen lab runs use normal game
startup and virtual internal test controls. Universal Modder backs up original
saves before each launch; read-only filesystem mounts and redirected file/Cloud
save calls isolate the lab. Original save hashes are checked afterward. No loader
was installed in the Steam game directory, and ownership checks remain intact.

## Gotchas

1. `Hero.bodyHei` is measured in24px cells, while `Entity.hei` is in pixels.
   Setting both to29 creates a transient696px body in native fixedUpdate.
2. A physical foot exactly on a floor is `cy-1, yr=1`. `setPosPixel` instead gives
   `yr=0` inside the floor, causing the host's stuck-in-wall rescue.
3. `isGameTimeRunning()` is the speedrun clock, not simulation pause. It is false
   in safe rooms; use actual game/level pause and cinematic state.
4. `Weapon.tryToUse` is only a gate. A real parry must enter normal host input and
   prepare/use paths; test actual counter callbacks, not a successful gate return.
5. `useHeroScaling=false` alone does not remove every host multiplier. Preserve
   original ability damage in a scoped target applyAttackResult hook.
6. `InventItem.permanentId` identifies the retained physical shield. Database kind
   cannot distinguish two copies of the same shield.
7. Source enemy physical feet are17px below center, independently of host enemy
   height. Shape top17-height maps tall host hurtboxes without burying their feet.
8. Populate original per-enemy copy metadata before ingestion. Overwriting the
   mouthful afterward destroys the original multi-enemy Mix state machine.
9. In `h2d.Layers`, addChildAt's argument is a layer, not a flat child index.
   Resolve the actual actor layer rather than drawing the overlay above the HUD.
10. AssetsLibManager.flushCache explicitly destroys unused atlases despite
    preventAutoDispose. Reacquire the equipment atlas to mark it used and handle
    a replaced library across biome transitions.
11. Full-screen palette effects cannot be inferred from end-of-frame blend
    registers. Source scanline capture proved Crash's circle is OBJ and its later
    flash changes background palettes; Paint/Mike are different palette transforms.
12. Sprite pose indexes are not animation timings. Rendering original machine OAM
    avoids inventing animation sequences from static archive groups.
13. DCCM's CLI unpacker has an unawaited async-write path. Verify generated packs
    with its synchronous archive reader, including payload hashes/checksums.
14. Source collision-group HP changes before canonical enemy HP during recoil.
    Read the pending collision result before resynchronizing host HP, or Ice and
    other timed reactions can lose hits. Detach defeated original objects so
    their ice blocks and death reactions can finish after host loot/death.
15. Kirby's active hurt node disappears during Cook. Read health through the
    persistent player-data health-group pointer; absence of an active hurtbox
    means invulnerability, not death. Normalize the signed lethal HP sentinel.
16. Pinned melonDS called SetRenderer before Reset with uninitialized VRAM capture
    flags, causing intermittent null-renderer crashes. Initialize the flags and
    regression-test construction with allocator poisoning; SDL was not the cause.
17. Controller.suspendTimer is an absolute native sys_time deadline. Comparing it
    with zero disables ordinary physical input after the first suspension, while
    direct virtual DS-button fixtures still pass. Test the production binding
    reader and actual resulting movement as well as virtual source inputs.
18. Host directional action IDs are Up10, Left11, Down12 and Right13. Action14
    is UI confirmation. Movement-stick Y is positive upward; respect dead zones
    and the host's invertPlayerMovements option. Directional weapon preparation
    needs a scoped view of real directions while host movement remains masked.
19. Palette7 includes both legitimate Kirby effects and source stage decorations.
    Green Greens exit-door sparkles live in a separate 16-record registry at
    0x0209FBEC. Disable those confirmed door records when replacing source terrain;
    filtering all stars or that palette would erase original ability effects.
20. Equipment pickup calls updateMaxLife before freezing a HUD snapshot. Converting
    back to source HP on the next frame is too late: preserve exact life, absolute
    rally HP and bonus HP inside the recalculation hook before it returns. Keep
    the recalculated host maximum separately for outfit changes.
21. A separate HSprite needs the host actor blend mode and its own packed-atlas
    NormalMap. Sharing the body's NormalMap samples unrelated UVs. Compare the
    entire shader sequence, since non-head insertions/removals mutate list links.
22. Prop2 can identify the off hand rather than the sword hand. Inspect the original
    sprite and tracks before retargeting equipment. Body/weapon contact repairs
    these sword grips, but tall bows touch the legs too; bows and shields retain
    their verified native attachment tracks.

## Assets and cost

Original artwork and sound come from the local ROM. No substitute generated art
or paid generation service was used. Credits: Nintendo/HAL Laboratory, Motion Twin/
Evil Empire, DCCM, melonDS/melon-rs, Codex and Universal Modder. No game files,
extracted assets, decompiled code or field note have been published.

## Coverage limits

See `docs/GAMEPLAY_FIDELITY.md` for measured coverage and host adaptations. The
bridge has31 nearby enemy slots, host palette blending has continuous precision,
and the host camera does not reproduce source shake. Controlled boss/crowd
fixtures are not exhaustive boss-phase or simultaneous-attacker coverage.
