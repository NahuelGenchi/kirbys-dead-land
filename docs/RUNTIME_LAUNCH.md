# Isolated Linux runtime verification

The official Linux DCCM 35.13.6 loader runs the owned native Dead Cells v35 bytecode in a separate lab. The `KirbySuperStarUltra` outfit, original-ROM movement and combat, copy abilities, retained-shield parry, saves, room transitions, and death/new-run lifecycle have passed the live checks below. These bounded tests do not establish every encounter or a complete campaign playthrough.

```sh
python3 scripts/setup_runtime.py --build
python3 scripts/stage_runtime.py --original-runtime \
  --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
python3 scripts/launch_runtime.py --seconds 50 --start-game \
  --outfit KirbySuperStarUltra --input-plan movement
```

For normal keyboard/controller play from a desktop terminal, run `python3 scripts/launch_runtime.py --play`. Choose New Game in the game menu to start as Kirby. This mode uses real input and audio, has no time limit, and records no framebuffer or emulator trace. It retains the same private save directory. `--play` cannot be combined with automated input or automatic new-game options. Windowed interactive use is available but has not been exercised by the offscreen integration tests.

The launcher changes only the private lab profile. It never installs the loader into the Steam game directory or sends desktop keyboard or mouse input. A bounded smoke process is terminated by its exact subprocess handle; bubblewrap's PID namespace cleans up that launch's children.

Staging and launching share an exclusive lab lock. A second launch or an attempt to replace the runtime while the game is running exits before changing the lab.

## Save isolation

A separate working directory is insufficient: this build's `tool.File` reads and writes Steam Cloud. The opt-in `LabIsolation` helper is installed before game initialization and redirects all four `tool.File` operations (`exists`, `getBytes`, `saveBytes`, `delete`) to the private `lab/save` directory. It also redirects the path used by local backups, disables the game's Cloud storage operations for this process, and prevents the in-game Cloud toggle from altering Steam settings. Steam authentication and ownership calls are not changed by this helper; Goldberg remains disabled and is omitted from staging.

The launcher additionally starts the game inside bubblewrap with a read-only host filesystem, a writable private lab, and a fresh `/tmp`. It retains Steam IPC needed by the owned installation. The launcher runs universal-modder save backups before every launch and compares SHA-256 inventories of the real local and Steam saves afterward. `save-isolation.ready` is recreated for each run; a stale marker cannot count as successful initialization.

Relevant environment variables are used only by the lab helper:

- `KIRBY_LAB_SAVE_ROOT`: must match the explicit isolated game root's `save` directory.
- `KIRBY_LAB_RUN_ROOT`: output directory for state markers and framebuffer captures.
- `KIRBY_LAB_START_GAME=1`: call the game's normal new-run menu action once.
- `KIRBY_LAB_RESUME_GAME=1`: call the game's normal saved-run launch action once.
- `KIRBY_LAB_SAVE_AFTER`: request a normal host save after this many original engine frames.
- `KIRBY_LAB_OUTFIT`: outfit ID assigned before starting that private run.
- `KIRBY_LAB_INPUT_PLAN`: `movement`, `combat`, `ability`, `visual`, `breakable`, `boss`, `crowd`, `aim`, `repro`, or `hazard`, for a reproducible offscreen test. `repro` tests normal keyboard/controller bindings and keeps the normal tutorials.
- `KIRBY_LAB_TEST_ABILITY`: optional source ability ID for the ability probe, exposed as `--probe-ability 2..25`. The probe prepares it through original inhale, supplied swallowed content, and Down before measuring combat. The separate `combat` probe uses a real enemy's random copy result.
- `KIRBY_LAB_TEST_SCROLLS`: optional `B,T,S` scroll counts for the ability probe, exposed as `--probe-scrolls`. Before measuring, the probe adds that many real `BrutalityUp`, `TacticUp` and `SurvivalUp` items (`InventItem.fromItem`) through the normal inventory and calls the host's `Hero.computeTiers`. `ability-damage.json` then records the tiers, the host and core colorless multipliers, and each copy hit's source HP drop and host damage.
- `KIRBY_LAB_COMBAT_ELITE=1`: with the `combat` plan, makes the swallowed Zombie an Elite (Elite skills disabled) and records in `combat.json` whether the copy was empowered and the damage that the second Zombie takes from each hit. Set it in the environment of `launch_runtime.py`.
- `KIRBY_LAB_PLAY=1`: keep outfit selection but disable all smoke capture and automatic input.

Without these variables the lab helpers do not run. Do not use their successful test as evidence that an ordinary Steam launch uses isolated saves.

For a user-session reproduction, copy the private profile into a separate data root and pass that root explicitly to both staging and launching. The October 4 recording investigation uses `repros/user-recording-20261004`; the user's main private profile is preserved. `repro` injects keyboard events or raw controller-button query results through the existing host bindings. It does not replace the original engine's input mask or force inventory contents. `KIRBY_LAB_REPRO_PLAN` names a private JSON timeline with `at`, `until`, and optional `actions`, `keys`, or `padKeys` arrays.

The optional `KIRBY_LAB_REPRO_HEALTH=1` fixture changes only the copied test character's health, position, and outfit: it sets 57/76 health, repeats real stat updates, places the character near an existing generated item, and checks an outfit round trip. The timeline must use the normal pickup binding to acquire that item. `KIRBY_LAB_REPRO_PICKUP=StartBow` selects the existing bow instead of the shield. `KIRBY_LAB_REPRO_ENCOUNTER=1` later places the test character near an existing natural Zombie without changing its AI. `KIRBY_LAB_REPRO_MOB_COMPARISON=1` briefly uses the normal outfit near that enemy. `KIRBY_LAB_HIDE_BITMAP=1`, with optional `KIRBY_LAB_HIDE_BITMAP_FROM`/`UNTIL` times, skips only Kirby's original bitmap draw for a visual comparison. These are diagnostic fixtures, not normal play options.

The second recording investigation added further opt-in diagnostics. `KIRBY_LAB_CAPTURE_ALL=1` keeps `--capture-fps` capturing during host cinematics and death, and adds the hero animation, cinematic and visibility state to `captures.jsonl`. `KIRBY_LAB_REPRO_HIT_AT` and `KIRBY_LAB_REPRO_LETHAL_AT` deliver a real host Zombie attack (20 damage, or 10 damage at 3 HP) through the normal attack path at that repro time. `KIRBY_LAB_DUMP_PHASES`, for example `rebirth:20,headPopLoop:5`, writes the scroller render tree near the hero when its animation reaches that frame. In `repro` runs, `repro-events.jsonl` also records host attacks entering the source and source health changes, and `repro-trace.jsonl` records the source palette flash entry and Kirby's palette bank.

The third recording investigation added more opt-in diagnostics. `repro` runs also record `host-motion.jsonl` (host position, animation and viewport zoom on every host frame), and drive the normal outfit too, without a source machine, so movement can be compared. `KIRBY_LAB_VISIBILITY=1` writes `visibility.jsonl` with the hero's visibility, sprite alpha, hero light and the body bitmap's alpha on every host frame. `KIRBY_LAB_MOB_SCALING=1` writes `mob-scaling.json` with every mob's life, elite flag, `trueLifeTier` and the host tier scale. `KIRBY_LAB_ZOOM_SCHEDULE`, for example `1:1.5u,10:2u`, sets the host viewport zoom once at each repro time (`u` also updates its size). `KIRBY_LAB_TOGGLES`, for example `30:nolight,34:none,38:nohead,46:blendnone`, switches the hero light, host head parts or the body bitmap's blend for A/B captures. `KIRBY_LAB_DUMP_PHASES` also accepts `@<lab ms>`. The render tree dump includes the scene lighting parameters and layer membership. The ability probe is run with `--probe-ability` source IDs: 18 Sword, 19 Hammer, 20 Parasol.

## Linux dependency repair

The released `fmt.hdll` references `tjInitDecompress` without declaring a TurboJPEG dependency. On this machine this crashed when the menu's JPEG news image loaded. The launcher downloads a pinned Ubuntu `libturbojpeg0` package, extracts it beneath the private data root, and preloads its library. No system package is installed.

- Package: `libturbojpeg0` 1:2.1.5-4ubuntu4, amd64.
- SHA-256: `ea835d7bb9800202058b79118ff27528b48a27a528c46f3a9bff435bbb5f0ed8`.
- Source: Ubuntu's official archive, URL recorded in `scripts/launch_runtime.py`.

DCCM's startup executable and HashLink executable also need executable permissions restored after Python ZIP extraction. `stage_runtime.py` does this for those two files in the private lab.

## Captures and verified result

SDL3's offscreen driver renders without opening a desktop window. OpenAL uses a null audio device. `LabSmokeCapture` reads the game's actual OpenGL framebuffer at two-second intervals; the launcher converts those private PPM captures to PNG after the process exits. Each run also records `frames.json`, `stdout.log`, and `report.json` under `lab/smoke-runs/<timestamp>`.

The 2026-10-04 `000140` baseline test reached `dc.pr.Game` with a live hero, loading complete, and the normal `PrisonerDefault` outfit after 11,093 main updates. Its final framebuffer visibly shows the hero in the starting cell with the jump tutorial. Both source-save inventories were unchanged. The earlier `235216` test visibly reached the main menu before revealing the TurboJPEG linkage error; it is not a passing run.

The `001810` test used the actual new `KirbySuperStarUltra` outfit and source engine. Kirby walked from host x=1356 to x=1533, jumped to y=3690.80, and landed on the upper platform at y=3744. The source advanced at approximately 59.8 frames per second. Its 1,201 trace entries show stable host/source movement, and the framebuffer shows Kirby with no default Beheaded head. The process completed its time limit without fatal errors; real saves were unchanged. This run verifies walking, jumping, rendering, and basic terrain transfer, not every ability or environment.

`--input-plan combat` equips an actual host shield, spawns a real Zombie, drives the original inhale and swallow inputs, and then attempts an evolved attack and shield parry. `combat.json` records each observed event. A completed game launch alone is not a passing combat result: check that the enemy was consumed, a copy ability was selected and activated, the retained shield identity matches, and a successful counter event was observed.

The `003840` combat test consumed a real Zombie and randomly selected Sleep (original ability 25). The same host shield, permanent ID 254, remained allowed. The original Sleep later expired to base form. Shield use was accepted, but this run did not observe a successful parry and is not evidence of a completed parry check.

The subsequent `004556` test passed the complete absorption/parry path: Zombie UID 596717 was consumed, random Sword was selected, the source engine activated ability 18, and shield ID 264 remained equipped. A normal shield key press while Sword was active produced `onShieldStartParry`; an actual host enemy attack then produced `onShieldCounterSuccessful`. The final `combat.json` records all these events as true. The test also drives the original Sword attack input, but its parry result should not be mistaken for a measurement of all Sword damage or combo behavior. The framebuffer visibly shows Sword Kirby. This 60-second run completed without fatal errors and real saves were unchanged.

The `20261005-204132` Elite Sword test ran `KIRBY_LAB_COMBAT_ELITE=1 python3 scripts/launch_runtime.py --data-root <copy> --seconds 60 --start-game --outfit KirbySuperStarUltra --input-plan combat` in a separate data root (`repros/elite-sword-20261005`). That root had its own lab copy and a native bridge built from this branch's `src/nds/lib.rs`, so the main private lab and the owner's play build stayed untouched. Elite Zombie UID 602774 was consumed, the source engine activated Sword (ability 18), and `empoweredAfterSwallow` and `empowered` were both true with multiplier 2. The second Zombie took original Sword hits of 36, 36 and 32, which are the original 18/18/16 doubled. Shield ID 910 remained allowed and the parry/counter path also passed. The 60-second run completed without fatal errors and real saves were unchanged; an earlier run of the same scenario before the final refactor gave identical results. It did not exercise losing the empowerment, save/resume or a biome transition.

The earlier `003840` run saved through `Main.writeSave` at original frame 5666. A separate `004025` process launched with `--resume-game` and its first source trace resumed at frame 5666, with exact source coordinates `(404.2734375, 105.5)`. The next frame was 5667. Both runs completed without fatal errors and left the real save inventories unchanged. Use `--save-after 1500` and then `--resume-game` to repeat this host-plus-source persistence check.

The evolved Sword state was also saved at frame 4816 during `004556`. The fresh `004720` process resumed at that exact frame, ability 18, and source coordinates `(393.515625, 105.5)`, then advanced to 4817. Its 40-second run passed with unchanged real saves.

`--resume-game --input-plan ability` uses an existing evolved save to test source attacks against a factory-created host Zombie. `ability-damage.json` records the actual host damage callback, target life, and the host/source health response to a later incoming attack. This is separate from the absorption/parry test so that ephemeral original abilities do not obscure the damage check.

The `005258` ability/damage run passed after health synchronization was integrated. Original Sword attacks caused real host damage (the first measurement was Zombie life 122 → 83, with 39 damage reported by the host callback; the target later died). A deliberate incoming 10-damage host attack at source frame 5166 changed both source and host health from 62 to 52 at frame 5167. Subsequent natural enemy hits kept both health values identical. The bounded 45-second run completed without fatal errors and real saves were unchanged.

The subsequent `005431` fresh-run regression passed absorption, random Wheel evolution, and a successful retained-shield counter with the health bridge enabled. The `005646` damage test then verified the correction for host damage scaling: the Wheel hit log reports matching `baseDmg`, `rawFinalDmg`, `finalDmg`, and `inflictedDmg` values of 23, 14, and 23, reducing target life 127 → 104 → 90 → 67. The deliberate incoming 10-damage hit changed both Kirby health values 69 → 59 on the next original tick. `ability-damage-events.jsonl` retains the per-hit evidence.

The `011928` movement/crouch test verified the scoped host hurtbox projection. Its 1,114 source trace entries include original 12×12 standing and 12×6 crouching hurtboxes. At every observation outside the host query phase, projection and restoration counts matched, no projection remained active, and the physical host height was restored to 29. The actual framebuffer shows Kirby standing correctly on the host floor. The projection covers `Level.update` (including enemy and hazard updates) and the common skill-area queries outside that update, restoring physical coordinates around Kirby's own movement, sprite, and serialization callbacks. This does not establish exhaustive coverage of every bespoke asynchronous attack or every boss.

The `012042` fresh-run regression passed with the scoped hurtbox enabled: a real Zombie supplied random Crash (source ability 23), and shield ID 257 successfully parried while Crash was active. All 5,259 projected scopes were restored. The source later returned naturally to base form; the normal checkpoint at source frame 4816 saved that base form and retained shield. Both runs completed without fatal errors and real saves were unchanged.

After correcting the host's tile-based `bodyHei` units and protecting the original position from Beheaded physics, `014116` measured exact Sword damage of 18 and 16: the Zombie's life changed 111 → 93 → 77 and it remained alive. `014250` measured Wheel hits of 23, 14, 23, 23, and 2, leaving its target alive at 32/117. Both runs observed a deliberate incoming 10-damage hit changing source and host health 76 → 66 on the next original tick. The earlier unexplained extra 250 target damage did not recur in either run.

`014250` also saved Wheel before combat at original frame 4366 and coordinates `(396, 105.5)`. A new `014414` process resumed at exactly that frame, ability, position, and 76 health, then advanced to frame 4367. The corrected physical body restored as height 29 and radius 9. The private checkpoint is preserved under `lab/checkpoints/wheel-hostbody-20261004-014250`.

Three development launches exited with native signal 139 before the first original-engine trace (`003305`, `013743`, and `014523`). A recovered native backtrace identified an uninitialized VRAM capture flag in the emulator constructor; the bridge now applies a checked initialization patch. The fix passed 64 constructor runs with poisoned allocator memory. The failed launches do not count toward verified results. The offscreen launcher reports nonzero exits instead of treating menu or partial framebuffer output as success.

DCCM emits Steam callback/manual-dispatch warnings on this configuration. The tested run nevertheless completed initialization, generated the level, rendered the hero, and saved to the private directory. No achievement or ranking functionality is claimed; DCCM itself disables those features.

The `015411` movement test additionally exercised actual host `TargetHelper.filterByArea` queries. Both original 12×12 standing and 12×6 crouching shapes accepted a point inside the hurtbox and rejected points two pixels above or beside it. Every query restored the physical host geometry. This run completed with source and host health both 76 and unchanged real save inventories.

The constructor-fixed `020312` regression consumed a real Zombie and randomly selected Jet (source ability 16). The same physical shield, ID 905, remained allowed and successfully countered an actual host attack while Jet was active. It saved the evolved state at original frame 4817 and coordinates `(393.515625, 105.5)`; a private copy is preserved in `lab/checkpoints/jet-hostbody-20261004-020312`. The run completed without fatal errors and left the real saves unchanged.

The `020615` breakable probe attacked an actual generated `BasicDoor` with original Sword. The host damage callback reported one damage, reducing its one remaining HP to zero; the game marked it broken. This verifies source attacks reaching a real non-enemy host obstacle. The fixture waits for the original copy animation before pressing attack; the earlier `020040` fixture pressed during that animation and did not hit.

The corrected `020932` Ice test reported exact host damage of 11 then 5, killing its 16-HP Zombie. The original frozen cube remained for 197 source frames after the host enemy died. Frame 010858 visibly shows the cube beside Ice Kirby. A subsequent incoming 10-damage attack kept host/source health at 76 → 66.

The final `021224` lifecycle test passed outfit changes, health preservation, a real transition to T_Courtyard, original fatal damage, and a normal new run afterward. Changing to the normal outfit froze source simulation and restored 54 px height and 100 max HP. After damage in that outfit, reactivating Kirby imported the correct 38/76 health and retained Jet/shield 905. A lethal original hit reached 0 HP at frame 5395 and the host recorded a death. The following new run reset to source frame 4166, base form, full 76 health, and no retained shield; subsequent gameplay advanced normally. No fatal errors or changes to real saves occurred.

The `022453` boss fixture used the actual Bridge arena and naturally created Behemoth. Its normal AI continued, inhale did not capture it, and original Sword dealt 32 host damage. The `022833` crowd fixture simultaneously maintained 31 live original proxy objects bound to 31 distinct living host Zombie UIDs. Original Sword generated 21 host callbacks of 18 damage each, totaling 378. Both completed without fatal errors or changes to real saves.

The final physical-input test `025047` passed using native bridge `f94997d82877bcdce29ed8d8cb3741545f08044ad813fa61f59f9406c6314b2c`. It verified all nine physical action mappings, both-axis inversion, and a real Pure Nail upward strike (`PureNailUp`, cycle 2). Jump and skill bindings stayed masked during host weapon aiming, and all five observed outer movement scopes restored their bindings. The production input path then moved Kirby 94.6875 px and produced a 49.890625 px jump. This profile binds W to both Up and Jump; the test recorded the exact combined original mask 65, or 129 with inversion enabled. The timestamp check now matches the host's absolute `sys_time` suspension deadline. The run finished without fatal errors or changes to real saves.

The final `025627` test started from an empty private save directory with no automated input plan. The normal new-game path activated Kirby, kept the hero awake at full 76 health, and advanced the original engine normally. The incompatible host jump/dodge/helper tutorials did not appear; the ordinary `attackM` prompt remained available. The 45-second launch completed without fatal errors or changes to real saves. Both the preceding fixture profile and this fresh-profile smoke save were preserved by directory rename under `lab/checkpoints`; the final `lab/save` directory is empty for the user's New Game. `lab/final-stage.json` records the exact runtime/native/outfit hashes and preserved checkpoint path.

`--start-game --input-plan cook` (`LabCookProbe`) checks Cook food drops. It
hurts Kirby in base form with a 40-damage host hit, prepares original Cook through
inhale, supplied content and Down, spawns one factory Zombie, and holds original
B. It records new healing `ItemDrop`s, walks Kirby onto the food with original
directions, and presses the real interact binding (`isDown` plus a `isPressed`
edge). `cook-food.json` and `cook-events.txt` record the kill, the cooked mob's
food loot, each new food item, the host `pickItem` call and every health change.
A passing run has `victimFoodLoot` and `newFoodCount` equal to 1, a `pickedItem`,
and matching host/source health rising after the pickup.

Run `20261005-205320` passed: one `SmallMeat` dropped from the cooked Zombie and
host pickup healed host and source 48 → 59. The original Cook food separately
healed 36 → 48 before the pickup. The 75-second run finished without fatal errors
and real saves were unchanged.
### Colorless copy damage (2026-10-05)

Issue #10. These runs used a separate data root, `repros/colorless-20261005`, so
the owner's play lab and save were untouched. Its native bridge was built from this
branch's `src/nds/lib.rs` (`main`'s source) with a private Cargo target:
`968d11cb285115707b5250ecb3c42bd1810fa19610c3d041eef59c6bfe3e3948`. Runtime DLL:
`7f84ee6d6d7e601ece731c2c3e456caba27a99ffcf4e3961377455cdf8bf7fa2`. Each run was a
fresh `--start-game --input-plan ability` for 80 seconds, ended by the expected
timeout without fatal log lines, and left the real saves unchanged.

- `204919`: `--probe-ability 8 --probe-scrolls 4,0,2`. Host tiers became 5/1/3,
  so the colorless tier was 5. The host's `Const.scaleHeroValueToTier(1, 5)`
  returned 1.7490062499999994, identical to the core copy. Original Wheel drops
  23, 14, 23 and 14 became host `finalDmg` 40, 24, 40 and 24, which matches the
  host's own colorless result for each base. The 127-life Zombie went
  127 → 87 → 63 → 23 → −1; the last hit was lethal on both sides. The incoming
  10-damage check kept host/source health at 76 → 66.
- `205107`: `--probe-ability 10 --probe-scrolls 4,0,2`. The 16-life Ice fixture
  received source HP 10 (the smallest that is lethal after scaling). One original
  breath dropped it to 0 and dealt host damage 17 in the same tick, killing the
  Zombie. The original frozen object stayed alive for 195 source frames after the
  host death (the pre-change `020932` run recorded 197).
- `205246`: `--probe-ability 8` with no scrolls. Tiers 1/1/1, multiplier 1. Kirby's
  hits of 23, 14, 23, 23 and 2 were applied unchanged. The Wheel hit spacing (8, 6
  and 8 source frames) matches `204919`. The incoming hit knocked Kirby back to
  base form, the surviving Zombie's AI lock expired and Kirby later died
  (`endKind: Death`). That fixture outcome does not involve the damage change.

Not covered live: copy forms other than Wheel and Ice, bosses, breakables and
targets above the native HP cap at nonzero scroll counts.

## Private showcase capture

`--capture-fps 15` records actual gameplay framebuffer frames and the original PCM submitted by the mod. It leaves desktop audio untouched. The capture manifest records wall-clock timestamps; machine/RAM diagnostic snapshots remain limited to every two seconds. The optional combat take adds walking, jumping, floating, and attacks after absorption/parry. Normal play never enables these helpers.

Use `scripts/build_showcase.py RUN OUTPUT.mp4 --ffmpeg /path/to/ffmpeg --duration 30` to encode the timestamped frames and original effects. The recording contains the mod's original effects only; it does not capture host music or other applications. Inspect a contact sheet with universal-modder's `um video contact` before sharing the clip.

The earlier `022250` take used native bridge SHA-256 `b799d1482911f62f975a1484bfc6e324d2baa246ff1cac05377bb672bde5e690`. Its real Zombie supplied random Wing (ability 15); shield ID 910 countered successfully while evolved. The 65-second process completed without fatal errors and preserved real saves. Its 20- and 30-second cuts remain private in the showcase directory.

The final `025145` take uses native bridge `f94997d82877bcdce29ed8d8cb3741545f08044ad813fa61f59f9406c6314b2c`, with source-stage door decorations removed. A real Zombie supplied random Fire (ability 11), and retained shield 919 successfully countered while evolved. The 65-second process completed without fatal errors and preserved real saves. The selected video is `~/.local/share/kirby-dead-cells/showcase/kirby-dead-cells-clean-20s.mp4`: 20 seconds, 1024×768, 264 actual captured frames encoded at 15 fps, with original 48 kHz stereo effects. The universal-modder probe passed and `contact-clean-20s.png` was inspected. Absorption, evolution, parry, walking, jumping, flight, and original Fire attacks are visible.

## Recording-driven regressions

The user's later recording exposed equipment-rendering and health-bar problems that the earlier bounded tests missed. The separate copied-profile run `033400` verifies the repaired health update path: after a real R-key pickup of the generated Old Wooden Shield, host health, source health, and both HUD states remain 57/76, the healing trail is 57, and recoverable health is zero. Twenty-four stat recalculations preserve the wound. A normal-outfit round trip produces 75/100 and restores exactly 57/76 with the source machine paused while inactive.

In that same run, the secondary-weapon tutorial (`attackS`) appears after pickup and completes on the first actual right-mouse shield use. Seven normal shield-start callbacks occur; a held right mouse produces both the visible wooden shield and original guard input. Keyboard ArrowDown+X produces original Down only; raw controller Down+X also activates the base-form host sword, matching the existing bindings. This test observes shield use, not a successful enemy counter.

Run `033651` adds the corrected sword grip metadata and records at 15 fps. The blade is attached to Kirby, the wooden shield is visible, and physical movement/jump inputs work near an existing generated Zombie. That enemy moves away to a ledge, so this take supplies visual evidence rather than a parry result. The reviewed 20-second cut is `showcase/recording-fixes/kirby-fixes-20s.mp4`; its manifest records three cuts from actual framebuffer captures, with original effects audio only. Fixture positioning and outfit changes are omitted from the cut. These runs complete without fatal errors or changes to real Steam saves.

The final-pack run `034038` uses outfit SHA-256 `6c141ee357dcdb5087acd55e12f79211338e6690de1e235238282b75bc5b8663`. It acquires the existing Beginner's Bow through the normal R-key pickup and fires it through the secondary weapon binding. The bow is visibly attached, and a projectile kills the generated Zombie for 109 damage. Frames `004800`, `004859`, and `005051` compare the same nearby enemy with Kirby, with only Kirby's bitmap hidden, and with the normal Beheaded outfit: the thin brown body and green head effects remain visible in all three. The comparison does not reproduce enemy occlusion by Kirby's transparent bitmap. This 65-second run finishes without fatal errors or real-save changes.

## Enemy projectiles

`--input-plan hazard` (`LabHazardProbe`) spawns a real Grenader, then a real Archer, 120px ahead of Kirby with their natural AI. It holds the original inhale until a hostile host projectile that the source had controlled is destroyed, then spits it (B) or swallows it (DOWN). The arrow phase jumps before inhaling (see below). `hazard.json` records each phase: the captured host type, mouthful state, Kirby/shooter health, outgoing hits after the action, and every hostile projectile's pairing and closest approach. Both runs used a separate data root, `repros/hazard-20261005`, so the owner's main lab was not restaged.

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/hazard-20261005 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/hazard-20261005 \
  --seconds 170 --start-game --outfit KirbySuperStarUltra --input-plan hazard
```

The first probe version (`20261005-205347`) held the inhale on flat ground. One `EnemyGrenade` was captured, held by the source and consumed into the mouthful at full health, but its one-frame spit tap produced no host hit. A host frame can advance zero source frames, so later taps last four frames. Archer arrows were never captured: frames show them passing above Kirby at the Beheaded's chest height, outside the original inhale box (0–34px above the foot). That version left Kirby idle beside a live Grenader after its phases. Later frames show grenade explosions hitting the non-inhaling Kirby (76 → 62), and the run ended in a host death. The probe now removes its shooter when it finishes.

Run `20261005-205827` (runtime SHA-256 `3b2d1291428646fc59a38301f0e9e94204193f6b54cde483796e8dcc1e41e7c6`, bridge `18ab44e430047464eb017009d617d107defef6c9cdfe6c4fb53ed39d2b89df65`) passed all three phases with Kirby at 76/76 throughout:

| Phase | Captured host object | Result |
| :--- | :--- | :--- |
| Grenader, spit | `dc.en.gr.EnemyGrenade` | Original mouthful; one star hit on the Grenader for 52 (95 → 43); mouthful cleared |
| Grenader, swallow | `dc.en.gr.EnemyGrenade` | DOWN: source ability 1, no copy, no outgoing hit |
| Archer, jump-inhale, spit | `dc.en.Bullet` | Caught in the air; one star hit on the Archer for 52 (56 → 4) |

All four hostile projectiles seen were paired; no hero-owned projectile was paired, but Kirby fired no bow in this run, so that exclusion was not exercised live. The process reached its time limit without fatal errors, and the real save inventories were unchanged.

## Exit-door walk (2026-10-05)

Issue #17. The lifecycle probe (`KIRBY_LAB_LIFECYCLE=1`) now places Kirby 1.5
cells left of the chosen exit before it activates it, so the host walks him in.
It records `door-cinematic`: the cinematic polls, the original actions and
facing seen, and the host X range. `KIRBY_LAB_EXIT=<destination>` picks that exit
when the level has it. These runs used a separate data root,
`repros/door-walk-20261005`, so the owner's play lab and saves were untouched.

```bash
KIRBY_LAB_LIFECYCLE=1 KIRBY_LAB_EXIT=T_Courtyard KIRBY_LAB_CAPTURE_ALL=1 \
  python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/door-walk-20261005 \
  --start-game --outfit KirbySuperStarUltra --seconds 120 --capture-fps 15
```

| Run | Version | Door cinematic |
| :--- | :--- | :--- |
| `213828` | Host terrain | T_SewerShort exit: Standing, Walk 28 of 193 polls, then Fall (`0x070E`) |
| `214244` | Host terrain | T_Courtyard door: Walk 21 of 154 polls, then Fall. Frames show Kirby falling up the doorway steps |
| `214713` | Flat floor | T_Courtyard door: Walk 148 of 149 polls, facing right, no Fall. Frames show walk poses up the steps until the fade |

Run `214713` used runtime SHA-256
`09e64805a55a23c25328bf89ce3159fe793a3f1d821a1b62b0035ad28077c972` and bridge
`278518576c2174c65c9041a3577bdb7fbe1361aef45ed13416618ff7a80652a0` (unchanged
native source). In all three runs the transition then completed with the same
ability, shield and 57/76 health. The later lethal phase recorded a death, and
each run ended at its time limit without fatal log lines and with real saves
unchanged. Run `213535` is not evidence: it lacked `--outfit`, so Kirby was not
active.
