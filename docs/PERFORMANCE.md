# Performance

What the mod's work costs on each host frame, what #123 changed, and how to measure it.

Every number here comes from the offscreen lab (`scripts/launch_runtime.py`, software OpenGL, no frame cap) on the owner's machine on 2026-10-11, with the OS power profile on `performance`. The lab draws the game on the CPU, which play on the GPU does not, so lab fps is not play fps. What carries over is the time of each stage of the mod's own work and the garbage it makes. No windowed play was measured: this record does not establish 60 fps in play.

## The mod's frame

On a host frame that steps Kirby, `KirbyNdsProvider.OnFrame` exports the terrain, pairs host enemies with original proxies (`Prepare`), runs one original frame in melonDS (`Native`), applies its result (`Complete`), and then draws: the background capture, a read of the original GPU memory, the OBJ decoder, the texture upload and the equipment layer. Items, the HUD and audio follow. Outside that, the host calls the mod's hooks during its own update.

Three costs stood out on `main`.

- The original frame itself, about 3 ms. The bridge uses melonDS's interpreter.
- The read of OBJ VRAM after it, 1.3 ms: one bus read for each of 262,144 bytes.
- Hooks on host functions that run for every entity. With a handler installed, each call of a hooked function costs about 2 µs more and leaves about 920 bytes of managed garbage, whatever the handler decides (measured in the game on `Mob.canUpdate`: 4.4 µs and 1,840 bytes hooked at `Mob` and `Entity`, 0.77 µs and 56 bytes after the handlers are removed, 0.38 µs and nothing for a function never hooked). At the start of the Prisoners' Quarters the host called `Entity.canUpdate` 370–590 times and `Mob.canUpdate` 140–330 times a frame. DCCM runs the game with the server garbage collector, not concurrent, so that garbage became a pause of about 5 ms nearly five times a second (288 collections a minute in the movement runs below).

## What #123 changed

**OBJ VRAM is read as a block.** The renderer reads all 256 KiB of engine A's OBJ VRAM after every original frame. `kssu_read_gpu` did that with one bus read a byte. It now copies whole 8-byte words inside OBJ VRAM through the accessors the bus read uses (`Copy_ObjVram`); any other request still takes the old path. Bridge case `obj_vram_block` requires the block to equal the bus read.

**The lower screen is not drawn.** The mod shows neither DS screen and reads only engine A. `kssu_main_screen_only` leaves engine B's 2D drawing out of each frame; `NdsRuntime` turns it on when it opens the machine, and tools that read the framebuffer keep the default. Bridge case `main_screen_only` runs 240 frames of input both ways and requires the same main RAM, GPU memory, layer capture, top screen and save state, and a black lower screen only while the switch is on.

**The held-entity hooks are installed only while something is held.** `KirbyEnemyHooks` stops the host from moving an enemy, projectile or item that the original engine holds. Its six hooks used to be installed for the whole session. They are now added the moment an entity becomes held (`KirbyEnemies.SetControlled`) and removed between host frames once nothing of that kind has been held for 30 frames. Enemies and the other kinds are separate groups, because a held enemy is stopped by `Mob.canUpdate` and never reaches the `Entity.canUpdate` handler, which every entity in the level would otherwise pay for. The handlers are unchanged.

**The sprite canvas is kept.** `NdsObjRenderer` drew onto a managed 2 MB canvas that was cleared and then copied into the texture's pixels for every drawn frame. It now draws into the texture's own pixels and clears only the rectangle the previous drawing covered. A rule test draws 300 random frames both ways and requires equal canvases.

The skip of the decode and upload on a frame whose inputs did not change belongs to #121.

## Measurements

Three builds were run with the same probe marks: `main` (`77aa909`) with the bridge it has now, #121 alone (PR #122, `dd58ee3`, with its bridge), and #123 on top of #121. Each run is one `launch_runtime.py --start-game --outfit KirbySuperStarUltra --input-plan <plan>` with `KIRBY_LAB_PERF=1` in its own private data root. Every run ended by its time limit with no fatal log and the real saves unchanged. The run folders, each with its `perf.json`, are under `repros/perf-123-base` (`main`), `repros/perf-123-mid` (#121) and `repros/perf-123` (#123) in the private data root, runs `20261011-213121` to `20261011-220348`.

Movement plan, 118–120 s of stepped frames a run. The first block is the mod's own time for one original frame, which does not depend on how fast the lab draws.

| | `main` (2 runs) | #121 alone (1 run) | #123 (3 runs) |
| :--- | ---: | ---: | ---: |
| Mod work per original frame | 5.56 ms | 4.87 ms | 3.20 ms |
| of it: the original frame | 3.09 ms | 3.04 ms | 2.59 ms |
| of it: GPU memory read | 1.28 ms | 1.28 ms | 0.04 ms |
| of it: decode and upload | 0.78 ms | 0.17 ms | 0.13 ms |
| Held-entity hook calls per host frame | not counted | 616 | 0 |
| Managed garbage per second | 54.9 MB | 52.1 MB | 16.0 MB |
| Time paused in collections | 2.3% | 2.3% | 0.6% |
| Host frames per second | 63.6, 66.6 | 68.3 | 99.6, 99.2, 89.4 |
| Mean host frame | 15.7, 15.0 ms | 14.6 ms | 10.0, 10.1, 11.2 ms |
| 99th percentile host frame | 26.4, 25.5 ms | 27.2 ms | 17.7, 17.8, 20.2 ms |
| Host frames over 20 ms | 8.5%, 5.9% | 6.1% | 0.6%, 0.7%, 1.0% |

The mod work rows leave out the hook calls themselves, which run inside the host's update: at the per-call figure above, 616 calls are about 1.2 ms a host frame (an estimate). The count was not taken on `main`, which runs the same hook code as #121. The third #123 run is the final build; the mod's own costs in it equal the first two (3.24 ms, 15.1 MB a second). Why its host frames were slower was not found.

Most of the decode and upload row is #121's doing: a frame whose inputs did not change is not drawn, so 0.98 uploads per original frame became 0.20. #123's part of that row is the decode itself, 0.15 ms on `main` and #121 and 0.015 ms here. The other rows changed in #123.

Other plans, one run each on `main` and on #123 (`crowd` also on #121):

| Plan | Mod work per original frame | Host frames per second | Host frames over 20 ms |
| :--- | ---: | ---: | ---: |
| `combat`, 62 s | 5.58 → 3.30 ms | 61.3 → 82.7 | 10.2% → 1.7% |
| `hazard`, 141 s | 5.53 → 3.11 ms | 56.6 → 87.8 | 16.9% → 1.1% |
| `items`, 60 s | 5.58 → 3.31 ms | 55.1 → 79.9 | 19.4% → 1.7% |
| `crowd`, 70 s | 8.50 → 6.74 ms (#121: 7.86) | 36.8 → 57.7 (#121: 38.9) | 92.8% → 15.4% (#121: 86.6%) |

In the `crowd` run the hooks are installed, as 30 enemies are held: 324 calls a host frame against 1,036 on #121, because the `Entity.canUpdate` handler is no longer installed for enemies.

The bridge alone, 900 original frames driven from Python with every call the host makes on a frame: 4.08 ms a frame with `main`'s bridge, 4.00 ms with #121's and 2.31 ms with #123's (`kssu_step` 2.77, 2.75 and 2.26 ms; the OBJ VRAM read 1.27, 1.22 and 0.017 ms).

The same runs show the behaviour held. `crowd`: the same 30 hits and 1,260 damage, the same life and held state for all 31 targets and identical original slots on `main` and #123. `hazard`: all three phases complete on both, with the grenade and the arrow in Kirby's mouth and his life unchanged. `items`: `passed` on both, including the item following the suction and returning to its place. `combat`: the same probe steps succeed on both; the copied ability is random (Yo-Yo and Sleep).

## The upper screen on frames with no effect (#148)

After #123 engine A, the upper screen, was still drawn on every scanline of every original frame. The host reads that drawing in one place: `NdsBackgroundEffects` copies three planes of the layer capture after each frame and decodes them to find a palette or colour effect that covers the whole screen, as Crash's flash does. On almost every frame it finds none.

The bridge can now leave those scanlines undrawn (`kssu_skip_idle_main`, off by default). A scanline is left out only when its capture is already known to read as "no effect": the unit is on and not blanked, no colour effect control is set, and the standard background palette equals the one the decoder compares against. Its sprites are still drawn, and a scanline that a display capture reads is always drawn, so the emulated machine is the same either way. `kssu_main_frame` says how the last frame went. `KirbyNdsProvider` turns the switch on; `NdsBackgroundEffects` and the lab's visual probe take an undrawn frame as the identity effect and a partly drawn one as unsupported, which is what the decoder returns for such frames, and read and decode only a drawn frame. `KSSU_DRAW_EVERY_FRAME=1` in the environment keeps every scanline drawn, for comparison on one build.

Measured 2026-10-13 on a private build of this branch (`repros/skip-148`), power profile `performance`.

The bridge alone, 720 original frames of input driven from Python, median `kssu_step`: 2.77 → 2.44 ms with Kirby alone and 3.05 → 2.72 ms with 28 proxies, 0.33 ms less in both. Leaving the sprites out as well would take another 0.04 ms; they stay drawn so that nothing a display capture could read is ever drawn late.

The lab, movement plan, 60 s of stepped frames a run, two runs each way on the same build (runs `20261013-234130` to `20261013-234605`):

| | Every frame drawn | Idle scanlines left out |
| :--- | ---: | ---: |
| Mod work per original frame | 3.41, 3.42 ms | 2.87, 2.89 ms |
| of it: the original frame | 2.58, 2.58 ms | 2.16, 2.13 ms |
| of it: the background capture | 0.074, 0.075 ms | 0.003, 0.003 ms |
| Host frames per second | 89.6, 88.7 | 96.1, 92.2 |

All four runs ended by their time limit with no fatal log and the real saves unchanged. The movement plan raises no screen effect.

That the answer the host gets is the same was checked three ways. Bridge case `skip_idle_main` plays 480 frames (ordinary play, then Crash) with and without the switch and requires the same main RAM, GPU memory and save state and the same effect on every frame: 419 frames are left undrawn and the 61 of the flash are drawn. `tools/verify_background.py` now replays each of its four cases with the switch on and requires the effect of every frame to equal the one decoded from the full capture: 1,200 frames, 888 undrawn, 0 different. In the game, the lab's `visual` plan (Crash, Paint and Mike through the real inhale) recorded no unsupported frame either way and the same effect on each of the 2,093 frames polled in both runs: 134, 301 and 94 polled frames with an active effect with the switch on (`20261013-234736`), 134, 301 and 93 with every frame drawn (`20261013-234846`). The two runs do not poll the same frames, hence the one frame of difference.

Checks on the final tree: `python3 scripts/check.py` passes; on the private build, all 86 `tools/nds_tests.py` cases pass, `tools/verify_renderer.py` 29 cases and 3,065,177 pixels with 0 mismatched, `tools/verify_background.py` 4 cases and 176,357,376 values with 0 mismatches, `tools/verify_native_init.py` 64 cycles. The shared bridge binary and vendor checkout were not replaced: play needs `python3 tools/nds_build.py` and a new stage after the merge. With the older bridge the runtime works as before and draws every frame.

Not established: any change in windowed play on the GPU (not run). No test or run produced a frame the bridge reports as partly drawn, so that answer is unexercised. No display capture ran on any frame left undrawn; whether the game ever starts one was not looked for, so the rule that keeps its scanlines drawn rests on the source alone.

## The original's processor at twice the DS clock (#150)

The bridge can run the emulated ARM9 at twice the DS clock (`kssu_arm9_clock`, docs/NDS_BRIDGE.md) and the runtime does by default (`DoubleArm9Clock`, docs/GAMEPLAY_FIDELITY.md). The interpreter's work follows the instructions the original executes, not the clock: a pass that fits in a frame costs the same and the ARM9 then rests for longer. The cost is the work of passes that did not fit, now done inside their frame.

The bridge driven from Python, private build `repros/clock-150`, Sword Kirby swinging through 360 frames (180 for the stacked rows), DS clock → twice:

| Scene | Frames ended inside a pass | `kssu_step` |
| :--- | :--- | :--- |
| No proxies | 0 → 0, same trace | 2.82 → 2.86 ms |
| 8 proxies around Kirby | 0 → 0, same trace | 3.10 → 3.16 ms |
| 31 proxies around Kirby | 3 → 0 | 3.74 → 3.86 ms |
| 24 proxies and a Sword helper | 5 → 0 | 3.96 → 4.15 ms |
| 31 proxies and a Sword helper | 2 → 0 | 4.08 → 4.32 ms |
| 12 proxies in one place, all hit by each swing | 15 → 0 | 3.36 → 3.51 ms |
| 20 proxies in one place, all hit by each swing | 22 → 12 | 3.58 → 3.84 ms |

Live, isolated profile, offscreen, `performance` power profile, one 120 s run per clock, DS clock → twice (`Kirby pacing` line and `perf.json`):

| Plan | Frames ended inside a pass in a minute | Original step | Host fps |
| :--- | :--- | :--- | :--- |
| `movement` (`20261014-002835`, `20261014-003037`) | 0 → 0 | 2.81 → 2.82 ms | 95.9 → 95.1 |
| `crowd --crowd-size 20` (`20261014-002620`, `20261014-002418`) | 7 → 5 | 3.60 → 3.69 ms | 79.7 → 79.5 |
| `crowd`, 31 Zombies (`20261014-001218`, `20261014-000955`) | 3,252 → 3,257 | 6.32 → 8.41 ms | 56.0 → 40.0 |

The last row is not the clock's doing: the swing that hits all 31 stopped the original for good at either clock until #151 ([the collision pool](#the-collision-pool-151)), and what is timed is an ARM9 that never rests again. It is the most a frame can cost: about 2.2 ms more at twice the clock. Three of the late frames in the 20-Zombie row are the Sword's load from the cartridge, which the clock does not shorten; the bridge replay of that fixture's input gives the other 4 → 2.

Checks on the final tree, 2026-10-14, private build (bridge SHA-256 `41ca6f7eae0ddc1b7e12972b8cd32751439aac4f1bbc5cb41aa1c832d6ae2819`): `tools/nds_tests.py` passes all 86 cases at the DS clock, and the 85 older ones return what the shared bridge recorded; `tools/verify_renderer.py` 29 cases, 3,065,177 pixels, 0 mismatched; `tools/verify_background.py` 4 cases, 176,357,376 values, 0 mismatches; `tools/verify_native_init.py` 64 cycles; `python3 scripts/check.py` passes. The shared bridge binary and its recorded results were not replaced; play needs `python3 tools/nds_build.py` and a new stage, and with an older bridge the runtime keeps the DS clock.

Not verified: windowed play with a controller, more than one live run per configuration, and any live crowd between 21 and 31.

## Measuring

`KIRBY_LAB_PERF=1` in the shell that runs `launch_runtime.py` makes `LabPerfProbe` write `perf.json` in the run folder every two seconds: host frame intervals, the time and managed bytes of each stage, original steps, texture uploads, calls of the held-entity hooks, collections and their pause. It counts only host frames that stepped Kirby, after the first one. It is off in play, where each mark costs one static test.

The `Kirby pacing:` log line, once a minute in any run including play, gives host and original fps, the mean time of the original step and of the drawing, and how many original frames ended inside a logic pass (#134): a few at each ability or helper load, otherwise none. More than that means Kirby can be drawn a terrain cell off for single frames again (docs/NDS_BRIDGE.md, Where a source frame ends). Check `powerprofilesctl get` first: the power-saver profile alone took the game well below 60 fps on 2026-10-10.

## The original on the recompiler (#153)

The original step is the largest part of the mod's frame, and it was melonDS's interpreter. `Recompiler` (default `true`) runs it on the core's recompiler instead; what that changes in the original, and the evidence that the rest is the same, is in docs/NDS_BRIDGE.md, The recompiler.

The bridge alone, 600 original frames of Sword Kirby swinging, median `kssu_step`, interpreter → recompiler, on a private build: 2.85 → 1.80 ms alone, 3.11 → 1.88 ms with 8 proxies and 3.71 → 2.06 ms with 31, about three fifths.

Lab runs of that build, isolated profile, software drawing, `performance` power profile, one 120 s run each with `Recompiler` false → true:

| Plan | Original step | Host fps | Frames ended inside a pass |
|---|---|---|---|
| `movement` | 2.82 → 1.81 ms | 103.1 → 110.3 | 0 → 0 |
| `crowd`, 31 Zombies | 6.42 → 2.37 ms | 61.8 → 91.2 | 3,264 → 10 |

The `crowd` row compares two different games. On the interpreter the original freezes at the first swing that hits 24 or more Zombies (#151), so its 6.42 ms is a frozen original running out every frame's budget. On the recompiler that write is not stopped and the original plays on: 96 hits on the Zombies against 30, and Kirby killed by them before the run ends. It is the first `crowd` run with a live original after the swing. Since #151 the interpreter's original plays on too ([the collision pool](#the-collision-pool-151)).

Not measured: play in a window on the GPU with vsync, and more than one run of each. The recompiler with #148 and #150 is below.

## The three together (#148, #150, #153)

The runtime's defaults turn on the idle-screen skip, twice the ARM9 clock and the recompiler. A clock change discards the recompiler's compiled blocks, which count cycles at the clock they were compiled at (docs/NDS_BRIDGE.md, The recompiler). Lab runs of the merged tree, private build `repros/merge-20261014`, isolated profile, software drawing, `performance` power profile, one 120 s run each, all three on:

| Plan | Original step | Host fps | Frames ended inside a pass |
|---|---|---|---|
| `movement` (`20261014-025856`) | 1.45 ms | 117.8 | 0 |
| `crowd --crowd-size 20` (`20261014-030339`) | 1.84 ms | 99.2 | 5 |
| `crowd`, 31 Zombies (`20261014-030116`) | 2.08 ms | 92.7 | 5 |

Against the runs above, on other builds and days: the `movement` step was 2.82 ms on the interpreter and 1.81 ms on the recompiler alone; the 20-Zombie step 3.60 ms at the DS clock and 3.69 ms at twice it, with 7 and 5 frames ended inside a pass. In the 31-Zombie run all 31 Zombies were destroyed after 66 hits; on the recompiler alone the Zombies killed Kirby. Each row is a different game, so the counts of late frames compare loosely. `perf.json` of the `movement` run: host frame median 8.4 ms, 99th percentile 15.0 ms, 50 of 10,415 frames over 20 ms.

Not measured: play in a window on the GPU with vsync, and more than one run of each.

## The collision pool (#151)

One swing into 24 or more enemies used up the original's 48 collision groups and stopped it for good (docs/NDS_BRIDGE.md, The collision pool). The bridge now adds 64 groups from the scene heap, one 37,136-byte block per stage. Lab runs of the 31-Zombie `crowd` on a private build (`repros/fix-151`), isolated profile, software drawing, `performance` power profile, one 120 s run each:

| Machine | Original step | Host fps | Frames ended inside a pass |
|---|---|---|---|
| Interpreter, DS clock (`20261014-122119`) | 3.67 ms | 78.4 | 10 in the logged minute; 7 after the crowd appeared |
| Recompiler, twice the clock (`20261014-122321`) | 2.08 ms | 85.8 | 5; 2 after the crowd appeared |

On the interpreter the same plan cost 6.42 ms with 3,264 late frames before (The original on the recompiler, above): that was a stopped original using up every frame. Now it plays on, and 3.67 ms is what that fight costs on the interpreter. The recompiler row matches the run of the three together (2.08 ms, 5 frames).

A harder pile, from the bridge's margin probe (docs/NDS_BRIDGE.md, The collision pool): 31 enemies stacked on one spot and attacked for 240 frames end up to 189 of 300 frames inside a pass on the interpreter at the DS clock (Mirror, B mashed), and 0 to 189 across the 24 abilities. Not measured at the runtime's defaults.

## Per-step bookkeeping (#160)

With the original step cut down, the largest part of the mod's own work left was `Prepare`, the pairing of host enemies with original proxies: 0.21 ms a step moving and 0.51 ms beside 31 Zombies. Most of it was managed. Every step it ran host type and damage tests (`canBeHitBy`, the ability item lookup) on every entity of the level before asking whether the entity was within reach, built a new candidate list and allocated a delegate for each target's critical roll. It now tests the reach first, keeps the list between steps and reuses one delegate; the mutation checks no longer allocate an array or a closure. The filters are the same, so the same entities are paired. The bridge half keeps its walk of the actor lists between proxy calls (docs/NDS_BRIDGE.md, after the freshness flag).

Lab runs, isolated profile, software drawing, `performance` power profile, one 120 s run each, 2026-10-14: `main` (`f498c5f`, runtime and bridge), the managed change alone on `main`'s bridge (`repros/perf-160-base`), and both (`repros/perf-160`, bridge `5f88a1ae…`, runtime `5399c1ee…`). The mod's work per original step is the `Step` section's total over the original steps.

| | `main` | Managed alone | Both |
| :--- | ---: | ---: | ---: |
| `movement`: `Prepare` | 0.206 ms, 3,838 B | 0.139 ms, 3,126 B | 0.122 ms, 2,917 B |
| `movement`: mod work per original step | 1.50 ms | 1.35 ms | 1.35 ms |
| `movement`: host fps | 107.2 | 110.2 | 111.6 |
| `crowd`, 31 Zombies: `Prepare` | 0.506 ms, 36,702 B | 0.447 ms, 34,839 B | 0.346 ms, 35,321 B |
| `crowd`: mod work per original step | 2.03 ms | 1.99 ms | 1.89 ms |
| `crowd`: host fps | 86.5 | 90.2 | 94.3 |

Runs `20261014-130811`, `131343`, `132819` (`movement`) and `131022`, `131546`, `133029` (`crowd`). All six ended by their time limit with no fatal log and the real saves unchanged. The three `crowd` runs played the same fight: 31 proxies at most, 30 fixture targets hit 66 times for 2,046 damage, all 31 destroyed. Host frames over 20 ms stayed at 0.5% moving and went 1.2% → 0.9% in the crowd, which one run each does not separate from noise.

What is left of `Prepare` in the crowd is the host's own per-target work, which must run on every step: its damage bonuses, status checks and `canBeHitBy` for each paired enemy. Its garbage, about 35 KB a step, is that work's, so the crowd's garbage per second did not fall.

The other two items the fix candidate named (docs/ROM_INTERNALS.md) were timed and left. Driven from Python with 31 proxies, median of 2,000 calls: `kssu_world` 1.7 µs (1.2 µs with none), `kssu_state` 0.3 µs, `kssu_hits` 0.6 µs, against 0.4 µs for a call that returns at once. The small vectors `kssu_world` allocates and the dozen `kssu_state` reads of a step are worth under 2 µs a step together.

## OAM and palette reads (#165)

After every original step the renderer reads the display control register, the 1 KiB of OAM, OBJ VRAM and 512 bytes of OBJ palettes, and the bridge's far-object check (#67) reads the OAM once more; the HUD reads engine B's OAM and a palette row while it picks up a new ability icon. Since #123 OBJ VRAM came as one block, but OAM and the palettes still took one bus read a byte. A fourth wrapper stage adds `Copy_GpuMemory`, which copies the palettes or the OAM as melonDS's own byte reads return them, zeros included for a half whose engine is powered off (docs/NDS_BRIDGE.md). `kssu_read_gpu` uses it for any 8-byte-aligned read inside either 2 KiB; other reads keep the bus.

Driven from Python on the new build, best of five runs of 2,000 calls: a 1 KiB OAM read takes 0.41 µs as a block and 3.62 µs a byte at a time (the same read one byte off), a 512-byte palette read 0.40 and 2.02 µs. Over 1,000 frames after 200, the renderer's and the HUD's reads together went from 0.036 to 0.028 ms a frame, and the original step did not change (2.82 and 2.84 ms on the interpreter).

Lab run, isolated profile, software drawing, `performance` power profile, 120 s, 2026-10-14 (`repros/gpu-165`, run `20261014-214926`, bridge `bc7bbe99…`), against the `movement` run of #160 (`20261014-132819`, the same runtime): `Read` 0.0421 → 0.0387 ms a step. Every other section, host fps (111.6 and 112.1) and the mod's work per original step (1.35 and 1.38 ms) stayed within what one run each separates. The saving is about 3 µs a step, as #160 expected. The run ended by its time limit with no fatal log and the real saves unchanged; its frames show Kirby drawn whole in his own colors.

Checks on the final tree: `python3 scripts/check.py` passes (26 + 6 Python tests, 470 rule assertions, the runtime builds). The bridge built from this branch against a private copy of the vendor tree (`repros/gpu-165`, sha256 `bc7bbe99a52fe96a26a3fb93cb5b2305f1530f0a27b618a1aa063e58406dfbce`): all 93 `tools/nds_tests.py` cases pass, and the 92 older ones return what the shared record holds. `tools/verify_renderer.py`: 29 cases, 2,997,289 pixels, 0 mismatched, 0 unsupported objects; `tools/verify_background.py`: 4 cases of 44,089,344 values, 0 mismatches; `tools/verify_native_init.py`: 64 cycles pass. The shared bridge, its record and the shared vendor checkout were not replaced: play gets the change after the merge with `python3 tools/nds_build.py` and a new stage. The runtime did not change, so an older bridge keeps the per-byte reads.

## The sprite draw hook (#175)

`KirbyHostVisuals` hooked `HSprite.draw` to skip every host sprite drawn from the outfit atlas: the arrival corpse, which uses the atlas before `Hero.initGfx`, the hero body, the death corpse and afterimage trails, all of which would show the outfit's static fallback pose. The host draws every sprite on screen through that one function, so the hook made each of them a call into the runtime, about 2 µs and 920 bytes ([What #123 changed](#what-123-changed)): 78.9 a frame with Kirby moving at the start of the Prisoners' Quarters, 135 in the `crowd` plan and about 200 wading in the Greenhouse. It was installed whenever the original runtime was enabled, so the Beheaded paid it too.

The outfit's in-game atlas page is now blank (`tools/build_outfit.py`), so those sprites draw nothing, and the hook is gone. The skin selector's preview rows read the fallback pose from a page of their own, `kirbySuperStarUltraPreview0.png`. `scripts/stage_runtime.py --original-runtime` refuses an outfit pack whose in-game page is not blank, since the runtime no longer hides it: play needs the pack rebuilt with `tools/build_outfit.py` and staged with `--outfit-pack`.

Lab runs, 2026-10-16: isolated profile `repros/perf-174-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile. "Before" is `main` with the lab tooling of #174 (runtime `a2a93f5a…`) and an outfit pack built from `main` (`8e44122d…`); "after" adds this branch (runtime `34ff593d…`) and the pack rebuilt from it (`2cbd0a47…`). The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement` | before | `20261016-151617`, `153025`, `174336` | 110.1 | 9.11 ms | 16.2 ms | 126.3 | 51 (7.5 ms) | 23 / 3 in 153 s | 108.6 |
| | after | `20261016-151918`, `152901` | 110.2 | 9.09 ms | 14.9 ms | 53.7 | 22 (6.3 ms) | 12 / 2 in 101 s | 29.8 |
| `crowd` | before | `20261016-153148`, `155744`, `173139` | 85.6 | 11.69 ms | 22.0 ms | 250.4 | 96 (7.0 ms) | 152 / 18 in 153 s | 481.7 |
| | after | `20261016-153312`, `173448` | 94.1 | 10.65 ms | 17.5 ms | 123.0 | 47 (5.6 ms) | 37 / 9 in 99 s | 340.8 |

Hooked calls fell by 78.8 a frame moving and 140.9 in the crowd, the garbage by 72.6 KB a frame moving (57%) and 127.4 KB in the crowd (51%), and .NET collections came less than half as often. Moving, the frame times moved less than one run of the same build differs from the next (102.1 to 116.4 fps for the three `before` runs). In the crowd the mean frame was 1.0 ms shorter and frames over 20 ms came 0.37 a second instead of 1.0; the slower `after` run (89.6 fps) was faster than the fastest `before` one (88.5), though the two `after` runs were 9 fps apart. Every `crowd` run played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage.

Behaviour, with traces and frame captures (`KIRBY_LAB_PERF=1`, the `movement` plan): Kirby's original state (position, speed, ability, health, graphic, flags and facing) and the host hero's position were the same as on the #174 build in all 2,907 original frames both runs reached (`20261016-170406` before, `170534` after; two runs of the #174 build, `170406` and `171656`, matched in all 3,000 they shared), and the plan's two hurtbox queries passed in both. Captured ten times a second from the start of play (`172903` before, `173003` after), the 47 moments both runs captured at the same original frame and animation frame show Kirby drawn the same and nothing of the outfit: in a 96-pixel square around Kirby the crops differ by 0.25 of 255 at the median, and the largest difference, 9.7, is the background, where the camera stood a few pixels apart. The arrival itself, captured ten times a second (`175605` before, `175705` after), looks the same in both: the cells cross the floor and rise, the head pops in a flash of light, and Kirby first shows in the same capture of `headPopStart` (its frames 20 and 19), with nothing of the outfit drawn before him. The 9 captures taken at the same animation frame in both differ by 0.59 of 255 at the median, the cells' particles and the light.

## Checks run

On the final tree, 2026-10-11:

- `python3 scripts/check.py`: passes (26 + 6 Python tests, 435 rule assertions, the runtime builds).
- The bridge built from this branch against a private copy of the vendor tree (sha256 `4485934e8fa96bd90a7b4948915205e1810d09643141baef5561791eec9aaeba`, the same on a second build): all 82 `tools/nds_tests.py` cases pass, and the 80 older ones return what the previous bridge's recorded results hold.
- `tools/verify_renderer.py`: 29 cases, 3,046,706 pixels, 0 mismatched, 0 unsupported objects. `tools/verify_background.py`: 4 cases of 44,236,800 values, 0 mismatches. `tools/verify_native_init.py`: 64 cycles pass.
- The lab runs above, 15 in all.

The shared bridge binary, its provenance and `oracle/bridge-test-results.json` were not replaced, and the shared vendor checkout was not patched: play needs `python3 tools/nds_build.py` and a new stage to get the bridge half. With the older bridge the runtime still works; it then keeps the per-byte read and draws both screens.

## Left as they are

Measured or seen in #123 and not changed.

- **The original frame, about 2.6 ms.** The bridge ran melonDS's interpreter. A recompiler was left out of #123: it changes the emulated timing that the recorded bridge evidence rests on. #153 added it behind a switch (The original on the recompiler, above).
- **A pile of enemies in recoil.** With the 30 fixture targets of the `crowd` plan stacked on one spot, the original frame takes about 6 ms. #150 found what that was: the first swing hit all of them and stopped the original for good, and the time was an ARM9 that never rests again, not the original's code for overlapping actors. #151 removed the stop ([the collision pool](#the-collision-pool-151)). Twenty stacked targets, which it survives, cost about 3.6 ms (the table above). Thirty proxies spread over the room cost about 0.9 ms together.
- **`HSprite.draw`.** `KirbyHostVisuals` hooked it to keep the Beheaded's graphics away from Kirby, and the host calls it 77–140 times a frame. #175 left the outfit's in-game page blank instead and removed the hook ([the sprite draw hook](#the-sprite-draw-hook-175)): 72.6 KB a frame less with Kirby moving, 127.4 KB in the crowd.
- **The texture upload.** A drawn frame still uploads the whole 1024×512 texture, about 0.6 ms in the lab.
- **The garbage collector mode** comes from DCCM's `runtimeconfig.json`. Overriding it from the environment (`DOTNET_gcServer=0`, `DOTNET_gcConcurrent=1`) changed nothing measurable.
- **A host shader made on first use.** The host logs `Shader missing from cache: shaderLinker_ad70e08c` the first time one sprite combination is drawn, here when a Zombie first hit Kirby. In the lab that one frame took 1.3 s. The same line is in 123 earlier lab and play logs back to 2026-10-04, so it is not new; what it costs on the GPU was not measured.

## Not verified

- Play in a window on the GPU, with vsync: no fps, pacing line or frame time from it. The lab has no frame cap and draws in software.
- That 60 fps holds in play, in every biome, in boss fights or over a long session.
- The game in motion. The verifiers compare the decoder and the background capture with the original output, and three still frames of the final build's lab runs were looked at (Kirby drawn whole, nothing left behind him). Nobody watched it play.
- The hooks being added and removed next to another DCCM mod that hooks the same host functions. This mod hooks none of them anywhere else.
- A held entity on the frames around a level change or a save restore beyond what the plans above exercise.
