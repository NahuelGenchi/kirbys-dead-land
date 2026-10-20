# Original-ROM oracle: measured results

On 2026-10-03, the supplied USA KSSU ROM was booted in a standalone headless
melonDS instance using FreeBIOS and a new isolated save. Virtual DS input reached
Spring Breeze / Green Greens and ran a 191-frame idle, right-walk, release,
A-button jump, and landing sequence. The same starting state and plan were run
twice. **The JSON traces, all six captured top-screen images, and final 16 MiB RAM
dumps were byte-identical.** This verifies a repeatable original-game observation
route; it does not verify a playable Dead Cells port.

No desktop keyboard/mouse input, visible emulator window, audio device, existing
user save, or Dead Cells process was used. The original ROM was read only.

## Measured behavior

The player entity in this one scene was `0x02049EC4` (kind 56, no parent). The
address is an observation, not a general player-identity rule. Screenshots show
the same controlled Kirby moving and jumping as the observed coordinates.

| Measurement | Observation in this scenario |
|---|---|
| Base form byte | `0x020BAF7B == 1` |
| Position encoding | Signed 16.16 at entity + `0x80`/`0x84` |
| Velocity encoding | Signed 8.8 at entity + `0x88`/`0x8A`; raw X displacement equals velocity × 256 on every trace tick |
| Idle | 60 frames at X=43, Y=153.5 with zero velocity |
| Right input | 20 frames; X=43 → 68.125 |
| Walk acceleration | Observed raw X velocities 80,160,240,320,352, then 352 |
| Walk speed reached | 352/256 = 1.375 original pixels per emulated frame |
| Release | Observed raw X velocity decreases by 16 each frame to zero; rests at X=82.5625 |
| Jump input | A (`mask=1`) for one frame, then released |
| Jump takeoff | At trace frame 111, Y=143.0234375, raw Y velocity=-2682 |
| Jump apex | Minimum sampled Y=103.609375 at trace frame 130 |
| Landing | Back at Y=153.5 with zero Y velocity at trace frame 156 |

These are samples from one base-form movement scenario, not universal physics
constants. In particular, the early jump velocity changes after releasing A are
not described as a single gravity constant. Held jumps, hovering, dash, water,
slopes, collision edges, recoil, ability movement, and attack timing remain to be
measured. A preliminary B-button probe showed attack behavior and was excluded
from the jump result.

## Provenance

Private run directories:

- `~/.local/share/kirby-dead-cells/oracle/motion-c`
- `~/.local/share/kirby-dead-cells/oracle/motion-d`
- Starting state: `~/.local/share/kirby-dead-cells/oracle/ready2/000.state`

| Item | SHA256 |
|---|---|
| ROM | `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027` |
| Starting state | `37166a991f37a4e84f7392fd33ca487672d65efae7f32b38e22b7cc2454a9a76` |
| Executed motion plan | `367de966b78575a54689b28e17575fb1e1b5fe9165403a95d419a842bdf12e7f` |
| Runner binary | `9c432b66b061322b87b121b9a6c433a51ec6823439089552a3f8dfbffb90c47b` |
| Both motion traces | `f87c1229f24905ef4279003562f981ff39126ecfacec0087df99fecad6fff161` |

Source pins:

- melon-rs `51f25edaf3b2df63f91d049eff8c38fb289150eb`.
- melonDS `82fdbc78483f43b310e920e21acc47787cb43564`.

`tools/oracle_build.py` checks those exact revisions, verifies bridge source
preconditions, and repairs the three camera callbacks to accept the core's
`void* userdata` argument. It refuses unexpected local modifications. The runner
calls `Nds` directly, without the frontend. All dependencies, binaries, states,
screenshots, RAM, and new save data remain in the private oracle lab.

## Reproduce

Requires existing Cargo/Rust, CMake, C++ build tools, and the upstream dependencies.
The setup script downloads sources and Cargo packages but installs no system
packages. Pillow is used only to convert captured screens to PNG.

```bash
python3 tools/oracle_build.py
python3 tools/oracle_run.py \
  --rom "$HOME/Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds" \
  --plan tools/oracle_bootstrap.json \
  --output ~/.local/share/kirby-dead-cells/oracle/reproduction-boot
```

The bootstrap plan records the actual original menu path and waits used to reach
Green Greens, exclusively through the virtual DS controls. It creates a fresh
isolated save. Use its final `025.state` with `--state`, and
`--plan tools/oracle_motion.json`, saving each repetition to a new output directory.
The combined bootstrap was also executed from a fresh save and its final
`025.state` used with the committed motion plan. That independent bootstrap
produced the same complete 191-row trace as `motion-c` and `motion-d`. Its
starting-state hash is different from the earlier capture path, which included
intermediate restores; the identical trace is the measured reproducibility result.

The recorded motion plan is formatted for readability in the repository; its
whitespace differs from the originally executed plan, so its file hash differs
while its input sequence is identical.

Each run records the ROM, plan, starting-state, executable, and output trace hashes.
The launcher requires a new output folder beneath the private lab and accepts
only a starting state from that lab. Captures include copyrighted original-game
content and must remain outside the source repository/package.

The next exactness milestone is original move/state/collision coverage and a
verified adapter for Dead Cells terrain, enemies, and rendering. This experiment
does not establish those interfaces.
