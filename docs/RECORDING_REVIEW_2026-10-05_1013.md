# Seventh actual-play recording review: Jet

User recording: `Screencast From 2026-10-05 10-13-43.mp4`, 17 seconds,
1920×1080, about 23 frames per second, no audio. It used the build staged with
the Bomb drag fix. The user reported that this copy ability no longer works,
although it did before.

## Jet's charged dash stays in place

Kirby has Jet. He charges (blue rings) and releases: the jet fires, its flame
and smoke appear and he takes the dash pose, but he does not move (0.4–2.0 s,
2.8–4.4 s, 5.2–6.0 s and later). In the original the charged jet carries him
forward.

**Cause.** Every copy-ability attack runs under the original action 142C, which
the speed match had taken for the exhale that ends a float. It classified it as
floating, so when no direction was held its braking cleared Kirby's horizontal
velocity every frame. Jet's dash, Wheel's roll and Yo-Yo's and Suplex's dashes
carry Kirby with their own velocity under that action.

A headless survey of all 24 abilities (standing, charged, dash and down attacks,
speed match off and on) found five broken: Wheel rolled 8 px instead of
321–559, Jet dashed 14 px instead of 216, Yo-Yo 4 instead of 124, Suplex 0.8
instead of 5.5 and Fighter 3 instead of 6. Everything else matched.

**Fix.** Ability use keeps its original motion: no braking, no added speed.
Only the exhale that directly follows a float still moves like floating, so a
floating Kirby keeps his speed when he lands and exhales.

The survey now finds every standing, charged, dash and down attack identical to
the original (125 cases); mid-air attacks differ only vertically because the
jump they start from is quicker. `tests/speed` checks Wheel (321 px), Jet
(216 px) and Yo-Yo (124 px) against the original.
