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
23. Host weapon frames sit at the Beheaded's torso height and glow through GlowKey.
    Child order in the shared layer decides whether that blade covers Kirby; keep
    the original body after the equipment. Check the source palette before blaming
    the host: KSSU itself flashes bank 0 after hits and while guarding.
24. The arrival corpse draws the hero atlas before Hero.initGfx, and the death
    corpse after the hero is destroyed. Resolve the outfit atlas through
    Assets.lib.get each frame instead of from the hero.
25. Hero.kill destroys the hero synchronously and fires the dispose event inside
    the source step. Continue a death animation from the frame event, and stop
    about 30 frames before the source starts its own life-lost transition.
26. Wall-time accumulation against a 59.96 Hz display drops a 59.83 Hz source frame
    every few seconds. Lock one source frame per host frame near the DS rate and
    let the audio queue absorb the drift.
27. Hero.applyAttackResult calls Hero.blink, an alpha pulse to 0.3. An overlay that
    copies the host sprite alpha vanishes every other frame on each hit.
28. Viewport.set_zoom scales from the view's corner; call updateSizes afterwards
    so tracking uses the zoomed size. The viewport is about 400 host px tall.
29. Host mob life is base x Const.scaleMobLifeToTier(trueLifeTier) x elite/boss
    extras. Give paired source enemies HP in source units so both games agree
    on the lethal hit; Ice freezes and death reactions depend on it.
30. The original dashes only on a double tap (walk 352, dash 528 in 1/256 px).
    A synthesized one-frame release after a press, and after landing, dashes.
31. The ROM's hurt/guard flash swaps palette banks 0-1 to orange, then to a
    half-whitened set, 3-of-6 frames. Fine on a DS screen; on a large monitor the
    whitened Kirby vanishes for a frame. Soften the flash, never hide objects.
32. Do not change the player's camera framing without asking; a zoom that makes
    the DS proportions "right" is still a surprise to someone used to the host view.
33. Hardware OAM after a source frame shows the previous frame's positions. Pair
    it with the state captured before the next World rebase, or Kirby jumps a whole
    cell for one frame at every window shift. Track positions, not just pixel
    counts, when reviewing "flicker": a moved sprite keeps all its pixels.
34. Host collision data: 1|2 solid, 8 one-way, 16 ladder, 64 roll zone (65 with
    bit 1), 128 breakable ground, 512/1024 slopes. A roll zone is a one-cell tunnel
    the Beheaded passes only rolling; exporting it as plain solid blocks Kirby.
35. The original action code is the u16 at 0x020BAF80 and is shared by every copy
    ability: 0406 stand/crouch, 060B walk, 060C dash, 070D jump, 070E fall, 070F
    float, 142C ability use (inhale, exhale, spit and every copy-ability attack), 0507
    landing, 0B18 slide.
36. The original clamps falling at 3.5 px per frame (896 in 1/256) for every state;
    writing a larger vy is clamped back. Add speed through the world rebase instead,
    and stop short of the floor so the original collision makes the contact.
37. Landing during an exhale keeps action 142C for several frames after the feet
    touch down. Detect a landing from the floor under the feet as well.
38. Starting a host skill from outside its input check works: DiveAttack.start and
    onOwnerLand bring the host's own sounds, effects, damage and floor breaking.
    Mask its dedicated action (36) from the host so it never starts on its own.
39. Never refuse Inventory.equip: Inventory.add has already listed the item with
    posID -1, and the HUD throws "unexpected posId" on the pickup. Decline items in
    InventItem.isUsefulFor, which every drop, altar, shop and timed reward checks
    first and answers with the host's own "Can't use that right now".
40. The original never stands on ladder cells, top included, and climbing is action
    0916; A kicks Kirby off a ladder at once (left or right takes seven frames).
    A floor probe for added movement must skip ladders and count one-way
    platforms only from above.
41. The default keyboard binds W and the Up arrow to both Up and jump. The host
    climbs with them at a ladder; send the original only Up from such a key while
    Kirby overlaps or holds a ladder, until it is released.
42. KirbyHurtbox projects the original hurtbox onto the host hero for all of
    Level.update (foot about 9 px higher, radius 6, height 12). Anything inside the
    level update that asks "is the hero on the ground here", like PressurePlate
    and HiddenTrigger, needs the physical body restored around it.
43. The original moves on a copy of the horizontal velocity at player+0x98;
    +0x88 is only what it reports. Writing +0x88 alone changes nothing, on the
    ground or in the air; clear both. Vertical velocity is read from +0x8A.
44. The original's floor test starts from where its previous frame ended. A host
    move that leaves less than one fall step (3.5 px) to a floor carries Kirby a
    fraction of a pixel into it for a frame; leave a full step. Walls and
    ceilings are tested fresh.
45. Slopes (bridge 132 rising right, 133 rising left) are sampled at Kirby's
    centre in whole pixel columns: surface = cell bottom - (column + 1) for 132,
    cell bottom - (24 - column) for 133. Running downhill across a slope-cell
    boundary leaves the ground briefly, natively too.
46. The original jump: take-off burst vy -2682, -2036, -1390, -744, then 44 of
    gravity a frame; its height is fixed at 49.9 px, tapped or held, and 32
    frames after take-off it turns into a fall with one motionless frame. Walls
    stop Kirby's centre 8.5 px away; ceilings stop his feet 18.5 px below.
47. Beheaded, measured in the lab at 30 host steps a second: run 236.5 px/s
    (0.329 cells a step), stop within ~3 px, roll 131 px at 339 px/s, fall
    terminal ~0.56 cells a step (403 px/s), dive ~30 px a step, ladder pulses of
    26 px every 0.267 s (97.5 px/s), jump 43 px (47 running) peaking at 0.25 s.
48. kssu_world moves every original actor by the player's delta: right for a
    window rebase, wrong for host movement of Kirby alone. Fed every frame (speed
    match), it dragged a lobbed Bomb along behind Kirby at his added speed, and on
    a chain it pulled a bomb under his feet. Move the window by whole cells and
    Kirby alone with kssu_move_player. Some actors (held-bomb parts) keep
    coordinates that are not world positions; never shift them for Kirby.
49. 142C is not just the exhale: every copy-ability attack runs under it, and
    Wheel's roll, Jet's charged dash, Yo-Yo's and Suplex's dashes carry Kirby with
    their own velocity. Braking it on a released direction left Wheel and Jet in
    place. Only the exhale that follows a float may move like floating.
50. Some original actors are attached: +0x80/+0x84 is an offset from the parent
    and +0x78/+0x7C (drawn position) = parent's drawn position (or sprite anchor,
    parent+0x40 -> +0x748) + offset, read before or after the parent moved that
    frame. Shifting the offset on a window rebase pushed Fire's head emitter 24
    px off Kirby per cell (flame left at the foot of a chain, embers trailing a
    run) and made 17 other effect classes jump. Rebase drawn positions; keep
    offsets. Recognize them by that equation, not by class: classes switch
    between attached and absolute.
51. The source camera's state is the i16 scroll pair at 0x0209F3A8 (x, y);
    0x0204222C/0x0204222E are recomputed from it every frame, so writes there
    vanish. It scrolls at most 16 px a frame and the original draws only its
    256x192 screen: a placement that left Kirby at screen x 376 hid him for six
    frames, and each window cell jumped his screen x by 24. Move the scroll with
    the window; recentre after a placement.
52. Kirby's pose count is a byte at (player+0x40)+0x6C8 (0x020BB630 in the
    seed); the pose advances on the frame it reads zero. Decrementing it (never
    below zero) plays the 2- and 3-frame walk poses in 1 and 2 frames at most,
    1.67 times the rate. The climb cycle is not timer-driven (hand-over-hand
    pulses) and does not respond to it.
53. KSSU and Dead Cells disagree on touching: the original hurts Kirby (7 HP)
    and bumps weak enemies to death; Dead Cells hurts neither (only attacks,
    plus Mob.contactAttack overrides on Spiker, Shocker, Shield, BackShield,
    Enforcer and two tentacle types). Excluding the original body-contact
    category (hit-node +0x46 mask 0x4000) keeps inhale, swallow, Suplex grabs
    and attacks working.

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
