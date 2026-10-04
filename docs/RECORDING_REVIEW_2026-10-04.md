# Actual-play recording review

User recording: `Screencast From 2026-10-04 03-01-37.mp4`, 96.82 seconds,
1920×1080, variable frame rate, no audio stream. The whole timeline was inspected
at half-second intervals, with denser tenth-second inspection of the final
corridor and enlarged equipment/health frames. Original recording remains intact;
review images are private under `research/user-recording-030137`.

## Observations

| Time | Observed behavior and interpretation |
|---|---|
| 0–3s | The host arrival cinematic runs. A separate black tuft/blob briefly appears above Kirby, then disappears. |
| 3–17s | Ground movement, puffed flight, landing, door breaking and room traversal occur. |
| 17–20s | The normal NPC conversation runs and returns to movement. |
| 20–31s | The player examines the starting bow/shield and equips the wooden shield. |
| 31–35s | Health text remains 76/76 while the green/orange bar shrinks and refills after shield pickup. This is a visible UI inconsistency. |
| 31–70s | The Secondary weapon tutorial remains visible. The recording alone does not reveal whether its required physical button was pressed. |
| 44–69s | Real attacks hit enemies, with host slash effects and damage numbers. The sword itself is not visibly held, including the 45.0s attack. The shield is in inventory but not visibly held. Atlas inspection confirms both have nonempty equipment frames, so runtime drawing needs investigation. |
| 72–94s | Repeated flattened/sliding poses and occasional low-ceiling bump stars; HP stays 76. The user recalls Down+X, but is unsure. A 600-frame original-engine tunnel comparison reproduces crouch/slide and preserves the same traces during world recentering. These poses are not sufficient evidence of damage recoil or broken terrain. |
| 94–96.82s | The inventory confirms Rusty Sword and Old Wooden Shield; the player switches to the desktop. |

The actual play log has no managed/native fatal error. The staged runtime matches
the prior tested build. Normal play intentionally records no detailed input or
source-state trace, and this session has no final launcher report. The initial
save predates the recording, so it cannot reconstruct the final corridor state.

## Follow-up verification

Reproductions used a separate data root copied from the user's private profile.
The original profile and game/saves were preserved. Tests exercised real binding
reads, normal weapon preparation, equipment drawing, shield tutorial completion
and pickup health UI with frame-by-frame traces. Earlier source and collision
tests did not establish these actual-play visuals.

## Health display correction

`Hero.updateMaxLife` temporarily converted Kirby's health into Dead Cells units.
Equipment pickup then copied that temporary value into the HUD's previous-state
cache before the next source update restored 76 HP. Rally and bonus health also
retained their previous units. This accounts for a bar animation without actual
health loss in the recording.

An active-session hook now lets the host calculate its maximum, records that
maximum for changing outfits, then restores exact source life/rally/bonus values
before equipment or HUD callers can observe the result. It avoids repeatedly
rounding wounded health through another maximum. Activation and deactivation
convert auxiliary values once and initialize the bar in the correct units.

Live run `20261004-033042` preserved 57/76 HP and absolute rally HP 57 through
24 real maximum-health recalculations. Changing to the normal outfit produced
75/100 HP, and returning to Kirby restored exactly 57/76. Run `033239` then
picked up the level's actual generated wooden shield using the real Interact
binding: host/source HP stayed 57, both HUD state caches stayed 57/76, the heal
trail was 57 and recoverable HP was zero. A second redundant Interact in that
test opened a nearby weapon dialog, so its later shield inputs are not counted
as block/parry evidence.

The corrected single-pickup run `033400` equipped the real `StartShield`, then
cleared the `attackS` tutorial on the first right-click at 8 seconds. Seven
actual shield-start callbacks ran. Holding the button produced both the original
Kirby guard input (512) and `blockWoodenShield`; frame `003363` visibly contains
the brown wooden shield, and its HP bar correctly shows 57/76. Thus the persistent
prompt in the user's recording is not a broken completion handler. The test uses
normal tutorials and weapon preparation, without forced inventory or weapon
manager results.

## Controls in the copied profile

The actual default binding arrays assign Space to Jump, E to Skill 1 (original
inhale/spit/copy attack), Q to Skill 2 (discard), and the right mouse button to
the secondary weapon. Arrow Down is direction action 12. Keyboard X is action
20 (a contextual UI action), whereas controller X is the primary weapon; these
must not be conflated when interpreting the user's uncertain recollection of
Down+X. Live physical-key injection of keyboard Down+X produced source mask 128,
exactly Down/crouch, and no jump or native slide command. The user's recollection
therefore cannot identify the inputs that caused every late pose in the video.

## Equipment and enemy rendering

The separate equipment sprite used an Alpha lighting pass rather than the host
actor's None pass and shared a NormalMap pointing at the body's unrelated atlas.
It now copies the host pass and owns a normal shader for its current equipment
page. Live palette/status shaders remain synchronized, including changes below
the shader-list head.

The starting sword also used the off-hand Prop2 track in two of its three combo
animations. Retargeting those sprites to the original body/weapon contact fixes
the detached blade. Bows and shields retain their verified Prop2 tracks; all
860 bow-named and 204 shield atlas rows preserve their previous positions. The
asset correction changes only equipment atlas origins, not original image pixels.

Live run `033651` visibly verified the attached sword combo, wooden shield,
jump and movement using normal bindings. Final pack run `034038` picked up the
actual generated Beginner's Bow, rendered its held bow and projectile, and killed
a natural Zombie with a 109-damage host arrow (`005160`). Neither run replaced
enemy AI. The sword/shield encounter did not produce a successful counter, so it
does not add to earlier retained-shield parry evidence.

The near Zombie's thin brown body and green glow remain visible with the NDS
bitmap shown (`004800`), hidden (`004859`), and the normal Beheaded selected
(`005051`). The suspected transparent-canvas occlusion was not reproduced; no
enemy shader, lighting or bitmap-depth workaround was added.

All bounded acceptance runs completed without fatal errors and preserved real
Steam saves. The main private profile's four files also remain byte-for-byte
unchanged. A reviewed 20-second clip under
`showcase/recording-fixes/kirby-fixes-20s.mp4` contains actual pickup, block, sword
and movement captures with original source effects audio; fixture setup cuts
are documented in its adjacent JSON manifest. The brief opening host cinematic
figure was investigated but not altered without confirmed sprite ownership.
