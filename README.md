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
  While copied, the pause screen (Escape) describes the ability and its main
  moves in the mod's controls; see
  [ability descriptions](docs/GAMEPLAY_FIDELITY.md#ability-descriptions).
- Absorbing an Elite always gives original Sword, which deals double damage
  until the ability is lost (hit off, discarded, made into a helper, or a new run).
  This is a deliberate deviation; see `docs/GAMEPLAY_FIDELITY.md`.
- Kirby fights only with original inhale and copy abilities plus a Dead Cells
  shield (#39). Every non-shield weapon button (left-click by default) is the
  original B button: inhale/spit in base form, the copy attack while copied.
  Host swords and bows never attack, including the starting sword.
- Copied Kirby retains the exact shield it carried for parrying. Taking a
  dropped shield swaps it in as in Dead Cells, in either form, and the new
  shield becomes the one that parries.
- Items Kirby cannot use (every weapon other than a shield, and skills) get
  Dead Cells' own "Can't use that right now" message and stay on the ground; a
  long press still recycles them.
- In a Kirby run, every new floor weapon other than a shield, and every skill,
  becomes a colorless **ability item**: one of the 24 copy abilities, shown with
  its original icon. Walking near one shows its name, description and colorless
  rarity. It cannot be picked up: inhale it and swallow it to copy that ability;
  spitting it out puts it back. While copied, the ability's item fills a weapon
  slot and the HUD; the forge and shops are not adapted yet, and quality and
  affixes have no effect yet (#56–#58). See
  [ability items](docs/GAMEPLAY_FIDELITY.md#ability-items-intentional-addition).
- Original inhale, mouthful, Down-to-swallow, spit, flight, guard, slide and copy
  discard are executed by the ROM. All 24 original copy forms load through the
  original transformation code, rather than replacing them with host weapons.
- Hostile Dead Cells projectiles (arrows, grenades) can be inhaled. The ROM
  holds them as an ordinary copyless mouthful: spit fires the original star at
  enemies, and swallowing gives nothing. Kirby's own arrows are never inhaled.
- Spat stars and discarded ability stars are projectiles in Dead Cells. The ROM
  still moves them through the level: spit stars burst on walls, the discarded
  star bounces. A star staggers each enemy it hits with the host dive's stun and
  knockback. A discarded star, harmless in the ROM, hits for the single
  spit star's original 52 damage. This is a deliberate deviation; see
  [star projectiles](docs/GAMEPLAY_FIDELITY.md#star-projectiles).
- Each ordinary enemy consumed by the original Cook also drops one Dead Cells
  healing food item through the game's own loot drop. Cook's original damage is
  unchanged; Bosses and Elites drop no food.
- Copy attacks count as colorless gear: their damage is multiplied by
  1.15^(tier − 1), where tier is 1 + your highest scroll count (Brutality,
  Tactics or Survival). With no scrolls this factor is ×1. See
  [colorless copy damage](docs/GAMEPLAY_FIDELITY.md#colorless-copy-damage).
- While copied, each non-shield weapon slot in the HUD (normally Left-Click)
  shows the current ability's original icon in a colorless slot. See the
  [HUD ability slot](docs/GAMEPLAY_FIDELITY.md#hud-ability-slot). The pause
  screen's equipment card for that slot shows the ability too.

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
jump) is his roll: it passes Dead Cells roll-zone tunnels and bursts doors.
Suplex's grab lunge, which doors resist, bursts them the same way. In
the air, the Dead Cells dive binding (Down + jump by default) makes him stomp
with the host's own dive attack: it hits and stuns enemies and breaks
breakable floors once the Ram rune is unlocked.

Kirby moves at the Beheaded's speeds, measured in the game: he runs and steers
in the air at 236.5 px/s, slides as far and as fast as the Beheaded rolls,
falls at 403 px/s, stomps at the dive's 900 px/s and floats across at running
speed. His climb speeds up as his run does, to 265 px/s up and 315 px/s down,
faster than the Beheaded's 97.5 px/s. He climbs ladders, chains and vines grown with
the Vine rune, and climbs from a vine onto the ledge it leads to. He stops when the direction is released and
turns at once, and his jump keeps its height but peaks as fast as the
Beheaded's. Dash attacks, Wheel's roll and other copy-ability moves faster
than his dash keep their original lead over the run. His animations are the
original ones; the walk and dash cycles play faster with the run so his feet
keep up.

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
| Weapon buttons other than a shield (left-click by default), base form | Inhale / spit (original B) |
| Weapon buttons other than a shield, copied form | Copy attack (original B) |
| Shield button | Host shield parry and original guard; while copied, only the retained shield |

Shared bindings remain shared: if W is assigned to both Up and Jump, it sends
both actions. Change either binding in the game's controls menu to separate them.
In the recorded profile's default keyboard layout, **left-click** and **E**
inhale/spit/attack with a copy ability, **Q** discards it, **Space** jumps, and
**right-click** uses the shield (or, if the second slot holds another weapon,
also inhales). Keyboard X is a contextual menu binding; controller X is the
primary weapon button. The adapter follows the configured actions, so
remapping them in the game's controls menu also changes Kirby's controls.
Host tutorials for replaced jump, dodge and Homunculus actions are suppressed
while playing Kirby; healing, equipment and interaction tutorials remain available.

## Verified behavior

- The new outfit walks and jumps in a real isolated Prisoners' Quarters run at
  the original approximately 59.8261 frames per second. Host/source positions
  remain stable, including landing on an upper platform.
- Normal host key bindings and inverted movement pass the live input check. In
  base form, holding left-click on the starting sword sent original B and ran the
  original inhale, with no host sword attack (run `20261006-122227`, #39). Its
  movement phase uses the production input reader.
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
- A generated Dead Cells door breaks from Suplex's dash grab lunge, through the
  host's roll check (run `20261006-211833`; the control without it left the door shut).
- A generated Dead Cells door breaks from original Sword damage. Actual biome
  transitions preserve copy and shield, and changing outfits preserves health.
  Lethal source damage ends the host run; New Game resets Kirby's full original state.
- Original Crash, Paint and Mike effects were verified in the running host game,
  with the HUD outside the background tint.
- Original ladder, Suplex, Cook, helper and Wheel helper riding have source tests.
- Kirby rides a real host `SimpleElevator` up and down in isolated live runs,
  standing on its surface at full speed both ways, and can jump on a rising one
  and land on it again.
  Original objects ride with him while it moves (an intentional deviation; see
  [Host elevators](docs/GAMEPLAY_FIDELITY.md#host-elevators-deviation)). Other
  elevator types and generated elevator levels were not run live.
- In live runs `20261005-232657` and `20261005-232901`, Kirby climbed natural
  vines grown through the host's own `VineLadder.activate`, hung, climbed down,
  stood on the ledge at the top and climbed back down from it.
- In live run `20261005-205320`, original Cook consumed a real Zombie and the host
  dropped exactly one `SmallMeat` food item from that enemy's loot. Kirby walked
  onto it and a real R press ran the host pickup, healing host and source health
  together from 48 to 59 (15% of 76). Kirby also ate the original Cook food in the
  ROM (36 to 48). Bosses and Elites were not exercised live; their exclusion has
  source tests only.
  Enemy capacity supports 31 simultaneous native proxies; bosses use original
  capture resistance and source damage timing while retaining host AI.
- In the real Concierge arena, original inhale could not capture the boss, while
  original Sword dealt 32 damage and its host AI remained active. A separate
  31-Zombie fixture kept all 31 paired objects active and delivered exact Sword
  damage to 21 targets. These controlled checks are scoped in
  [the host boundary report](docs/HOST_BOUNDARY_TESTS.md).
- Live natural Grenader grenades and an Archer arrow (caught mid-jump; arrows
  fly above the original inhale box on flat ground) entered the original
  mouthful without hurting Kirby. Each spat star hit its shooter for 52 damage;
  a swallowed grenade gave no copy. See [enemy projectiles](docs/RUNTIME_LAUNCH.md#enemy-projectiles).
- Live spit stars hit a paired Zombie (through its source object) and an
  unpaired one (through the host). A discarded star hit a Zombie behind Kirby.
  Each hit stunned the Zombie for about a second and pushed it away. Beside a
  real level wall, a spit star burst on the wall face and a discarded star
  bounced off it. Stars far from Kirby keep flying and hitting; past the drawn
  range (below) they are not drawn. See [star projectiles](docs/RUNTIME_LAUNCH.md#star-projectiles).
- Shots stay drawn to about 252 px either side of Kirby, double the DS
  screen's 128 px (#63): live, a fully charged Plasma ball was drawn 246 px
  left of Kirby. Charged Plasma, Ninja's star, Beam's wave and Sword's beam
  were cut off before. A wider host view can still lose a shot near its edge.
  See [far shots](docs/GAMEPLAY_FIDELITY.md#far-shots-63).
- Taking a real exit door, Kirby walks into the doorway in the original Walk
  action until the host fade, instead of gliding in one standing frame. See
  [exit-door walk](docs/RUNTIME_LAUNCH.md#exit-door-walk-2026-10-05).
- Ice uses Dead Cells' own freeze status: Kirby's breath freezes the host enemy
  for 3 s (with host diminishing returns), his later Ice hits damage it without
  shattering the freeze, and the host thaw follows. An Ice kill leaves no original
  ice block. See [Ice freeze](docs/GAMEPLAY_FIDELITY.md#ice-freezes-with-the-host-status-intentional-deviation).
- Original sound effects play at 48 kHz stereo. Only the source BGM player is
  muted, retaining Dead Cells music. Jump/landing audio and the SDL queue passed
  tests, including coexistence with the host's SDL3 and successful queuing to the
  default desktop audio device.
- In live Kirby runs the start level's starting bow, weapons and skills became
  ability items while shields and gold stayed. An ability item was inhaled, spat
  back to its cell, inhaled again and swallowed into its ability, which then
  filled the weapon slot and the HUD; discarding cleared both. See
  [ability items](docs/RUNTIME_LAUNCH.md#ability-items-55).
- Actual lab launches have preserved the hashes of the user's original local
  and Steam saves. DCCM disables achievements.

## Build and verification

The local project uses Python 3.12+, Pillow, NumPy, the pinned .NET 10 SDK, DCCM 35.13.6,
and the pinned melon-rs/melonDS sources. The setup tools download dependencies
beneath `~/.local/share/kirby-dead-cells/`, not into system directories.

```sh
python3 scripts/setup_runtime.py --build
python3 tools/nds_build.py
python3 tools/capture_ability_icons.py
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
slopes, ledges and one-way platforms, that dash attacks and Wheel's roll keep
their original lead over the run, and that thrown objects and attached
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
