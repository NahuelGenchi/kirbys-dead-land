# Kirby gameplay fidelity and original-game oracle

Status: **research, a working headless original-ROM oracle, and observed base-form
motion; original gameplay has not been ported or verified in Dead Cells.**
See [the measured oracle results](ORACLE_RESULTS.md) for the actual repeatable run.
Sprite extraction does not recover movement,
move logic, hit timing, collision rules, or damage behavior. No guessed movement
constants are described as NDS-accurate in this project.

## Verified local source

The user-supplied ROM was read without modifying it:

- Header title: `KIRBY USDX E`.
- Game code: `YKWE`; revision byte: `0`.
- File length: `134217728` bytes.
- SHA256: `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`.
- ARM9 image: file offset `0x4000`, RAM base `0x02000000`, entry
  `0x02000800`, length `291320` bytes.
- ARM9 overlay table contains `157` entries. Overlay 23 loads at `0x02071240`,
  24 at `0x0207F180`, and 27 at `0x0214CD80`.

ROM headers establish the architecture and file layout. They do not establish
which routines implement Kirby's movement. The ARM9 and overlay code cannot be
loaded as a native x86 Dead Cells plugin. Using that original code would require
an ARM execution/recompilation layer plus the game's surrounding services, or a
carefully validated port of the relevant routines.

## Sources examined

1. [Nintendo's KSSU page](https://www.nintendo.com/en-gb/Games/Nintendo-DS/Kirby-Super-Star-Ultra-271298.html)
   describes inhaling/swallowing and pressing Down to copy an enemy, and X to make
   a helper. [Nintendo's DS manual index](https://en-americas-support.nintendo.com/app/answers/detail/a_id/16905/)
   links the original instruction booklet. These establish intended controls,
   not numerical physics or complete move frame data.
2. [melon-kssu, inspected commit](https://github.com/benhall-7/melon-kssu/tree/0af58922f3f940244bc239f186508963950fddb3)
   is a game-specific emulator observation project. Its memory-layout notes and
   Lua probe are useful research leads, not a standalone port of Kirby's gameplay.
   Its `docs/hit-definitions.md` explicitly calls the findings non-authoritative
   and requires validation against RAM dumps and decompilation. Some older Lua
   field interpretations differ from the later research notes; raw fields must
   be retained until independently checked.
3. [MApRX](https://github.com/wwylele/MApRX) is a KSSU map editor, not a physics
   implementation. [Kirby-Copy-Ability-Maker](https://github.com/s6465456/Kirby-Copy-Ability-Maker)
   contained only a README when inspected, so it supplies no executable ability
   implementation.

Repository and web searches on 2026-10-03 did not locate a complete public KSSU
gameplay decompilation/recompilation. This is a search result, not proof that none
exists. A Super Nintendo Kirby Super Star project would not establish the Nintendo
DS remake's exact behavior.

## Original-ROM controller: concrete feasibility

The companion emulator wrapper was inspected at
[melon-rs commit 51f25ed](https://github.com/benhall-7/melon-rs/tree/51f25edaf3b2df63f91d049eff8c38fb289150eb).
Its `Nds` Rust API exposes `set_nds_cart`, `setup_direct_boot`, `set_key_mask`,
`run_frame`, `current_frame`, `main_ram`, `main_ram_mut`, `savestate`,
`read_savestate`, and `update_framebuffers`. Its post-frame observer receives the
frame number, input state, and read-only RAM. Those are concrete building blocks
for a deterministic original-game oracle, including trajectory and active-hit
observations from the candidate memory layout above.

The wrapper's pinned
[melonDS core constructor](https://github.com/melonDS-emu/melonDS/blob/82fdbc78483f43b310e920e21acc47787cb43564/src/NDS.cpp)
uses FreeBIOS for ARM9/ARM7 and generated firmware by default. It supports direct
boot for that configuration. A separate proprietary BIOS dump is therefore not
an inherent prerequisite for this inspected source route. The headless runner now
compiles and boots the supplied ROM locally; three upstream camera-shim signature
mismatches were corrected by the checked setup script.

The wrapper is an experimental Rust/OpenGL frontend, not an existing Dead Cells
controller plugin. Its renderer returns complete 256×192 screen buffers. The
inspected APIs do **not** provide any of the following:

- A callback that substitutes Dead Cells terrain/collision queries during Kirby's
  original motion and ability updates.
- An adapter creating original-engine enemy/contact/grab/projectile objects from
  Dead Cells enemies and reflecting absorption, damage, death, and parry results.
- A Kirby-only transparent RGBA render target. Whole-screen framebuffers include
  the stage and other game objects; object masks/OAM/VRAM mapping would need work.

`main_ram_mut` is raw byte access, not proof that any of those interfaces are
known. MApRX edits static KSSU maps and block properties; it does not document a
live cross-engine collision API. Forcing the emulated character's coordinates to
host coordinates after each frame would not preserve original grounded states,
wall contacts, grab targets, attack collision ordering, or terrain-dependent
ability behavior. Rendering Kirby over Dead Cells while leaving those mechanics
in a separate emulated stage would not satisfy the requested playable character.

Consequently, emulator-backed execution is a possible engineering route, with a
bounded first deliverable of original gameplay traces. A faithful in-game adapter
still requires the three missing interfaces above and actual integration tests.
There is no demonstrated shortcut from a ROM and screen compositor to this mod.

## Offline RAM inspection tool

`tools/oracle_snapshot.py` reads an already captured raw main-RAM dump. It does
not run an emulator, attach to a process, change memory, or send input. It checks
the exact source ROM fingerprint, bounds-checks pointer reads, rejects linked-list
cycles, and preserves unclassified hit-definition words instead of guessing their
meaning. It refuses to infer which entity is Kirby from a table slot alone.

```bash
python3 tools/oracle_snapshot.py /path/to/main-ram.bin \
  --rom '/home/rayzza/Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds' \
  --frame 120 --out /path/to/new-observation.json
```

After independently identifying Kirby's entity address in the current capture,
pass `--entity 0xADDRESS` to group its candidate motion values and owned hitboxes.
The output file is created exclusively; existing observations are not overwritten.

The tool accepts raw 4 MiB DS main RAM or a 16 MiB emulator dump beginning at the
DS main-RAM base. It only follows addresses inside the first 4 MiB. A save file or
compressed savestate is not a raw RAM dump and must be exported by the emulator.

Candidate memory facts from the source project:

| Value | Candidate location / encoding | Local runtime status |
|---|---|---|
| Entity chain heads | `0x02049DEC`, 7 pointers | Parsed actual Green Greens gameplay |
| Entity X/Y | entity + `0x80` / `0x84`, signed 16.16 | Observed idle/walk/jump and screenshots agree |
| Entity velocity fields | entity + `0x88` / `0x8A`, signed 16-bit | Observed 8.8 velocity; X deltas match all motion-trace ticks |
| Hit chain heads | `0x02076F00`, 32 pointers | Unverified |
| Hit owner | hit + `0x38` | Unverified |
| Hit bounds | hit + `0x24`, four signed 16-bit values | Unverified |
| Hit definition | pointer at hit + `0x14`, 28 bytes | Semantics incomplete |
| Current ability | byte at `0x020BAF7B` | Value 1 observed in base form; other IDs unverified |
| Camera X/Y | `0x0204222E` / `0x0204222A` | Unverified |

Passing the tool's synthetic tests establishes parser safety only. Actual local-ROM
idle/walk/jump observations now support the subset described above. Hit semantics,
ability routines, and full-state coverage remain unverified. Neither those base
motion observations nor an upstream screenshot prove cross-game equivalence.

## Asset groups and animation research

The PD archive groups are not established timed-animation sequences. For example,
the actual `aswd.b` contains four 42-pose groups: two palette variants of a body
pose list and two variants of an additional graphics list. `bkir.b` contains two
491-pose groups. Those structures alone do not supply per-move frame durations.
The examined `info02/gaswd` and `info02/gkiswd` files contain 512-byte nibble-pattern
graphics data, so those files are not evidence of ability timing tables.

The extractor agent located `bkir`/`aswd` asset-path strings in the uncompressed
ARM9 overlay 25. That is a lead for following the original code's references to
sprite/move state, not a decoded timing format. MApRX's publicly documented script
and animation readers concern map cells, items, palettes, and tilesets; they do
not establish Kirby's move duration/state-machine format.

Independent ROM review checked all 42,909 OAM part entries across archive groups.
They use non-affine 4bpp geometry. 3,944 entries have left-facing X coordinates
different from simply mirroring the right-facing rectangle, and 4,742 entries do
not toggle the horizontal-flip bit between facings. The extractor now renders
the original left-facing attributes separately. Index-zero transparency, RGB555,
tile order, both flip flags, and negative pivots have synthetic regression tests.
These graphic tests do not establish original animation timings.

## Ability inventory and requested changes

The source project's candidate ability-byte table contains these IDs. These are
research labels, not verified live IDs or an implemented feature list:

| ID | Form | ID | Form | ID | Form |
|---|---|---|---|---|---|
| 1 | Base | 2 | Cutter | 3 | Mirror |
| 4 | Beam | 5 | Fighter | 6 | Suplex |
| 7 | Yo-Yo | 8 | Wheel | 9 | Bomb |
| 10 | Ice | 11 | Fire | 12 | Plasma |
| 13 | Ninja | 14 | Stone | 15 | Wing |
| 16 | Jet | 17 | Copy | 18 | Sword |
| 19 | Hammer | 20 | Parasol | 21 | Paint |
| 22 | Mike | 23 | Crash | 24 | Cook |
| 25 | Sleep | | | | |

The requested rules should be applied at the boundary between Kirby's controller
and Dead Cells:

- Complete the original inhale/mouthful/swallow sequence. Treat "absorbed" as
  **successful swallowing/copy resolution**, since immediately transforming at
  the beginning of inhaling would change the original sequence. Roll once per
  completed absorption event; consume the enemy once. Spitting an enemy out must
  not also grant a copy ability.
- Choose randomly from the implemented original copy-ability pool. Do not pretend
  unsupported abilities are implemented by assigning each a generic projectile.
  The full request includes limited-use and utility forms such as Sleep, Cook,
  Mike, Crash, Paint, and Copy; silently excluding them would narrow the request.
- In base form, permit Dead Cells sword, bow, and shield actions. Other weapon
  families are outside the user's allowed set.
- With a copy ability, route attacks to that ability's original action set.
  Keep the already-held shield available for parrying; reject ordinary sword/bow
  attacks. A native Kirby Sword form is separate from a Dead Cells sword item.
- Keep the shield item identity and existing parry behavior across copy changes,
  ability loss, and return to base. Do not grant a new shield on transformation.

Random-selection distribution, repeat behavior, enemy eligibility, and interactions
with bosses/elites must be made explicit in implementation. A uniform draw with
repeats allowed is a reasonable default for "random". Original-game exclusions
and limited-use consumption rules still require evidence. Nothing in this document
authorizes replacing every original move with an approximation.

## Evidence required before claiming fidelity

Use an isolated emulator save and fixed, recorded input sequences with the exact
ROM hash. Record the emulator build, game mode, initial savestate hash, RNG seed,
frame clock, and capture timing. Independently identify the controlled entity.

For the base form, capture standing, walking, dash initiation and release,
turnaround, jumping with different hold lengths, airborne reversal, hovering,
air release, falling, crouch, slide, inhaling, mouthful movement, swallow, spit,
guard, water movement, damage recoil, invulnerability, death, and ability loss.
Then capture every original input variant of every copy ability, including charge,
direction, aerial, dash, contact/grab, and limited-use variations where applicable.

Record per-frame position and velocity in raw fixed-point units, state transitions,
animation frame and duration, hit/hurt/grab boxes, damage, knockback, immunity,
projectile lifetime, and resource consumption. Asset animation tables alone cannot
prove when those frames are selected by a move state machine.

The Dead Cells adapter needs an explicit conversion between NDS pixels/ticks and
the host world's units, collision queries, enemy targets, damage, and rendering.
Its internal controller traces should match the original before applying host
coordinates. Compare at simulation ticks, not render FPS. Keep the user's three
requested rule changes in separate comparison scenarios. A successful build,
recognized sprite sheet, or random-choice unit test does not satisfy these checks.
