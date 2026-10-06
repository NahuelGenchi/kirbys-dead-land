# Fourth actual-play recording review

User recording: `Screencast From 2026-10-04 21-08-49.mp4`, 97.4 seconds,
1920×1080, 20 frames per second (variable), no audio; play log
`lab/play-runs/20261004-210827`. It used the build staged after the third
review. The user reported that Kirby still flickers, and asked why the game is
now zoomed.

## Flicker

Every frame was extracted with its presentation time and Kirby's pixels were
measured. 57 single-frame dropouts occurred, all one recording frame long
(the recording captures every third display frame). Zoomed in, each is one of
three things:

- **Whitened Kirby (34 frames).** The body turns pale white-pink and vanishes
  against light scenery (for example 59.4–60.0 and 83.38 s). This is the ROM's
  own flash: after a hit it replaces Kirby's first two palette banks on 3 of
  every 6 frames, first with an orange set (`(247,156,90)` for the main pink)
  and then with a half-whitened set (`(247,231,247)`), which it also uses on 2
  of every 4 frames while guarding. OBJ traces over dashes, jumps, hits and
  guard never dropped a body object, and the decoded body never fell below 882
  pixels, so nothing is hidden; only the colours change.
- **Yellow Kirby (13 frames).** The same ROM flash while a copy ability's cap
  palette is affected (36–37.5 s, Fighter).
- **Host effects (10 frames).** White impact bursts, a parry flash and the
  final desktop frame.

The host-side hit blink fixed in the third review no longer occurs: the hero
sprite alpha stayed at 1 in every lab frame. On the DS's small screen the
palette flash reads as a quick blink; on a 24-inch monitor the whitened body
disappears for a frame against light walls, which reads as flicker.

**Fix.** `PaletteFlashSoftener` (Kirby.Core) tracks the stable palette of
Kirby's first two banks and, while a bank differs from it, shows a blend of
the two (`FlashStrength` 0.35: 35% flash, 65% stable). A palette that stays
changed for 4 frames becomes the new stable palette, so copy
transformations, Stone, Ice and other lasting changes are unaffected; only
sub-4-frame alternations are softened. The flash timing is kept as a gentle
tint. Setting `FlashStrength` to 1 restores the original flash.

Lab run `213157` (Ninja) and `213303` (base): through a real Zombie hit and a
held guard the body colour alternated between `(223,172,236)` and
`(225,156,236)` instead of reaching the whitened palette; alpha stayed 1.

## Zoom

The third review set `CameraZoom` to 2 so Kirby's on-screen size and speed
matched the DS screen. The user did not want the view changed. The default is
back to 1 (the Beheaded's camera); the option remains for anyone who prefers
the DS framing. Hold-to-dash and the damage scale stay, so Kirby still moves
at his original dash speed and copy abilities keep their host-scale damage.

## Verification

Separate data root `repros/bugfix-20261004`; all runs ended without a fatal
error and left the real saves unchanged. Combat probe `213605` consumed a real
Zombie, copied Ninja at random and countered with the retained shield. Offline:
20 Python tests, 6 parser tests and 52 rule, pacer, dash and palette assertions
pass; the Release build has no warnings. The play log's new pacing lines
showed the vsync lock engaged on the user's display (locked 100% and 98% in
the first two minutes).
