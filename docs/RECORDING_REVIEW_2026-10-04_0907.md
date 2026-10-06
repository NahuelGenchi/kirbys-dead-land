# Second actual-play recording review

User recording: `Screencast From 2026-10-04 09-07-58.mp4`, 105.4 seconds,
1920×1080, about 22.75 frames per second, no audio. The matching play log is
`lab/play-runs/20261004-090737`; it has no fatal error and ends with a host
death record. The whole timeline was inspected at two frames per second, then
every frame of the suspicious stretches was cropped around Kirby. The display
was 1920×1080 at 59.96 Hz with vsync enabled.

## Observations and causes

| Time | Observed | Cause |
|---|---|---|
| 0–8 s | Kirby stands next to the host corpse while green slime crawls onto his face; a flame column then rises from his head. | Before the hero exists, the arrival corpse draws the outfit atlas's static fallback pose. During `rebirth`, host slime particles cover the body, and `HeroHead`/`Fx.heroHeadPopFinal` ignite the Beheaded's spirit flame at its neck height, just above Kirby. |
| 9–35 s and later | Kirby repeatedly turns white or pale for a few frames, especially while attacking. | Host sword frames are drawn for the Beheaded's tall body, so the GlowKey-lit blade crosses Kirby's face. The equipment overlay was added after Kirby's bitmap and so drew in front of him. Reproduced in lab run `092916` with an unchanged source palette. |
| 26–30, 39–44, 52–53 s | Alternating normal and pale or red frames at about 15–30 Hz. | Authentic original behavior. The ROM replaces Kirby's palette bank 0 with a warm then gray flash for 3 of every 6 frames after a hit, and for 2 of every 4 frames while guarding. Offline bridge traces reproduced both; walking, dashing, crouching, inhaling, jumping and sliding never flash. |
| 36–90 s | Cyan Ice Kirby, salmon Suplex Kirby, tan Stone Kirby, a muscle-man statue. | Authentic per-ability body palettes and Stone's rare forms, matching the original framebuffer references. |
| 92–96 s | After the lethal hit, Kirby stands idle through "Body desecrated!" and then dissolves. | The host destroys the hero immediately and stages a corpse, which drew the static fallback Kirby. The original machine stopped at the lethal frame, so Kirby's own death animation never played. |
| Throughout | Occasional uneven motion. | Wall-time accumulation against a 59.96 Hz display drops or doubles an original frame every few seconds, with jitter near frame boundaries. |
| Throughout | Kirby dies after relatively few Prisoners' Quarters hits. | Host damage was applied unscaled to Kirby's 76 HP, while host enemies are balanced for the Beheaded's 100+ HP; host health upgrades had no effect on Kirby. |

The brown screen tints at 7 s and 26 s are the host arrival flash and timed-door
effect, and the red tints are host damage/low-health effects. They are not
source background effects.

## Corrections

- **Weapon layering.** `KirbySpriteRenderer.KeepBodyInFront` keeps Kirby's
  original body above the equipment overlay, so blades, bows and shields show
  around Kirby instead of washing out his face. The host material is unchanged.
  Lab run `095009` compared five layering modes; reordering alone fixed it.
- **Fallback sprite.** `KirbyHostVisuals` resolves the outfit atlas through the
  host asset manager every frame while the Kirby outfit is selected, and skips
  every HSprite drawn from it: the arrival corpse, hero body, death corpse and
  host afterimage trails. The arrival corpse uses the atlas before
  `Hero.initGfx`, so hero-based detection was too late (run `102734`).
- **Arrival.** Kirby stays hidden from `rebirth` until frame 17 of
  `headPopStart`, appearing in the host's white flash. The `HeroHead` emitter is
  silent while Kirby is active and the flame burst is skipped for the Kirby
  outfit. The spirit crawling to the body and leaving it at death are unchanged.
- **Death.** The provider keeps the renderer and machine after the host
  destroys a dead hero and runs the original lethal recoil, star burst and
  spin-away for 180 frames from the host frame event, with the body anchored
  where Kirby fell. Stepping stops before the source's own life-lost transition;
  an offline trace reached an emulated ARM9 data abort at about 217 frames. A
  host revival reloads the clean seed machine. Run `100337` shows the sequence.
- **Frame pacing.** `FramePacer` runs exactly one original frame per host frame
  when the display is within 3% of the DS refresh, accumulates real time
  otherwise and rounds host hitches. Audio drops or repeats one stereo frame in a
  hundred to hold the queue between 512 and 4,096 frames.
- **Damage.** Incoming host damage is converted to the same fraction of Kirby's
  76 HP as of the Beheaded's current maximum, so host difficulty and Survival
  upgrades still apply. A 20-damage Zombie hit now costs 15 HP (run `101442`).

## Verification

All runs used a separate data root, `repros/bugfix-20261004`, with a fresh
private profile. Each completed without fatal errors and left the real saves
unchanged.

- `100704`, `102734`: arrival with no fallback, slime or flame on Kirby.
- `101027`: shield pickup, original guard, base-form sword behind Kirby, shield
  parry FX behind Kirby, and a normal-outfit round trip at 57/76 and 75/100.
- `101739`: the existing combat probe consumed a real Zombie, randomly copied
  Plasma, and countered with the retained shield. Its launcher report was lost
  to a capture truncated at termination; the converter now skips such a frame.
- `102834`, repeated as `103501` with the staged DLL: arrival, sword swings,
  walking, jumping, a 20-damage hit
  with original hurt flash and recoil, guard, and a lethal hit followed by the
  original death animation over "Body desecrated!" and the next run's arrival.
- Offline: 20 Python tests, 6 parser tests and 39 rule/pacer assertions pass;
  the Release build has no warnings.

The offscreen lab renders at about 67 Hz, so it exercises the accumulating path
of `FramePacer`; the locked path is covered by unit tests rather than a 60 Hz
live capture. Kirby still holds his current original pose while a base-form
host weapon swings, because base Kirby has no weapon animation in the ROM.
Host elevators and floating platforms, which are interactive entities rather
than collision-grid cells, have not been exercised with Kirby.
