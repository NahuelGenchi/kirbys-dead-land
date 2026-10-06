# Kirby's Dead Land

*Kirby Super Star Ultra in Dead Cells.*

[Issues](https://github.com/NahuelGenchi/kirbys-dead-land/issues) · [Milestones](https://github.com/NahuelGenchi/kirbys-dead-land/milestones) · [Pull requests](https://github.com/NahuelGenchi/kirbys-dead-land/pulls) · [Contributing](CONTRIBUTING.md) · [Agent instructions](AGENTS.md)

A new Linux character mod built with [Universal Modder](https://github.com/rehan-remade/universal-modder),
using the supplied Kirby Super Star Ultra USA ROM as the running gameplay engine.
Kirby is playable in the actual Dead Cells game, with original ROM movement,
copy abilities, graphics and sound. Live tests cover absorption, copying,
retained-shield parries, damage, save/resume, boss resistance and 31-enemy capacity.
Complete parity for every host interaction is not established.

The original ROM runs locally in an embedded melonDS instance. Its controller,
physics, action timers, copy abilities, sprite OAM and sound effects drive Kirby.
Dead Cells supplies the level, enemies, equipment and progression. The ROM and
installed game are read without modification. The existing Sonic/Knuckles mod is
preserved; generated assets and native dependencies remain in the private data root.

## Requested rules

- Absorbing a Dead Cells enemy selects a random original copy ability.
- Absorbing an Elite always gives original Sword, which deals double damage
  until the ability is lost (hit off, discarded, made into a helper, or a new run).
  This is a deliberate deviation; see `docs/GAMEPLAY_FIDELITY.md`.
- Base Kirby can use Dead Cells swords, bows and shields.
- Copied Kirby uses the original ability's attacks and retains the exact shield
  it carried for parrying. Equipping replacement weapons while copied is blocked.
- Items Kirby cannot use (other weapons and skills, or any weapon while copied)
  get Dead Cells' own "Can't use that right now" message and stay on the ground;
  a long press still recycles them.
- Original inhale, mouthful, Down-to-swallow, spit, flight, guard, slide and copy
  discard are executed by the ROM. All 24 original copy forms load through the
  original transformation code, rather than replacing them with host weapons.

## Play

The local build is staged and ready. Keep Steam running, then run from this folder:

```sh
./play-kirby
```

Choose **New Game** for a new run, or continue your existing private Kirby save.
New runs start with the new Kirby outfit.
The launcher uses the real display, keyboard/controller and sound, with separate
local saves, Cloud save redirection, backups and a read-only view of the installed
game. It leaves no loader in the Steam game directory. Five complete user play
recordings were reviewed after the initial release; see the
[first](docs/RECORDING_REVIEW_2026-10-04.md),
[second](docs/RECORDING_REVIEW_2026-10-04_0907.md),
[third](docs/RECORDING_REVIEW_2026-10-04_1109.md),
[fourth](docs/RECORDING_REVIEW_2026-10-04_2108.md) and
[fifth](docs/RECORDING_REVIEW_2026-10-04_2143.md) recording reviews for observed
problems, corrections and the scope of subsequent live checks. Kirby now appears
in the arrival flash, keeps host weapons behind his body, plays his original
death animation, steps one original frame per frame on a ~60 Hz display, and takes
host damage scaled to his 76 HP. He no longer blinks out when hit, a held
direction dashes, and copy abilities deal damage on the host's scale. The
original game's hurt and guard palette flash is kept but softened to a tint,
because the whitened frames vanished against light scenery on a large monitor.
Kirby no longer jumps a terrain cell for a frame as he moves. His slide (Down +
jump) is his roll: it passes Dead Cells roll-zone tunnels and bursts doors. In
the air, the Dead Cells dive binding (Down + jump by default) makes him stomp
with the host's own dive attack: it hits and stuns enemies and breaks
breakable floors once the Ram rune is unlocked.

Kirby moves at the Beheaded's speeds, measured in the game: he runs and steers
in the air at 236.5 px/s, slides as far and as fast as the Beheaded rolls,
falls at 403 px/s, stomps at the dive's 900 px/s, climbs at 97.5 px/s and
floats across at running speed. He stops when the direction is released and
turns at once, and his jump keeps its height but peaks as fast as the
Beheaded's. His animations are the original ones; the walk and dash cycles
play faster with the run so his feet keep up.

As in Dead Cells, touching an enemy does not hurt Kirby: only enemy attacks do
(and the few Dead Cells enemies whose bodies are spiked or electric). Bumping
into enemies does not hurt them either; inhaling, sliding and copy attacks do.

Six options in the staged `config.json` tune the adaptation: `CameraZoom`
(1 keeps the Beheaded's camera; 2 shows about the DS screen's height),
`AlwaysDash` (a held direction dashes; a light analog tilt still walks),
`MatchHostSpeed` (true: the Beheaded's speeds; false keeps the DS speeds; the
stomp always moves at the dive's speed),
`OriginalTouchDamage` (false: Dead Cells' rules; true restores the original
game's touch damage, which hurts Kirby and weak enemies on contact),
`CopyDamageScale` (3.5: a Zombie takes about two Sword hits) and
`FlashStrength` (0.35; 1 restores the original full flash).

Existing Dead Cells bindings are used:

| Dead Cells action | Kirby action |
|---|---|
| Move / directional input | Original movement and move combinations |
| Jump | Jump / puff / flight |
| Down + jump on the ground | Original slide; passes roll-zone tunnels and bursts doors like a roll |
| Dive attack binding in the air or on a chain (Down + jump by default) | Stomp through the host dive attack; a floating Kirby exhales first, a climbing Kirby kicks off the chain |
| Dodge | Original Kirby guard |
| Skill 1 | Inhale / spit / copy ability attack |
| Down with a mouthful | Swallow and transform |
| Skill 2 | Discard the current copy ability |
| Homunculus | Create / return the original helper |
| Weapon buttons, base form | Equipped allowed sword, bow or shield |
| Weapon buttons, copied form | Copy attack; retained shield button still parries |

Shared bindings remain shared: if W is assigned to both Up and Jump, it sends
both actions. Change either binding in the game's controls menu to separate them.
In the recorded profile's default keyboard layout, **E** inhales/spits/attacks
with a copy ability, **Q** discards it, **Space** jumps, and **right-click** uses
the secondary weapon/shield. Keyboard X is a contextual menu binding; controller
X is the primary weapon button. The adapter follows the configured actions, so
remapping them in the game's controls menu also changes Kirby's controls.
Host tutorials for replaced jump, dodge and Homunculus actions are suppressed
while playing Kirby; healing, equipment and interaction tutorials remain available.

## Verified behavior

- The new outfit walks and jumps in a real isolated Prisoners' Quarters run at
  the original approximately 59.8261 frames per second. Host/source positions
  remain stable, including landing on an upper platform.
- Normal host key bindings, inverted movement and upward Pure Nail attacks pass
  the live input check. Its movement phase uses the production input reader,
  moving 94.6875px and rising 49.890625px during the original jump.
- Native movement rebasing preserves original position, velocity and animation
  traces. Original one-way platforms, slopes and swimming have source tests.
- The runtime sprite compositor matched 3,068,161 original framebuffer pixels
  over 29 cases: idle, all 24 abilities and four left-facing attack sequences.
  These comparisons do not prove every move or every background/window effect.
- Actual host Zombies are consumed through original suction and mouthful capture,
  followed by random source transformations. Retained physical shields perform
  real parries/counters while Kirby is evolved. Multi-enemy contents use original Mix.
- Source Sword collisions preserve 18/16 damage events and repeat-hit timing.
  Live Wheel attacks delivered exact 23/14/23 damage, and incoming host damage
  kept source and host health equal. Original guard/invulnerability have source tests.
- Fresh game processes resumed the exact saved source frame, position, copy and
  physical shield. A source save during suction reproduced the next 30 frames exactly.
- Standing 12×12 and crouching 12×6 source hurtboxes passed actual host collision
  queries: interior attacks hit and attacks just outside miss. Every temporary
  projection restored the host physical body afterward.
- A generated Dead Cells door breaks from original Sword damage. Actual biome
  transitions preserve copy and shield, and changing outfits preserves health.
  Lethal source damage ends the host run; New Game resets Kirby's full original state.
- Original Crash, Paint and Mike effects were verified in the running host game,
  with the HUD outside the background tint.
- Original ladder, Suplex, Cook, helper and Wheel helper riding have source tests.
  Enemy capacity supports 31 simultaneous native proxies; bosses use original
  capture resistance and source damage timing while retaining host AI.
- In the real Concierge arena, original inhale could not capture the boss, while
  original Sword dealt 32 damage and its host AI remained active. A separate
  31-Zombie fixture kept all 31 paired objects active and delivered exact Sword
  damage to 21 targets. These controlled checks are scoped in
  [the host boundary report](docs/HOST_BOUNDARY_TESTS.md).
- Live Ice damage kills the host enemy once and leaves the original frozen block.
  Source tests also verify kicking the block into another enemy.
- Original sound effects play at 48 kHz stereo. Only the source BGM player is
  muted, retaining Dead Cells music. Jump/landing audio and the SDL queue passed
  tests, including coexistence with the host's SDL3 and successful queuing to the
  default desktop audio device.
- Actual lab launches have preserved the hashes of the user's original local
  and Steam saves. DCCM disables achievements.

## Build and verification

The local project uses Python 3.12+, Pillow, NumPy, the pinned .NET 10 SDK, DCCM 35.13.6,
and the pinned melon-rs/melonDS sources. The setup tools download dependencies
beneath `~/.local/share/kirby-dead-cells/`, not into system directories.

```sh
python3 scripts/setup_runtime.py --build
python3 tools/nds_build.py
python3 tools/build_outfit.py --help
python3 scripts/stage_runtime.py --original-runtime \
  --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
python3 scripts/launch_runtime.py --seconds 50 --start-game \
  --outfit KirbySuperStarUltra --input-plan movement
```

The original private seed state is
`~/.local/share/kirby-dead-cells/oracle/ready2/000.state`; a ROM-derived state or
asset pack is not bundled in source control. See [original-ROM observations](docs/ORACLE_RESULTS.md)
for the bootstrap provenance, and [runtime launch instructions](docs/RUNTIME_LAUNCH.md)
for dependencies, isolation and test outputs.

```sh
python3 scripts/check.py
python3 tools/nds_tests.py
python3 tools/verify_renderer.py
python3 tools/verify_background.py
python3 tools/verify_native_init.py
dotnet run --project tests/speed -c Release -- \
  "$HOME/.local/share/kirby-dead-cells/oracle/vendor/melon-rs/target/release/libkssu_bridge.so" \
  "/path/to/Kirby Super Star Ultra (USA).nds" "$HOME/.local/share/kirby-dead-cells/oracle/ready2/000.state"
```

`tests/speed` runs Kirby's movement on the original controller with the speed
match and checks it against the Beheaded's measured speeds, walls, ceilings,
slopes, ledges and one-way platforms, and that thrown objects and attached
effects (Bomb's bombs, Fire's and Plasma's head emitters) keep their original
places as the source window moves, that Kirby stays framed on the original
screen, and that his walk cycle keeps up with the run.

The static extraction tools remain available for research and outfit scaffolding:
`tools/extract_kirby.py`, `tools/build_assets.py`, and `tools/verify_assets.py`.
Live gameplay uses original runtime graphics, including palettes resolved by the ROM.

## Compatibility and limits

This build targets the installed Steam Linux Dead Cells v35, build 23762174,
and the supplied USA revision 0 ROM. The renderer adapts source RGB5 palette
transforms to the host's continuous RGB blending; source camera shake is not
applied to the host camera. The enemy bridge has 31 simultaneous nearby slots.
Every host enemy, hazard, item and boss phase has not been individually tested.
See the detailed fidelity report for measured coverage and these boundaries.

Details: [ROM format](docs/ROM_FORMAT.md), [renderer](docs/RENDERING.md),
[runtime launch](docs/RUNTIME_LAUNCH.md), [gameplay fidelity](docs/GAMEPLAY_FIDELITY.md),
[tutorial integration](docs/TUTORIALS.md), [work tracking](docs/TRACKING.md), and [work journal](MODLOG.md). Older research notes describe the initial foundation;
the dated journal records subsequent implementation.

## Roadmap and tracking

| Milestone | Outcome |
| :--- | :--- |
| **M0 · Project foundation** | Repository, documentation, agent instructions and traceable delivery ([#1](https://github.com/NahuelGenchi/kirbys-dead-land/issues/1)) |
| **M1 · Playable mod** | Kirby playable in Dead Cells with original-ROM gameplay; play-test bugs and fixes |
| **M2 · Gameplay coverage** | Measured parity across host enemies, hazards, items, bosses and biomes |
| **M3 · Release** | Packaging and publishing without ROM, game files or generated packs |

Every change, whether a feature, bug fix, enhancement, document or repository
operation, starts with an issue assigned to a milestone and is delivered through a
pull request that closes it. Each new feature gets its own new branch from
`main`, and nothing is pushed directly to `main`. Read
[CONTRIBUTING.md](CONTRIBUTING.md) before working; agents start with
[AGENTS.md](AGENTS.md). The [tracking guide](docs/TRACKING.md) covers templates,
labels and the metadata workflow.

## Credits

Kirby Super Star Ultra: Nintendo / HAL Laboratory. Dead Cells: Motion Twin /
Evil Empire. Framework: Dead Cells Core Modding. ROM observation research:
[melon-kssu](https://github.com/benhall-7/melon-kssu). Built with Codex and Universal
Modder. Converter and adapter source are separate from the user's copyrighted
ROM and game assets; generated local packs are not redistributable releases.
