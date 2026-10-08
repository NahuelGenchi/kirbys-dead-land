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
- `KIRBY_LAB_INPUT_PLAN`: `movement`, `combat`, `ability`, `visual`, `breakable`, `boss`, `crowd`, `aim`, `repro`, `hazard`, `stars`, `water`, or `items`, for a reproducible offscreen test. `repro` tests normal keyboard/controller bindings and keeps the normal tutorials. `items` is the ability item check (see [ability items](#ability-items-55)).
- `KIRBY_LAB_TEST_ABILITY`: optional source ability ID for the ability probe, exposed as `--probe-ability 2..25`. The probe prepares it through original inhale, supplied swallowed content, and Down before measuring combat. The separate `combat` probe uses a real enemy's random copy result.
- `KIRBY_LAB_TEST_SCROLLS`: optional `B,T,S` scroll counts for the ability probe, exposed as `--probe-scrolls`. Before measuring, the probe adds that many real `BrutalityUp`, `TacticUp` and `SurvivalUp` items (`InventItem.fromItem`) through the normal inventory and calls the host's `Hero.computeTiers`. `ability-damage.json` then records the tiers, the host and core colorless multipliers, and each copy hit's source HP drop and host damage.
- `KIRBY_LAB_TEST_HUD=1`: with the ability probe, exposed as `--probe-hud`. After the new run settles, it adds a real `StartShield` beside the starting sword through the normal inventory. It later discards the copy with the original Select button, and writes `hud.jsonl` whenever a HUD weapon slot changes: real item, slot item, icon texture, frame group, icon size and scale, and ammo flag. Each distinct decoded icon is saved as private `hud-icon-<hash>.rgba`.
- `KIRBY_LAB_TEST_PAUSE=1`: with `--probe-hud`, exposed as `--probe-pause`. At 700 probe frames it opens the normal pause screen (`new DefaultPause()`, whose constructor pauses the game), instead of the discard. It writes `pause-cards.jsonl` for every pause equipment card: whether it was drawn as the ability, the real item, and the card's object tree with texts. The run ends paused; set `KIRBY_LAB_CAPTURE_ALL=1` to capture the paused screen.
- `KIRBY_LAB_TEST_LEGENDARY=1`: with `--probe-hud`, exposed as `--probe-legendary`. Once the copy's ability item is in a weapon slot, it gives that item the host `Legendary` affix, calls `Hero.hudInitItems`, and appends the slot and affix result to `hud.jsonl`.
- `KIRBY_LAB_COMBAT_ELITE=1`: with the `combat` plan, makes the swallowed Zombie an Elite (Elite skills disabled) and records in `combat.json` whether the copy was empowered and the damage that the second Zombie takes from each hit. Set it in the environment of `launch_runtime.py`.
- `KIRBY_LAB_PLAY=1`: keep outfit selection but disable all smoke capture and automatic input.

Without these variables the lab helpers do not run. Do not use their successful test as evidence that an ordinary Steam launch uses isolated saves.

For a user-session reproduction, copy the private profile into a separate data root and pass that root explicitly to both staging and launching. The October 4 recording investigation uses `repros/user-recording-20261004`; the user's main private profile is preserved. `repro` injects keyboard events or raw controller-button query results through the existing host bindings. It does not replace the original engine's input mask or force inventory contents. `KIRBY_LAB_REPRO_PLAN` names a private JSON timeline with `at`, `until`, and optional `actions`, `keys`, or `padKeys` arrays.

The optional `KIRBY_LAB_REPRO_HEALTH=1` fixture changes only the copied test character's health, position, and outfit: it sets 57/76 health, repeats real stat updates, places the character near an existing generated item, and checks an outfit round trip. The timeline must use the normal pickup binding to acquire that item. `KIRBY_LAB_REPRO_PICKUP=StartBow` selects the existing bow instead of the shield. `KIRBY_LAB_REPRO_ENCOUNTER=1` later places the test character near an existing natural Zombie without changing its AI. `KIRBY_LAB_REPRO_MOB_COMPARISON=1` briefly uses the normal outfit near that enemy. `KIRBY_LAB_HIDE_BITMAP=1`, with optional `KIRBY_LAB_HIDE_BITMAP_FROM`/`UNTIL` times, skips only Kirby's original bitmap draw for a visual comparison. These are diagnostic fixtures, not normal play options.

The second recording investigation added further opt-in diagnostics. `KIRBY_LAB_CAPTURE_ALL=1` keeps `--capture-fps` capturing during host cinematics and death, and adds the hero animation, cinematic and visibility state to `captures.jsonl`. `KIRBY_LAB_REPRO_HIT_AT` and `KIRBY_LAB_REPRO_LETHAL_AT` deliver a real host Zombie attack (20 damage, or 10 damage at 3 HP) through the normal attack path at that repro time; `KIRBY_LAB_REPRO_HIT_SIDE=-1` places the 20-damage attacker on Kirby's left instead of his right. `KIRBY_LAB_DUMP_PHASES`, for example `rebirth:20,headPopLoop:5`, writes the scroller render tree near the hero when its animation reaches that frame. In `repro` runs, `repro-events.jsonl` also records host attacks entering the source and source health changes, and `repro-trace.jsonl` records the source palette flash entry and Kirby's palette bank.

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

Issue #39 removed host sword and bow attacks, so the `aim` plan (`LabWeaponAimProbe`) now holds the real slot-0 weapon binding (left-click, key 0) on the new run's starting weapon through the production reader, while forcing the host's button-down checks for that item. It passes when that binding reads as original B (mask 2) in base form, the source inhale flag (`ActionFlags & 1`) is set within 60 frames, the item makes no host attack, and the earlier mapping, inversion, masking and movement checks still hold. Run `20261006-122227`, in the separate data root `repros/left-click-20261006` (bridge `libkssu_bridge.so` SHA-256 `654c2023ff6a0a17775253ce4ede68a3089f19f668abc6a55b5f1b6ef5441597`, unchanged by #39), passed: `StartSword`, mask 2, 21 held inputs in base form, 32 inhale frames, no `Weapon.tryToUse` call reached the sword, all nine masks and inversions matched, 195.5 px moved and a 49.97 px jump. No fatal errors; real saves unchanged.

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/left-click-20261006 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/left-click-20261006 \
  --seconds 50 --start-game --outfit KirbySuperStarUltra --input-plan aim
```

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

### HUD ability slot (2026-10-05)

Issue #18. Same separate root, `repros/colorless-20261005`. Bridge rebuilt from
this branch's `src/nds/lib.rs`: `a72004d5d2b14e1abee6fadd929457c259c97c49e6b3ea0be4aa99d557d24915`. Runtime DLL:
`41afcb92b89c9b5bad5a853b7d86e562eaeef1803222492916c3ef9c839b99d3`. Each run was a fresh
`--start-game --input-plan ability --probe-ability 18 --probe-hud` for 90 seconds,
ended by the expected timeout without fatal log lines, and left the real saves
unchanged.

- `215606` (`--probe-scrolls 4,0,2`): Sword was copied at source frame 4412. At
  4482 the Left-Click slot (`StartSword`) switched to the decoded original Sword
  icon (green emblem, hash `bbb65ecd…`). It got the host `skillSlotColorless`
  frame, icon width 32 at scale 1 (host item icons: 24 at `pixelScale` 1.5), and
  ammo pips disabled. The slot item stayed `StartSword`. The shield slot was not
  overlaid. Captured frames show the gray colorless corners on the Brutality-red
  background of the highest stat.
- `215830` (no scrolls): the same icon and frame at 4482, again on a red background
  (tie → Brutality). The fixture's Select discard returned Kirby to base form at
  5313. The slot then showed the host `StartSword` icon (`cardIcons.png`, scale
  1.5), without the colorless frame, with the ammo flag restored.

Earlier attempts while building the fixture are not evidence:
- `214716`: lab recorder crash.
- `214753`, `214838`, `215019`: the shield fixture tried to stock a third weapon.
- `215110`: before the steady-icon and shield-slot fixes. It showed the original
  white copy flash and the red hurt icon flickering in the slot, the shield slot
  overlaid, and the base-form icon not restored.

The lab-added shield never reached its HUD slot (`slotItem` null) in any run. This
fixture limitation means the shield slot's own drawing was not observed.

### Pause equipment card (2026-10-06)

Issue #41. Same root, `repros/colorless-20261005`. Bridge from current `main`'s
`src/nds/lib.rs`: `3b9f9055b3a69290947e5a147afa363382de0c7156b4c7a53459cd0f1f4c57b1`. Runtime DLL: `af89b15e2ae9624e3d442bc8cb407bbc289dd4c5d343d38e1d74fa78429c1f1d`.
Each run used `--start-game --input-plan ability --probe-hud --probe-pause` for 75
seconds with `KIRBY_LAB_CAPTURE_ALL=1`, the starting sword in the Left-Click slot
and the shield beside it. Each ended by the expected timeout without fatal log
lines and left the real saves unchanged.

- `124012` (`--probe-ability 11 --probe-scrolls 0,0,4`): the pause opened as Fire
  Kirby (source frame 5119).
  - The `StartSword` card was drawn as the ability: name markup `{Fire@CL}`
    (shown in `#D8D8D8`), main line "Copy ability", the host's "Colorless (the
    highest level applies)" line, and the original Fire icon in the card's icon
    slot.
  - The captured screen shows that card in the gray colorless frame beside the
    unchanged "Old Wooden Shield I" and "Prisoner's Collar I" cards.
- `124145` (no ability): the same fixture in base form drew the normal "Rusty
  Sword I / 138 DPS" card.

Earlier attempts while building this:
- `123608`: before relabeling. It showed "Rusty Sword I / 241 DPS" on the
  colorless card.
- `123814`: `ui.Text.set_text` escaped raw HTML, so the name showed literal tags.
  Fixed by editing the host's own markup (`rawText`).
Correction (#36): the red background in `215606` and `215830` was not the highest
stat. Both runs had the starting sword in the slot, the same kind as the
`StartSword` stand-in. Through the host's same-item-kind path, the colors then came
from the real Brutality sword. The owner saw the same red with Survival highest.

### HUD slot stat color (2026-10-06)

Issue #36. Same root. Bridge rebuilt from current `main`'s `src/nds/lib.rs`:
`3b9f9055b3a69290947e5a147afa363382de0c7156b4c7a53459cd0f1f4c57b1`. Runtime DLL: `86f969f092cc464cf225affea2d2e0d06b4906413aab9b918fc42d8df1bae779`. Each run used
`--start-game --input-plan ability --probe-ability 18 --probe-hud` for 90 seconds,
with the starting sword in the Left-Click slot. Each ended by the expected timeout
without fatal log lines and left the real saves unchanged. `hud.jsonl` now records
the slot's two background shader colors.

- `013806` (`--probe-scrolls 0,0,4`, tiers 1/1/5): the copied Sword slot had
  colors `17ce75` (Survival green) and `b2c0dd` (colorless gray), and the captured
  frame shows the green background. The starting sword's `ec2424` red returned
  after the discard.
- `013956` (`--probe-scrolls 0,4,0`, tiers 1/5/1): `a051ff` (Tactics purple) and
  `b2c0dd`; `ec2424` again after the discard.

### Legendary ability item HUD frame (2026-10-07)

Issue #82. Separate root `repros/legendary-hud-20261007`. Bridge built privately
there from `main`'s `src/nds/lib.rs` (`df42119e…`): `ce0067c123eb843200bd7d5c6a5197886016b31f7977ba89f24f270d4362308d`.
Runtime DLL: `1fb2576ded89b3ffedc7cc5efc283dd26b28ca0f18da78f91ed1388bfc0fbdf9`.
Outfit pack copied from `repros/scroll-claim-20261007` (with the ability item rows).

- `20261007-124255`: `KIRBY_LAB_CAPTURE_ALL=1 ... --seconds 90 --start-game
  --outfit KirbySuperStarUltra --input-plan ability --probe-ability 18
  --probe-scrolls 0,0,4 --probe-hud --probe-legendary`. Sword was copied at source
  frame 4412 and slot 0 got `KirbyAbilitySword`; the probe gave it `Legendary`
  (`hasLegendary: true`). From 4482 the overlaid slot showed the original Sword
  icon in the host `skillSlotLegendary` frame, colors `17ce75` (Survival green)
  and `f4db4d` (legendary gold), ammo pips hidden; the shield slot was unchanged.
  The captured frame (host frame 10530) shows gold corners on the green slot. The
  Select discard at 5314 emptied the slot. Expected timeout, no fatal log lines,
  real saves unchanged.

No run of the previous code with a Legendary item was made; before #82 every
copied slot was drawn from the Colorless-only stand-in, which `setBgColors` gives
`skillSlotColorless` (`b2c0dd`), as in `013806` above.

## Private showcase capture

`--capture-fps 15` records actual gameplay framebuffer frames and the original PCM submitted by the mod. It leaves desktop audio untouched. The capture manifest records wall-clock timestamps; machine/RAM diagnostic snapshots remain limited to every two seconds. The optional combat take adds walking, jumping, floating, and attacks after absorption/parry. Normal play never enables these helpers.

Use `scripts/build_showcase.py RUN OUTPUT.mp4 --ffmpeg /path/to/ffmpeg --duration 30` to encode the timestamped frames and original effects. The recording contains the mod's original effects only; it does not capture host music or other applications. Inspect a contact sheet with universal-modder's `um video contact` before sharing the clip.

The earlier `022250` take used native bridge SHA-256 `b799d1482911f62f975a1484bfc6e324d2baa246ff1cac05377bb672bde5e690`. Its real Zombie supplied random Wing (ability 15); shield ID 910 countered successfully while evolved. The 65-second process completed without fatal errors and preserved real saves. Its 20- and 30-second cuts remain private in the showcase directory.

The final `025145` take uses native bridge `f94997d82877bcdce29ed8d8cb3741545f08044ad813fa61f59f9406c6314b2c`, with source-stage door decorations removed. A real Zombie supplied random Fire (ability 11), and retained shield 919 successfully countered while evolved. The 65-second process completed without fatal errors and preserved real saves. The selected video is `~/.local/share/kirby-dead-cells/showcase/kirby-dead-cells-clean-20s.mp4`: 20 seconds, 1024×768, 264 actual captured frames encoded at 15 fps, with original 48 kHz stereo effects. The universal-modder probe passed and `contact-clean-20s.png` was inspected. Absorption, evolution, parry, walking, jumping, flight, and original Fire attacks are visible.

### Ability descriptions (2026-10-06)

Issue #45. The pause card itself comes from #41 (PR #43, not yet on `main` at the
time). These runs therefore used a local-only test branch: this branch merged with
`fix/41-pause-ability-card`, never pushed. Same root, `repros/colorless-20261005`,
with `KIRBY_LAB_CAPTURE_ALL=1 ... --start-game --input-plan ability --probe-hud
--probe-pause` for 75 seconds. Every run ended by the expected timeout without
fatal log lines and left the real saves unchanged.

- `125345` Fire, `125617` Yo-Yo and `125747` Stone (the longest summary): the card
  showed the ability name, "Copy ability", the summary and the controls line,
  then the host's Colorless line. The host wrapped the text inside the card. Yo-Yo
  was named "Yo-Yo".
- `125923` Fire, after the controls line was shortened to fit one line (runtime
  DLL `716140dae4c43f628687419a28c33417a13122b1b8b8cf1b66a3e1efb4d5fb4e`): the
  captured card shows the summary on two lines and the controls on one.
- `125501` Cook: the probe's attack presses used up single-use Cook before the
  pause opened, so the card was the normal "Rusty Sword I". This is expected and
  is not evidence for Cook's text.

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

## Gold deposits

`KIRBY_LAB_BREAKABLE_TARGET=gold` makes the `breakable` plan spawn a real
`PileOfGold` at Kirby (constructor, then `init`), copy Sword, and tap attack until
it breaks. `breakable-hits.jsonl` records each host `onDamage`; `breakable.json`
counts `PileOfGold.dropLoot` calls. Separate data root `repros/gold-20261006`
(the owner's play session held the main lab); the bridge was unchanged.

```sh
KIRBY_LAB_BREAKABLE_TARGET=gold python3 scripts/launch_runtime.py \
  --data-root ~/.local/share/kirby-dead-cells/repros/gold-20261006 \
  --seconds 60 --start-game --outfit KirbySuperStarUltra --input-plan breakable
```

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261006-013823` | #35, attack held 130 frames | Two hits, life 4 → 2, two gold drops |
| `013937` | #35, repeated attack | Four hits of 1 life each, life 4 → 0, pile destroyed, four gold drops |
| `014052` | control: #35 pairing change reverted | No hits, life 4, no gold |

Each run reached its time limit without fatal errors, and real saves were unchanged.
`GoldNugget` and copy forms other than Sword were not run.

## Suplex lunge into doors

Issue #50. The ability probe copies Suplex (`--probe-ability 6`) and replays
original buttons from `KIRBY_LAB_ABILITY_PLAN`, counted from when the copy is
ready: Left over frames 90–120, then Right from 130 to 200 with B at 150–152 (a
dash, then the dash grab). `KIRBY_LAB_MOVES_SETUP=door@1` places Kirby 2.5 cells
left of the first breakable, unlocked generated door, facing it. `LabMovesProbe`
logs `door-hit` (`Door.onDamage`), `door-broken` (`Door.onDie`) and `GrabStrike`
on every source frame. Separate data root `repros/suplex-door-20261006`; bridge
unchanged (`libkssu_bridge.so` SHA-256
`ef72fc725634bc14784498b66150ae29743754105073f84aec227ce26768d13c`, built from this
`src/nds/lib.rs`).

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/suplex-door-20261006 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
KIRBY_LAB_ABILITY_PLAN=<plan> KIRBY_LAB_MOVES_SETUP=door@1 \
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/suplex-door-20261006 \
  --seconds 45 --start-game --outfit KirbySuperStarUltra --input-plan ability --probe-ability 6 --capture-fps 10
```

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261006-211833` | #50 | Dash grab lunge from source frame 4385 at 1728; door (97, 85) hit for 1 and broken on frame 4390 with `GrabStrike` set; Kirby recoiled (action `1228`). Frame `007094` shows the door bursting, `007344` the open doorway |
| `211959` | control: `canBreakDoors` hook for the lunge removed | Same lunge from frame 4383 into door (317, 177); Kirby recoiled at frame 4390 from x 7595.5, 12.5 px left of the door's cell; no door hit or break event |

The two runs generated different levels, so the doors differ. Both reached their
time limit without a fatal log, and real saves were unchanged. Not run live: the
standing, Down+B and mid-air lunges, a door approached from the right, and a
lunge that grabs an enemy just before a door.

## Host elevators

`LabMovesProbe` has three elevator scenarios for `KIRBY_LAB_MOVES_SETUP`: `elevator-spawn` builds a real `SimpleElevator` in the tallest open three-cell shaft and initializes it as `Level.spawnEntitiesFromMarkers` does (the first Prisoners' Quarters level has none), `elevator` stands Kirby on the first elevator, and `elevator-use` activates it through `canBeActivated`/`onActivate`. `moves-trace.jsonl` then records the elevator surface beside Kirby's feet on every source frame. The runs used a separate data root, `repros/elevator-20261006`, and a bridge rebuilt from this branch (`libkssu_bridge.so` SHA-256 `b2545e79b5fcbfd9670e252e369255be571122d169d05166ee2ef942fd40f8a8`).

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/elevator-20261006 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
KIRBY_LAB_REPRO_PLAN=<private plan: Space at 5.35 s and 8.45 s> \
KIRBY_LAB_MOVES_SETUP="elevator-spawn@2.5,elevator@3.5,elevator-use@5,elevator-use@8" \
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/elevator-20261006 \
  --seconds 45 --start-game --outfit KirbySuperStarUltra --input-plan repro --capture-fps 10
```

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261006-000400` | lifted window, scan only | No elevator in the first level; nothing placed |
| `000609` | lifted window, objects moved apart from Kirby | Kirby rode 1104 → 840 px within 0.34 px of the surface, but alternated fall (`070E`) and landing (`0507`) during the ride; 22 host landings of height 0 |
| `000906`, `001130` | whole-pixel window shift | Still 9 fall frames; per-step diagnostics showed a standing Kirby dropped right after a `kssu_world` shift undone by `kssu_move_player` |
| `001330` | window placed with Kirby while carried | 68 moving frames all standing (`0406`), feet on the surface (2×10⁻¹³ px); 22 host landings remained |
| `001557` | Beheaded (`PrisonerDefault`) | Same ride: no host landings |
| `001925` | host cell position copied from the elevator | 69 moving frames standing, feet exactly on the surface, no host landings; 2640 → 2376 px |
| `002051`, `002312` | final | Up: jump at 5.4 s, caught again by the rising elevator, carried to the top and standing there. Down: Kirby jumped; the faster elevator dropped away and he landed on it at the bottom with the original long-fall bounce. 76/76 health throughout. Two height-0 host landings: the elevator's departure tick and the genuine landing on the rising elevator |

In those runs the runtime never had to raise Kirby, so the host's carry did the work; every run with a descent included a jump before it reached full speed. The owner's 2026-10-06 01:15 recording showed Kirby falling behind a descending elevator ([#33](https://github.com/NahuelGenchi/kirbys-dead-land/issues/33)):

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261006-011803` | #28 as merged, no inputs | Standing until the descent reached about 10 px a tick; at f4686 the surface dropped 9.8 px without carrying him, and he fell behind it in `070E` to the bottom, where he bounced |
| `012037` | runtime carry of a standing Kirby, no inputs | Up 60 and down 56 moving frames, all standing, feet exactly on the surface at up to 12.4 px a frame; stopped at both ends at 76/76. The runtime carried him 74 times (`elevator-carry` diagnostic) |
| `012131` | same, jumps at 5.35 s and 8.45 s | Both jumps left the platform; the rising elevator caught him again; after the descending jump he landed on it at the bottom |

The owner's 2026-10-06 12:26 recording, made with that fix, showed a different fault at the bottom stop: Kirby sank into the platform for a frame, then rebounded and landed with the original's fast-landing stars. Two lab additions reproduce it. `KIRBY_LAB_FRAME_SLEEP_MS=min,max` stalls each host frame by a random time, emulating a desktop below 60 fps. `KIRBY_LAB_ELEVATOR_CX` picks the elevator at a cell column (`KIRBY_LAB_ELEVATOR_TRAVEL` sets the spawned elevator's travel). The runs below used `repros/elevator-real-20261006`, which has its own copy of the owner's private save (the main profile was not touched), resumed with `--resume-game`. The resume generated a level whose real `SimpleElevator` (column 389, four cells wide, 64 cells of travel) stops in a notch of level floor. `elevator-step` diagnostics log each step's placement and the terrain column under Kirby.

```sh
KIRBY_LAB_FRAME_SLEEP_MS=10,40 \
KIRBY_LAB_MOVES_SETUP="elevator@2,elevator-use@3.5,elevator-use@13,elevator-use@23,elevator-use@33" \
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/elevator-real-20261006 \
  --seconds 45 --resume-game --capture-fps 10
```

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261006-124646`, `124727` | descent carry (#33 as first proposed) | At the bottom arrival the elevator overshot its stop 4.9 px into the solid floor row (`cy` 106, `yr` 0.205). The lifted window put Kirby's body inside that row; the original pushed him 12 px up, and the elevator came back 24 px below him. He fell (`070E`) and landed (`0507`) |
| `125056`–`130031` (13 runs) | floor top while the surface is sunk into level floor | Every frame standing; Kirby never off the surface at rest. Our steps landed during an overshoot in 4 runs (15 steps), all handled by the new rule |
| `130203`–`130452` (4 runs, final build) | same | 3 arrivals each, 12 sunk steps over 3 runs, all frames standing, 76/76 health |
| `130556` (spawned elevator, final build, jumps) | same | Jumps still leave the platform both ways; he lands again and stands at both stops |

Spawned test elevators never stopped inside level floor, which is why the earlier runs missed this.

The owner's 2026-10-06 19:50 recording (with the fixes above) showed Kirby riding correctly but drawn above the platform on the way up and sunk into it on the way down. The trace now also records the elevator sprite's `y`, the host hero sprite's `y` and Kirby's bitmap `y`. Run `195232` measured the elevator sprite drawn up to 13 px behind its physical surface while Kirby's bitmap followed his physical position (up to 11.1 px apart from the platform as drawn). With the draw offset, runs `195451`, `195557`, `195651` and `195746` keep Kirby's drawn feet 0.5 px from the drawn platform on every moving frame (his usual whole-pixel anchor). The exception is the tick an elevator dips into its floor stop, when Kirby stays on the floor's top 3 px above it. All frames were standing at 76/76. Spawned-elevator run `195850` (jumps) still leaves and lands on the platform both ways, and the offset is at most 2.3 px in the air. The host's height-0 landing calls rise to 30–50 a ride when the frame rate is stalled; they play nothing.

Each run reached its time limit without a fatal log, and the real save inventories were unchanged. The tutorial tip in the captured frames comes from the fresh lab save.
## Shield swap

Issue #31. `LabItemProbe` (`KIRBY_LAB_ITEM`) can drop `shield`: the first
droppable host shield kind that is neither equipped nor already dropped. It logs
every `Inventory.equip`, the host replace dialog's result, the item drops left on
the level, and Kirby's retained shield with `CanUse` for each equipped shield.
`KIRBY_LAB_REPLACE_SIDE=right` replaces the right-hand item. With
`KIRBY_LAB_REPRO_ABILITY`, the probe's clock starts once Kirby holds the copy, as
the repro plan's does. Both runs used a separate data root,
`repros/shield-swap-20261006`, staged from this branch (runtime SHA-256
`bf8baed2140f15b3764642138007753e800e88de78487dd14d89c94cb382a5da`; bridge
`b2545e79b5fcbfd9670e252e369255be571122d169d05166ee2ef942fd40f8a8`, built from the
unchanged `src/nds/lib.rs`).

```sh
KIRBY_LAB_REPRO_ABILITY=18 KIRBY_LAB_REPRO_HIT_AT=7.4 KIRBY_LAB_REPRO_PLAN=<plan> \
KIRBY_LAB_ITEM=shield@1,shield@3,sword@11 KIRBY_LAB_REPLACE_AT=5 KIRBY_LAB_REPLACE_SIDE=right \
python3 scripts/launch_runtime.py --data-root <root> --seconds 70 --start-game \
  --outfit KirbySuperStarUltra --input-plan repro
```

The plan presses the real interact binding (action 6) at 1.6, 3.6, 11.6 and 13.6 s
and holds the slot-2 weapon binding (action 3) over 7.0–7.8 and 9.0–9.8 s.

| Run | Form | Result |
| :--- | :--- | :--- |
| `20261006-004147` | Sword (source ability 18) | Interact took a Warrior's Shield (permanent ID 257) into the empty slot; retained 257. The second interact opened the host replace dialog for a Cudgel (`Shield`, `StunShield`); validating the right-hand item ran `Inventory.equip` and left the Cudgel (260) in slot 2, with the Warrior's Shield dropped on the level. Retained 260, `CanUse` true, copy still Sword. Holding the shield binding gave original guard input 512 and the host `block` animation; a 20-damage host Zombie hit at 7.4 s resolved `Partial` (5 after the Cudgel's 75% absorb) and Kirby stayed at 76/76. The dropped BroadSword reported `isUsefulFor` false. |
| `20261006-004316` | Base form | Same swap: Warrior's Shield 252 → Cudgel 255, retained 255, same guard, block and `Partial` 5 result, 76/76. BroadSword `isUsefulFor` true. |
| `20261006-004027` | Base form, no hit | First run of the same swap (253 → 256); guard 512 and `block` while held. |

All three runs reached their time limit without fatal log lines, and real saves
were unchanged. A first take in base form before copying, then a swap while
copied (the owner's exact order), and a parry-timed counter with the new shield
were not run; the shield's existing parry path was not changed.
## Enemies in water

`--input-plan water` (`LabWaterProbe`) looks for a host pool cell with a floor beside a dry bank (the bank may be up to three cells higher), places Kirby on the bank and creates a real Zombie in the pool, holding its AI so it stays in the water. For 120 source frames it records whether the source claims the Zombie, then it taps toward the pool, holds the original inhale until the Zombie is consumed (600-frame limit) and presses DOWN. `water.json` records the site, the frames the Zombie was source-controlled before any input, consumption and mouthful, the first copy after DOWN, and Kirby's life. A generated level may have no usable pool (`site: null`); launch again. All runs used the separate data root `repros/water-20261005`, whose bridge is built from this branch into its own `target/` so other sessions' builds of the shared oracle bridge cannot replace it.

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/water-20261005 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/water-20261005 \
  --seconds 70 --start-game --outfit KirbySuperStarUltra --input-plan water --capture-fps 15
```

| Run | Bridge SHA-256 (prefix) | Site | Controlled before input | Inhale | After DOWN |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `20261006-000610` | `41be0932ef86` (before #27) | 1-deep pool, bank 1 cell up | 143 of 143 host frames; Zombie pinned out of place | Timed out after 600 frames, Zombie life 123 unchanged | — |
| `20261006-001059` | `b2545e79b5fc` (another branch's build, without #27) | Pool under a walkway, bank 3 cells up | 185 of 185; original defeat stars at the Zombie | Timed out | — |
| `20261006-000045` | `4967f6f4ac84` (#27) | Bank 1 cell up | 0 | Consumed 37 frames in, mouthful | Cutter |
| `20261006-001715` | `4967f6f4ac84` | Bank 1 cell up | 0 | Consumed 37 frames in, mouthful | Paint |
| `20261006-001840` | `4967f6f4ac84` | 1-cell pool | 0 | Consumed 37 frames in, mouthful | Cutter (source trace, frame 4955; the probe then read too early) |
| `20261006-002101` | `4967f6f4ac84` | 1-cell pool | 0 | Consumed 38 frames in, mouthful | Copy at frame 4557; a natural enemy's hit at 4738 knocked it loose |
| `20261006-002601` | `4967f6f4ac84` | 12-cell pool | 0 | Consumed 37 frames in, mouthful | No copy observed; Kirby kept the inhale pose after consumption (not investigated) |

Every run kept the real saves unchanged and logged no fatal errors. Natural enemies near the pool hurt Kirby in some runs; the probe does not control them. Sword or other attacks on an enemy in water, and a walkway-over-pool site with the fixed bridge, were checked only in the native suite (`proxy_water_sword`, `proxy_water_below`), not live.

## Ice host freeze (2026-10-06)

Issue #42; behavior in [GAMEPLAY_FIDELITY.md](GAMEPLAY_FIDELITY.md#ice-freezes-with-the-host-status-intentional-deviation).
The ability probe with `--probe-ability 10` now keeps the Zombie's full life. `KIRBY_LAB_TEST_TARGET_LIFE` sets a life that outlasts the breath. `ability-target-trace.jsonl` adds `hostFrozen`, `hostFreezeSeconds` and `sourceIceBlocks` (original objects running the frozen-block routine `0x021583EC`). `ability-target-affects.jsonl` logs every host status change and pop text on the target. All runs used the separate data root `repros/ice-freeze-20261006` with the shared bridge `654c2023ff6a…`, built from this branch's unchanged `lib.rs` (source `586a15a349c9…`).

```sh
KIRBY_LAB_TEST_TARGET_LIFE=2000 python3 scripts/launch_runtime.py --data-root <root> --seconds 80 \
  --start-game --outfit KirbySuperStarUltra --input-plan ability --probe-ability 10 --capture-fps 10
```

| Run | Build | Target | Result |
| :--- | :--- | :--- | :--- |
| `20261006-124355` | First version: freeze only | Zombie, 130 life | Freeze 3.0 s on the first hit (frame 4383); killed on the fourth (4401). No original ice block in 2,606 traced frames. |
| `20261006-124601`, `124804` | Same | 2,000 life | The freeze seemed continuous, but captures showed a "Slowed" pop on every hit. |
| `20261006-125326` | Same, with the affect log | 2,000 life | Each later breath hit shattered the freeze (thaw slow 133, stun 8, "Slowed") and the next tick refroze it. This led to Frost-typed hits. |
| `20261006-125935` | Frost hits (`5fc9c63bbf46…`) | 2,000 life | Freeze on at 4385. The next six hits (1962 → 1782) left it on; no "Slowed". It ended at 4564 (179 source frames) with one host thaw: "Slowed", slow 133 until 4634, stun 8 until 4601. Captures show the host ice encasement and freeze icon, with frost-coloured damage numbers. No original ice block. |
| `20261006-130140` | Frost hits | Zombie, 120 life | Freeze on the first hit (4382); hits 38, 31, 31, 21 without a thaw; killed at 4400. The proxy was retired in the same tick (flags 80, then unbound). Captures show the normal host death and no ice block. No original ice block in 2,684 traced frames. |
| `20261006-130608` | Final: freeze only after a host-resolved hit (`f11d17a85139…`) | 2,000 life | Same as `125935`: freeze on at 4382, seven hits (2000 → 1782) with no thaw, one natural thaw at 4562 (stun until 4598, slow until 4633). No original ice block in 2,763 traced frames. |
| `20261006-130738` | Final | Zombie, 131 life | Freeze on the first hit (4382), hits 38, 31, 31, 31 with no thaw, killed at 4400. No original ice block in 2,845 traced frames. |

Each run ended by the expected timeout, logged no fatal errors and left the real saves unchanged. The probe's incoming 10-damage hit took Kirby from 76 to 68 in these runs. Other branches' recent ability runs recorded 68–75 for the same hit, so this change did not cause the difference. Not run live: freeze resistance on bosses and Elites, the diminishing factor across repeated freezes, Ice against breakables and hazards, and an Ice kill other than the breath.
## Screen-wide attacks (#69)

`--input-plan screen` (`LabScreenProbe`) prepares Crash through original
inhale, supplied content and Down (`--probe-ability 21` or `22` for Paint or
Mike). It places six real Zombies with locked AI on Kirby's floor: 40 px ahead
(paired), 110 px ahead kept without a source object (`KirbyEnemies.TestUnpaired`),
230 px ahead and behind, 360 px ahead and 1400 px ahead, then holds original B.
`screen.json` records the host view, the original's window, and for each Zombie
its position, pairing, whether the original reaches it and whether it is in view
when B goes down, the host strikes and when it died. `screen-events.txt` also
logs each source hit and host damage event. Results are in
[Screen-wide attacks](GAMEPLAY_FIDELITY.md#screen-wide-attacks-69).

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/screen-attack-20261007 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/screen-attack-20261007 \
  --seconds 90 --start-game --outfit KirbySuperStarUltra --input-plan screen
```

The repro root links `dotnet`, `research`, `oracle/ready2` and `oracle/vendor`
to the main data root. The older `outfit-build/res.pak` lacks the ability item
kinds; with it, level loading failed with `Invalid ItemKind: KirbyAbilitySleep`
(run `20261007-003844`).

## Star projectiles

`--input-plan stars` (`LabStarProbe`, issue #20) runs five phases with real Zombies:

1. **Spit, paired target:** Kirby inhales one Zombie 36 px ahead, then spits the original star at a second Zombie 150 px ahead.
2. **Spit, unpaired target:** the same, with the target kept without a source object (`KirbyEnemies.TestUnpaired`).
3. **Discarded star:** Kirby swallows a Zombie for a copy, then SELECT discards the star backwards through a Zombie 60 px behind him.
4. **Spit into a wall:** the probe searches the host collision map for a flat floor ending in a solid wall at least six cells tall, places Kirby five to seven cells from it facing the wall, and spits.
5. **Discarded star into a wall:** at the same wall, with Kirby facing away.

Enemy AI is locked until each star is fired. Targets get 5000 life, so a star does not kill them and their stagger stays visible. `stars.json` records, per phase:
- the strikes reported by `KirbyStars.TestObserver`, and whether each went through the source object;
- target life;
- the first and last source frames with Stun (affect 8), and whether affect 16 was set;
- the target's displacement 12 frames after the hit;
- every star's host box per source frame, and its horizontal turns.

The run used a separate data root, `repros/stars-20261005`, so the owner's main lab was not restaged.

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/stars-20261005 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/stars-20261005 \
  --seconds 150 --start-game --outfit KirbySuperStarUltra --input-plan stars
```

Earlier probe versions:
- `20261005-220106` (three phases): every star killed its Zombie outright. The paired hit in phase 1 was not recognized as a star hit, because a lethal source hit removes the target's own collision records in that frame. The adapter now also tests the enemy's host-sized box, and targets get 5000 life.
- `220438` (three phases): all three passed with stun and affect 16.
- `221013` (five phases): phases 1–3 and 5 passed. Phase 4 turned Kirby toward the wall but spawned its victim from `hero.dir`, which stayed +1, so it was never inhaled and the phase timed out. The bridge's facing byte read 0 for all 7151 traced frames, including left-facing ones. Wall phases now use the side they turned Kirby to.

Run `20261005-221418` passed all five phases with Kirby at 76/76. It used runtime SHA-256 `21773889b035b4f05f10847e85778544a623d1a7ab3920f4e148edc03d52dd1d` and bridge `278518576c2174c65c9041a3577bdb7fbe1361aef45ed13416618ff7a80652a0`, unchanged by this work.

| Phase | Result |
| :--- | :--- |
| Spit, paired target | Source path, 182 damage (5000 → 4818); Stun frames 4463–4523; affect 16; moved 5 px with the original recoil |
| Spit, unpaired target | Host path, 182 damage; Stun 4697–4758; affect 16; pushed 25 px away |
| Discarded Parasol star | Host path, 182 damage; Stun 5007–5066; affect 16; pushed 27 px away; four floor bounces 64 frames apart |
| Spit into a wall (face x 984) | The star's last box was 974–994, centered on the wall face; no turn |
| Discarded Sword star into the same wall | Turned with its left edge at 983, then came back |

The process reached its time limit without fatal errors, and the real save inventories were unchanged. The captures show the discarded star above Kirby right after SELECT. Near the wall, about 156 px from Kirby, no star is drawn, because the original leaves objects outside its 256-pixel screen out of OAM. (#63 and #67 later drew such pieces; see [Far shots](#far-shots-63-67).)
## Far shots (#63, #67)

The ability probe copies Plasma (`--probe-ability 12`) and replays original
buttons from `KIRBY_LAB_ABILITY_PLAN`, counted from when the copy is ready: Up
and Down alternating every two frames over frames 30–230 (a full charge), Right
at 232, B at 240–242; then the same charge, Left at 532 and B at 540–542.
Separate data root `repros/far-shots-20261006`, with an outfit pack rebuilt
from this branch (`8e44122d86f4d8b509bfdd08ee4acde4bf9f4b4cc21fd3e9a7d0e30d3b5be7af`;
the older shared `outfit-build/res.pak` predates ability items and stops at the
title screen with `Invalid ItemKind: KirbyAbilitySleep`), runtime DLL
`e7b5cf83c190ec7a1272a48c9d3190fde31543c6e8f5bd2457f9e9465c7221ff` and bridge
`60f2fc39f40457797d80f5133ede21d39078f18b12c3a0f866318db153b5d487`, both built
from this branch.

```sh
python3 tools/build_outfit.py --output ~/.local/share/kirby-dead-cells/repros/far-shots-20261006/outfit-build
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/far-shots-20261006 \
  --original-runtime --outfit-pack ~/.local/share/kirby-dead-cells/repros/far-shots-20261006/outfit-build/res.pak
KIRBY_LAB_ABILITY_PLAN=<plan> KIRBY_LAB_CAPTURE_ALL=1 \
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/far-shots-20261006 \
  --seconds 60 --start-game --outfit KirbySuperStarUltra --input-plan ability --probe-ability 12 --capture-fps 15
```

Run `20261006-235639` reached its time limit without a fatal log, and the real
saves were unchanged. `ability-actors.jsonl` places the ball against the 15 fps
captures:

| Shot | Ball drawn (captures) | What ended it |
|---|---|---|
| Right, source frames 4481–4528 | At 114 and 174 px (`008267`, `008273`) | A level wall about 190 px right: sparks at its face at 204 px (`008277`). The ball's object went on to 336 px inside the wall, which the host draws over Kirby's sprite layer; in the ROM the charged ball passes through walls |
| Left, source frames 4782–4830 | At 114, 174, 210 and 246 px (`008476`–`008490`; at 246 px its outer half is past the edge of the drawn range) | The drawn range: not drawn at 270 px and beyond, while its object flew on to 324 px |

The offscreen 1024×768 view showed about 360 host px each side of Kirby
(estimated from the ball's position in the captures), so here roughly the last
100 px to the screen edge stay empty. Not run live: Ninja, Beam
and Sword far shots, spit stars, and an enemy hit beyond the old 128 px.

#67 rerun with the same plan and data root, the outfit pack rebuilt from
`main` (same SHA-256), runtime DLL
`885ada9313d9673e6d13afc5a86b980b1c61fe0eb2c228410db11e433608f0e7` and bridge
`a326559a3747f7371e4c51a7ba1494e15e237c19bf44e0388d68d63d2e0079cf`, both built
from the #67 branch. Run `20261007-004025` reached its time limit without a
fatal log, and the real saves were unchanged.

| Shot | Ball drawn (captures) | What ended it |
|---|---|---|
| Right, source frames 4497–4538 | Sparks at 144 px (`007594`) | The same level wall, about 190 px right; the object went on to 360 px behind its tiles |
| Left, source frames 4795–4841 | Whole ball at 102, 156 and 300 px (`007819`, `007827`, `007849`); at 354 px half of it, at the left edge of the lab view (`007858`) | The view's edge; the object reached 378 px. Captures between those show only its sparks: the ball's pieces are drawn one frame in three in the ROM |

Gameplay ran at 54.3 source frames per second over 33 s of play, against 54.1
in the #63 run (offscreen lab with capture). Not run live: Ninja, Bomb, Beam,
Crash and Sword far shots, spit stars, and an enemy hit beyond 128 px.
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
## Facing and left parry (#19)

The owner's 2026-10-05 recording showed the host shield and its parry arc on Kirby's right while he faced left. All three runs below used a separate data root, `repros/left-parry-20261005`, with `KIRBY_LAB_REPRO_HEALTH=1` to pick up the generated Old Wooden Shield, and a private `KIRBY_LAB_REPRO_PLAN` timeline (walk left or right for 0.4 s, then hold the shield binding for 1 s).

Before the fix, run `20261005-214458` traced `FacingRaw` (player+`0x15`) at 0 and `hero.dir` at 1 after walking in either direction, and every host block animation faced right. Over the same run, bit 31 of player+`0x6C` was set on all 44 frames with leftward velocity and clear on all 20 with rightward velocity.

With the fix (runtime SHA-256 `15b0b560908f0627bb0131a3464690f047ed055704ba7f1eab70ca91159dc4dc`, bridge `278518576c2174c65c9041a3577bdb7fbe1361aef45ed13416618ff7a80652a0`), run `20261005-215025` showed `hero.dir` -1 and the host shield sprite mirrored (`scaleX` -1) in both left-facing blocks, and +1 in the right-facing ones. `KIRBY_LAB_REPRO_HIT_SIDE=-1` places the `KIRBY_LAB_REPRO_HIT_AT` attacker on Kirby's left. With Kirby facing left and the hit 0.1 s into the block:

| Run | Attacker | Host result |
| :--- | :--- | :--- |
| `20261005-215320` | Left | `onShieldStartParry` and `onShieldCounterSuccessful`; hit `Block`; health stayed 57/76 |
| `20261005-215401` | Right | Hit `Normal`; the attack entered the next original collision tick |

All three runs reached their time limit without fatal errors, and the real saves were unchanged. Holding the opposite direction while raising the shield was traced but not resolved (see [GAMEPLAY_FIDELITY.md](GAMEPLAY_FIDELITY.md)).

## Ability items (#55)

`--input-plan items` (`LabAbilityItemProbe`) checks the
[ability items](GAMEPLAY_FIDELITY.md#ability-items-intentional-addition) on a new
run. It lists the start level's floor items and places Kirby on a dry floor with
no other interactive object within eight cells and no enemy within sixteen. It
then creates a Broadsword in front of him, an explosive grenade three cells
behind him and a starting shield, through the normal `ItemDrop` constructor.
Kirby holds the original inhale on the sword's ability item and spits it out with
B, pressed until the item is back. He inhales it again, swallows it with Down,
then discards the copy with Select. `ability-items.json` records each step;
`ability-items.jsonl` records every replacement, slot change and returned item.

All runs used the separate data root `repros/ability-items-20261006` (a copy of
the private lab), the outfit pack built from this branch (SHA-256
`8e44122d86f4d8b509bfdd08ee4acde4bf9f4b4cc21fd3e9a7d0e30d3b5be7af`) and the
shared private bridge build (`ef72fc725634bc14784498b66150ae29743754105073f84aec227ce26768d13c`;
this branch does not change `src/nds`):

```sh
python3 scripts/launch_runtime.py --data-root "$HOME/.local/share/kirby-dead-cells/repros/ability-items-20261006" \
  --seconds 90 --start-game --outfit KirbySuperStarUltra --input-plan items
```

Final runtime DLL `d769b054bc53811d209f3e1ec91946c6c0344260ad2d28f21b740513f7440c75`,
three runs, all `passed: true`, each ended by its time limit without fatal log
lines and with the real saves unchanged:

| Run | Start level replacements (item ID → ability) | Probe item | Result |
| :--- | :--- | :--- | :--- |
| `20261006-223826` | `StartBow` → Jet, `FastGrenade` → Cook, `LightningWhip` → Plasma | Paint | Spat back to its cell; swallowed into Paint; slot and HUD held that item; discard cleared both |
| `20261006-223958` | `StartBow` → Sleep, `StunMace` → Stone, `ExtraHeal` → Cutter, `FastGrenade` → Plasma, `StandardTurret` → Mike | Beam | Same |
| `20261006-224129` | `StartBow` → Stone, `StandardTurret` → Sleep, `DualDaggers` → Mike | Ice | Same |

In each run the starting shield and the gold bag stayed, the shield the probe
created stayed a shield, the Broadsword and grenade became ability items with the
`Colorless` affix at item level 1, `isUsefulFor` was false, and the item followed
the original suction (proxy flags 2 and 32) into the mouth.

Earlier iterations of the same probe found and fixed three problems before the
final build. `HUD.refreshItems` did not show the new slot item (`215522`), so
`Hero.hudInitItems` is used. A spat item returned with the host's loot bounce
(`221819`). A spit star's original recoil on a returned item's proxy pushed the
item three cells (`223514`): the item now follows only the original inhale, and
pairs only in base form. `221525` captured the host item tip on the floor: "Bomb I",
"Copy ability", the Bomb description and controls line, and "Colorless (the
highest level applies)" with the host's "Take" prompt.

Not run live: the short-press hint, a long-press recycle, a copy from an enemy
filling the slot, a mouthful of an item and an enemy together, a shield
displaced from a full slot, save and resume with an ability item in the slot,
other biomes, chests, timed doors and Boss Rush.

### Challenge rewards and shops (#61)

`--input-plan rewards` (`LabRewardItemsProbe`) places Kirby on a clear floor of a
new run and creates, through their own constructors, an item altar with a
Broadsword, a 60-second timed item with an explosive grenade and a shop booth
with a Lightning Whip (price argument 1000). With 5000 gold it activates each
as a short press does and writes `rewards.json`.

`20261006-235600` (`repros/ability-items-20261006`): the altar held Ice, the
timed item Mike and the booth Stone. Taking the altar item disabled the altar;
the timed pedestal emptied; buying took 1000 gold and removed the booth. Each
item lay on the floor once as a floor ability item. Captured frames show the
altar's ability tip and the "-1000" gold pop. `passed: true`, saves unchanged,
no fatal log. Not run: real kill-count or timed-door rooms, pick-one altar
groups, a level saved before this fix, a shop short of gold, mimics and
legendary altars.
### Pause card and save of an ability item (#62)

`KIRBY_LAB_ITEMS_KEEP=1` makes the `items` probe keep the swallowed copy and give
its item the host `Grenade` affix (the passive a forge reroll offered the owner).
`KIRBY_LAB_ITEMS_RESUME=1` with `--resume-game` records the weapon slots 300
source frames after the resume, then opens the normal pause screen and appends
each equipment card's texts to `pause-cards.jsonl`.

- `20261006-234721`: Wheel copied from a floor item, `Grenade` added, saved with
  `--save-after 1000` at source frame 5166 (ability Wheel).
- `20261006-235012` (resume): slot 0 held `KirbyAbilityWheel`, level 1, with
  `Grenade` and `Colorless`. Its pause card read "Wheel I", "Copy ability", the
  Wheel description, "Colorless (the highest level applies)." and "Launches a
  grenade (15 damage)", in the captured frame. Saves unchanged, no fatal log.

The owner's missing passive was the stand-in card hiding the real item. Affixes
do not act on copy attacks until #57.

### Ability item quality (#56)

- `KIRBY_LAB_ITEMS_QUALITY=2 … --input-plan items` run `20261007-003043`: a "++"
  Broadsword became a Hammer ability item with two `QualityUp`, host damage bonus
  0.667 and the info line "+67% copy damage"; the probe passed (spit, re-inhale,
  swallow, slot, HUD, discard).
- `KIRBY_LAB_COMBAT_ELITE=1 … --input-plan combat` run `20261007-003221`: the
  swallowed Elite Zombie gave Sword (ability 18), empowered, with copy damage
  multiplier 2.25 from the Legendary item; the shield parry and counter passed.
- Both: saves unchanged, no fatal log. Not run: a host-rolled "+" or legendary
  floor drop, the card text on screen, the Elite hit damage against a second
  enemy (the combat probe's second Zombie was not struck in this run).

### Ability item affixes (#57)

`KIRBY_LAB_ITEMS_AFFIXES=1 … --input-plan items` gives the probe's Broadsword the
host `BleedOnHit` affix before it is dropped (it must carry over), forces the
floor item to Sword, and after the swallow adds `FrontArrow` and `BleedOnHit` to
the copy's item. A Zombie with 30000 life stands three cells ahead; Kirby presses
B every 16 frames for 240 frames, then stops while its life is sampled.

- `20261007-004739`, `004958` and `005200` (`repros/ability-items-20261006`):
  15 affix uses (one per press), a host `dc.en.Bullet` (the arrow) spawned, and
  the Zombie lost life after the attacks stopped (bleed; 29125 → 29059 in
  `004739`). In `005200` the replaced Broadsword's `BleedOnHit` carried over to
  the ability item. All `passed: true`, saves unchanged, no fatal log.
- `20261007-004127` (before the fix) showed no affix use: the press check also
  required an empty original mouth, whose count bit stays stale after a swallow.

Not run: the Grenade affix, on-kill affixes, a forge reroll in play, and the
arrow captured on screen.

## Scroll claim (#80)

`--input-plan scroll` (`LabScrollProbe`) places Kirby on a clear floor of a new
run and creates four stationary Zombies (AI locked, 1000 life) three to six
cells ahead of him and a real `SurvivalUp` scroll two cells ahead, through their
own constructors. Kirby walks toward them; once a Zombie is paired, the scroll is
claimed as a short press does (`ItemDrop.onActivate`). With
`KIRBY_LAB_SCROLL_INHALE=1` Kirby holds the original inhale instead, and the
claim happens once a Zombie is in the suction (proxy flag 32). Every host frame
after the claim appends the pause, awake and cinematic state, whether the
original advanced and the proxy flags to `scroll-trace.jsonl`; `scroll.json`
holds the result.

Separate data root `repros/scroll-claim-20261007`, outfit pack built from this
branch (`8e44122d86f4d8b509bfdd08ee4acde4bf9f4b4cc21fd3e9a7d0e30d3b5be7af`),
shared bridge build (`9a56cd32346fa0758b2e047fafbe6d548d193f70d10199f0bae1144ec264a2fc`;
this branch does not change `src/nds`):

```sh
KIRBY_LAB_SCROLL_INHALE=1 python3 scripts/launch_runtime.py \
  --data-root "$HOME/.local/share/kirby-dead-cells/repros/scroll-claim-20261007" \
  --seconds 75 --start-game --outfit KirbySuperStarUltra --input-plan scroll --capture-fps 5
```

| Run | Build | Plan | Result |
| :--- | :--- | :--- | :--- |
| `20261007-115546` | Before the fix | Walk | The claim started a host cinematic of 214 host frames; the host walked the hero 18 px back to the scroll and the original advanced 18 frames with four Zombies paired. No source object ended; no fatal log |
| `20261007-115757` | Before the fix | Inhale | During the cinematic the walk frames swallowed slots 2 and 3 (flag 4). The first normal step then failed with `NDS bridge returned -3` in `KirbyEnemies.Prepare`, the owner's crash (exit 255, fatal log) |
| `20261007-115950` | Fixed (`e5dfe8ab…76c97b`) | Inhale | Slots 1 and 2 swallowed during the cinematic; the next normal step removed both host Zombies and freed the slots, Kirby kept the mouthful, the Survival tier went from 1 to 2. `passed: true`, no fatal log |
| `20261007-120114` | Fixed | Walk | Tier 1 to 2, all four Zombies paired before and after. `passed: true`, no fatal log |

The real saves were unchanged in every run. Not run live: a projectile or a
dying enemy ending during the cinematic, Brutality and Tactics scrolls, scrolls
that offer a choice, the exit-door walk with paired enemies, and the owner's own
Ramparts room.

## Mutation menu filter (2026-10-08)

Issue #90. `--probe-menu [filtered|unfiltered]` (`KIRBY_LAB_TEST_MENU`, with
`--input-plan ability`) unlocks every hidden mutation and three ordinary ones in
the private profile, opens the real `ui.PerkSelect` and writes the mutations it
listed to `mutation-menu.json`. Runs `20261008-021537` (unfiltered) and
`20261008-021638` (filtered) covered the first nine; the list then grew to twelve
and runs `20261008-022159` (unfiltered) and `20261008-022301` (filtered) were
repeated. Unfiltered it listed all twelve hidden mutations and the three ordinary
ones; filtered it listed none of the twelve and all three ordinary ones. No fatal log. The mutation
NPC's `PerkMaster.isVisible` hook was not exercised.
