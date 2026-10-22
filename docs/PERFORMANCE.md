# Performance

What the mod's work costs on each host frame, what #123 changed, and how to measure it.

Every number here comes from the offscreen lab (`scripts/launch_runtime.py`, software OpenGL, no frame cap) on the owner's machine on 2026-10-11, with the OS power profile on `performance`. The lab draws the game on the CPU, which play on the GPU does not, so lab fps is not play fps. What carries over is the time of each stage of the mod's own work and the garbage it makes. No windowed play was measured: this record does not establish 60 fps in play.

## The mod's frame

On a host frame that steps Kirby, `KirbyNdsProvider.OnFrame` exports the terrain, pairs host enemies with original proxies (`Prepare`), runs one original frame in melonDS (`Native`), applies its result (`Complete`), and then draws: the background capture, a read of the original GPU memory, the OBJ decoder, the texture upload and the equipment layer. Items, the HUD and audio follow. Outside that, the host calls the mod's hooks during its own update.

Three costs stood out on `main`.

- The original frame itself, about 3 ms. The bridge uses melonDS's interpreter.
- The read of OBJ VRAM after it, 1.3 ms: one bus read for each of 262,144 bytes.
- Hooks on host functions that run for every entity. With a handler installed, each call of a hooked function costs about 2 µs more and leaves about 920 bytes of managed garbage, whatever the handler decides (measured in the game on `Mob.canUpdate`: 4.4 µs and 1,840 bytes hooked at `Mob` and `Entity`, 0.77 µs and 56 bytes after the handlers are removed, 0.38 µs and nothing for a function never hooked). At the start of the Prisoners' Quarters the host called `Entity.canUpdate` 370–590 times and `Mob.canUpdate` 140–330 times a frame. That garbage became a pause of about 5 ms nearly five times a second (288 collections a minute in the movement runs below). This paragraph first said DCCM runs the server collector; in the game it is the workstation collector in its concurrent mode ([Host internals](HOST_INTERNALS.md#two-garbage-collectors), #174).

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

Since #174 it also counts the Beheaded's frames in a run without Kirby, lists every frame over 20 ms with what was in it (`perf-frames.json`), and takes these switches, all set in the same shell and ignored in play:

- `KIRBY_LAB_PERF=quiet`: no frame captures, trace or machine dumps, and the probes write their reports twice a second, so the figures are those of a played frame.
- `KIRBY_LAB_PERF_HOST=1`: times the host's own phases (the frame, events, update, `Level.update`, `Level.postUpdate`, drawing, texture uploads, present). It adds about 20 µs a frame.
- `KIRBY_LAB_PERF_HOOKS=1`: counts the calls of every hooked host function a frame, and how many of them found no handler.
- `KIRBY_LAB_PERF_ALLOC=1`: samples the runtime's allocations by type.
- `KIRBY_LAB_BARE=1`: loads the mod with none of its gameplay hooks, for a "Dead Cells alone" baseline.
- `KIRBY_LAB_SEED=<n>`: starts every new game with that seed, so runs before and after a change play the same level.
- `KIRBY_LAB_REAL_KEYS=1`: plays the input plan through the host's key table, as a keyboard would, instead of handing Kirby the plan's keys directly.

Since #184 the probe also writes `perf-arrival.json`: one row for every host frame from Kirby's activation to his 600th counted frame, with the frame's interval, its original steps, the time and .NET JIT time (`System.Runtime.JitInfo`) of each stage and host phase, the methods the game thread compiled, and the count of methods compiled on all threads so far. A row runs from one of the mod's frame marks to the next; the mark sits inside the host's update, so the host update in a row is the previous frame's. With `DOTNET_JitStdOutFile=<file>` and `DOTNET_JitDisasmSummary=1` in the same shell, the runtime lists every method it compiles in the order `JitInfo` counts them, so a row's methods can be named.

`launch_runtime.py --gpu` draws an offscreen test on the GPU instead of in software, and `--shader-cache` keeps the GL driver's shader cache in the lab's cache folder for that run.

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

## Kirby against the Beheaded (#174)

What a Dead Cells frame costs with and without Kirby. How the host runs that frame, and why its hooks cost what they do, is in [Host internals](HOST_INTERNALS.md).

Lab runs on 2026-10-16 in the isolated profiles `repros/perf-174-kirby` and `repros/perf-174-beheaded` of the private data root, `performance` power profile, one run each. Every run starts a new game with the same seed (`KIRBY_LAB_SEED=174`), so it plays the same level, and uses `KIRBY_LAB_PERF=quiet`: no frame captures, and the probes write their reports twice a second. "Dead Cells alone" has the mod loaded but idle (`KIRBY_LAB_BARE=1`, no gameplay hooks). "Beheaded" is the same game with the mod's hooks on and the Beheaded's outfit. Software drawing is llvmpipe; GPU drawing is the AMD Radeon R9 380 (radeonsi), offscreen and without vsync (`--gpu`). Runtime: `main` (`f6b691b`) with the #174 lab tooling; bridge `bae16c0e7dafbec06239e0212cac1b436ed25db01aaaeeb1d03d6eaf7aed0169`. The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports. Every run ended by its time limit with no fatal log and the real saves unchanged.

| Plan, drawing | Game | Host fps | Mean frame | Managed KB a frame | .NET collections (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement`, software | Dead Cells alone | 137.6 | 7.27 ms | 39.3 | 18 (1.8 ms) | 2 / 1 | 6.6 |
| | Beheaded | 125.8 | 7.95 ms | 134.6 | 52 (7.0 ms) | 3 / 1 | 103.8 |
| | Kirby | 108.1 | 9.25 ms | 126.5 | 47 (7.4 ms) | 14 / 1 | 108.7 |
| `movement`, GPU | Dead Cells alone | 1,853.4 | 0.54 ms | 21.1 | 143 (3.5 ms) | 2 / 2 | 6.0 |
| | Beheaded | 1,272.7 | 0.79 ms | 113.7 | 475 (6.7 ms) | 0 / 0 | not counted |
| | Kirby | 1,092.9 | 0.92 ms | 119.9 | 430 (7.2 ms) | 1 / 1 | 107.0 |
| `crowd`, software | Beheaded | 119.5 | 8.37 ms | 217.3 | 78 (6.9 ms) | 7 / 2 | not counted |
| | Kirby | 84.5 | 11.83 ms | 251.3 | 74 (7.3 ms) | 46 / 2 | 482.4 |
| `crowd`, GPU | Beheaded | 906.6 | 1.10 ms | 195.8 | 602 (6.7 ms) | 0 / 0 | not counted |
| | Kirby | 674.3 | 1.48 ms | 214.8 | 475 (6.8 ms) | 2 / 1 | not counted |
| `water`, wading in the Greenhouse, software | Beheaded | 54.5 | 18.34 ms | 280.2 | 37 (6.8 ms) | 204 / 6 | not counted |
| | Kirby | 47.3 | 21.15 ms | 277.3 | 32 (7.5 ms) | 927 / 11 | not counted |
| `water`, wading in the Greenhouse, GPU | Dead Cells alone | 1,211.2 | 0.83 ms | 21.8 | 75 (3.5 ms) | 2 / 2 | 6.0 |
| | Beheaded | 673.2 | 1.49 ms | 237.6 | 423 (7.1 ms) | 1 / 1 | 234.4 in a later run |
| | Kirby | 556.4 | 1.80 ms | 262.1 | 388 (7.2 ms) | 1 / 1 | not counted |

Runs `20261016-124219`, `123929`, `123108`, `124440`, `121811`, `123338`, `121130`, `123458`, `122051`, `121931`, `121510`, `121340`, `151256`, `122312`, `122212` and `151517`, in table order; 50 s of played frames for `movement` and `crowd`, 40–44 s for `water`. The `crowd` plan has no "Dead Cells alone" row: without the mod its 30 Zombies are free, and that is another fight.

What the table shows:

- **The mod's hooks cost the Beheaded too.** Against Dead Cells alone, the Beheaded allocates 95 KB more a frame moving and 216 KB more in the Greenhouse, and its .NET collections pause about four times as long (7.0 against 1.8 ms). On the GPU its frame is 0.25 ms longer moving and 0.66 ms longer in the Greenhouse. The `HSprite.draw` hook was on for every outfit.
- **Kirby adds the original's step.** The mod's whole step took 1.41 ms, of which the original frame in melonDS was 1.21 ms and `Prepare` 0.14 ms. The original runs at 60 Hz, so at 108 fps that is 0.55 steps a host frame. The mod's drawing took 0.15 ms a frame, the equipment layer 0.11 ms, the hero synchronization 0.10 ms and the input read 0.04 ms.
- **On the GPU everything is short.** Kirby's frame is 0.92 ms against 0.54 ms for Dead Cells alone, and a 7 ms collection still fits in a 60 Hz frame. What a 7 ms pause costs depends on the display: at 144 Hz a frame has 6.9 ms.

Where the time of a software-drawn `movement` frame goes, from the host phases (`KIRBY_LAB_PERF_HOST=1`, runs `20261016-122412` and `122613`):

| Phase | Kirby | Beheaded |
| :--- | ---: | ---: |
| Whole frame | 8.79 ms, 139 KB | 7.77 ms, 147 KB |
| Game update, with the mod's frame work | 1.82 ms, 58 KB | 0.93 ms, 70 KB |
| of it: `Level.update` | 0.56 ms, 29 KB | 0.51 ms, 29 KB |
| `Level.postUpdate` | 0.08 ms, 10 KB | 0.15 ms, 29 KB |
| Drawing the scene | 6.60 ms, 72 KB | 6.46 ms, 67 KB |
| Scene sync, texture uploads, present | 0.30 ms | 0.23 ms |

Drawing is three quarters of a software-drawn frame, and play draws on the GPU. The drawing's garbage is all the `HSprite.draw` hook's: no other managed code runs while the scene is drawn.

Frames over 20 ms, by what took the time (the probe's classification of each slow frame, lab frames included):

| Run | Over 20 ms | .NET collection | Host | Lab report | Mod | First Kirby frame | HashLink collection | Over 33 ms |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Kirby, `movement`, software | 19 | 11 | 3 | 4 | 0 | 1 | 0 | 2 |
| Beheaded, `movement`, software | 7 | 1 | 3 | 3 | – | – | 0 | 1 |
| Dead Cells alone, `movement`, software | 3 | 0 | 2 | 1 | – | – | 0 | 1 |
| Kirby, `crowd`, software | 57 | 35 | 11 | 6 | 3 | 1 | 1 | 4 |
| Kirby, `water`, software | 1,058 | 32 | 897 | 128 | 0 | 1 | 0 | 18 |
| Beheaded, `water`, software | 254 | 37 | 176 | 41 | – | – | 0 | 10 |

- **.NET collections** are most of Kirby's slow frames outside water: 6–8 ms pauses on top of a 9–12 ms frame.
- **The first Kirby frame** took 47–64 ms in every run: 45–48 ms of mod work, 12–15 ms of it the first original step and the rest the mod's code compiled on first use.
- **The host's own slow frames** are drawing in software: one 90 ms frame about nine seconds into the Beheaded's and Dead Cells alone's `movement` runs (89.6 ms of it drawing where the phases were timed), and the Greenhouse's water, which llvmpipe draws at 18–21 ms a frame. On the GPU the same water ran at 556–1,211 fps with one or two slow frames.

The reference the owner gave, `repros/merge-20261015` run `20261015-053030` (software drawing, frame captures on), allocated 162 KB a frame and had 8 frames over 33 ms in 44 s. The quiet `movement` run above, on `main` with the #174 tooling, allocated 126.5 KB a frame without the lab's work (130 KB with it) and had 2 frames over 33 ms in 50 s, one of them a lab report. Most of the difference is the captures.

Not measured: windowed play with vsync, more than one run of each, and the Beheaded's hooked calls on the GPU outside the Greenhouse.

## Found inside Dead Cells, ranked (#174)

By what each costs a Kirby run; how each works is in [Host internals](HOST_INTERNALS.md).

| # | What | Where it shows | Evidence |
| ---: | :--- | :--- | :--- |
| 1 | A hooked host function crosses into .NET on every call: about 2 µs and 920 B, whatever the handler does | 108.7 calls a frame with Kirby moving, 482 in the crowd; most of the 126–251 KB a frame he allocates | IL, hook counter |
| 2 | Each of those calls leaves a finalizable object, so its garbage cannot die young: almost every .NET collection is a blocking generation-1 one of 6–8 ms | 11 of the 19 frames over 20 ms moving, 35 of 57 in the crowd; 1.8–3.5 ms pauses without the hooks | IL, runs |
| 3 | Every sprite is drawn through one function, `HSprite.draw`, 79 times a frame moving and about 200 in the Greenhouse | a hook there was half of all the garbage; on the GPU in the Greenhouse it took the Beheaded's frame from 0.83 to 1.49 ms | bytecode, runs |
| 4 | Removing a handler leaves the function detoured into .NET for the session (0.77 µs and 56 B a call); DCCM's direct route back to the original is never taken by DCCM itself, and turning it back on crashed the game | `Mob.canUpdate` and `Entity.canUpdate`, 500–900 calls a frame, after the first held enemy; 3 of 4 `crowd` runs crashed | IL, runs (#123, #178) |
| 5 | Entities update in a strict order: every `preUpdate` and `fixedUpdate` (30 Hz) inside `Level.update`, every `postUpdate` and `spriteUpdate` after it | two hurtbox hooks ran after their projection had ended, 5 calls a frame moving and 34 in the crowd, for nothing | bytecode, hook counter |
| 6 | A static Haxe function read through a proxy builds a delegate on every call; `AsHaxeString` allocates in both heaps | Kirby's input allocated 25 KB a frame more with real keys than with the lab's injected input | IL, runs |
| 7 | Drawing is most of a software-drawn frame (6.6 of 8.8 ms), and one draw takes about 90 ms, probably a shader made on first use (not traced) | a 90 ms frame in every Beheaded movement run; on the GPU the whole host frame is 0.5–0.8 ms | runs |
| 8 | Levels are generated and loaded on the main thread; the game forces one HashLink full collection a level | loads, not play inside a level; HashLink's collector costs little in a level (5–17 KB a frame) | bytecode, runs |
| 9 | The first frame with Kirby takes 47–64 ms: the mod's code compiling on first use and a 12–15 ms first original step | once a session | runs |

## Fixes, ranked (#174)

From the costs above, largest expected gain first. The expected gains are estimates from per-call costs and hook counts. The last column is what each fix measured alone against the #174 build, two runs each in software against three `before` runs (`repros/perf-174-kirby`, seed 174, 2026-10-16); each fix's PR records its runs and adds its own section here.

| # | Cost now | Fix | Expected gain | Issue | Measured |
| ---: | :--- | :--- | :--- | :--- | :--- |
| 1 | The `HSprite.draw` hook: 79 calls a frame moving, 135 in the crowd, about 200 in the Greenhouse; about 70 KB a frame moving, more than half of all the garbage | Draw the outfit's host sprites from a blank atlas page and drop the hook | about −80 hooked calls and −70 KB a frame moving; .NET collections about half as often | #175 | −78.8 hooked calls and −72.6 KB a frame moving (126.3 → 53.7), −140.9 and −127.4 KB in the crowd; .NET collections 51 → 22 and 96 → 47 a minute |
| 2 | After the first held entity, `Mob.canUpdate`, `Entity.canUpdate` and four more stay on DCCM's managed path: +0.4 µs and +56 B on each of 500–900 calls a frame | Take DCCM's detour off them when the held-entity hooks are removed, and write it again before the next hold | −30 to −50 KB and −0.2 to −0.35 ms a frame for the rest of the session after the first hold | #178 | in the crowd 481.7 → 201.6 hooked calls a frame (all 280 with no handler gone), −17.1 KB a frame (250.4 → 233.3), .NET collections 96 → 79 a minute, the mean frame −0.87 ms |
| 3 | Kirby's input read: a new delegate for every key test, arrays and Haxe strings, about 25 KB a frame with real keys | Read the key table once per read and keep the shield tag | about −25 KB a frame with a keyboard or a pad | #176 | with keys through the host's key table, a read 73 µs and 13,948 B → 19 µs and 8 B; −28.6 KB a frame (150.8 → 122.2) |
| 4 | Two hurtbox hooks that only run after the projection has ended, and second handlers on `Hero.preUpdate` and `Hero.fixedUpdate` | Drop them; one handler each | −5 hooked calls a frame moving, −34 in the crowd | #177 | −4.9 hooked calls and −6.0 KB a frame moving, −30.8 and −32.6 KB in the crowd |
| 5 | The other always-on hooks: `getMovementSticks` 7.3, `HealFlask.isEnabled` 3.3, `hasUnlockedItem` 2, `updateWeapon` 2 and `updateHeadFx` 1 a frame | Replace each with a state set once at activation, or install it only while needed | about −15 hooked calls and −15 KB a frame | #185–#189 | each alone against `main` at `28f4086` (`repros/perf-185-kirby`, 2026-10-17), its hook gone from the counts: hooked calls a frame moving −7.3 (#185), −3.3 (#186), −2.1 (#187), −2.1 (#188) and −1.0 (#189), −15.8 added up, and −15.7 KB a frame (44.6 before); in the crowd −16.4 calls and −18.1 KB (69.5 before). The five were not run together |
| 6 | The first Kirby frame, 47–64 ms | Compile the mod's hot paths ahead of time | about −30 ms once a session, during the arrival | #184 | against `main` with the four fixes (`repros/perf-184`, 2026-10-17): 53.6–54.6 → 38.3–39.3 ms, the JIT in it 30.8–31.3 → 14.7–14.9 ms; played frames unchanged |

## The four fixes together (#174)

#175, #176, #177 and #178 on one branch (`measure/all2`: the #174 lab tooling and the four fix branches merged; runtime `f878decf…`, outfit pack `2cbd0a47…`), against the #174 build (`a2a93f5a…`, pack `8e44122d…`). Lab runs on 2026-10-16 in `repros/perf-174-kirby`, as in the table above, two runs each unless said. The frames over 20 and 33 ms leave out the lab's report frames.

| Plan, drawing | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement`, software | before | `20261016-151617`, `153025`, `174336` | 110.1 | 9.11 ms | 16.2 ms | 126.3 | 51 (7.5 ms) | 23 / 3 in 153 s | 108.6 |
| | all four | `20261016-170048`, `174208` | 119.5 | 8.37 ms | 12.6 ms | 43.9 | 21 (5.9 ms) | 3 / 2 in 97 s | 24.7 |
| `crowd`, software | before | `20261016-153148`, `155744`, `173139` | 85.6 | 11.69 ms | 22.0 ms | 250.4 | 96 (7.0 ms) | 152 / 18 in 153 s | 481.7 |
| | all four | `20261016-163343`, `174040` | 102.4 | 9.77 ms | 15.4 ms | 68.5 | 28 (5.0 ms) | 18 / 7 in 97 s | 32.0 |
| `movement`, GPU | before | `20261016-164133`, `164547` | 1,111.2 | 0.90 ms | 2.7 ms | 119.9 | 481 (7.3 ms) | 2 / 2 in 109 s | 107.0 |
| | all four | `20261016-164258`, `164423` | 1,628.2 | 0.61 ms | 2.1 ms | 38.3 | 232 (5.6 ms) | 0 / 0 in 109 s | 23.1 |
| `water`, GPU (wading in the Greenhouse) | before | `20261016-164712`, `174504`, `174927` | 568.5 | 1.76 ms | 10.5 ms | 260.0 | 533 (7.7 ms) | 6 / 3 in 194 s | 257.9 |
| | all four | `20261016-174631`, `174759` | 1,036.8 | 0.96 ms | 2.5 ms | 38.5 | 153 (5.7 ms) | 2 / 2 in 130 s | 23.1 |

- **Garbage.** A Kirby frame made 43.9 KB moving in software, against 126.3 before and 39.3 for Dead Cells alone with the mod idle ([Kirby against the Beheaded](#kirby-against-the-beheaded-174)), and 68.5 KB in the crowd, against 250.4. On the GPU, moving, 38.3 KB against 119.9.
- **Hooked calls** fell to about 25 a frame moving: 8 once a frame for DCCM's events and the hooks the mod works in, 15.5 for the always-on hooks of row 5 of "Fixes, ranked", and about one for 30 Hz hooks; in the crowd, about 7 more for the enemies Kirby holds.
- **.NET collections** came less than half as often (51 → 21 a minute moving and 96 → 28 in the crowd in software; on the GPU, where frames come ten times as fast, 481 → 232 moving and 533 → 153 wading), and their median pause fell from 7–8 ms to 5–6 ms: fewer finalizable objects survive each one.
- **Frame time.** In software the mean frame fell by 0.74 ms moving (110.1 → 119.5 fps) and by 1.92 ms in the crowd (85.6 → 102.4 fps), and every run with the fixes was faster than every run without: moving 119.1 and 120.0 fps against 102.1 to 116.4, in the crowd 100.7 and 104.1 against 83.2 to 88.5. Drawing, most of a software frame, is unchanged. On the GPU the mean frame fell by a third moving (0.90 → 0.61 ms) and by 45% wading in the Greenhouse (1.76 → 0.96 ms): 565 to 576 fps in the three runs without the fixes, 1,035 and 1,038 with them.
- **Slow frames.** Frames over 20 ms came 0.03 a second moving in software instead of 0.15, and 0.19 in the crowd instead of 1.0. Of the frames over 33 ms left, one a run is the first Kirby frame (row 6 of "Fixes, ranked", not done here); in the crowd, the frame the lab makes the 31 zombies appear in and the frame Kirby's sword first hits 30 of them at once, 570 to 650 KB of HashLink objects; in the Greenhouse, one 61 ms host frame a run with no mod work in it.

The owner's reference, run `20261015-053030` (162 KB a frame, 8 frames over 33 ms in 44 s, captures on), compares with the `movement` rows: with the four fixes the same plan made 43.9 KB a frame against 126.3 before, and one frame over 33 ms a run, the first Kirby frame.

Every run above ended by its time limit with no fatal log and the real saves unchanged. Runs during which other programs used more than 0.6 cores on average (another session's browser check at 16:28–17:02, another program's benchmark after 17:32) were run again and are in no table here: `20261016-162824`, `162949`, `163218`, `164837`, `165019`, `165144`, `165601`, `165729`, `165920`, `173307` and `173627`.

## The bridge on its own (#174)

A private script drove the shared bridge (`bae16c0e…`) from Python the way the runtime does: the recompiler, the ARM9 at twice the DS clock, the main screen only and the idle upper screen skipped, with a repeating walk, jump, walk back and inhale. Unpaced for 150 s, one step took 0.940 ms at the median, 1.021 ms on average and 1.009 ms at the 95th percentile (146,604 steps; a stack sampler was attached for part of that time). In the game the same step, `Native`, averaged 1.20 ms in the `movement` runs.

120 stack samples of that process (`gdb -batch`), 96 of them inside `kssu_step`:

| Where the step was | Share of the 96 |
| :--- | ---: |
| Recompiled ARM code | 50.0% |
| Timers and the event scheduler | 16.7% |
| The sound unit (SPU) | 12.5% |
| Other emulation | 11.5% |
| 2D drawing | 7.3% |
| 3D | 2.1% |

The mod uses every part: Kirby's sound effects come from the SPU, and the background capture from engine A's 2D drawing. About one sample in six was a memory access from recompiled code that took melonDS's slow path (`SlowRead9`, `SlowWrite9`). Fast memory would shorten those, but it needs a SIGSEGV handler and the bridge runs inside the game's process, which has its own; it stays off (`tools/nds_jit_patch.py`, #153). No bridge change came out of this, and the shared bridge was only read.

## The head effects (#189)

`KirbyHostVisuals` hooked `HeroHead.updateHeadFx` to keep the head's particle emitter, the Beheaded's spirit flame, silent while Kirby played. The host calls it once a frame from `Hero.beforeRender`, and each call went into the runtime, about 2 µs and 920 bytes ([What #123 changed](#what-123-changed)).

In the game's bytecode `updateHeadFx` places the head's sprites and sets the eye's visibility, then returns before its emitter when the head mode is none or `heroHasHead` is false. `Hero.initColorMap` sets `heroHasHead` from the skin's row, `PolloPower.onEnd` sets it from the same row, and the head and skin selectors set it while the player chooses. The Kirby outfit's row has had no head since its first version (`tools/build_outfit.py`, 2026-10-04); the hook came a day later, for a flame its comment said the arrival cinematic still lit, and the arrival check below shows none without it. The hook is gone. `ClearHead`, at activation, still kills the head's particles and now also sets `heroHasHead` false, so a session never starts with the flame on. `Fx.heroHeadPopFinal` is still skipped for the Kirby outfit.

Lab runs, 2026-10-17, one batch for the five changes of row 5 of "Fixes, ranked" (#185–#189): isolated profile `repros/perf-185-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), 80 s, `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile, outfit pack `2cbd0a47…`. "Before" is `main` at `28f4086` (runtime `cc05a678…`), three runs a plan; "after" is this change on the same commit (`dde7288f…`), two runs. Each plan ran in the order before, #185 to #189, before, #189 to #185, before, so each change's runs fall between `before` runs. The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement` | before | `20261017-013440`, `014637`, `020450` | 109.5 | 9.15 ms | 14.5 ms | 44.6 | 27 (4.5 ms) | 10 / 3 in 145 s | 24.9 |
| | after | `20261017-014509`, `014944` | 110.5 | 9.05 ms | 14.1 ms | 43.5 | 22 (5.8 ms) | 3 / 0 in 97 s | 23.9 |
| `crowd` | before | `20261017-020709`, `021915`, `024937` | 99.4 | 10.07 ms | 15.6 ms | 69.5 | 40 (3.6 ms) | 21 / 9 in 145 s | 32.3 |
| | after | `20261017-021633`, `023106` | 100.2 | 9.98 ms | 15.6 ms | 68.1 | 27 (5.0 ms) | 16 / 7 in 99 s | 31.1 |

The hook's call a frame is gone, moving and in the crowd, and nothing else in the counts moved beyond the held-enemy hooks' spread in the crowd (6.6 to 7.0 calls a frame). The garbage fell by 1.1 KB a frame moving and 1.4 in the crowd, about what one call of 920 bytes makes (0.9 KB). At about 2 µs a call the hook cost about 2 µs of a 9 to 10 ms frame, too little for these runs to show: moving, this change's two runs (112.0 and 108.9 fps) fell within the three `before` runs (104.7 to 114.6), and in the crowd they made 99.5 and 100.9 fps against 98.6 to 99.8, where the ten runs of the five changes spread from 97.0 to 104.1. .NET collections are not compared: in the `before` runs they came more often from run to run through the batch (22, 25 and 32 a minute moving, 32, 38 and 48 in the crowd), with shorter pauses, whatever the build. Every `crowd` run played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage, and each logged the detach. Every run ended by its time limit with no fatal log and the real saves unchanged. Runs during which other programs used more than 1.5 cores on average, or another lab game ran, were run again and are in no table here: `before` `20261017-020208` (3.93 cores), and the first two tries of `013440` (`013144`, 1.00 cores, and `013312`, 0.61), run again under the batch's first limit of 0.6 cores; and this change's first try of its second `crowd` run, `022341` (7.18 cores). The three warm-up runs before the batch (`012436`, `012752`, `012931`) are not counted. In the runs kept, other programs used 0.2 to 1.4 cores of the machine's 28.

Behaviour, with the `arrival` plan in the Prisoners' Quarters (`KIRBY_LAB_ARRIVAL_LEVEL=PrisonStart`, captures ten times a second, 45 s, seed 174; `repros/live-189-kirby`, run `20261017-030657`, against `main` in `repros/live-base-arrival`, `20261017-031016`): the arrival trace, which now records the host head, had `heroHasHead` false, the eye hidden and no head particle alive in all 1,790 host frames from activation, while the head's mode went from none to electric (the head pop) to normal. Both runs played the same animations (`rebirth`, `idle`, `headPopStart`, `headPopLoop`, `headPopEnd`, `idle`). The 24 captures both took at the same original frame and animation frame differ by 0.37 of 255 at the median and 1.94 at most; the largest differences, near the centre in `headPopStart`, are the cells' electric burst. After the head pop neither run shows a flame above Kirby. Not run: `PolloPower` during a Kirby session, the owner's launcher.

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

## The Collector's unlock check (#187)

In a Kirby run the Collector hides the entries that do nothing for Kirby, Health Flask I–IV among them (#99, #101). `ItemMetaManager.canInvestOnItem` refuses every investment until Health Flask I is unlocked, so while that check runs `KirbyCollector` answers `hasUnlockedItem` with true for a hidden entry. The handler on `hasUnlockedItem` was always installed, and the host asks it twice a frame for its own reasons; each call went into the runtime, about 2 µs and 920 bytes ([What #123 changed](#what-123-changed)), and the handler then let the host's answer through.

The handler is now added by the first `canInvestOnItem` check of a Kirby run and taken off between host frames 30 frames after the last one, with DCCM's detour (`DccmDetours`, [the detour after the last handler](#the-detour-after-the-last-handler-178)), as `KirbyEnemyHooks` does for held entities. A Collector panel that checks its entries again within those 30 frames keeps it.

Lab runs, 2026-10-17, one batch for the five changes of row 5 of "Fixes, ranked" (#185–#189): isolated profile `repros/perf-185-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), 80 s, `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile, outfit pack `2cbd0a47…`. "Before" is `main` at `28f4086` (runtime `cc05a678…`), three runs a plan; "after" is this change on the same commit (`ed57c19e…`), two runs. Each plan ran in the order before, #185 to #189, before, #189 to #185, before, so each change's runs fall between `before` runs. The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement` | before | `20261017-013440`, `014637`, `020450` | 109.5 | 9.15 ms | 14.5 ms | 44.6 | 27 (4.5 ms) | 10 / 3 in 145 s | 24.9 |
| | after | `20261017-014213`, `015433` | 113.4 | 8.82 ms | 13.3 ms | 42.1 | 27 (3.9 ms) | 1 / 1 in 97 s | 22.8 |
| `crowd` | before | `20261017-020709`, `021915`, `024937` | 99.4 | 10.07 ms | 15.6 ms | 69.5 | 40 (3.6 ms) | 21 / 9 in 145 s | 32.3 |
| | after | `20261017-021225`, `023944` | 99.2 | 10.08 ms | 15.8 ms | 66.9 | 25 (4.9 ms) | 15 / 6 in 99 s | 30.2 |

The hook's 2 calls a frame are gone, moving and in the crowd, with no call left on the detour, and nothing else in the counts moved beyond the held-enemy hooks' spread in the crowd (6.6 to 7.0 calls a frame). The garbage fell by 2.5 KB a frame moving and 2.6 in the crowd, about what 2 calls of 920 bytes make (1.8 KB). At about 2 µs a call the hook cost about 4 µs of a 9 to 10 ms frame, too little for these runs to show: moving, this change's two runs (114.5 and 112.3 fps) fell within the three `before` runs (104.7 to 114.6), and in the crowd they made 101.5 and 97.0 fps against 98.6 to 99.8, where the ten runs of the five changes spread from 97.0 to 104.1. .NET collections are not compared: in the `before` runs they came more often from run to run through the batch (22, 25 and 32 a minute moving, 32, 38 and 48 in the crowd), with shorter pauses, whatever the build. Every `crowd` run played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage, and each logged the detach. Every run ended by its time limit with no fatal log and the real saves unchanged. Runs during which other programs used more than 1.5 cores on average, or another lab game ran, were run again and are in no table here: `before` `20261017-020208` (3.93 cores), and the first two tries of `013440` (`013144`, 1.00 cores, and `013312`, 0.61), run again under the batch's first limit of 0.6 cores; and this change's first `movement` run, `014044` (1.82 cores). The three warm-up runs before the batch (`012436`, `012752`, `012931`) are not counted. In the runs kept, other programs used 0.2 to 1.4 cores of the machine's 28.

Behaviour, with the Collector check (`--input-plan ability --probe-collector`, 45 s, seed 174; `repros/live-187-kirby`, run `20261017-030331`, against `main` in `repros/live-base-collector`, `20261017-030442`): both gave the same counts, gates and lists (the host's count 0 and Kirby's 14; Random Starter Shield and Gold Reserves closed to the host's check and open to Kirby's; none of the twelve hidden entries listed and all five kept ones), and the handler was off before the checks, stayed off through the host's own, came on with Kirby's first and was still on while the open panel listed its entries ([Runtime launch](RUNTIME_LAUNCH.md#collector-filter-2026-10-08)). Not seen: the handler coming off again, since the panel stays open to the end of the run; in the `movement` and `crowd` runs it was never on. Not run: the owner's launcher.

## The detour after the last handler (#178)

`KirbyEnemyHooks` stops the host from moving an enemy, projectile or item that the original engine holds. Its six hooks are added when something becomes held and removed between host frames once nothing of that kind has been held for 30 frames ([What #123 changed](#what-123-changed)): `Mob.canUpdate` and `Mob.fixedUpdate` for enemies; `Entity.canUpdate`, `Bullet.fixedUpdate` and `Grenade.fixedUpdate` for the other kinds; `Entity.updatePositionXY` for positions. DCCM 35.13.6 removes a handler by taking it off its list and nothing more. The function stays detoured into .NET for the rest of the session, and each call pays 0.77 µs and 56 bytes, against 0.38 µs and nothing for a function never hooked. In the `crowd` plan that was 280 calls a frame with no handler at all, 259 of them `Mob.canUpdate`.

The first version of this change put each such function back on the callback's direct route to the original, the value DCCM's manager sets before its first handler. The game then crashed, a segmentation fault after the held enemies were let go, in 3 of the 4 `crowd` runs that had it (`20261016-153722`, `153826` and `153945`; `153559` ended normally). DCCM itself never takes that route: the manager is made by the first `+=`, which clears it in the same call. Why it crashed was not traced, and that version was dropped.

`DccmDetours` undoes the detour instead. When `KirbyEnemyHooks.Relax` removes a group, each of its functions that has no handler left, and whose callback still calls DCCM's own adapter (no HarmonyX processor in its place), has its native detour undone (`NativeHooks.HookHandle.Disable`) and runs as it did before it was hooked. Before `Hold` adds the group again, the detour is written again (`Enable`). MonoMod frees the trampoline to the original when it undoes a detour and makes a new one when it writes it, so the manager's closure over the original, made once in its constructor, is made again from the new trampoline. The manager's fields are private and reached by reflection; when they are missing nothing changes, and the log says so once. A function another mod still has a handler on keeps its detour; a handler another mod adds while the detour is off is not called until Kirby next holds something of that kind and the detour is written again. The first detach logs `DCCM detour taken off 2 of 2 unhooked host functions.`

Lab runs, 2026-10-16: isolated profile `repros/perf-174-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), the `crowd` plan, `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile. "Before" is `main` with the lab tooling of #174 (runtime `a2a93f5a…`); "after" adds this branch (`8439a9a2…`). The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame | of them with no handler |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `crowd` | before | `20261016-153148`, `155744`, `173139` | 85.6 | 11.69 ms | 22.0 ms | 250.4 | 96 (7.0 ms) | 152 / 18 in 153 s | 481.7 | 280.0 |
| | after | `20261016-155620`, `173912` | 92.5 | 10.82 ms | 19.9 ms | 233.3 | 79 (7.4 ms) | 69 / 5 in 101 s | 201.6 | 0.0 |

Every call that crossed into .NET with no handler waiting is gone, and with it 280 of the 482 hooked calls a frame. The garbage fell by 17.1 KB a frame, about what 280 calls of 56 bytes make (15.7 KB), and .NET collections came 79 times a minute instead of 96. The mean frame was 0.87 ms shorter and frames over 20 ms came 0.68 a second instead of 1.0; the slower `after` run (89.6 fps) was faster than the fastest `before` one (88.5), though the two `after` runs were 6 fps apart. The garbage is less than the 30 to 50 KB expected in "Fixes, ranked", which assumed 500 to 900 such calls a frame: with 31 zombies there were 280.

Every `crowd` run with this change ended by its time limit, with no fatal log and the real saves unchanged: the two above, two that other programs disturbed and are left out of the table (`20261016-162949`, `173627`), and the four of all four fixes together (`162824`, `163343`, `172248`, `174040`). Each played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage, and each logged the detach.

## The weapon update (#188)

`KirbyInput` runs the hero's host update with the direction bindings set to none, and its handler on `HeroWeaponsManager.updateWeapon` gave them back to the weapon update inside it, so a weapon that reads the held direction finds the player's. The host calls `updateWeapon` twice a frame, once for each weapon slot, and each call went into the runtime, about 2 µs and 920 bytes ([What #123 changed](#what-123-changed)).

In the game's bytecode `updateWeapon` reads the direction bindings and the sticks only inside `if (w.isCharging())`, and `Weapon.isCharging` is the current skill's charge being under way; while it charges, the host turns the hero to the held direction (`Weapon.allowTurnDuringCharge`, true for every weapon but `RichterVampireKiller`, `TPSword`, `Trident` and `WiggleWhip`) and calls `onOwnerTurned`. In a Kirby session the only host weapon use left is the retained shield ([Facing and left parry](RUNTIME_LAUNCH.md#facing-and-left-parry-19)): Kirby's own attacks never start a host weapon. The handler is now added in a frame in which the shield's binding is down, or while the assist mode's automatic attack is on, and taken off between host frames once 30 frames have passed with neither and no main weapon charging, with DCCM's detour (`DccmDetours`, [the detour after the last handler](#the-detour-after-the-last-handler-178)).

Lab runs, 2026-10-17, one batch for the five changes of row 5 of "Fixes, ranked" (#185–#189): isolated profile `repros/perf-185-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), 80 s, `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile, outfit pack `2cbd0a47…`. "Before" is `main` at `28f4086` (runtime `cc05a678…`), three runs a plan; "after" is this change on the same commit (`60951cd6…`), two runs. Each plan ran in the order before, #185 to #189, before, #189 to #185, before, so each change's runs fall between `before` runs. The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement` | before | `20261017-013440`, `014637`, `020450` | 109.5 | 9.15 ms | 14.5 ms | 44.6 | 27 (4.5 ms) | 10 / 3 in 145 s | 24.9 |
| | after | `20261017-014341`, `015221` | 112.3 | 8.91 ms | 13.0 ms | 42.4 | 26 (3.9 ms) | 2 / 2 in 99 s | 22.8 |
| `crowd` | before | `20261017-020709`, `021915`, `024937` | 99.4 | 10.07 ms | 15.6 ms | 69.5 | 40 (3.6 ms) | 21 / 9 in 145 s | 32.3 |
| | after | `20261017-021455`, `023240` | 100.8 | 9.92 ms | 15.3 ms | 66.8 | 26 (4.9 ms) | 17 / 6 in 99 s | 30.2 |

The hook's 2 calls a frame are gone, moving and in the crowd, with no call left on the detour, and nothing else in the counts moved beyond the held-enemy hooks' spread in the crowd (6.6 to 7.0 calls a frame). The garbage fell by 2.2 KB a frame moving and 2.7 in the crowd, about what 2 calls of 920 bytes make (1.8 KB). At about 2 µs a call the hook cost about 4 µs of a 9 to 10 ms frame, too little for these runs to show: moving, this change's two runs (114.6 and 110.0 fps) fell within the three `before` runs (104.7 to 114.6), and in the crowd they made 101.2 and 100.4 fps against 98.6 to 99.8, where the ten runs of the five changes spread from 97.0 to 104.1. .NET collections are not compared: in the `before` runs they came more often from run to run through the batch (22, 25 and 32 a minute moving, 32, 38 and 48 in the crowd), with shorter pauses, whatever the build. Every `crowd` run played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage, and each logged the detach. Every run ended by its time limit with no fatal log and the real saves unchanged. Runs during which other programs used more than 1.5 cores on average, or another lab game ran, were run again and are in no table here: `before` `20261017-020208` (3.93 cores), and the first two tries of `013440` (`013144`, 1.00 cores, and `013312`, 0.61), run again under the batch's first limit of 0.6 cores. The three warm-up runs before the batch (`012436`, `012752`, `012931`) are not counted. In the runs kept, other programs used 0.2 to 1.4 cores of the machine's 28.

Behaviour, with the new `shield` plan (`--input-plan shield`, 50 s, seed 174; `repros/live-188-kirby`), which equips the starting shield (`StartShield`) through the inventory, holds its binding through the host's key table for 120 original frames with Left held from the 40th to the 80th, and records the handler, the shield's charge, the direction keys the host reads inside the masked update and the shield's turns. Run `20261017-031430`, on this branch moved onto `main` at `9706475` (runtime `75337a1a…`): the handler was off before the press and on in all 118 frames polled while the shield was held, in all of which the shield charged; the host read the real direction keys 1,074 times there and turned the shield 40 times while Left was held; the handler came off 16 original frames after the release (30 host frames) and stayed off. The first run (`20261017-030540`, runtime `60951cd6…`) saw the same but was marked failed: the plan counted presses per input read and held frames per original frame. That count was fixed and the plan run again ([Runtime launch](RUNTIME_LAUNCH.md#facing-and-left-parry-19)). Not run: the assist mode's automatic attack, a charge from anything but the shield, the owner's launcher.

## The input read (#176)

`KirbyInput` turns the host's controls into the DS keys the original reads, about twice a host frame. For each action it asked the host for the controller's bindings again, and tested each bound key through `dc.hxd.Key.Class.isDown`. A static Haxe function read through a proxy's `Class` is a new delegate on every access, with its argument boxed, so each test made garbage. With the lab's injected input most tests stop early; with keys pressed through the host's key table, as a keyboard's are (`KIRBY_LAB_REAL_KEYS=1`), every bound key is tested.

One read now takes the binding arrays, the input modes and the host's key table (`hxd.Key.keyPressed`) once, and tests each key against that table. In the game's bytecode `Key.isDown(code)` is `keyPressed[code] > 0`, and false past the end of the table; the read keeps both. Controller buttons still go through `Controller.padIsDown`, an instance method that allocates nothing.

Lab runs, 2026-10-16: isolated profile `repros/perf-174-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), the `movement` plan, `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile. "Before" is `main` with the lab tooling of #174 (runtime `a2a93f5a…`); "after" adds this branch (`ce6c7971…`). Two runs each, and a third `before` run with the lab's input; the frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Input | Build | Runs | Host fps | Mean frame | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Input read |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| the lab's | before | `20261016-151617`, `153025`, `174336` | 110.1 | 9.11 ms | 126.3 | 51 (7.5 ms) | 23 / 3 in 153 s | 20 µs, 1,704 B |
| | after | `20261016-152041`, `152738` | 110.0 | 9.09 ms | 122.4 | 49 (7.9 ms) | 14 / 1 in 101 s | 11 µs, 14 B |
| keys (`KIRBY_LAB_REAL_KEYS=1`) | before | `20261016-163507`, `164008` | 114.0 | 8.77 ms | 150.8 | 64 (6.6 ms) | 7 / 3 in 103 s | 73 µs, 13,948 B |
| | after | `20261016-163631`, `163844` | 114.2 | 8.76 ms | 122.2 | 52 (7.6 ms) | 10 / 3 in 105 s | 19 µs, 8 B |

With keys, a read went from 73 µs and 13.9 KB to 19 µs and 8 bytes: 28.6 KB less garbage a frame and 12 fewer .NET collections a minute. The frame times moved less than one run of the same build differs from the next.

Behaviour, with traces and frame captures (`KIRBY_LAB_PERF=1`, the `movement` plan): with the lab's input, which is keyed on the original's frames, all four fixes together (`20261016-171529`) matched the #174 build (`170406`) in Kirby's original state and the host hero's position in all 2,930 original frames both runs reached. With keys through the host's key table (`KIRBY_LAB_REAL_KEYS=1`) a press is timed in host milliseconds, so the original frame that first sees it depends on frame timing: two runs of the #174 build (`171952`, `175309`) differed from each other in 331 of 2,968 original frames, and this branch's two runs (`172120`, `175437`) differed from them in 221 to 227, and from each other in none of 2,976. The plan's two hurtbox queries passed in every run.

## The movement sticks (#185)

`KirbyInput` runs the hero's host update with the movement bindings set to none, so the host does not move the hero that Kirby's original moves. A stick set to move the hero would still have moved it, so a hook on `ControllerAccess.getMovementSticks` returned no stick for the hero's controller during that update. The host asks for the movement sticks 7.3 times a frame, and each call went into the runtime, about 2 µs and 920 bytes ([What #123 changed](#what-123-changed)).

In the game's bytecode `getMovementSticks` lists the sticks whose function in the options (`lstickFunction`, `rstickFunction`) is 0; `getCameraSticks` lists those set to 1 and `OptionsBase.get_padKeys` those set to 2, and nothing lists a stick set to −1. The masked update now sets each stick whose function is 0 to −1 and puts the options back when it returns, as it does with the bindings, and gives them back to the player while a charging weapon reads its aim inside that update (`WithHostDirections`). The hook is gone. `Options.save`, which writes the options to disk, is called only from the options and controls menus.

Lab runs, 2026-10-17, one batch for the five changes of row 5 of "Fixes, ranked" (#185–#189): isolated profile `repros/perf-185-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), 80 s, `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile, outfit pack `2cbd0a47…`. "Before" is `main` at `28f4086` (runtime `cc05a678…`), three runs a plan; "after" is this change on the same commit (`3976bdbd…`), two runs. Each plan ran in the order before, #185 to #189, before, #189 to #185, before, so each change's runs fall between `before` runs. The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement` | before | `20261017-013440`, `014637`, `020450` | 109.5 | 9.15 ms | 14.5 ms | 44.6 | 27 (4.5 ms) | 10 / 3 in 145 s | 24.9 |
| | after | `20261017-013630`, `015949` | 112.0 | 8.93 ms | 14.3 ms | 37.5 | 22 (5.1 ms) | 3 / 1 in 96 s | 17.6 |
| `crowd` | before | `20261017-020709`, `021915`, `024937` | 99.4 | 10.07 ms | 15.6 ms | 69.5 | 40 (3.6 ms) | 21 / 9 in 145 s | 32.3 |
| | after | `20261017-020928`, `024802` | 100.8 | 9.91 ms | 15.2 ms | 62.0 | 34 (4.3 ms) | 13 / 5 in 99 s | 24.9 |

The hook's 7.3 calls a frame are gone, moving and in the crowd, and nothing else in the counts moved beyond the held-enemy hooks' spread in the crowd (6.6 to 7.0 calls a frame). The garbage fell by 7.1 KB a frame moving and 7.5 in the crowd, about what 7.3 calls of 920 bytes make (6.6 KB). At about 2 µs a call the hook cost about 15 µs of a 9 to 10 ms frame, too little for these runs to show: moving, this change's two runs (110.0 and 114.0 fps) fell within the three `before` runs (104.7 to 114.6), and in the crowd they made 100.8 and 100.9 fps against 98.6 to 99.8, where the ten runs of the five changes spread from 97.0 to 104.1. .NET collections are not compared: in the `before` runs they came more often from run to run through the batch (22, 25 and 32 a minute moving, 32, 38 and 48 in the crowd), with shorter pauses, whatever the build. Every `crowd` run played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage, and each logged the detach. Every run ended by its time limit with no fatal log and the real saves unchanged. Runs during which other programs used more than 1.5 cores on average, or another lab game ran, were run again and are in no table here: `before` `20261017-020208` (3.93 cores), and the first two tries of `013440` (`013144`, 1.00 cores, and `013312`, 0.61), run again under the batch's first limit of 0.6 cores. The three warm-up runs before the batch (`012436`, `012752`, `012931`) are not counted. In the runs kept, other programs used 0.2 to 1.4 cores of the machine's 28.

Behaviour, with the `aim` plan (`--input-plan aim`, 50 s, seed 174; `repros/live-185-kirby`, run `20261017-025404`), which now also asks the host for the hero's movement sticks: in all 19 weapon updates it looked at inside the masked hero update the host listed no stick; inside `WithHostDirections` there, and between frames, it listed one, the profile's left stick (left set to movement, right to the camera), and the options read the same after each frame. The plan's other checks failed in this run and on `main` alike (`repros/live-base-aim`, `20261017-025515`): the plan presses its keys through a hook on `Key.isDown`, which `KirbyInput` no longer calls since #176 (it reads the host's key table), so every key it pressed read as nothing and Kirby neither inhaled nor moved. Not run: a real controller's stick, the owner's launcher.

## The hurtbox hooks (#177)

`KirbyHurtbox` moves the hero's host hurtbox onto Kirby's original body while `Level.update` runs, so the host's hit tests find Kirby where he is drawn, and puts it back when `Level.update` returns. While that projection is on, the hero's own movement, equipment and save data must keep the physical foot, and four handlers did that: on `Hero.preUpdate`, `Hero.fixedUpdate`, `Hero.postUpdate` and `Entity.spriteUpdate`.

Two of them never found the projection on. The host calls every `postUpdate` and `spriteUpdate` from `Level.postUpdate`, after `Level.update` has returned, and `Entity.spriteUpdate` runs for every entity in the level: 3.9 hooked calls a frame with Kirby moving at the start of the Prisoners' Quarters, 32.5 in the `crowd` plan. The other two doubled the mod's own handlers on `Hero.preUpdate` and `Hero.fixedUpdate`, and every handler on a hooked function is paid again on each call: about 2 µs and 920 bytes ([What #123 changed](#what-123-changed)).

Those two hooks are gone, and `Hero.preUpdate` and `Hero.fixedUpdate` keep one handler each, `KirbyMod`'s, which now runs its work inside `KirbyHurtbox.Physical`. DCCM calls the handler added last first, and `KirbyHurtbox.Install` ran after `KirbyMod` added its own, so the projection was already restored around all of the mod's work there; the order is unchanged. Nothing else in the runtime hooks those four functions outside the lab's `LabMovesProbe`.

Lab runs, 2026-10-16: isolated profile `repros/perf-174-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile. "Before" is `main` with the lab tooling of #174 (runtime `a2a93f5a…`); "after" adds this branch (`1f069148…`). Two runs each against three `before` runs; the frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement` | before | `20261016-151617`, `153025`, `174336` | 110.1 | 9.11 ms | 16.2 ms | 126.3 | 51 (7.5 ms) | 23 / 3 in 153 s | 108.6 |
| | after | `20261016-152205`, `152615` | 110.5 | 9.05 ms | 15.2 ms | 120.3 | 49 (7.5 ms) | 15 / 2 in 101 s | 103.7 |
| `crowd` | before | `20261016-153148`, `155744`, `173139` | 85.6 | 11.69 ms | 22.0 ms | 250.4 | 96 (7.0 ms) | 152 / 18 in 153 s | 481.7 |
| | after | `20261016-153435`, `154106` | 84.8 | 11.79 ms | 21.1 ms | 217.8 | 93 (5.5 ms) | 81 / 11 in 101 s | 450.9 |

Hooked calls fell by 4.9 a frame moving and 30.8 in the crowd, and the garbage by 6.0 and 32.6 KB a frame. The frame times moved less than the three `before` runs differ from each other (102.1 to 116.4 fps moving). Every `crowd` run played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage.

Behaviour, with traces and frame captures (`KIRBY_LAB_PERF=1`, the `movement` plan): Kirby's original state (position, speed, ability, health, graphic, flags and facing) and the host hero's position were the same as on the #174 build in all 2,912 original frames both runs reached (`20261016-170406` before, `171401` after), and the plan's two hurtbox queries passed in both. All four fixes together (`171529`) matched in all 2,930. The frames show Kirby drawn whole.

## The first Kirby frame (#184)

The first host frame in which the original steps comes right after the arrival cinematic, when the head-pop animation ends. #174 measured it at 47–64 ms and put most of it down to the mod's code being compiled on first use. The arrival record and the runtime's list of compiled methods ([Measuring](#measuring)) now show what it spends.

On `main` (`28f4086`) with this branch's probe, the IL build took 53.6 and 54.6 ms over that frame in two runs. The .NET JIT on the game thread took 30.8–31.3 ms of it, 261 methods; the first original step (`Native`) took 12.1–12.4 ms, of which 0.2 ms was compiling the bridge's call stubs; the rest was the mod's other work and the host's frame. Of the IL compiled in that frame (run `20261017-002217`), 63% was `Kirby.Runtime`'s and 13% `Kirby.Core`'s; 15% was framework collections made for the mod's types, such as `Dictionary<(int, int), bool>` and `List<CopyCritical.Timed>`; the rest was DCCM's stubs for calls into HashLink, the game proxies' accessors and other framework code. The frame before it, the first in which Kirby is active, compiled 52 more methods (5.5 ms), and the activation frame 334 (54.5 ms).

The build now compiles `Kirby.Core` and `Kirby.Runtime` ahead of time (ReadyToRun) with crossgen2, the compiler behind the SDK's `PublishReadyToRun`, at the version of the runtime the game runs (10.0.12, restored from NuGet with the build; `src/runtime/Kirby.Runtime.csproj`). The packed DLLs carry native code beside their IL. The IL build in `bin/` is unchanged, and `-p:ModReadyToRun=false` packs it instead: the DLLs it packed were byte for byte those of `bin/`. The runtime uses a method's native code at its first call when the code's recorded assumptions about other assemblies still hold, compiles the method as before when they do not, and still compiles hot methods again in the background at tier 1.

The code is compiled for the build machine's system; `-p:ModReadyToRunOS=windows` compiles it for Windows on Linux (#244, [Windows](RUNTIME_RECON.md#windows-244)). The performance probe itself runs on Linux only.

Lab runs, 2026-10-17: isolated profile `repros/perf-184`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), the `movement` plan, `KIRBY_LAB_PERF=quiet` with the host phases (`KIRBY_LAB_PERF_HOST=1`), software drawing, `performance` power profile, outfit pack `2cbd0a47…`, bridge `bae16c0e…`. The two builds of the same tree, IL (`7256e416…`) and ReadyToRun (`d3274ea9…`), were swapped in the same lab between games, IL first. "JIT" is the game thread's.

| Build | Runs | First Kirby frame | its JIT (methods) | `Native` | Frame before | Activation frame (JIT, methods) | JIT from activation to the 600th played frame |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| IL | `20261017-001822`, `002030` | 54.6, 53.6 ms | 31.3, 30.8 ms (261) | 12.4, 12.1 ms | 13.3, 13.4 ms | 747, 760 ms (54.6, 54.5 ms; 334) | 119.0, 117.4 ms (772 methods) |
| ReadyToRun | `20261017-001915`, `002123` | 39.3, 38.3 ms | 14.9, 14.7 ms (135) | 12.4, 11.8 ms | 11.6, 12.0 ms | 758, 754 ms (44.1, 47.0 ms; 194) | 80.4, 80.6 ms (431, 432 methods) |

The first Kirby frame is 15.3 ms shorter, all of it JIT: half the 30 ms expected in "Fixes, ranked". It is still over 33 ms, so it is still one of each run's frames over 33 ms. In the ReadyToRun build no method of the mod's own was compiled in that frame (run `20261017-002310`): what was left is framework collections made for the mod's types (69 methods, 65% of the IL), the game proxies' accessors (35), DCCM's stubs for calls into HashLink (14, each compiled fully optimized, about 2.5 KB of code) and other framework code. The next frame, which catches up three or four original steps and holds the lab's first report, took 29–33 ms in both builds. The first original step itself took about 12 ms, against 1.2 ms on later frames; that time is spent inside the bridge, which this change does not touch, and what it is spent on was not measured.

Two more crossgen2 options were tried and left out: inlining across assemblies, and compiling into the mod's DLLs the framework generics it uses (`--opt-cross-module:*`, `--non-local-generics-module:*`; runtime `df88f4ff…`, runs `20261017-004011` and `004105`). With them the first Kirby frame compiled 61 and 73 methods instead of 135, most of the framework collections gone, but its JIT took 11.6 and 14.1 ms against 14.7–14.9, and the frame 35.8 and 38.7 ms against 38.3–39.3. What it still compiled (run `003918`) was mostly DCCM's stubs (12 methods, 37% of the IL) and the game proxies' accessors (25, 34%), which no ahead-of-time compile of the mod's assemblies reaches. The gain was within what two runs of one build differ by, so the build keeps the standard options.

The activation frame, about 0.75 s on the first frame of the rebirth, is the same in both builds: `OnActivated` took about 300 ms (the ROM's SHA-256 69 ms, opening the machine 215 ms, audio 10 ms) and the first equipment draw 377–391 ms, in which the renderer asks the host for the outfit's equipment atlas and allocates its textures (which of the two takes the time was not measured); texture uploads took 27 ms. Its JIT, 54.5 ms in the IL build and 44–47 ms in the ReadyToRun one, is a small part of it. This change leaves it as it is.

Once Kirby plays, the two builds run the same. The frames over 20 and 33 ms leave out the lab's report frames.

| Build | Runs | Host fps | Mean frame | p99 by run | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| IL | `20261017-001822`, `002030` | 111.5 | 8.93 ms | 13.0, 11.9 ms | 54.1 | 21 (7.0 ms) | 2 / 2 in 28 s |
| ReadyToRun | `20261017-001915`, `002123` | 109.8 | 9.07 ms | 15.2, 13.9 ms | 54.3 | 22 (7.1 ms) | 6 / 3 in 32 s |

Both ReadyToRun runs (110.2 and 109.3 fps) fall between the two IL runs (109.2 and 113.8), and the garbage and collections are the same. Each run's first frame over 33 ms is the first Kirby frame. The ReadyToRun runs' other slow frames were one with a .NET collection (a 10.3 ms pause) and, in `002123`, three in a row in which the host's drawing took 15 to 21 ms instead of about 6 and the texture upload up to 10 ms; the mod's managed work in each was under 1 ms. Two 80-second runs of each build were taken for more frames, but other programs used more than 0.6 cores during all of them except one ReadyToRun run (`20261017-003749`: 112.0 fps, mean 8.89 ms, p99 13.0 ms, 54.5 KB a frame), so they are left out: `20261017-002901`, `003029`, `003156`, `003324` and `003526`.

Behaviour, with frame captures ten times a second through the arrival (`KIRBY_LAB_PERF=1`; IL `20261017-002520`, ReadyToRun `002759`): both builds played the same animations (`rebirth`, `idle`, `headPopStart`, `headPopLoop`, `headPopEnd`, `idle`), Kirby first shows in the head-pop flash in the same capture of `headPopStart` (its frames 19 and 20, as in #175), with nothing of the outfit drawn before him, and the first capture of play is at the same original frame (4168) with the same health. Kirby's original state and the host hero's position, traced in the same runs, were the same in all 866 original frames both reached (4167 to 5057), and the plan's two hurtbox queries passed in both. The 20 captures taken at the same animation frame in both differ by 0.66 of 255 at the median; the largest difference, 6.6, is the flash, and the rest is the cells' particles and the head pop's light, which the two runs catch at different moments. The 32 captures of play taken at the same original frame differ by 0.37 at the median.

Not measured: GPU drawing and play in a window with vsync, where the frame is shorter and the same 15 ms is a larger share of it, and a first launch after a reboot, when the DLLs are read from disk. Not run: the owner's launcher.

## The activation frame (#193)

The first frame of the arrival's rebirth, in which the runtime takes the hero, took 752–792 ms in [the first Kirby frame (#184)](#the-first-kirby-frame-184). The run folder's activation trace (`activation.txt`, which since #193 names each mark's thread and, with `KIRBY_LAB_PERF`, adds HashLink's collections and memory to some marks) splits it. `OnActivated` took 302–331 ms: the ROM's SHA-256 70 ms, opening the machine (`kssu_open`) 214–221 ms and audio 9–10 ms. The first equipment draw took 387–397 ms: 368–383 ms of it was the host asset manager's `get` of the outfit's equipment atlas, which reads its pages into textures (223 MB allocated, two HashLink collections), followed by 27 ms of texture uploads.

Three changes:

- **The ROM's hash.** `kssu_open` checks the ROM's SHA-256 itself, as it has since the bridge's first commit, so the runtime no longer hashes the ROM before opening it. It hashes it only when the bridge refuses to open, to say which of the ROM and the seed state was refused.
- **The machine.** `Game.loadMainLevel` flushes the host's atlas cache (`AssetsLibManager.flushCache`) once per level, on the game thread. The mod hooks it: when the hero wears the Kirby outfit and the provider has no machine yet, `NdsRuntime.Open` starts on a worker thread, and `OnActivated` takes the machine it opened, waiting for it if it is not ready. Nothing else runs a machine while it opens: the provider has none, and melonDS's current machine is per thread. Opening took 270–329 ms on the worker and ended 5.8–5.9 s before the activation.
- **The equipment atlas.** The same hook gets the atlas just before the flush, so the manager counts it as used and keeps it ([Rendering](RENDERING.md)).

Lab runs, 2026-10-17, as in #184: isolated profile `repros/perf-193`, a new game from seed 174, the `movement` plan, `KIRBY_LAB_PERF=quiet` with the host phases, software drawing, `performance` power profile, outfit pack `2cbd0a47…`, bridge `bae16c0e…`. Before is the #184 tree as merged (`9706475`, runtime `609d7248…`), after is this change (`34597534…`), swapped in the same lab between games.

| Build | Runs (other programs) | Activation frame | `OnActivated` | First equipment draw | Texture uploads | JIT in it (methods) | Frame of the first original step (the step) |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| before | `20261017-015856`, `020837` (0.25, 0.31 cores) | 792.5, 751.9 ms | 331.2, 301.9 ms | 397.1, 387.2 ms | 27.5, 26.9 ms | 45.3, 44.0 ms (195) | 38.5, 41.3 ms (11.9, 12.0 ms) |
| | `030126`, `030226` (0.70, 0.74) | 763.4, 759.4 ms | 308.2, 302.1 ms | 388.2, 389.9 ms | 27.1, 29.4 ms | 44.5, 45.6 ms (195) | 44.6, 42.3 ms (12.6, 11.8 ms) |
| after | `20261017-020617` (0.52) | 100.3 ms | 15.5 ms | 15.5 ms | none | 46.7 ms (194) | 40.0 ms (12.0 ms) |
| | `020116`, `020357`, `025304`, `025622`, `030801`, `030912`, `031124`, `031229` (0.65–1.74) | 100.0–113.9 ms | 15.4–22.9 ms | 15.5–17.1 ms | none | 42.0–46.2 ms (194) | 43.5–51.7 ms (11.9–12.7 ms) |

The activation frame is 0.64–0.69 s shorter. Almost half of what is left is the .NET JIT, the same in both builds; the first equipment draw is now the sprite and its shaders, most of it JIT. Other programs used more than the lab's 0.6-core limit during most of these runs, which are given apart; their activation frames agree with the quiet runs'. One more after run was stopped by the lab's memory watchdog when another session's lab game started beside it (`021056`), and one with 2.93 cores wrote no arrival record (`031330`).

The frame with the first original step, 38.5–44.6 ms before and 40.0–51.7 ms after, had the same JIT (15–21 ms, 135–153 methods) and the same step (11.8–12.7 ms) in both builds. The two slowest, both 51.7 ms, spent 9 ms more in the terrain stage (`030801`) or compiled 152 methods instead of about 138 and drew slower in a run other programs disturbed (`020116`, 1.00 cores). The frame after the activation took 83–90 ms in both builds, 72–73 ms of it the host's drawing; what the host draws there was not traced.

What moved into the level load, from lifecycle runs (`KIRBY_LAB_LIFECYCLE=1`, `KIRBY_LAB_EXIT=T_Courtyard`: the outfit off and on, a level exit to the Courtyard, a death and the new run's first level) with the level load and the new game marked in both builds (before `f0ab3332…`, after `260fcf86…`), alternated. Two more before runs were disturbed by other programs (0.99 and 2.71 cores); their marks are given because they agree with the others.

| Build | Run | New game to the flush | Atlas `get` before the flush | Flush to activation | Activation to Kirby's first draw | From the flush to Kirby's first draw |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: |
| before | `20261017-022844`, `024259` | 5.40, 6.73 s | | 6.75, 6.73 s | 0.70, 0.70 s | 7.45, 7.43 s |
| before (disturbed) | `022050`, `023659` | 5.99, 6.87 s | | 6.77, 6.75 s | 0.72, 0.72 s | 7.49, 7.47 s |
| after | `20261017-023421`, `024529` | 5.71, 6.87 s | 1.04, 1.07 s | 6.13, 6.14 s | 0.05, 0.05 s | 7.22, 7.26 s |
| after (timing run) | `020617` | | 1.13 s | 6.13 s | 0.05 s | 7.31 s |
| final tree | `20261017-031856` | 5.65 s | 1.14 s | 6.13 s | 0.05 s | 7.31 s |

During the load the atlas `get` took 1.04–1.14 s, against 0.37–0.38 s in the activation frame, with the same allocations and the same two HashLink collections; why it is slower there was not traced. The rest of the load to the activation took 6.13–6.14 s in every after run against 6.73–6.77 s in every before run, which was not traced either, so from the flush to Kirby's first draw the after build was 0.12–0.27 s sooner. From the new game the totals are within the spread of the level generation (5.4–6.9 s before the flush).

Later in the lifecycle:

- **Outfit off and on**, in the same level: the activation finds the machine and the atlas, and the atlas `get` takes 0.0–0.1 ms in both builds.
- **The level exit**: the hero carries over to the Courtyard, so there is no activation, and the flush took 0.1–1.1 ms in both builds.
- **After the death**, the new run's flush took the same in both builds of each pair (0.21 s in the first, 0.50 s in the second). In the before build the next activation's equipment draw made the atlas's textures again, 341–349 ms in the activation frame; in the after build the atlas had been kept, and that draw took 0.0 ms.

**The first original step** took 11.8–12.7 ms in all these runs, against about 1.2 ms later. It is the recompiler translating all the code the first frame runs, from the seed state; [the bridge](NDS_BRIDGE.md#the-recompiler-153) measures it outside the game. It cannot be moved off the arrival without changing what the original does or the core: running the original ahead of the host before the arrival hands it control would change the original's frames, and loading the seed state again afterwards clears the translated blocks (the first frame after a load took 10.2 ms). It is left as it is.

Behaviour, with frame captures ten times a second through the arrival (`KIRBY_LAB_CAPTURE_ALL=1`; before `20261017-021352`, after `021811`): both builds played `rebirth`, `idle`, `headPopStart`, `headPopLoop` and `headPopEnd` (the before run also caught the `idle` between the head pop and play, which the after run's captures fell either side of). Kirby is hidden in the captures of `headPopStart`'s flash and drawn from its frame 17, the reveal frame the renderer sets: the before run caught him hidden at frame 16 and drawn at 19, the after run hidden at 14 and drawn at 17. The 14 captures taken at the same animation frame in both differ by 0.83 of 255 at the median and at most 3.2, at the head pop's first frame, where the cells' particles differ. Kirby's original state and the host hero's position were the same in all 880 original frames both traced (4167 to 5064), and the plan's two hurtbox queries passed in both.

The final tree differs from the after builds above only in how the lab trace reaches HashLink's figures and in comments (runtime `90dc7363…`). Its one lifecycle run (`20261017-031856`, 0.29 cores) is the last row above: an activation frame of 102.7 ms, and Kirby's health, ability and shield kept through the outfit change and the exit, as the lifecycle checks it.

Not run: GPU drawing, play in a window, the owner's launcher, a first launch after a reboot, a level loaded through the host's `reloadCache`, and the activation that opens the machine on the game thread, which this change keeps for a hero that becomes Kirby with no level loaded in the outfit; no lifecycle run reached it.

## The health flask (#186)

Kirby has no health flask (#101). `KirbyHealing` turned it off with a hook on `HealFlask.isEnabled`, the test the host already uses while its Captain Chicken pet is active, so F, fountains and flask recharges found no flask. The host asks it 3.3 times a frame, and each call went into the runtime, about 2 µs and 920 bytes ([What #123 changed](#what-123-changed)).

A Kirby session now takes the hero's `HealFlask` out of its main skills when it starts, with the host's own `HeroMainSkillsManager.unregisterSkill` (which `HeroMainSkill.destroy` calls), and puts it back in the same place when it ends; the log names the place (`the health flask left the hero's skills (place 6 of 9)`). With no flask among the skills, nothing reads F as a heal and nothing fills a flask. The skill list goes into the save, so a handler on `HeroMainSkillsManager.serialize` puts the flask back in its place while the host writes the list and takes it out again after, and a save made during a Kirby session still holds the flask and its charges. That handler runs once a save, not every frame.

Lab runs, 2026-10-17, one batch for the five changes of row 5 of "Fixes, ranked" (#185–#189): isolated profile `repros/perf-185-kirby`, a new game from seed 174 each time (`KIRBY_LAB_SEED=174`), 80 s, `KIRBY_LAB_PERF=quiet` with the hook counter (`KIRBY_LAB_PERF_HOOKS=1`), software drawing, `performance` power profile, outfit pack `2cbd0a47…`. "Before" is `main` at `28f4086` (runtime `cc05a678…`), three runs a plan; "after" is this change on the same commit (`21a630dd…`), two runs. Each plan ran in the order before, #185 to #189, before, #189 to #185, before, so each change's runs fall between `before` runs. The frames over 20 and 33 ms leave out the frames in which the lab wrote its reports.

| Plan | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement` | before | `20261017-013440`, `014637`, `020450` | 109.5 | 9.15 ms | 14.5 ms | 44.6 | 27 (4.5 ms) | 10 / 3 in 145 s | 24.9 |
| | after | `20261017-013758`, `015601` | 109.2 | 9.17 ms | 14.4 ms | 41.8 | 29 (3.5 ms) | 5 / 2 in 97 s | 21.6 |
| `crowd` | before | `20261017-020709`, `021915`, `024937` | 99.4 | 10.07 ms | 15.6 ms | 69.5 | 40 (3.6 ms) | 21 / 9 in 145 s | 32.3 |
| | after | `20261017-025124`, `024125` | 101.6 | 9.85 ms | 15.1 ms | 65.6 | 26 (4.7 ms) | 11 / 5 in 99 s | 28.7 |

The hook's 3.3 calls a frame are gone, moving and in the crowd, and nothing else in the counts moved beyond the held-enemy hooks' spread in the crowd (6.6 to 7.0 calls a frame). The garbage fell by 2.8 KB a frame moving and 3.9 in the crowd, about what 3.3 calls of 920 bytes make (3.0 KB). At about 2 µs a call the hook cost about 7 µs of a 9 to 10 ms frame, too little for these runs to show: moving, this change's two runs (105.8 and 112.6 fps) fell within the three `before` runs (104.7 to 114.6), and in the crowd they made 99.1 and 104.1 fps against 98.6 to 99.8, where the ten runs of the five changes spread from 97.0 to 104.1. .NET collections are not compared: in the `before` runs they came more often from run to run through the batch (22, 25 and 32 a minute moving, 32, 38 and 48 in the crowd), with shorter pauses, whatever the build. Every `crowd` run played the same fight: 31 proxies at most, 30 targets hit 66 times for 2,046 damage, and each logged the detach. Every run ended by its time limit with no fatal log and the real saves unchanged. Runs during which other programs used more than 1.5 cores on average, or another lab game ran, were run again and are in no table here: `before` `20261017-020208` (3.93 cores), and the first two tries of `013440` (`013144`, 1.00 cores, and `013312`, 0.61), run again under the batch's first limit of 0.6 cores; and this change's first `crowd` run, `021056`, during which another session's lab game ran. The three warm-up runs before the batch (`012436`, `012752`, `012931`) are not counted. In the runs kept, other programs used 0.2 to 1.4 cores of the machine's 28.

Behaviour, with the `healing` plan (`KIRBY_LAB_HEALING_SWORD=1 KIRBY_LAB_HEALING_EXIT=1`, 100 s, seed 174; `repros/live-186-kirby`, run `20261017-025728`, against `main` in `repros/live-base-healing`, `20261017-025924`): F started no flask, the fountain healed 38 of the 76 it asked, the level's exit kept Kirby's stored ability in the F slot, and the flask stayed out of the skills throughout, with the same lives as on `main` ([Runtime launch](RUNTIME_LAUNCH.md#f-slot-and-fountain-2026-10-10)). Not run: a run save during a Kirby session (the lab's level exit wrote only the profile), so the `serialize` handler was not exercised; the owner's launcher.

## Checks run

On the final tree, 2026-10-11:

- `python3 scripts/check.py`: passes (26 + 6 Python tests, 435 rule assertions, the runtime builds).
- The bridge built from this branch against a private copy of the vendor tree (sha256 `4485934e8fa96bd90a7b4948915205e1810d09643141baef5561791eec9aaeba`, the same on a second build): all 82 `tools/nds_tests.py` cases pass, and the 80 older ones return what the previous bridge's recorded results hold.
- `tools/verify_renderer.py`: 29 cases, 3,046,706 pixels, 0 mismatched, 0 unsupported objects. `tools/verify_background.py`: 4 cases of 44,236,800 values, 0 mismatches. `tools/verify_native_init.py`: 64 cycles pass.
- The lab runs above, 15 in all.

The shared bridge binary, its provenance and `oracle/bridge-test-results.json` were not replaced, and the shared vendor checkout was not patched: play needs `python3 tools/nds_build.py` and a new stage to get the bridge half. With the older bridge the runtime still works; it then keeps the per-byte read and draws both screens.

On `main` at `28f4086`, 2026-10-17 (#190): the four fixes as merged (#179–#183), which no lab run had played before. Its `src/` is that of `measure/all2` in [the four fixes together](#the-four-fixes-together-174).

- `python3 scripts/check.py`: passes (26 + 6 Python tests, 505 rule assertions, the runtime builds; runtime `0659fedf…`).
- Lab runs in a new isolated profile, `repros/main-28f4086`, staged with outfit pack `2cbd0a47…` and bridge `bae16c0e…`, with the settings of the four fixes' rows: a new game from seed 174, `KIRBY_LAB_PERF=quiet` with the hook counter, 80 s, software drawing, `performance` power profile, and `--crash-dump small`. Both ended by their time limit with no fatal log, no dump and the real saves unchanged, as did a third `movement` run that other programs disturbed (`20261017-000945`, 0.84 cores; not in the table). The frames over 20 and 33 ms leave out the lab's report frames.

| Plan, drawing | Build | Runs | Host fps | Mean frame | p99 | Managed KB a frame | .NET collections a minute (median pause) | Frames over 20 / 33 ms | Hooked calls a frame |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `movement`, software | all four | `20261016-170048`, `174208` | 119.5 | 8.37 ms | 12.6 ms | 43.9 | 21 (5.9 ms) | 3 / 2 in 97 s | 24.7 |
| | `main` | `20261017-001113` | 109.5 | 9.14 ms | 14.0 ms | 42.6 | 17 (6.1 ms) | 3 / 1 in 48 s | 23.9 |
| `crowd`, software | all four | `20261016-163343`, `174040` | 102.4 | 9.77 ms | 15.4 ms | 68.5 | 28 (5.0 ms) | 18 / 7 in 97 s | 32.0 |
| | `main` | `20261017-001241` | 96.1 | 10.40 ms | 15.9 ms | 68.0 | 25 (5.0 ms) | 9 / 3 in 48 s | 31.1 |

- **Garbage, collections and slow frames** are those of the four fixes: 42.6 and 68.0 KB a frame against 43.9 and 68.5, and frames over 20 ms 0.06 a second moving and 0.19 in the crowd, against 0.03 and 0.19 (0.15 and 1.0 before the fixes). The frames over 33 ms are the same events: moving, the first Kirby frame (53.7 ms); in the crowd, the frame the zombies appear in (63.0 ms) and the first sword hit on 30 of them (40.7 and 83.7 ms), after the same number of original steps as in the four fixes' runs. The crowd run's first Kirby frame is not among them: the probe counts a frame only when the frame before it also stepped Kirby, and the first one it counted was a 20.5 ms catch-up of three original steps. That run played the same fight as before (31 proxies at most, 30 targets hit 66 times for 2,046 damage) and logged the detach.
- **Hooked calls:** `ItemMetaManager.hasUnlockedItem` ran once a frame here and twice in all 69 hook-counted runs in `repros/perf-174-kirby`; the rest matched. The code is the same and the two profiles' saves are not; which state decides it was not traced.
- **Frame time:** 8% fewer frames a second moving and 6% fewer in the crowd. The mod's own work per original frame was at most 3% longer (1.72 against 1.67 ms moving, 2.66 against 2.60 and 2.64 ms in the crowd), and so was the original's frame in the same bridge (1.21 against 1.18 and 1.19 ms moving). The rest of the frame, the host's own and mostly drawing in software, took 0.6 to 0.7 ms longer moving (8.20 against 7.50 and 7.56 ms) and 0.4 to 0.6 ms longer in the crowd (8.75 against 8.11 and 8.36 ms). Runs of one build have spread as widely before (the #174 build's three moving runs, 102.1 to 116.4 fps; the disturbed run of this one, 114.8 fps), so one run of each does not show a slowdown.

Not run on `main`: a second run of each plan, the GPU and Greenhouse rows, keys through the host's key table, behaviour traces with frame captures, and the owner's launcher.

## Left as they are

Measured or seen in #123 and not changed.

- **The original frame, about 2.6 ms.** The bridge ran melonDS's interpreter. A recompiler was left out of #123: it changes the emulated timing that the recorded bridge evidence rests on. #153 added it behind a switch (The original on the recompiler, above).
- **A pile of enemies in recoil.** With the 30 fixture targets of the `crowd` plan stacked on one spot, the original frame takes about 6 ms. #150 found what that was: the first swing hit all of them and stopped the original for good, and the time was an ARM9 that never rests again, not the original's code for overlapping actors. #151 removed the stop ([the collision pool](#the-collision-pool-151)). Twenty stacked targets, which it survives, cost about 3.6 ms (the table above). Thirty proxies spread over the room cost about 0.9 ms together.
- **`HSprite.draw`.** `KirbyHostVisuals` hooked it to keep the Beheaded's graphics away from Kirby, and the host calls it 77–140 times a frame. #175 left the outfit's in-game page blank instead and removed the hook ([the sprite draw hook](#the-sprite-draw-hook-175)): 72.6 KB a frame less with Kirby moving, 127.4 KB in the crowd.
- **The texture upload.** A drawn frame still uploads the whole 1024×512 texture, about 0.6 ms in the lab.
- **The garbage collector mode.** DCCM's shell `runtimeconfig.json` asks for the server collector, but the game runs the workstation one in its concurrent mode (#174). Overriding it from the environment (`DOTNET_gcServer=0`, `DOTNET_gcConcurrent=1`) changed nothing measurable, which fits.
- **A host shader made on first use.** The host logs `Shader missing from cache: shaderLinker_ad70e08c` the first time one sprite combination is drawn, here when a Zombie first hit Kirby. In the lab that one frame took 1.3 s. The same line is in 123 earlier lab and play logs back to 2026-10-04, so it is not new; what it costs on the GPU was not measured.

## Not verified

- Play in a window on the GPU, with vsync: no fps, pacing line or frame time from it. The lab has no frame cap and draws in software.
- That 60 fps holds in play, in every biome, in boss fights or over a long session.
- The game in motion. The verifiers compare the decoder and the background capture with the original output, and three still frames of the final build's lab runs were looked at (Kirby drawn whole, nothing left behind him). Nobody watched it play.
- The hooks being added and removed next to another DCCM mod that hooks the same host functions. This mod hooks none of them anywhere else.
- A held entity on the frames around a level change or a save restore beyond what the plans above exercise.
