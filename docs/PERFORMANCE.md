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

## Measuring

`KIRBY_LAB_PERF=1` in the shell that runs `launch_runtime.py` makes `LabPerfProbe` write `perf.json` in the run folder every two seconds: host frame intervals, the time and managed bytes of each stage, original steps, texture uploads, calls of the held-entity hooks, collections and their pause. It counts only host frames that stepped Kirby, after the first one. It is off in play, where each mark costs one static test.

The `Kirby pacing:` log line, once a minute in any run including play, gives host and original fps, the mean time of the original step and of the drawing, and how many original frames ended inside a logic pass (#134): a few at each ability or helper load, otherwise none. More than that means Kirby can be drawn a terrain cell off for single frames again (docs/NDS_BRIDGE.md, Where a source frame ends). Check `powerprofilesctl get` first: the power-saver profile alone took the game well below 60 fps on 2026-10-10.

## Checks run

On the final tree, 2026-10-11:

- `python3 scripts/check.py`: passes (26 + 6 Python tests, 435 rule assertions, the runtime builds).
- The bridge built from this branch against a private copy of the vendor tree (sha256 `4485934e8fa96bd90a7b4948915205e1810d09643141baef5561791eec9aaeba`, the same on a second build): all 82 `tools/nds_tests.py` cases pass, and the 80 older ones return what the previous bridge's recorded results hold.
- `tools/verify_renderer.py`: 29 cases, 3,046,706 pixels, 0 mismatched, 0 unsupported objects. `tools/verify_background.py`: 4 cases of 44,236,800 values, 0 mismatches. `tools/verify_native_init.py`: 64 cycles pass.
- The lab runs above, 15 in all.

The shared bridge binary, its provenance and `oracle/bridge-test-results.json` were not replaced, and the shared vendor checkout was not patched: play needs `python3 tools/nds_build.py` and a new stage to get the bridge half. With the older bridge the runtime still works; it then keeps the per-byte read and draws both screens.

## Left as they are

Measured or seen in #123 and not changed.

- **The original frame, about 2.6 ms.** The bridge runs melonDS's interpreter. A recompiler was left out of #123: it changes the emulated timing that the recorded bridge evidence rests on.
- **A pile of enemies in recoil.** With the 30 fixture targets of the `crowd` plan stacked on one spot, the original frame takes about 6 ms. Thirty proxies spread over the room cost about 0.9 ms together, so the time is the original game's own code for overlapping actors, not the bridge.
- **`HSprite.draw`.** `KirbyHostVisuals` hooks it to keep the Beheaded's graphics away from Kirby, and the host calls it 77–140 times a frame. It is needed on every frame while the outfit is on. At the per-call figure above that is 70–130 KB of the 166 KB a frame still allocated; this share is an estimate, not a measurement.
- **The texture upload.** A drawn frame still uploads the whole 1024×512 texture, about 0.6 ms in the lab.
- **The garbage collector mode** comes from DCCM's `runtimeconfig.json`. Overriding it from the environment (`DOTNET_gcServer=0`, `DOTNET_gcConcurrent=1`) changed nothing measurable.
- **A host shader made on first use.** The host logs `Shader missing from cache: shaderLinker_ad70e08c` the first time one sprite combination is drawn, here when a Zombie first hit Kirby. In the lab that one frame took 1.3 s. The same line is in 123 earlier lab and play logs back to 2026-10-04, so it is not new; what it costs on the GPU was not measured.

## Not verified

- Play in a window on the GPU, with vsync: no fps, pacing line or frame time from it. The lab has no frame cap and draws in software.
- That 60 fps holds in play, in every biome, in boss fights or over a long session.
- The game in motion. The verifiers compare the decoder and the background capture with the original output, and three still frames of the final build's lab runs were looked at (Kirby drawn whole, nothing left behind him). Nobody watched it play.
- The hooks being added and removed next to another DCCM mod that hooks the same host functions. This mod hooks none of them anywhere else.
- A held entity on the frames around a level change or a save restore beyond what the plans above exercise.
