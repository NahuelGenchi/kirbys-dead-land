# Sixth actual-play recording review: Bomb

User recording: `Screencast From 2026-10-05 02-07-33.mp4`, 31.1 seconds,
1920×1080, about 23 frames per second (variable), no audio. It used the build
staged with the Beheaded speed match. The user reported that the Bomb copy
ability is bugged.

## Bombs trailing Kirby

Every frame of the recording was extracted and cropped around Kirby. Bomb Kirby
pulls a bomb out and holds it overhead (0.73–0.93 s). When he runs off, the
bomb does not stay where he released it: it hangs at head height behind him and
drifts the same way he runs, more slowly, until it explodes far from where it
was thrown (0.98–2.2 s, 3.3–5.5 s, 7.2–7.8 s, 25.8–27.7 s). On a chain at 22.2 s
a bomb ended up under his feet. The original lobs the bomb high and slowly, so
it does hang in the air for about a second, but in place.

**Cause.** `kssu_world` moves every original actor by the same delta as Kirby:
correct when the 100×9-cell window moves under him by whole cells, but the speed
match (and the stomp's descent) also move Kirby a few pixels every frame through
that call. Each of those moves carried his bomb, other thrown objects, effects
and the helper along with him. A slowed climb carried a bomb downwards.

A headless replay with Bomb Kirby showed it: a bomb lobbed from a standstill
drifts 0.43 px a frame in the original; with the speed match it drifted
1.7 px a frame while Kirby walked away, and an effect that never moves in the
original crept after him by exactly his added speed.

**Fix.** A new bridge call, `kssu_move_player`, moves only Kirby (with his cached
terrain probes and the position records `kssu_world` moves with him). The
runtime gives `kssu_world` only the whole-cell window change, then moves Kirby
alone by the host's movement; host teleports and the first placement after a
load still move everything. An older bridge without the call keeps the previous
behavior.

In the replay the bomb's path with the speed match is now identical to the
original's, frame for frame; `tests/speed` checks it (45 frames, 0 px apart).
Lab run `022523` (Bomb Kirby throwing, then running and jumping away; 30 fps
capture) logged a bomb lobbed at x≈3306 staying at 3306–3312 while Kirby dashed
180 px the other way, then exploding against the wall it hit; the captures show
each bomb holding its place against the background. The bridge's 59 original-ROM
cases pass.

## Remaining

The renderer draws the original's frame (one frame old, like the DS display)
anchored at Kirby's current host position, so original objects that are not
attached to Kirby are drawn shifted by Kirby's movement during one frame: about
2 px at DS speed before, up to about 6 px at full running speed now. It shows
only as a small shift of a bomb or effect when Kirby starts or stops; anchoring
the frame where the DS drew it would instead pull Kirby off his host effects and
equipment.
