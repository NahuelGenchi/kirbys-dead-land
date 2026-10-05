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
| Clock | 33,513,982 / 560,190 source ticks per second, independently of host rendering |
| Movement | The ROM updates fixed-point position, velocity and action state; the host follows its physical foot |
| Terrain | Nearby host solids, one-way floors, slopes, ladders and water become source collision cells; all linked source objects and cached terrain probes move together when the window rebases |
| Input | Existing host bindings become original digital buttons; ordinary source directional combinations still apply |
| Graphics | Live original OAM, OBJ VRAM and palettes supply body, abilities, helper and effects; allowed host equipment has a separate overlay |
| Background effects | Original captured palette changes supply Crash/Paint/Mike timing and colors; host scenery receives the corresponding blend |
| Sound | Original stereo sound effects continue; the source music player is muted so Dead Cells music remains |
| Enemy collision | Live host enemies have paired source collision objects with their host dimensions; original routines resolve damage cadence, suction, grabs, throws and Cook |
| Boss collision | Original resistance bits prevent inhale, Suplex capture and Cook; damage still uses original collision/debounce, while the host retains boss AI and phases |
| Incoming damage | Host shield/parry resolves first; remaining damage enters the next original collision tick, preserving original guard, recoil and invulnerability |
| Outgoing damage | Source HP deltas become exact host damage; the host's normal hero damage multiplier does not alter copy attacks |
| Player health | Original full health is 76. Host and source health are synchronized, including host healing and original food |
| Saves | A versioned, ROM-checked native machine snapshot and physical shield/enemy bindings travel with the private host save slot |

The source machine remains alive across host level transitions. Original helper,
projectile and copy states belong to that machine. Room changes retire paired
enemies and leftover Cook food through original destructors while retaining the
helper. A new run reloads the original seed; changing away from Kirby restores
the host character body and proportionate health. Returning imports its current
health fraction. Ordinary host progression, menus, death and level cinematics
remain host systems.
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
keeps the original source behavior.

Base form permits host swords, bows and shields. Copied form maps attack buttons
to the original ability and keeps only the exact previously equipped shield for
host parries. Shield identity uses the physical item's permanent ID. Source copy
loss, discarded-star recovery, helper creation and limited-use ability expiration
update the same equipment policy.

All 24 standard source copy forms are included: Cutter, Mirror, Beam, Fighter,
Suplex, Yo-Yo, Wheel, Bomb, Ice, Fire, Plasma, Ninja, Stone, Wing, Jet, Copy,
Sword, Hammer, Parasol, Paint, Mike, Crash, Cook and Sleep. Special states reached
through those forms, such as riding the Wheel helper, execute in the ROM too.

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
The input gate uses the host's exact native clock and absolute suspension expiry.

The real Concierge retained active host AI, resisted 96 original inhale frames
and took 32 original Sword damage. A controlled crowd of 31 live host Zombies
had 31 simultaneous source bindings; 21 targets each took exactly 18 damage.
The boss fixture blocked incoming hits and the crowd fixture locked enemy AI;
these verify the adapter paths, not full boss fights or simultaneous crowd attacks.
See [HOST_BOUNDARY_TESTS.md](HOST_BOUNDARY_TESTS.md) for exact evidence.
Ice damage and its surviving frozen block, background-effect placement, and an
actual biome transition have passed live checks. The final native suite passes
59 cases; source audio and host SDL coexistence tests also pass. Confirmed source
death ends the host run, and an actual new run resets Kirby's state. Cook's
temporary invulnerability preserves health through its persistent health group.
The host scenery blend has continuous RGB precision, whereas the original uses
integer RGB5 palettes. Original camera shake is not applied to the host camera.
Original Green Greens exit-door sparkle records are disabled when exporting host
terrain; original player, helper and ability effects remain active.
Complete one-to-one behavior for every host enemy, hazard,
item and boss has not been established. These boundaries must remain explicit;
a successful native or renderer test alone is not proof of every host interaction.
