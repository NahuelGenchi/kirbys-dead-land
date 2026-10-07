# Original-ROM gameplay integration

The playable controller now executes the supplied USA revision 0 ROM inside the
Dead Cells process. It uses original code for movement, animation, attacks,
invulnerability, copy forms, helper behavior and sound. This is an engine adapter,
not a collection of estimated movement constants or substitute host weapons.

ROM SHA256: `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`.
The pinned emulator, memory contracts and native regression evidence are recorded
in [NDS_BRIDGE.md](NDS_BRIDGE.md). The original bootstrap experiment remains in
[ORACLE_RESULTS.md](ORACLE_RESULTS.md); it predates the live integration.

## Boundary between the games

| System | Original code and host adapter |
|---|---|
| Clock | 33,513,982 / 560,190 source ticks per second. A display within 3% of that rate presents one original frame per host frame, like an emulator synchronized to vblank; other rates accumulate real time |
| Movement | The ROM updates fixed-point position, velocity and action state; the host follows its physical foot. With `MatchHostSpeed` (default) Kirby moves at the Beheaded's measured speeds: each original frame's own motion is carried on through free space (run and air control 236.5 px/s, float across at the same speed, fall 403 px/s, floating up and down at the run's tempo), the original horizontal velocity is cleared when the direction is released or reversed, and the jump keeps its 50 px but peaks after 0.25 s, as the Beheaded's does. Added motion stops short of walls and ceilings, leaves the original a full fall step above floors, follows slopes at the original's whole-pixel surface, and never applies to hits or swimming. Ladders, chains and vines climb as much faster than the original as the run, 265 px/s up and 315 px/s down (original 139 and 165, the Beheaded's average 97.5): at the original's speed the owner still found climbing too slow (#48, #51). The added climb stops a full step short of a ladder's ends, the one-way top it leads onto, ceilings and floors, so the original makes its own last steps onto and off the ladder. Ability moves keep their original motion unless they carry Kirby: a dash attack, or any move faster than the original dash, moves at the run's scale (see [Carrying ability moves](#carrying-ability-moves-40)). The original walk and dash cycles play faster with the run (up to 1.67 times, one frame a pose), so his feet cover about the ground they did natively. `false` keeps the original DS speeds (the stomp keeps the dive's) |
| Terrain | Nearby host solids, one-way floors, slopes, ladders and water become source collision cells; all linked source objects and cached terrain probes move together when the window rebases by whole cells, while effects attached to a parent (Fire's head flame, Plasma's spark, attack effects) keep their offsets from it. Host movement of Kirby (speed match, stomp) moves only him and his terrain probes, so thrown bombs, cutters, effects and the helper keep their original paths. Over a moving host elevator the window is lifted to its surface and original objects ride with him (see [Host elevators](#host-elevators-deviation)) |
| Input | Existing host bindings become original digital buttons; ordinary source directional combinations still apply. A held direction dashes through the original double tap (`AlwaysDash`); a light analog tilt walks. A key bound to both Up and jump (W and the Up arrow by default) climbs at a ladder or chain, as in the host, instead of jumping first |
| Terrain | Nearby host solids, one-way floors, slopes, ladders and water become source collision cells. Vines grown with the Vine rune (`en.inter.VineLadder`) have no collision bits, only level spots (0, where the Beheaded climbs, and 18); their cells become source ladders, and the solid blocks a vine passes through or ends under become one-way ladder tops in that column. A ladder under a solid block stops the original below it, so this deviation lets Kirby climb onto the ledge, as the Beheaded's `exitLadderTop` does; all linked source objects and cached terrain probes move together when the window rebases by whole cells, while effects attached to a parent (Fire's head flame, Plasma's spark, attack effects) keep their offsets from it. Host movement of Kirby (speed match, stomp) moves only him and his terrain probes, so thrown bombs, cutters, effects and the helper keep their original paths |
| Input | Existing host bindings become original digital buttons; ordinary source directional combinations still apply. A held direction dashes through the original double tap (`AlwaysDash`); a light analog tilt walks. A key bound to both Up and jump (W and the Up arrow by default) climbs at a ladder, chain or vine, as in the host, instead of jumping first |
| Roll | Kirby's original slide is his roll and covers the Beheaded's roll: 131 px at up to 339 px/s. Host roll-zone tunnels (collision 65) open while he slides and stay open until he leaves one entered by sliding; walking cannot enter. The slide's fast part counts as a roll for the host's door and breakable-tile checks, and Suplex's grab lunge for its door check (see [Suplex's lunge bursts doors](#suplexs-lunge-bursts-doors-50)) |
| Dive attack | The player's host dive binding in the air or on a ladder or chain (original jump, fall, float or climb) starts the host `DiveAttack` under its own rules. The original controller keeps its falling or exhaling animation and physics (3.5 px/frame cap) and drops straight down; the host adds 11.5 px/frame of descent through free space, 15 px a frame in all like the Beheaded's dive (about 900 px/s), and leaves the original a full fall step to make the floor contact; the host skill resolves the landing (shockwave, stun, damage, breakable floors with the Ram rune). Only what stops the original fall counts as floor: solids, slopes and one-way platforms from above, never ladders or chains. Enemy touch damage is off while stomping and for 30 frames after landing |
| Camera | `CameraZoom` (default 1, the host camera) can zoom the host viewport; 2 shows about 200 host pixels of height, close to the DS screen's 192. Host cinematics and host zoom changes keep their framing |
| Palette flashes | The original hurt and guard flashes of Kirby's first two palette banks are shown at `FlashStrength` (0.35) of their colour change, with original timing; lasting palette changes are adopted after four frames |
| Graphics | Live original OAM, OBJ VRAM and palettes supply body, abilities, helper and effects; allowed host equipment has a separate overlay. OAM shows the previous source frame, so it is centred on that frame's Kirby position. The original draws only its 256-pixel screen: its camera moves with the window, and a placement frames Kirby at once, so he and the effects around him stay on that screen |
| Background effects | Original captured palette changes supply Crash/Paint/Mike timing and colors; host scenery receives the corresponding blend |
| Sound | Original stereo sound effects continue; the source music player is muted so Dead Cells music remains |
| Enemy collision | Live host enemies have paired source collision objects with their host dimensions; original routines resolve damage cadence, suction, grabs, throws and Cook |
| Enemy projectiles | Live hostile host arrows/grenades have paired copyless source objects; original suction, mouthful, spit star and swallow decide their fate |
| Star projectiles | The ROM moves Kirby's spit, big spit and discarded ability stars through host terrain (spit stars burst on walls, the ability star bounces). Each star strikes host enemies once and staggers them with the host dive's stun and bump; paired enemies take spit-star damage through their source object, all others from the host (see below) |
| Boss collision | Original resistance bits prevent inhale, Suplex capture and Cook; damage still uses original collision/debounce, while the host retains boss AI and phases |
| Host triggers | Pressure plates (trap plates and pulley points too) and hidden triggers test Kirby's physical foot on the ground, as for the Beheaded; enemy attacks still test the projected original hurtbox |
| Facing | The host hero's direction follows the original facing (bit 31 of player+0x6C) every source frame, so host shields block the way Kirby faces |
| Incoming damage | Host shield/parry resolves first; remaining damage, converted to the same fraction of Kirby's 76 HP as of the host's current maximum, enters the next original collision tick, preserving original guard, recoil and invulnerability |
| Touching enemies | As in Dead Cells, touching an enemy hurts neither Kirby nor the enemy: the original body-contact damage is excluded for every paired enemy, while Kirby's inhale, slide and copy attacks still hit, and the host's own touch attacks (spiked or electric enemies) still reach him as incoming damage. `OriginalTouchDamage: true` restores the original rule (7 of 76 HP to Kirby, and weak enemies destroyed by the bump) |
| Outgoing damage | Paired original enemies hold the host life in original units: host life ÷ (`CopyDamageScale` 3.5 × the host's per-tier life scale). Original HP deltas are multiplied back, so both games agree on the lethal hit; the result is then scaled like Dead Cells colorless gear (see below), and the deliberate Elite Sword bonus below multiplies it again; no other host hero multiplier alters copy attacks |
| Player health | Original full health is 76. Host and source health are synchronized, including host healing and original food |
| Saves | A versioned, ROM-checked native machine snapshot and physical shield/enemy bindings travel with the private host save slot |
| Arrival and death | Kirby appears in the host arrival flash rather than standing in its corpse sequence. On death the original lethal recoil and spin-away play over the host death sequence |

The source machine remains alive across host level transitions. Original helper,
projectile and copy states belong to that machine. Room changes retire paired
enemies and leftover Cook food through original destructors while retaining the
helper. A new run reloads the original seed; changing away from Kirby restores
the host character body and proportionate health. Returning imports its current
health fraction. Ordinary host progression, menus, death and level cinematics
remain host systems.
When a host cinematic walks the hero, such as leaving through an exit door, the
original controller keeps running with the same direction held, so Kirby walks
with the original animation and facing. The host keeps the position and the fade.
See [Cinematic walks](#cinematic-walks-intentional-deviation).
Host tutorial prompts that require the replaced jump, dodge or Homunculus actions
are suppressed only for Kirby. Existing matching prompts are canceled without
marking them complete; healing, equipment and interaction prompts still use the
host flow. See [TUTORIALS.md](TUTORIALS.md).

## Requested changes

Each bound host enemy receives one uniformly selected original copy ability.
Its source collision metadata carries that ability before absorption; the adapter
does not overwrite the mouthful aggregate afterward. Thus multiple captures use
the original Mix machinery. Each actual captured host enemy is removed once, and
Kirby's form changes when the ROM performs the swallow/transformation. Spitting
keeps the original source behavior. Floor ability items carry their own ability
the same way; see [ability items](#ability-items-intentional-addition).

### Projectiles in the mouthful

Hostile host projectiles (`dc.en.Bullet`, such as Archer arrows, and
`dc.en.Grenade`, such as Grenader grenades) are paired with source objects
exactly like enemies, through `KirbyEnemies`. Only projectiles for which
`Entity.isOpponent(hero)` holds are paired, so Kirby's own bow arrows, repelled
grenades and countered bullets stay host-only. A projectile's source object:

- keeps Waddle Dee's native copy byte 0, so the ROM builds its ordinary copyless
  mouthful (mouth buffer `[0,1,0,…]`): B spits the original star and DOWN
  swallows into nothing;
- stays inhalable (no boss resistance) but has no source body contact; the host
  projectile still hits Kirby through the normal host damage path when it is
  not caught;
- uses the host projectile's radius/height as its hurtbox and has 1 HP.

While the original suction controls it, `KirbyEnemyHooks` suspends the host
projectile's update (`Entity.canUpdate`, `Bullet.fixedUpdate`,
`Grenade.fixedUpdate`, `updatePositionXY`) and it follows the source object, so
it neither hits Kirby nor detonates. When the source reports the mouthful (or a
Cook capture), the host projectile is destroyed without a copy, loot or
explosion. The spat star then damages paired host enemies through the existing
source-HP-delta path. An original attack that damages a projectile's source
object also destroys it; this is the deliberate host mapping of a lethal source
hit on a 1 HP object. `MouthfulHazards` in `src/core/` holds these decisions.

The original inhale box spans 2px behind to 60px in front of Kirby and 34px
above to 4px below his foot. Archer arrows fly at the Beheaded's chest height,
about 55–60px above the floor, so on flat ground they pass over both that box
and Kirby's 12×12 hurtbox. Kirby catches them by inhaling in the air; low
projectiles such as grenades are caught from the ground. This is the ROM's
geometry, not an adapter limit.

Kirby fights only with the original inhale and copy abilities plus a host shield
(#39, owner request 2026-10-06). Every non-shield weapon button (left-click by
default) is original DS B in both forms: inhale/spit in base form, the copy attack
while copied. `KirbyRules.ResolveAttack` never routes to a host sword or bow, so
`Weapon.tryToUse` declines even the starting sword already in the inventory; its
sprite stays in the HUD slot but never swings. Base form uses any equipped shield;
copied form keeps only the exact retained shield for host parries. A shield can be taken in either form: as in Dead Cells, Take swaps it
for the equipped one (the old one drops), and the newly equipped shield becomes
the retained shield (#31). The retained shield follows the host inventory every
frame: it stays while equipped, otherwise the first equipped shield, or none, is
retained. If two shields are equipped, the most recently taken one parries while
copied. Every weapon other than a shield, and every skill, is declined in either
form through `InventItem.isUsefulFor`, the check the host makes
before every item drop, altar, shop purchase and timed-door reward. The host then
shows its own "Can't use that right now" message, leaves the item in place and
charges nothing. Shield identity uses the physical item's permanent ID. Source copy
loss, discarded-star recovery, helper creation and limited-use ability expiration
update the same equipment policy.

Before #31, copied Kirby also declined shields, so the owner's 2026-10-06
recording (Sword copy, wooden shield, dropped Cudgel V after the Concierge) showed
"Can't use that right now" on every R press. The apparent guard pose in that
recording is the dropped Cudgel's sprite overlapping a standing Kirby; no guard
input was involved. Live `20261006-004147` (Sword copy) and `20261006-004316`
(base form) each took a Warrior's Shield, then swapped it for a Cudgel through the
host replace dialog; see `docs/RUNTIME_LAUNCH.md`. Guard and parry code is unchanged.
### Gold deposits (#35)

Copied attacks reach a host entity only through a source proxy that
`KirbyEnemies` pairs with it. Before #35 the pairing accepted mobs, doors,
breakable props, explosive barrels and `isBreakableByHero` tiles, so the host
gold deposits (`en.inter.PileOfGold`, `en.inter.GoldNugget`) were never hit by a
copy attack; base-form host weapons broke them. Both now pair like breakable
props: not inhalable, no copy, no touch damage. Per `hlboot.dat` both call
`Entity.initLife` and `set_targetable(true)` in their constructors and drop gold
from their own `onDamage`/`onDie`, so the host still decides every gold drop.
Live (`docs/RUNTIME_LAUNCH.md`, "Gold deposits"): Sword Kirby broke a real
`PileOfGold` in four hits (life 4 → 0, four host gold drops); the same fixture
without the change did no damage. `GoldNugget` was not run live. Other host hittables outside the pairing list (`PiggyBank`,
`Gift`, `BreakableWall`, `HiddenBlock`, `Flower`, …) are unchanged and unchecked.

### Star projectiles

Issue #20. Kirby's stars act as host projectiles. The ROM still spawns, moves and
removes them. The host terrain window is their world, so the wall behavior is
original. The three original objects, told apart by their update routine
(`src/core/StarProjectiles.cs`), measured in the native bridge:

| Original object | Routine | Damage word | Enemies | Walls |
|---|---|---|---|---|
| Spit star (one object in the mouth) | `0x020BD718` | 52 | Bursts on the first enemy, in the frame of the hit | Bursts when its center reaches the wall |
| Big spit star (two or more) | `0x020BDD08` | 60 | Passes through, hitting each enemy it crosses | Bursts |
| Discarded ability star (SELECT, or a hit that knocks the copy off) | `0x02166A54` | 0 | Passes through without damage | Bounces off walls and floors |

The spit stars do not bounce in the ROM, so the adapter does not make them
bounce. After every source frame `KirbyStars` reads each star's original hit
records from the native hit list and strikes host enemies with them:

- **Paired enemies, spit stars:** the original collision stays the authority.
  The star's 52 or 60 source damage reaches the host through the existing
  source-HP-delta path, and the single star bursts on the source object as in
  the ROM. When that HP drop coincides with a spit star touching the enemy's
  source body (or, for a lethal hit, whose body records are already gone, its
  host-sized box), the host adds the stagger below.
- **Enemies without a source object** (outside the pairing window, or beyond
  the 31 slots): the host applies the star's own original damage word, converted
  as a paired enemy's HP drop would be (unit scale, Elite and colorless
  multipliers). The ROM star does not burst on them.
- **Discarded ability star, every enemy (intentional deviation):** the original
  star is a harmless collectible. By owner request it strikes each host enemy it
  crosses once for 52 source damage, the single spit star's original value. It
  keeps bouncing and can still be inhaled again to recover the ability.

The host strikes each enemy at most once per star (`StarStrikes`). On a paired
enemy the original collision decides how often a big star hits: `star_big`
recorded five 60-damage hits on one boss-mode proxy. The stagger is applied once
per star. Stagger follows the
host's own dive landing (`tool.mainSkills.DiveAttack.onOwnerLand`, read from
`hlboot.dat`). Before the hit come affect 16 for 0.7 s and a horizontal
`bump` of 0.2–0.3 away from the star. After a successful hit comes Stun
(affect 8) for the `DiveAttack` skill's `props.affect` of 1 s, times
`getDiminishingFactor(9, 3, 6)`. Host resistances, such as bosses, apply
inside those host calls. While the original recoil of a source hit holds a
paired enemy, the source controls its position, so that enemy moves with the
original recoil (5 px measured) instead of the host bump.

Limits: the original culls objects outside its 256-pixel screen from OAM, so a
star more than about 128 px from Kirby keeps flying and striking but is not
drawn. The ability star's room edge is the edge of the 100×9 source terrain
window, which starts 16 cells to Kirby's left. In the bridge it bounced off
that left edge, not a host wall, so far from Kirby it can turn in open air. A star that hits an unpaired enemy does not burst on it. Only Zombies
were struck live.

### Elite Sword (intentional deviation)

Elite host mobs do not roll a random ability: their source collision metadata
always carries original Sword (ability 18). The ROM still performs the inhale,
swallow and transformation. Swallowing an Elite marks a pending empowerment.
Kirby becomes empowered only when the original transform produces that Elite's
Sword; a mouthful that the original Mix machinery turns into another ability is
not empowered.

While empowered, copy damage to host targets is doubled
(`KirbyRules.EmpoweredDamageMultiplier`). This is a deliberate design choice, not
original behavior. The multiplier applies on top of the original-unit scale in
the Outgoing damage row: the paired source enemy holds host life ÷ (unit scale ×
multiplier), and each source HP drop is multiplied back by the same product
(`KirbyEnemies`). A hit that the source enemy survives never kills the host
enemy, so original death reactions stay in the same tick as the host kill.
`CopyDamageScale` in the core keeps the tested pairing rule for a single
multiplier.

The bonus ends whenever the copy is lost: a hit that knocks it off, a discard
(a re-caught star is an ordinary Sword), creating a helper, spitting the Elite
out before swallowing, or a new run. It survives saves and biome transitions.
Ordinary enemies keep the uniform random roll, and a Sword copied from them is
not doubled. Bosses are unchanged because they resist inhale. Known limit: source
damage is attributed per paired enemy, not per attacker. If an earlier helper is
still alive when Kirby later becomes empowered, that helper's hits are doubled
too. No visual cue shows the empowered state yet.

All 24 standard source copy forms are included: Cutter, Mirror, Beam, Fighter,
Suplex, Yo-Yo, Wheel, Bomb, Ice, Fire, Plasma, Ninja, Stone, Wing, Jet, Copy,
Sword, Hammer, Parasol, Paint, Mike, Crash, Cook and Sleep. Special states reached
through those forms, such as riding the Wheel helper, execute in the ROM too.

### Cook food drops (intentional addition)

The original Cook consumes bound ordinary enemies (proxy flags 136) and pops out
original food inside the ROM, which Kirby eats through source health. By owner
request, each host enemy that Cook consumes also drops one Dead Cells healing food
item. Just before the existing host kill, the adapter adds
`LootType.InvItem(InventItem.fromItem(LootGen.getSmallHealingKind()))` to that
mob's own loot. The host's death path (`Mob.onDie` → `dropLoot` →
`Level.dropMobLoots` → `updatedDelayedLoots`) then creates an ordinary `ItemDrop`
at the mob's last stable ground point with the host's normal loot scatter. The
item follows the player's food option, and the host's interaction, heal amount
and pickup rules apply unchanged. `KirbyRules.ClaimCookFood` allows one item per
host entity ID until the next level, run or restore.

Cook damage is unchanged. Bosses keep their original Cook resistance (the native
`boss_cook` case records no damage and a rejected capture), so they neither take
Cook damage nor drop food. Elites are consumed by Cook exactly as before and keep
their own host loot, but receive no food; the host has no existing food drop rule
for Bosses or Elites. Because the ROM's Cook food also remains, one Cook can heal
Kirby twice: once from original food and once from host food.
### Colorless copy damage

Intentional deviation from exact original damage, requested in issue #10. Copy
attacks count as Dead Cells colorless (gray) gear. They scale with the player's
highest scroll stat, using the host's own formula. Decompiled from `hlboot.dat`
SHA-256 `59350cad475a2375b9e39d7cd13fdb6e4ed9a027fd827ae5140f913c1c07ce7e`:

- `Hero.computeTiers` sets each tier to 1 + that stat's scroll count.
- `Entity.getRelevantTierFor` gives an item with the `Colorless` affix
  `max(brutalityTier, tacticTier, survivalTier)`.
- `AttackUtils.computeFinalDmg` applies `Const.scaleHeroValueToTier(damage, tier)`,
  which is `damage × 1.15^(tier − 1)`, then truncates with `Std.int`.

| Highest scroll stat (scrolls) | 0 | 1 | 2 | 4 | 8 |
|---|---|---|---|---|---|
| Multiplier | ×1 | ×1.15 | ×1.3225 | ×1.749 | ×3.059 |
| Wheel hit of 23 | 23 | 26 | 30 | 40 | 70 |

Each source tick, `KirbyEnemies.Prepare` reads the three host tiers and calls the
host's `Const.scaleHeroValueToTier(1, tier)` for the multiplier.
`ColorlessDamage` in `src/core/` holds the arithmetic and an independent copy of
the formula for tests. The colorless multiplier stacks with the original-unit
scale (Outgoing damage row) and the Elite Sword bonus: each source HP drop becomes
`Std.int(drop × unit scale × Elite bonus × colorless multiplier)` host damage.
The table shows the colorless factor alone. One source tick's drop is scaled as one value, as the host scales one
attack.

Lethality stays in agreement. The paired source enemy receives the smallest HP
whose truncated, scaled damage reaches the host's remaining life (rounded up from
life ÷ combined multiplier and checked with the host's arithmetic). Original death
reactions therefore still occur in the tick of the host kill. (Ice now leaves no
original block; see [Ice freezes with the host status](#ice-freezes-with-the-host-status-intentional-deviation).)
Source HP stays clamped to the native range (32,760); a single-tick source
kill at that cap kills the host enemy.

Unchanged: incoming damage, retained-shield parries, boss
capture resistance, source attack cadence and repeat-hit debounce. Only the scroll
multiplier is applied. Host weapon quality, affixes, mutations/perks, critical hits
and other hero bonuses still do not alter copy attacks.

This replaces the per-Boss-Cell curve proposed in #7 / PR #8, which was not merged.
At 0 scrolls the colorless factor is ×1, leaving the unit-scale and Elite behavior
unchanged except that host damage is truncated rather than rounded (at most 1 HP).

Evidence: core tests compare every hit with host truncation for tiers 1–40 and
host lives 1–400, including the floating-point case that division alone gets
wrong. Live runs `204919` (Wheel), `205107` (Ice) and `205246` (no scrolls) are
recorded in [RUNTIME_LAUNCH.md](RUNTIME_LAUNCH.md#colorless-copy-damage-2026-10-05).
With 4/0/2 scrolls, the host returned the same multiplier as the core copy.
Wheel 23/14 became 40/24, and lethal Ice still left the original frozen object.
Those runs predate the original-unit scale from #3, so they show the colorless
factor, not today's absolute numbers.
Other copy forms, bosses and breakables have not been run live with scrolls.

### Host elevators (deviation)

Dead Cells elevators (`en.inter.Elevator` and its `SimpleElevator`,
`PurpleElevator` and `MultiFloorElevator` subclasses all run
`Elevator.fixedUpdate`) are entities, not terrain. Each host tick an elevator
collects every hero, mob or item whose foot is within 2 px of its own foot and
inside its width. It then moves, marks only the one cell row under its foot as
solid (clearing the previous row), writes its `yr` as the ground height of the
cell it is in, and gives the collected entities its own `cy`/`yr`. While it
moves, its surface lies `(yr − 1) × 24` px above that solid row.

The original sees only whole cells. Before this fix, the first carry left Kirby
2–3 px above the ROM floor, the original dropped him back onto the cell row,
and he was out of the elevator's 2 px reach from then on. When its solid row
moved up a cell, the row under him was cleared and he fell (owner recording,
2026-10-05 23:51; [#28](https://github.com/NahuelGenchi/kirbys-dead-land/issues/28)).

While Kirby is inside an elevator's carry width, from its surface up to three
cells above it (`ElevatorRide`):

- The original's terrain window is lifted by the surface's offset from the
  solid row, so the ROM floor is the elevator surface.
- Kirby whose feet were within the host's 2 px of the surface on the previous
  original frame, and whom the original is not moving upward, moves with it,
  up or down, by the host's own carry rule. The host's carry alone missed him
  on fast descents: from about 10 px a tick the surface dropped away, and the
  original's fall (at most 3.5 px a frame) never caught it until the bottom
  ([#33](https://github.com/NahuelGenchi/kirbys-dead-land/issues/33), owner
  recording 2026-10-06 01:15, lab run `20261006-011803`).
- A rising elevator that passed his feet since the last original frame lifts
  him onto its surface, as the host rescues the Beheaded.
- A fast elevator overshoots its stop by up to a tick of travel and is put back
  on the next tick. Where the stop is set into level floor, that overshoot takes
  its surface into the solid floor cell. Kirby then stands on the floor's top
  (no lift, no carry into it). Following the elevator put his body inside the
  floor; the original pushed him out, the elevator came back a cell below him,
  and he dropped onto it with a squash, or at desktop frame rates with the
  original fast-landing rebound and stars (owner recording 2026-10-06 12:26,
  lab runs `20261006-124646` and `124727`).
- While the elevator moves, the source window is placed with Kirby, so **original
  objects (thrown bombs, cutters, effects, the helper) ride along with him**.
  This is the intentional deviation. Moving them alone and then Kirby back by
  himself, as the speed match does, left the parts attached to him a step off,
  and the original dropped a standing Kirby for a frame every few frames of the
  ride (live runs `20261006-000609` to `001130`, see [RUNTIME_LAUNCH.md](RUNTIME_LAUNCH.md#host-elevators)).
- The host draws a moving elevator's platform behind its physical surface, by
  up to a tick of travel (13 px measured on a 64-cell elevator). Kirby standing
  on it is drawn with the same lag (`KirbySpriteRenderer.SetDrawOffset`, set from
  an `Elevator.postUpdate` hook), as are his sword and shield. Off the
  platform, the offset eases back within a few frames. Drawn at his physical
  position instead, he floated above the platform on the way up and sank into
  it on the way down (owner recording 2026-10-06 19:50). His physics position is
  unchanged.
- His host cell position is copied from the elevator exactly, as its own carry
  does. A pixel round trip could leave him a rounding error below the cell's
  ground height, and the host then called its landing handler every few
  ticks.
- Enemies are placed in the same lifted window, so their positions relative to
  Kirby are unchanged. Other floors and ceilings are offset by up to one cell
  only while Kirby is over a moving elevator. At rest, the lift is zero.

Live checks (separate data root `repros/elevator-20261006`, a real
`SimpleElevator` built and initialized as `Level.spawnEntitiesFromMarkers` does):
Kirby rides up and down standing (`0406`) with his feet on the surface, including
full-speed descents of 12.4 px a frame, can jump on a rising elevator and land on
it again, and stops at both ends. A jump on a fast descending elevator leaves it
behind; he lands on it once it stops. Real generated elevators (a copy of the
owner's save, resumed in a separate data root) were also ridden 64 cells down and
up at a frame rate stalled to 25–100 fps, including overshoots into the floor at
the bottom stop. Not run live: `PurpleElevator` and `MultiFloorElevator` levels
and walking off a moving elevator onto a landing. The host calls its landing
handler with height 0 (no sound, smoke or shake below 0.3 cells) on a departure
and, at low frame rates, a few dozen times a ride when it carries Kirby between
two original frames. Floating platforms (`en.inter.FloatingPlatform`) are a separate
host mechanism and are not covered.
### Enemies in water (intentional deviation)

Issue #27. In the original game a Waddle Dee that enters water is defeated. When
the in-water byte `(d+0x50)+7` turns on, the source calls the enemy's enter-water
handler at `(d+0x54)+8`. Waddle Dee's reaction table `0x0219C7C8` holds the
default handler `0x02159440`, which calls the defeat entry `0x02158FC0`. While an
enemy recoils from a hit, the shared recoil handler `0x02156540` calls the saved
table's handler, or that same defeat if the entry is empty. Every host enemy is
paired with a Waddle Dee proxy, and host water is exported as source water. So a
Dead Cells enemy standing in a pool had a dying proxy: the source claimed it
(flags 3), the adapter pinned the host enemy to it every tick, inhale and attacks
missed, and after about 60 frames it was rebound in the same water. The
reserved damage carrier died the same way next to a swimming Kirby, so host hits
did not reach him in water.

Dead Cells enemies do not die in water, and the host owns enemy life. The bridge
therefore points that one table entry at the original return-zero stub
`0x020CF36C` (`mov r0,#0; bx lr`) in emulated RAM, after validating both the stub
and the entry. Kirby's swimming, the exit-water handler and other enemies' water
reactions are unchanged. Water itself still works on Kirby as before.
### Ice freezes with the host status (intentional deviation)

Issue #42. In the original game an enemy reduced to zero HP by Ice becomes a
kickable ice block; the runtime used to detach that source object so the block
stayed in Dead Cells beside the host death. The owner asked for Dead Cells' own
freeze status instead. Host routines were read from `hlboot.dat` (SHA-256
`59350cad…07ce7e`) with crashlink:

- `tool.weap.Freeze.onExecute` (Frost Blast) applies affect 23 for the weapon's
  `props.duration` × `getDiminishingFactor(3, 1, 8)`. The MDK's v35 `data.cdb`
  gives that duration as 3 s.
- `Entity.getDiminishingFactor` counts uses per key; the count resets 5 s after
  the last use.
- `en.Mob.onAffectChange`: when affect 23 ends the host pops "Slowed" and applies
  its thaw slow (133) and stun (8).
- `MobAttackTarget.preHitResultUpdate`: a hit on a frozen mob removes affect 23
  (the thaw above) unless the attack has tag 13 or its damage type is
  `ExtraDamage`, `Poison`, `Bleed` or `Frost`; small hits that are not `Fire`
  also leave it. The type is compared with the game's global enum value by
  identity, so a newly allocated `Frost` value does not count.

While the source step runs with Ice (ability 10), `KirbyEnemies.Complete`:

- deals each copy hit as host `Frost` damage. `KirbyDamage.Frost` finds the
  game's own singleton among the module globals (type `DamageType`, constructor
  `Frost`). A continuing breath therefore damages a frozen enemy without
  shattering the freeze, like the host's ice weapons; other attacks still
  shatter it as usual.
- freezes a host enemy that survives a hit the host resolved (not blocked or
  parried) and is not already frozen: affect 23 for 3 s × the host diminishing
  factor, as Frost Blast does. Host resistances apply inside `setAffectS`.
  Unlike Frost Blast, it neither resets the factor after 18 s nor pops
  "Resists" at factor 0.
- retires the source object at once (`proxy_set(active=false)`) when the host
  enemy dies, instead of detaching it, so the original ice block never appears.
  The host death, loot and death effects are unchanged.

Ice damage, its timing and the lethal hit still come from the original breath.
Other abilities keep their original death remnants. Limit: the choice is made from
Kirby's ability in that source step, so a non-breath kill while Kirby holds Ice
(for example an Ice slide) also has no original remnant.

Evidence: `nds_tests.py` case `proxy_ice_release` kills a 16-HP proxy with the
original breath (11 + 5), retires it in the lethal frame and finds no object
running the frozen-block routine `0x021583EC` in the 94 frames after. The same
scan on the old detach path finds the block in all 94. Live runs are in
[RUNTIME_LAUNCH.md](RUNTIME_LAUNCH.md#ice-host-freeze-2026-10-06).

### Vines (#25)

`VineLadder.buildLadder` (run when the Vine rune grows a vine, and again on load
for a grown one) writes no collision bits. It adds spot 0 and spot 18 to each
cell from the seed to the first solid cell, and spots 1 and 18 above that block.
A hanging vine marks spots 0 and 18 down through the ledge it hangs from. The
Beheaded climbs wherever spot 0 is set (`Hero.preUpdate`); `LevelMap.initSpots`
sets it from collision bit 16 for ordinary ladders, which is all the terrain
export read. `KirbyVines` hooks `buildLadder` and records that column; each
query reads the spots again. Vine cells are exported as source ladders, and the
solid blocks a vine passes through or ends under as one-way ladder tops. The
`ladder_top` bridge case shows why: under a solid block the original climb stops
at y 52.5, below it; under a one-way top it reaches the top cell (y 20).

Evidence, in the separate data root `repros/vine-20261005` with natural vines
grown through the host's own `VineLadder.activate`:

- `20261005-232657`: W climbs (`0x916`) at about 99 px/s, and Kirby stands on
  the ledge at the vine's top (feet at the block's top, 1656). A jump there lands
  on the ledge again. A natural Grenader on that ledge later killed him.
- `20261005-232901`: climb, hang, climb down mid-vine, climb to the ledge, then
  Down on the ledge climbs back down the vine.
- `20261005-233002`: an ordinary ladder, same plan: climb, hang and descend as
  before.

- `20261006-200031` (#48, original climb speed, separate data root
  `repros/climb-20261006`): two climbs up at 138.8 px/s and two down at
  165.0 px/s per original frame; Kirby stood on the ledge (feet 2712, the
  block's top, one frame after 2711.19 as before) and Down climbed back to the
  floor (2880). Life stayed 76.
- `20261006-211757` (#51, the climb sped up as the run, same data root): two
  climbs up at 265.2 px/s and one down at 315.3 px/s per original frame;
  Kirby stood on the ledge (1344, the block's top) and climbed back to the
  floor (1656). A natural Grenader on the ledge hit him at 9.6 s, and its
  recoil overlapped the second Down, which did not start a descent.
- `20261006-211905` (#51, `ladder` setup, an ordinary ten-cell ladder): two
  climbs each way at 265.2 and 315.3 px/s; Kirby stood on the ladder top (600)
  each time and climbed down to the floor (864). Life stayed 76.

Not run: hanging vines (`goingUp` false), a stomp from a vine, and a vine in a
saved run after loading.
### Carrying ability moves (#40)

Every copy-ability attack runs under the original action `142C`, which the speed
match had left at the original speed. The run is 1.91 times the original dash
(3.94 px a frame), so a dash attack or Wheel's roll, which the original makes
faster than the dash, ended up at or below running speed. Owner recordings
2026-10-06 12:19 (Wing's Condor Head) and 12:30 (Wheel).

`SpeedMatch.ActionFilter` now marks an ability move as carrying Kirby when it
starts straight from a dash, or once its horizontal velocity exceeds the
original dash's (528, 2.0625 px a frame). It stays marked until the ability
action ends. Its motion in the carried direction is scaled by the run's factor
(`GroundScale`), so it keeps the original's lead over the dash whatever is held.
The carried direction is the dash's, then the move's own once it is fast, so
Wheel keeps its speed after turning. Motion the other way keeps the original
speed. On the floor, the added motion follows the surface. When the original
lifts Kirby (Suplex's stumble, Wheel's wall bounce), it is added in the air.
Either way it stops a pixel short of walls, as the run does. The float exhale
and moves slower than the dash keep their original motion. This replaces the
earlier rule that every ability move keeps its original motion
([Jet fix](RECORDING_REVIEW_2026-10-05_1013.md)); braking still never applies to
them.

A headless survey of all 24 abilities (original controller, `tests/speed`
harness) compared standing, charged (70 frames), Down+B, mid-air and dash
attacks with and without the speed match. Only these change:

| Move | Original peak | Matched peak | Frames |
| :--- | ---: | ---: | :--- |
| Dash attacks: Cutter, Beam, Fighter, Suplex, Yo-Yo, Wheel, Bomb, Ice, Fire, Plasma, Ninja, Stone, Wing, Jet, Copy, Sword, Hammer, Parasol | 1.9–6.9 px | 1.91 times that | Same as the original for all 24 |
| Wing's Condor Head | 4.0 px | 7.64 px | 51 |
| Suplex's dash grab | 6.9 px | 13.1 px | 57 |
| Wheel's roll, from any start | 3.5 px | 6.69 px | Same |
| Jet's bursts and charged dash; Suplex's standing, Down+B and mid-air grab lunge | 4.0–6.9 px | 1.91 times that | Same |

Each dash attack's peak is the same multiple of the matched run as the
original's is of the original dash (for example Wing 1.94, Cutter 2.73, Suplex
3.33), and its distance is 1.9 times the original's. Mirror, Paint, Mike, Crash,
Cook and Sleep do not move in their dash attacks. Fighter attacks when B is
released, still straight from the dash. All other standing, charged, Down+B and
mid-air attacks keep their original motion.

`tests/speed` checks Wing, Suplex, Cutter, Fighter and Sword dash attacks
against the original, a Suplex dash attack into a wall, and Wheel's roll, Jet's
charged dash and Suplex's grab from a standstill (321, 216 and 124 px originally;
the last was labelled Yo-Yo before, but ability 6 is Suplex).

Live, in the separate data root `repros/dash-attack-20261006` (runtime
SHA-256 `62c31847e3cfae10716d7f24f23e4a211c283274603b116c89404523573a5abe`, core
`e9e87b28f7441230e6f59259e037bf360384339b0fac28a8eafc9598f66f21c0`, shared bridge
`654c2023ff6a0a17775253ce4ede68a3089f19f668abc6a55b5f1b6ef5441597`), with the
ability probe replaying original buttons (`KIRBY_LAB_ABILITY_PLAN`):

- `20261006-124027`, Wing: a dash left at 3.94 px a frame, then Condor Head at
  7.64 px a frame, slowing at its end as the original's does. The rightward
  pass only walked (2.63 px a frame), so its attack was the ordinary Wing attack.
- `20261006-124135`, Wheel: a dash left at 3.94 px a frame, then the roll at
  6.69 px a frame until it met a wall. The original's wall bounce then
  lifted Kirby, and he stayed outside the wall. A standing start rolled one
  frame at 6.69 px into a wall right beside him.

Both runs ended at their time limit without fatal log lines. Not run live:
other abilities, slopes and ledges during a carrying move, Wheel turning.
### Suplex's lunge bursts doors (#50)

Owner recording 2026-10-06 21:05: Suplex Kirby's dash attack ran into a closed
door, bounced off it and left it intact. A copy attack reaches a host door only
through the original object `KirbyEnemies` pairs with it, and every non-mob
pairing (doors, props, gold, breakable tiles) takes the boss option: original
hit-node mask `0x53` rejects grab, Cook and inhale and keeps damage. Suplex's
grab only captures, so it never connected with a door. Every other copy dash
attack damages the door's object.

Doors keep that resistance; grabbing the door's Waddle Dee stand-in would draw
it in Kirby's hands. Instead `HostStrikes.Grab` (`src/core/`) reports Suplex's
lunge to the host's own `Hero.canBreakDoors`, as the slide's fast part already
does, so the host bursts the door as it does for the Beheaded's roll. It holds
while Suplex (ability 6) carries Kirby (`SpeedMatch.Carry`) at 2 px a frame or
more: the 24-frame lunge of the dash grab (1760 falling to 1024), and the
standing, Down+B and mid-air lunges that carry him. The whiff stumble after the
lunge (32 frames at 384) does not count. Headless (`tests/speed`): a wall within
the lunge ends the grab on the frame Kirby meets it (action `0509`, velocity
0); the strike holds until he is a pixel from it. Breakable tiles are unchanged:
`canBreakTiles` still reports only the slide.

Live in `repros/suplex-door-20261006` (see `docs/RUNTIME_LAUNCH.md`, "Suplex
lunge into doors"): with the change, the lunge broke a real generated door on
its sixth frame; the control without the door hook bounced off a door the same
way and left it closed.

### Cinematic walks (intentional deviation)

Issue #17. Before this change, the source machine stopped during every host
cinematic. When the exit-door cinematic walked the hero into the doorway, Kirby
moved and faded in his last standing frame. Now, while a cinematic moves the hero
sideways, `CinematicWalk` (`src/core/`) turns that motion into a held DS Left or
Right, and `StepCinematicWalk` advances the original controller with it. Host
fixed updates skip some rendered frames, so the direction stays held for 0.2 s
without movement. A move of more than one cell in one frame is a placement, not
a walk.

The door cinematic plays the Beheaded's `walk`, then `stairs`: it lifts the hero
up steps inside the doorway, where the host map has no floor. With real terrain
the ROM switched to its Fall action (`0x070E`) there. During a cinematic walk the
original therefore gets a flat solid floor at the cell boundary where the walk
began, with nothing above it. The drawing follows the host body as usual, so Kirby
still climbs the steps on screen. Enemies, incoming damage, health sync and
abilities stay paused until normal play resumes. Original effect audio plays.
Without horizontal movement, for example during the arrival cinematic, the source
stays frozen as before.

Live run `20261005-214713` took the T_Courtyard door: 148 of 149 cinematic polls
were in the original Walk action (`0x060B`), facing right, with no Fall. Captured
frames show changing walk poses up the steps until the fade. Exits where the
host drops the hero, and walks to the left, have not been checked live.
### Ability descriptions

Issue #45. Intentional addition, mod-written text: these descriptions do not come
from the ROM. The original game's ability explanations name DS buttons, which do
not match Dead Cells bindings, so the owner chose text written for the mod's
controls. `AbilityDescriptions` in `src/core/` holds one short summary per copy
ability, followed by the shared line "Skill 1 or this button: attack. Skill 2:
discard."

The summaries name only well-established moves of the original game, such as
Fire's dash fireball or Stone's drop, and give no damage numbers. Not every listed
move has been checked live in the mod.

They appear on the pause screen's ability card (#41). Cards describe their item
through `InventItem.getGameplayDesc`; for the colorless stand-in, `KirbyHud`
returns the current ability's text, and the host lays it out, wraps it and
scrolls it. Nothing else describes the stand-in.

### HUD ability slot

Issue #18. In copied form every weapon button whose slot does not hold a shield
fires the ability (`KirbyInput`). Those HUD slots now show the ability instead of
the equipment Kirby cannot use while copied; shield slots keep the real shield.

- **Icon:** the original game's own bottom-screen ability icon. It is a 32×32,
  4bpp sub-engine OBJ at screen (4, 4); `AbilityIcon` in `src/core/` finds and
  decodes it from live sub OAM, sub OBJ VRAM and OBJ palette RAM. After each copy
  the icon is accepted once it is unchanged for 30 source frames, then held until
  the ability changes. The original copy flash and hurt variants are therefore
  not mirrored. It is drawn near the host item icon size (`Skill.ICON_SIZE`, 24)
  in whole screen pixels, so it is not resampled.
- **Frame and colors:** the host draws the slot itself. `KirbyHud` passes the
  host's `Skill.useItem` a `StartSword` stand-in with the `Colorless` affix, so the
  slot gets the host's `skillSlotColorless` gray frame and the colors it computes
  for colorless gear. It then puts the real item back as the slot's item, so
  inventory, equip and cooldown logic are unchanged, and hides ammo pips. Like
  colorless gear in Dead Cells, the background takes the color of the highest
  scroll stat, paired with the colorless gray.
  The slot's item is cleared before the stand-in is passed (#36). The host's
  same-item-kind path in `useItem` colors the slot from its previous item before
  switching. With the starting sword in the slot, that made every copy slot red.
  Ties take the first tier the host lists; no tie has been run live since #36.
- **Refresh:** the HUD is redrawn when the ability, its icon or the scroll tiers
  change. Base form, discard, ability loss and deactivation redraw the real
  equipment.
- **Pause equipment screen (#41):** `ui.pause.DefaultPause` builds each weapon
  card as a `ui.NewItemDesc` from the slot's item. While copied, the card of an
  equipped non-shield weapon is built from the same colorless stand-in, then the
  real item is put back. The host therefore draws the colorless card frame, its
  own "Colorless (the highest level applies)" line, and the card's icon slot. The
  existing `Skill.useItem` hook fills that slot with the original icon.
  `KirbyHud` then renames the card's first two texts:
  - The name becomes the ability (`YoYo` is shown as "Yo-Yo"). Only the name
    inside the host markup changes, so the colorless name color stays.
  - The main info line becomes "Copy ability". The stand-in sword's DPS does not
    describe copy damage, which also includes the original-unit and Elite
    scaling.

  Shield cards, other equipment and base form are unchanged. Only cards whose
  process is the pause screen are affected. An empty weapon slot, which also
  fires the ability, keeps the host's empty card.

Limits: the stand-in's own data (Brutality starting sword) is only drawn, never
equipped. A host weapon that changes its own HUD icon (Shovel, via
`HUD.updateIcon`) could briefly replace the ability icon until the next refresh;
this was not exercised. Live checks used a 1024×768 HUD (`pixelScale` 1.5).

### Ability items (intentional addition)

Issue #55, step 1 of #54. Kirby fights only with his own moves, copy abilities
and a Dead Cells shield, so host weapons and skills on the floor are useless to
him. While Kirby plays, they become **ability items**: one Dead Cells item per
original copy ability. The original game has no such items; this is a mod
addition built from host items and the original inhale and swallow.

- **Items.** `tools/build_outfit.py` adds 24 `KirbyAbility<Name>` rows to the
  private outfit pack (host item group Melee, not droppable by the host's own
  pools) with matching `weapon` rows copied from the starting sword's. The host
  needs a weapon row and weapon object for any item in a weapon slot;
  `Hook__Weapon.create` builds the starting sword's class for an ability item
  (the host throws for an item ID it does not know), and `KirbyMod` never lets
  it attack. Icons are the original bottom-screen ability icons that the HUD
  shows, captured headlessly by `tools/capture_ability_icons.py` and reduced from
  32×32 to the host's 24×24 item cells (area average, then snapped to the icon's
  own colors). Each item is created with the host's `Colorless` affix, so the
  highest scroll stat applies, at the item level of the item it replaces; it has
  no other rolls yet (#56, #57).
- **Replacement.** `ItemDrop`'s constructor passes its item through `initItem`
  before anything else, and every floor item is built that way: level loot,
  chests, enemy drops and the run's starting items. When the run's outfit is
  Kirby, a host weapon other than a shield, or a skill (host kind `Active`),
  becomes a random ability item, chosen uniformly among the 24 like an enemy's
  copy and not from the level seed. Shields, amulets, food, gold, blueprints and
  keys stay. Saved levels are unserialized without `initItem` and keep their
  items. Shop booths are a separate entity and still sell host weapons and
  skills (#58). The run's starting Rusty Sword is equipped by the host, not
  dropped; it stays in its slot until the first copy replaces it (#39 owns the
  rule that it never attacks).
- **Taking one.** `InventItem.isUsefulFor` is false for an ability item for every
  hero. A short press shows "Inhale and swallow it to copy"; a long press still
  recycles it for gold. Walking near one shows the host's own item tip: its name
  (`Yo-Yo` with a hyphen) and item level, "Copy ability" in place of the weapon
  row's DPS, the ability's description from `AbilityDescriptions`, and the host's
  "Colorless (the highest level applies)" line. The prompt keeps the host's
  "Take" label.
- **Inhale and swallow.** While Kirby is in base form, the only form that can
  inhale, ability items pair with the same 31 original proxies as enemies
  (`KirbyEnemies`), with the item's copy byte, full HP, no body contact and a
  hurtbox of at least 16×16. They take no damage. The item follows its proxy only
  while the original inhale pulls it (proxy flags 2 and 32), and the host's own
  float physics is held meanwhile; an original hit recoil (a spit star or a slide
  striking the proxy) never moves it. Once it is in the original mouth, the item
  leaves the floor. The original swallow
  then copies its ability, exactly as for an enemy carrying the same copy byte;
  in a mouthful of several objects the original roulette decides, and only the
  item of the chosen ability is taken. When the original mouth empties in base
  form (a spit, or a mouthful lost), the items go back where they lay before the
  suction, without the host loot bounce. A mouthful carried into another level
  returns under Kirby there. Copied Kirby cannot inhale, as in the original, so
  he discards his copy first.
- **Weapon slot.** Once per host frame `KirbyAbilityItems.Sync` keeps the slots in
  step with the copy: the copy's item sits in one non-shield slot, and base form
  has none. The swallowed floor item itself goes into the slot; a copy from an
  enemy gets a new base item of its ability at the level's loot level. The slot
  is the one already holding that item, else a slot holding another non-shield
  weapon (removed: it is unusable to Kirby), else an empty slot, else a shield
  other than the retained one (dropped to the floor). The host HUD is then
  reassigned with `Hero.hudInitItems` (`HUD.refreshItems` only redraws each
  slot's current item). Losing the copy removes its item; #56 will keep it with
  the dropped star for 7 seconds. The HUD overlay of the original icon (#18)
  still draws over the slot.

Limits: an ability item's proxy is an ordinary original enemy object, so base
Kirby's attacks can strike it. In lab run `20261006-223514` a spit star struck a
spat item's proxy and, before the recoil rule above, pushed the item three cells.
A single spit star is therefore expected to burst on an ability item as on an
enemy (not checked visually). Making the proxy inhalable but not hittable needs a
bridge change. Items in Kirby's mouth when the game is saved are not kept; after a
reload, the original swallow gives a new base item of that ability. The forge
and shops do not handle ability items yet (#58), and quality and affixes have
no effect yet (#56, #57). Only the Prisoners' Quarters start level and items the
probe created were checked live; other biomes' loot, chests, timed doors, Boss
Rush and the Collector's armory were not exercised.

## Evidence and current limits

Source regression checks cover rebasing, solid ceilings, one-way floors, slopes,
water, ladders, collision damage and debounce, original guard, suction/save-load,
Suplex and Cook. Additional original-framebuffer checks cover all standard forms,
helper graphics, and the captured full-screen effects. See the native, rendering
and audio reports for exact scenarios; these are not an exhaustive input sequence
for every move in the game.

Actual isolated Dead Cells runs have verified walking/jumping, real enemy capture,
random copying, retained-shield parry/counter, exact copy damage, incoming health
synchronization, and fresh-process resume at the saved source frame. The smaller
source hurtbox also changes from standing12×12 to crouching12×6 during host combat
queries without changing the physical foot outside those queries. Detailed run
IDs are in [RUNTIME_LAUNCH.md](RUNTIME_LAUNCH.md).

Live run `20261004-025047` also exercised the production input reader through
actual host key bindings: all nine action masks and inverted directions matched,
including the host's shared Up/Jump binding. Kirby moved94.6875px and jumped
49.890625px. Actual Pure Nail preparation selected its upward attack while host
jump and skill inputs stayed suppressed and every binding scope was restored.
That host weapon path no longer exists (#39). Live run `20261006-122227` held the
real left-click binding on the new run's `StartSword` in base form: the production
reader sent original B (mask 2), the source inhale flag was set for 32 frames,
and while the probe forced the host's button-down check for the sword, no
`Weapon.tryToUse` call reached the host sword and it made no attack. The
nine action masks, inversion, movement (195.5px) and jump (49.97px) matched again.
The input gate uses the host's exact native clock and absolute suspension expiry.

The real Concierge retained active host AI, resisted 96 original inhale frames
and took 32 original Sword damage. A controlled crowd of 31 live host Zombies
had 31 simultaneous source bindings; 21 targets each took exactly 18 damage.
The boss fixture blocked incoming hits and the crowd fixture locked enemy AI;
these verify the adapter paths, not full boss fights or simultaneous crowd attacks.
Live run `20261005-204132` swallowed a real Elite Zombie, which gave original
Sword (ability 18) with the empowered flag set; a second real Zombie then took
original Sword hits of 36, 36 and 32 (the original 18/18/16, doubled). That run
predates the original-unit scale from #3, so it shows the doubling, not today's
absolute numbers. It also did not exercise empowered-state loss, save/resume or biome transitions live;
the source rule tests cover those paths.
See [HOST_BOUNDARY_TESTS.md](HOST_BOUNDARY_TESTS.md) for exact evidence.
Live run `20261005-205320` (`--input-plan cook`) verified the Cook food path with
one real Zombie. Cook consumed it (life 120), the mob's loot held exactly one food
item when the host killed it, and exactly one new host `SmallMeat` `ItemDrop`
appeared in a level that had none. The drop landed 104 px from the kill point,
from the host's own loot scatter. Kirby reached it with original directions, and
a real interact (R) press ran `Hero.pickItem`. Host and source health then rose
together from 48 to 59. The earlier jump from 36 to 48 was the original Cook food.
Earlier runs `204940` and `205147` repeated the single drop and pickup. In run
`204739` the drop appeared but the fixture held R without a key-press edge, so
no pickup occurred; that fixture was corrected. Bosses, Elites, several cooked
enemies at once and save/resume with an uneaten drop were not exercised live.
Before #19 the host direction came from player+0x15, which never changes with
facing, so the host shield always faced right. Live run `20261005-215320` faced
Kirby left and raised the shield as a real host Zombie hit from the left: the
host parried it (`onShieldCounterSuccessful`, hit `Block`, no damage). Run
`215401` repeated this with the attacker on the right; the hit was `Normal` and
went on to the original collision. Raising the shield while holding the opposite
direction is not resolved: the original guard keeps Kirby's facing, the host
turns its shield toward the held direction within its own update, and the
provider resets the host direction to Kirby's facing every source frame. The
direction a hit is checked against in that case was not measured.
Ice damage and its surviving frozen block, background-effect placement, and an
actual biome transition have passed live checks. The final native suite passes
68 cases; source audio and host SDL coexistence tests also pass. Confirmed source
death ends the host run, and an actual new run resets Kirby's state. Cook's
temporary invulnerability preserves health through its persistent health group.
The host scenery blend has continuous RGB precision, whereas the original uses
integer RGB5 palettes. Original camera shake is not applied to the host camera.
Original Green Greens exit-door sparkle records are disabled when exporting host
terrain; original player, helper and ability effects remain active.
Live run `20261005-205827` caught two natural Grenader grenades and one natural
Archer arrow (`dc.en.Bullet`, caught mid-jump) with Kirby at full health
throughout. Each spat star hit its shooter for 52 host damage, and the swallowed
grenade left Kirby in base form with no copy. Only `EnemyGrenade` and the
Archer's plain `Bullet` were exercised live; other bullet subclasses (homing,
piercing, boss bullet patterns), Kirby's own bow arrows near the inhale box,
and saving during a projectile's suction were not.
Live run `20261005-221418` (`--input-plan stars`, issue #20) checked the star
projectiles with real Zombies given 5000 life, so that their stagger was
visible. In five phases, with Kirby at 76/76 throughout:
- A spit star hit a paired Zombie through its source object for 182 host
  damage. It was stunned (affect 8) and given affect 16, and it moved 5 px with
  the original recoil.
- A spit star struck a Zombie kept without a source object for 182 through the
  host path. It was stunned and pushed 25 px away.
- A discarded Parasol star crossed a Zombie behind Kirby for 182. It was stunned
  and pushed 27 px away, while the star bounced on the host floor every 64 frames.
- Beside a real level wall, a spit star burst with its center on the wall face.
- A discarded Sword star bounced off the wall face (its left edge at 983, the
  wall at 984).

Stun lasted about one second (59–61 source frames in that run, 85–88 in the
earlier run `221013`). Big stars, bosses, Elites, other enemy types, several
enemies struck by one star, and save/resume with a star in flight were not
exercised live; the native `star_big` case covers the big star's original
damage and piercing.
Complete one-to-one behavior for every host enemy, hazard,
item and boss has not been established. These boundaries must remain explicit;
a successful native or renderer test alone is not proof of every host interaction.
