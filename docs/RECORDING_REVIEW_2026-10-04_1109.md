# Third actual-play recording review

User recording: `Screencast From 2026-10-04 11-09-21.mp4`, 99.1 seconds,
1920×1080, about 23 frames per second (variable), no audio, with play log
`lab/play-runs/20261004-110858`. It used the build staged after the second
review. Every frame was extracted with its presentation time; suspicious
stretches were cropped frame by frame around Kirby. The user reported that
Kirby appears and disappears constantly, feels slow to move along the map, and
deals almost no damage with copy abilities.

## Kirby appearing and disappearing

Kirby vanished on alternate frames at 65.97, 66.05, 66.9–67.1, 67.5–67.6 and
68.3–68.5 s, each time an enemy hit him. HP fell from 76 to 12 over the clip.

- Original damage reaches the host through `Hero.applyAttackResult`, which calls
  the host's `Hero.blink(15)`. That pulses the hero sprite's alpha between 0.3
  and 1 every other frame, and Kirby's body copies the host alpha. Lab run
  `112754` traced the call and the alpha values on a real Zombie hit.
- The ROM never hides Kirby: OAM traces after hits in base form and with a copy
  ability keep all of Kirby's objects. Its own feedback is a palette flash: warm
  and gray for 3 of every 6 frames after a hit, 2 of every 4 while guarding.
- Fix: `KirbyHostVisuals` skips `Hero.blink` while original damage is applied.
  Other host blinks, such as immunity after a pit respawn, are unchanged. Lab
  runs `120448` and `124528` keep alpha at 1 through hits.

Kirby also turned pale cyan in some darker spots. A controlled lab A/B
(`123840`, 44 samples per mode) showed no difference between the alpha and the
opaque actor pass. The paleness follows the level's drifting foreground fog,
which the host draws over every actor, the Beheaded included.

## Slow movement

| Measurement (lab) | Host px/s | Cells/s |
|---|---|---|
| Beheaded run (`112952`) | 250.8 | 10.4 |
| Kirby walk, original 1.375 px/frame | 82.3 | 3.4 |
| Kirby dash, original 2.0625 px/frame | 123.4 | 5.1 |

The host viewport is about 400 host pixels tall (683×400 here) against the DS
screen's 192, so at the host zoom Kirby looked half his original size and moved
across the screen at half his original apparent speed. Holding a direction
walked, because the original dashes only on a double tap.

- `KirbyCamera` applies the host viewport's own zoom (2 by default) and
  recomputes its size with `updateSizes`, without which tracking drifts towards
  a corner (`113431`, `113602`). The view becomes about 342×200 host pixels.
  The camera eases in after the arrival, leaves host cinematics alone, keeps a
  host zoom change and applies the factor on top of it (0.7 became 1.4 in
  `124247`), and restores the host framing for the normal outfit (`124345`).
- `DashAssist` (Kirby.Core) turns a held direction into the original
  press-release-press double tap, and re-taps once after landing if Kirby is
  still walking. A light analog tilt below 0.75 still walks. ROM tests showed a
  re-tap on landing reaches dash speed and a mid-air double tap is ignored.
- At zoom 2 the dash covers 0.35 screen widths per second, the same as the
  Beheaded's run at the host zoom. A level still takes about twice the
  Beheaded's time in absolute terms, because the original Kirby is about one
  cell tall and moves at his original speed through rooms built for a hero
  more than twice his height.

## Copy-ability damage

Recorded Parasol hits showed 3, 6, 10 and 12 damage against enemies with
110–180 life.

- Green Greens' natural enemies have 16 and 18 HP. A typical ability deals
  6–18 per hit (Sword 18 and 16), so the original kills a basic enemy in one
  or two hits. The host's basic enemies have 60–160 base life (Zombie 120),
  scaled per mob tier by `Const.scaleMobLifeToTier`: tier 1 ×1.0, 2 ×1.21,
  3 ×1.45, 10 ×4.16, 20 ×14.2. Prisoners' Quarters already has tiers 1–3.
- A 10-second ROM benchmark (`repros/bugfix-20261004/kssu_dps.json`) gives typical abilities 60–115
  original damage per second against a stationary target.
- Each paired original enemy now has HP in original units, host life divided
  by `CopyDamageScale` (3.5) × the target's tier scale, and damage is
  multiplied back. Both games therefore agree on the lethal hit: Ice still
  freezes its target into the original block (`121012`). Elite and boss extra
  life and each type's base life still apply.
- Lab results against real Zombies: Hammer 119 + 8 (127 life), Parasol 56 + 62
  (118) and 56 + 63 + 3 (122), Sword 63 + 56 (120 life, one HP left, matching
  the original 35-HP equivalent).

## Other changes

- The body bitmap is anchored to the original whole-pixel position instead of
  the fractional physics position (a fraction is several screen pixels at
  zoom 2).
- Normal play logs one frame-pacing summary a minute: host and original frame
  rates, lock share, frames with no or several original steps, and step and
  render times.
- The launcher skips a capture truncated when a bounded lab run ends.

## Verification

All runs used the separate data root `repros/bugfix-20261004`. Each finished
without a fatal error and left the real saves unchanged. The combat probe
(`121150`) still consumed a real Zombie, copied a random ability (Mirror) and
countered with the retained shield. Death plays the original animation and a
new run resets the zoom and source machine (`121327`). Offline: 20 Python tests,
6 parser tests and 46 rule, pacer and dash assertions pass; the Release build
has no warnings.

Not measured live: the vsync lock at 60 Hz (the offscreen lab renders at about
67 Hz), host boss arenas that zoom by themselves, and late-biome enemies. Host
elevators and floating platforms have still not been exercised with Kirby.
