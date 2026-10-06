# Eighth actual-play recording review: Fire

User recording: `Screencast From 2026-10-05 10-47-27.mp4`, 14.3 seconds,
1920×1080, about 23 frames per second, no audio. It used the build staged with
the copy-ability motion fix. The user reported that the Fire copy ability is
bugged.

## Fire's head flame leaves Kirby on chains

Fire Kirby climbs a chain. His head flame does not climb with him: it stays at
the foot of the chain and falls one to three cells behind as he climbs
(6.4–8.2 s). When he stands at the top it is back on his head (8.5 s). Climbing
down, it stays above him at the top of the chain (8.8–9.7 s) and returns when
he reaches the floor (10.0 s). Running, the flame stays on his head, but the
embers trail a little further behind him than in the original.

**Cause.** The original attaches some actors to a parent: their `+0x80/+0x84`
holds an offset from the parent instead of a position, and each frame they
are drawn at a point on the parent plus that offset (`+0x78/+0x7C`). Engine
helpers `0x0200F208` and its kin add the parent's drawn position; `0x020C838C`
and `0x020CD368` add the parent's sprite anchor (`parent+0x40 → +0x748`, the
point `0x020A8F90` returns). Fire's head emitter (effect particles,
update `0x020CD588`, attach mode `+0x4A` = 1) is one of them; it spawns the
flame particles at its drawn position every six frames. `kssu_world` shifted
every actor's `+0x80/+0x84` by the window's change, so each time the 100×9-cell
window moved by a cell the emitter's offset from Kirby grew by 24 px. The window
moves vertically while he climbs and horizontally while he runs: after walking
seven cells the emitter was 168 px behind him, so the embers came from behind
him. The flame came back each time he stood on solid ground.

A headless replay measured the emitter's distance from Kirby: always 0 px with a
window that never moves, but 24, 48 and then 72 px on a climb, and up to 168 px
walking. A sweep of every copy ability, comparing each original actor's place
relative to Kirby with and without window moves, found the same fault in 18
actor classes: Plasma's spark, and effects of base Kirby, Mirror, Beam, Fighter,
Suplex, Wheel, Bomb, Ice, Fire, Plasma, Ninja, Stone, Jet, Sword and Parasol,
which jumped 24–264 px.

**Fix.** The bridge records every actor's drawn position and sprite anchor as
each original frame begins. When the window moves, `kssu_world` leaves an
actor's offset alone if its drawn position equals its parent's point plus that
offset, whether the parent's point was read before or after the parent moved
that frame (or plus the offset's whole pixels, mirrored with facing, for sprite
bones). It also moves drawn positions with the window, so an attached actor
that reads its parent's before the parent runs again stays on it. Absolute
actors (drawn position equal to their position) and actors that keep no drawn
position move as before.

After the fix the emitter stays at 0 px from Kirby while he climbs up and down
and runs both ways (Plasma's spark 1.9 px at most); the old bridge leaves Fire's
emitter 216 px away on the same path. The ability sweep finds no attached actor
still jumping with the window (three Fighter objects differ by up to 4 px late
in the sweep, as they did before the change). `tests/speed` checks both
(24 checks), the bridge's 59 original-ROM cases pass, and `scripts/check.py`
passes.

Lab run `113838` (Fire Kirby placed at the foot of a ten-cell chain, climbing to
the top, hanging, climbing down, then running both ways; 30 fps capture): the
flame stays on his head in every phase. Plasma on a chain (`115618`) keeps its
spark on him too. Regressions: Jet's dash `114238` (−216 and −77 px, as before),
Wheel's roll `114407` (to the wall about 290 px away and back, as before), Bomb
`114459` (lobbed bombs hold their places; the bomb held overhead stays on him),
Bomb damage `114614` (56, as before), combat `114959` (absorb, copy, shield parry
and counter) and the stomp `115313` (zombie killed). Two stomp runs with the late
plan (`114757`, `115235`) started the dive within two cells of the zombie's
one-way platform, where the host drops through it as the Beheaded does, so they
landed below it.

## Remaining

Embers are particles that rise from where they were spawned, so with the faster
Beheaded run they are spread over more ground behind Kirby than at the
original's walking speed. Paint's screen effect node and Crash's flash sit at
fixed coordinates and were not affected.
