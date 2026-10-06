# Fifth actual-play recording review

User recording: `Screencast From 2026-10-04 21-43-49.mp4`, 60.5 seconds,
1920×1080, about 23 frames per second (variable), no audio; play log
`lab/play-runs/20261004-214131` (pacing locked 99–100%). It used the build
staged after the fourth review. The user reported that Kirby still flickers a
lot, that Down + Space rolls but does not pass the places only a roll passes,
and that Down + Space in the air does not stomp.

## Flicker: one-frame position jumps

Kirby's pink pixels and the background motion were tracked in every frame.
Besides animation changes, Kirby was drawn about 60–80 screen pixels (21–28
host pixels, one terrain cell) away from his path for single frames, then
snapped back: rightwards while dashing right (for example 2.75, 3.33, 3.92 and
4.30 s), upwards while floating up a shaft (6.77–8.47 s) and downwards while
falling (14.5–15.3 s, 51.89 s, where he sank into the floor for a frame).
About one in three cell crossings landed on a recorded frame.

The fourth review measured only pixel dropouts and attributed them to the
ROM's palette flash, which is real but minor. It missed these jumps, which
were the main flicker.

**Cause.** The original machine sees a 100×9-cell window of the host level.
`kssu_world` rebases Kirby's coordinates every frame and the window moves by
one cell when he crosses a cell edge, but the source camera is not rebased.
The game builds OAM during a frame and the hardware shows it from the next
one, so after a source frame OAM describes the previous frame's positions.
The renderer centred the canvas on the newest state's screen position, so on
the frame after each rebase the new position was paired with the old OAM and
Kirby was drawn one cell off.

A headless bridge replay of the runtime's per-frame rebase confirmed it.
Paired with the same frame's state, the body OBJ offset jumped by 18–19 pixels
on every rebase: 17 times in 200 dash frames, 13 times floating and 21 times
jumping forward. Paired with the previous frame's state, it stayed exactly
(−16, −16) in all of them.

**Fix.** `NdsRuntime` records Kirby's screen position after each source frame,
before the next `World` call, and exposes the one matching OAM as
`DisplayedOrigin`. The renderer centres the canvas on it. The bitmap still
follows the current host position, so only the pose is one frame old, as on
the DS.

Live lab runs log both origins and the body bounds. In run `230948` (dash,
jumps, float, stomps; 1146 rendered frames) the old pairing would have drawn
35 single-frame jumps of 15–22 pixels; the fixed renderer drew none. Run
`224958` gave 36 and 0. The 30 fps captures show no jumps; the two flagged
frames are a dive through a platform and a water splash.

## Rolling through roll zones

Dead Cells' `ColType.RollZone` cells have collision data 65: bit 1 (solid for
the Beheaded) plus bit 64. `DodgeRoll.canPreventStuckInWall` lets a rolling
hero stand in them and `extendDodgeRoll` keeps him rolling until he is out.
They form one-cell-high tunnels under walls, often leading to secret zones.
The terrain export turned every `(c & 3) != 0` cell into solid source terrain,
so Kirby's slide (Down + jump, his roll) stopped at them.

**Fix.** Roll-zone cells are exported as open while Kirby slides (original
action `0x0B18`, the same code for every copy ability) or presses Down + jump
on the ground, which opens them for the frame the slide starts.
`RollZoneGate` keeps a zone entered by sliding open until Kirby is clear of it,
so a slide that ends inside never traps him, while walking cannot enter.
Kirby's original body fits a 24-pixel tunnel (checked headless).

During the fast part of the slide, `Hero.canBreakDoors` and
`Hero.canBreakTiles` also report a roll, so the host's own `Door` and
`BreakableTile` checks burst doors and shatter tiles, as a roll does.

Lab run `225230` used a real zone in a generated level. Walking stopped at its
edge; the slide went in; Kirby walked out of the far side; the next zone
blocked a walk and passed a slide. Run `230011`: a slide into a closed door hit
it and Kirby then walked through. Kirby's own slide also attacks the door's
source proxy, so he does the original little bounce back.

## Stomp (Dead Cells dive attack)

The host binds the dive to Down + jump by default (`Options.diveAttackBind`:
0 Down + jump, 1 hold Down, 2 press Down, 3 a dedicated action, 36). Kirby's
input hid jump and Down from the host, and the original game has no ground
pound, so pressing them in the air only floated.

**Fix.** In the air (original jump, fall or float), the player's dive binding
starts the host's own `DiveAttack`, with its normal rules (`canUse`, including
the 1.5-cell minimum height). The skill brings its sounds, fast-fall effects
and pass-through of the platform just below. The original controller keeps
its own animation and physics:

- Kirby's buttons are released.
- His horizontal speed is zeroed and his fall pushed to the original
  3.5-pixel cap.
- A floating Kirby first exhales his air, the original way to stop floating.

The host adds 4.5 pixels of descent a frame through free space, stopping one
pixel above the floor so the original collision makes the contact. On landing
the skill's `onOwnerLand` runs its shockwave, stun, damage, breakable floors
and end.

While stomping, and for 30 source frames after the landing, enemy proxies do
not hurt Kirby by touch (`kssu_proxy_contact`), like the host dive. Kirby's
own attacks still hit them, and host attacks still hurt him. Deep water ends
the dive without a landing; a hit, death, a scene change or the host ending
the skill cancels it. The host's dedicated dive action is now also hidden
from the host, so it never starts a dive by itself.

Lab results:

| Run | Result |
|---|---|
| `224958` | Stomps from a float (2.4 cells) and from a jump (5.9 cells) both landed through the host skill. |
| `225611` | A stomp onto a real Zombie killed it (123 life) with no damage to Kirby. |
| `231121` | Float stomp on the final build: exhale, full-speed fall, Zombie killed (126 life). |
| `225825` | With the Ram rune granted for the run, the stomp shattered a `BreakableGround` and Kirby fell through. |
| `230055` | Without it, the floor refused (shook) as for the Beheaded. |
| `230948` | A stomp into deep water ended without a landing. |

## Verification

Separate data root `repros/bugfix-20261004`; every run ended without a fatal
error and left the real saves unchanged. Combat probe `230623` consumed a real
Zombie, copied a random ability and countered with the retained shield.
Offline: 20 Python tests, 6 parser tests and 77 rule, pacer, dash, palette,
roll-zone and stomp assertions pass; the Release build has no warnings.

Not verified live: breakable tiles (none in the Prisoners' Quarters levels
tested), other dive bindings than the default, and a stomp onto a moving
platform or elevator.

## Follow-up: crash when taking an item from a chest

After this build the user reported a crash while opening a chest. Play log
`lab/play-runs/20261004-231913` ended with the host's
`ui.HUD.onItemPick: unexpected posId`. The path was the replace-item dialog
(`ReplaceItem.onValidate`) → `Hero.pickItem` → `applyItemPickEffect` →
`hudOnItemPick`.

**Cause.** `Inventory.add` lists the new item with `posID = -1` and calls
`Inventory.equip` to give it a slot. Since the first version, the mod refused
`equip` for items outside Kirby's rules: weapons other than swords, bows and
shields, any skill, and any weapon while copied. The item then stayed listed
without a slot and the HUD threw. Every pickup of such an item crashed, with or
without the replace dialog.

Lab run `233522` reproduced it exactly with the previous build. In base form,
with a free slot, interacting with a dropped Backstabber gave the same fatal
error.

**Fix.** The equip hook no longer refuses. Kirby declines unusable items
through `InventItem.isUsefulFor`, which the host checks before every item drop,
altar, legendary altar, shop booth and timed-door reward. When it is false the
host shows its own "Can't use that right now" message, leaves the item, and
charges nothing; a long press still recycles a dropped item.

Lab results:

| Run | Result |
|---|---|
| `233628` | The Backstabber is declined three times with the host message and no crash. |
| `233707` | A Broadsword is picked up into the free slot. |
| `234402` | With full slots, a Katana opens the replace dialog; validating it replaces the Rusty Sword in slot 0 and drops the Rusty Sword. |
| `233904` | With the Sword ability, a Broadsword is declined. |
| `234504` | The combat probe still equips and retains its shield and counters. |

## Follow-up: stomping from a chain

Recording `Screencast From 2026-10-04 23-59-46.mp4` (17 s): on a chain, Down +
Space made Kirby hop off and float. Each later press burst the dive landing
in mid-air on the chain or beside its platform.

**Causes.**

- **No stomp from a chain.** The original climb is a separate action (`0x0916`),
  so the stomp did not start from it. Down + A then reached the original, which
  jumps Kirby off a ladder.
- **Ladders counted as floor.** The stomp's floor check counted every non-empty
  exported cell, including ladders. Headless tests show the original never
  lands on ladder cells, top included, so stomps started next to a chain landed
  at once.
- **Platforms counted from below.** One-way platforms counted even when Kirby's
  feet were already below their top, as where the chain passes through the
  wooden platform.

**Fix.**

- A climbing Kirby can stomp: the original A kicks him off for one frame, then
  the stomp's fall takes over. The host's minimum height still applies.
- The floor check ignores ladders and chains, and counts a one-way platform
  only from above.

Lab run `000547` used a real six-cell chain: Kirby climbed it, stomped from the
climb and landed 6.7 cells lower on the floor through the host dive. Regression
runs `000659` (float stomp onto a Zombie, killed) and `000742` (moves; 0
position jumps against 41 for the old pairing) still pass. Offline checks pass
with 80 assertions.

## Follow-up: W jumps before climbing

Recording `Screencast From 2026-10-05 00-12-44.mp4`: at a chain, W made Kirby
jump and then grab the chain. The host's default keyboard binds W and the Up
arrow to both Up and jump (jump's second and third keys), and the host climbs
with them at a ladder. Kirby's input sent the original both A and Up; the
original jumped on A and grabbed the chain on Up a few frames later. The
earlier chain lab run `000547` showed the same hop (jump `0x070D`, then climb
0.3 s later).

**Fix.** `KirbyInput` reports when jump is held only through keys or buttons
that are also bound to Up. While Kirby climbs or his body overlaps a ladder or
chain cell, the provider drops the jump half of such a key until it is
released, so holding it to the top does not jump either. A pure jump key
(Space) still jumps off a chain, and away from ladders W and the Up arrow
still jump.

Lab results:

| Run | Result |
|---|---|
| `001748` | W at the foot of a chain climbed at once; holding it to the top stood Kirby there; Space then jumped. |
| `001935` | On open ground, W and the Up arrow each jumped. |
| `002008` | W climbed and Down + Space stomped from the climb to the floor 3.7 cells below. |

## Follow-up: pressure plate does not switch on

The user reported a floor switch that stayed off with Kirby standing on it. It
is the host `PressurePlate`. Each fixed step it requires the hero to be:

- on the ground: the cell below solid, the foot at the ground height and
  `dy` zero;
- within 18 pixels of the plate's centre;
- in the plate's row.

`KirbyHurtbox` projects Kirby's original hurtbox onto the host hero during
`Level.update`, so enemy attacks test his real original body: radius 6,
height 12, its bottom about 9 pixels above his foot. It restores the physical
foot only during the hero's own updates. The plate updates inside
`Level.update` and saw the foot at `yr` 0.625 instead of 1, so Kirby was never
on the ground for it.

A lab trace of the hero through each update phase confirmed it: the foot was
at `yr` 1 after `Hero.preUpdate` and 0.625 at the level's collision pass.

**Fix.** `PressurePlate.fixedUpdate` (which also serves trap plates and pulley
points) and `HiddenTrigger.fixedUpdate` now run with the physical body. Combat
geometry keeps the projection.

Lab results:

| Run | Result |
|---|---|
| `004409` | Kirby standing on a generated level's plate triggered it after 0.9 s. |
| `004459` | The combat probe still swallowed, copied (Parasol) and countered. |
| `004617` | A host Zombie hit still took 76 to 61 health. |

Offline checks pass with 80 assertions.

