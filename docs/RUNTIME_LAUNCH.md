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
- `KIRBY_LAB_INPUT_PLAN`: `movement`, `combat`, `ability`, `visual`, `breakable`, `boss`, `crowd`, `aim`, `repro`, `hazard`, `stars`, `water`, `items`, `scroll`, `tentacle` or `arrival`, for a reproducible offscreen test. `repro` tests normal keyboard/controller bindings and keeps the normal tutorials. `items` is the ability item check (see [ability items](#ability-items-55)).
- `KIRBY_LAB_TEST_ABILITY`: optional source ability ID for the ability probe, exposed as `--probe-ability 2..25`. The probe prepares it through original inhale, supplied swallowed content, and Down before measuring combat. The separate `combat` probe uses a real enemy's random copy result.
- `KIRBY_LAB_TEST_SCROLLS`: optional `B,T,S` scroll counts for the ability probe, exposed as `--probe-scrolls`. Before measuring, the probe adds that many real `BrutalityUp`, `TacticUp` and `SurvivalUp` items (`InventItem.fromItem`) through the normal inventory and calls the host's `Hero.computeTiers`. `ability-damage.json` then records the tiers, the host and core colorless multipliers, and each copy hit's source HP drop and host damage.
- `KIRBY_LAB_TEST_HUD=1`: with the ability probe, exposed as `--probe-hud`. After the new run settles, it adds a real `StartShield` beside the starting sword through the normal inventory. It later discards the copy with the original Select button, and writes `hud.jsonl` whenever a HUD weapon slot changes: real item, slot item, icon texture, frame group, icon size and scale, and ammo flag. Each distinct decoded icon is saved as private `hud-icon-<hash>.rgba`.
- `KIRBY_LAB_TEST_PAUSE=1`: with `--probe-hud`, exposed as `--probe-pause`. At 700 probe frames it opens the normal pause screen (`new DefaultPause()`, whose constructor pauses the game), instead of the discard. It writes `pause-cards.jsonl` for every pause equipment card: whether it was drawn as the ability, the real item, and the card's object tree with texts. The run ends paused; set `KIRBY_LAB_CAPTURE_ALL=1` to capture the paused screen.
- `KIRBY_LAB_TEST_LEGENDARY=1`: with `--probe-hud`, exposed as `--probe-legendary`. Once the copy's ability item is in a weapon slot, it gives that item the host `Legendary` affix, calls `Hero.hudInitItems`, and appends the slot and affix result to `hud.jsonl`.
- `KIRBY_LAB_COMBAT_ELITE=1`: with the `combat` plan, makes the swallowed Zombie an Elite (Elite skills disabled) and records in `combat.json` whether the copy was empowered and the damage that the second Zombie takes from each hit. Set it in the environment of `launch_runtime.py`.
- `KIRBY_LAB_ARRIVAL_LEVEL`: the level the `arrival` plan starts a new run in (default `Ossuary`; for example `PrisonStart`). Kirby stands still and `arrival-trace.jsonl` records, every host frame, the hero's host position, the viewport centre, size and zoom, the camera's `vertical` lead and dive state as the host reads them, and the source camera (#126).
- `KIRBY_LAB_WATER_LEVEL`: the level the `water` plan starts a new run in (default: the normal start), for example `SewerDepths` or `SwampHeart`. `KIRBY_LAB_WATER_WADE=1` makes the plan wade instead of placing a Zombie (see [Shallow water](#shallow-water-144)). Set both in the environment of `launch_runtime.py`.
- `KIRBY_LAB_PLAY=1`: keep outfit selection but disable all smoke capture and automatic input.
- `KIRBY_LAB_PERF=1` (set in the shell that runs `launch_runtime.py`; ignored in play): write `perf.json` in the run folder with the time and managed bytes of each stage of the mod's frame, host frame intervals and collections. See [Performance](PERFORMANCE.md).

Without these variables the lab helpers do not run. Do not use their successful test as evidence that an ordinary Steam launch uses isolated saves.

For a user-session reproduction, copy the private profile into a separate data root and pass that root explicitly to both staging and launching. The October 4 recording investigation uses `repros/user-recording-20261004`; the user's main private profile is preserved. `repro` injects keyboard events or raw controller-button query results through the existing host bindings. It does not replace the original engine's input mask or force inventory contents. `KIRBY_LAB_REPRO_PLAN` names a private JSON timeline with `at`, `until`, and optional `actions`, `keys`, or `padKeys` arrays.

The optional `KIRBY_LAB_REPRO_HEALTH=1` fixture changes only the copied test character's health, position, and outfit: it sets 57/76 health, repeats real stat updates, places the character near an existing generated item, and checks an outfit round trip. The timeline must use the normal pickup binding to acquire that item. `KIRBY_LAB_REPRO_PICKUP=StartBow` selects the existing bow instead of the shield. `KIRBY_LAB_REPRO_ENCOUNTER=1` later places the test character near an existing natural Zombie without changing its AI. `KIRBY_LAB_REPRO_MOB_COMPARISON=1` briefly uses the normal outfit near that enemy. `KIRBY_LAB_HIDE_BITMAP=1`, with optional `KIRBY_LAB_HIDE_BITMAP_FROM`/`UNTIL` times, skips only Kirby's original bitmap draw for a visual comparison. These are diagnostic fixtures, not normal play options.

The second recording investigation added further opt-in diagnostics. `KIRBY_LAB_CAPTURE_ALL=1` keeps `--capture-fps` capturing during host cinematics and death, and adds the hero animation, cinematic and visibility state to `captures.jsonl`. `KIRBY_LAB_REPRO_HIT_AT` and `KIRBY_LAB_REPRO_LETHAL_AT` deliver a real host Zombie attack (20 damage, or 10 damage at 3 HP) through the normal attack path at that repro time; `KIRBY_LAB_REPRO_HIT_SIDE=-1` places the 20-damage attacker on Kirby's left instead of his right. `KIRBY_LAB_DUMP_PHASES`, for example `rebirth:20,headPopLoop:5`, writes the scroller render tree near the hero when its animation reaches that frame. In `repro` runs, `repro-events.jsonl` also records host attacks entering the source and source health changes, and `repro-trace.jsonl` records the source palette flash entry and Kirby's palette bank.

The third recording investigation added more opt-in diagnostics. `repro` runs also record `host-motion.jsonl` (host position, animation and viewport zoom on every host frame), and drive the normal outfit too, without a source machine, so movement can be compared. `KIRBY_LAB_VISIBILITY=1` writes `visibility.jsonl` with the hero's visibility, sprite alpha, hero light and the body bitmap's alpha on every host frame. `KIRBY_LAB_MOB_SCALING=1` writes `mob-scaling.json` with every mob's life, elite flag, `trueLifeTier` and the host tier scale. `KIRBY_LAB_ZOOM_SCHEDULE`, for example `1:1.5u,10:2u`, sets the host viewport zoom once at each repro time (`u` also updates its size). `KIRBY_LAB_TOGGLES`, for example `30:nolight,34:none,38:nohead,46:blendnone`, switches the hero light, host head parts or the body bitmap's blend for A/B captures. `KIRBY_LAB_DUMP_PHASES` also accepts `@<lab ms>`. The render tree dump includes the scene lighting parameters and layer membership. The body-over-terrain investigation (#116) added `KIRBY_LAB_GHOSTS`, either `layers` (one copy of Kirby's body bitmap in every render layer, left to right below his feet, with `layer-census.json` listing each layer's visible children and the `DP_*` constants in `repro-events.jsonl`) or a list of `dx:dy:alpha|none[:layers above his]` copies at host-pixel offsets, and `KIRBY_LAB_TELEPORT=from:until:dx:dy`, which holds the hero at that host-pixel offset from where he was at `from` seconds (repro time) until `until`, for example inside a solid mass. The ability probe is run with `--probe-ability` source IDs: 18 Sword, 19 Hammer, 20 Parasol.

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

## Secret walls (#84)

`KIRBY_LAB_BREAKABLE_TARGET=hidden` makes the `breakable` plan spawn a real
`HiddenBlock` beside Kirby (constructor with direction 1 and a small food reward,
then `init`) and copy Sword. The probe re-places Kirby beside the wall each frame
of the attack, because the transform's Right press otherwise carries him past it.
`breakable-hits.jsonl` records each host `onDamage`; `breakable.json` counts
`HiddenBlock.onActivate` calls and lists the `ItemDrop`s within three cells of the
wall. `hidden-base` presses attack in base form instead and passes only when the
wall keeps its life and is not opened.

```sh
KIRBY_LAB_BREAKABLE_TARGET=hidden python3 scripts/launch_runtime.py \
  --data-root ~/.local/share/kirby-dead-cells/repros/hidden-block-20261008 \
  --seconds 60 --start-game --outfit KirbySuperStarUltra --input-plan breakable
```

Separate data root `repros/hidden-block-20261008`, staged with the ability-item
outfit pack (`outfit-build-55`, SHA-256 `8e44122d…`) and the matching asset pack
(`assets-final`, SHA-256 `3cdd1193…`). A lab staged with the older
`outfit-build/res.pak` fails at startup with `Invalid ItemKind`.

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261008-002608` | control: no pairing change | Copy Sword active, wall `canBeHit` true 28 px away, no proxy for the wall, no hits, no activation, life 3 |
| `20261008-002727` | pairing only (`Eligible`) | Three copy hits, life 3 → 0, not opened, no activation: the activation was missing |
| `20261008-002858` | pairing and activation | One activation, life 0; drops not yet recorded |
| `20261008-003014` | pairing and activation, drops recorded | One activation; `SmallMeat` dropped at the wall |
| `20261008-003144` | base form (`hidden-base`), final code | Base form, no activation, no damage, life 3, wall `canBeHit` true 20–27 px away |
| `20261008-003248` | final code, copy Sword | One activation; `SmallMeat` dropped beside the wall |

Each run reached its 60-second limit (`exit_code` -15, expected) with
`save_isolation_installed` true, `source_saves_unchanged` true and no fatal log.
The owner's `--play` session on the main lab was not restaged or stopped.
Not run: an R-key press beside a wall as a parity control, copy forms other than
Sword, and rewards other than food.

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

### One shield (#139)

`KIRBY_LAB_ITEM` also takes `state@seconds`: it drops nothing and records the
inventory, the retained shield and the item drops on the level. Separate data
root `repros/one-shield-139`, staged from the #139 branch; the private plan
presses the copy discard (action 5) at 1.0 s and interact (action 6) at 4.6 and
7.6 s.

```sh
KIRBY_LAB_REPRO_ABILITY=18 KIRBY_LAB_REPRO_PLAN=<plan> \
KIRBY_LAB_ITEM=state@0.5,state@3,shield@4,shield@7,state@7.5,state@9,state@12 \
python3 scripts/launch_runtime.py --data-root <root> --seconds 70 --start-game \
  --outfit KirbySuperStarUltra --input-plan repro
```

Run `20261013-203959`: Sword's item sat in slot 0; the discard left both slots
empty in base form. The first interact took a Warrior's Shield (permanent ID 277)
into slot 0, retained 277. The second interact took another shield (item ID `Shield`,
280): the host equipped it into the empty slot 1 with no replace dialog, so both
shields were equipped at that instant (the owner's report). At 9.0 s and 12.0 s
the inventory held only shield 280 in slot 1, retained 280, and the Warrior's
Shield 277 lay among the level's item drops. The run reached its time limit
without fatal log lines.

Not run: a shop purchase (it reaches the same `Inventory.equip`, but no shop was
used), a resumed save that already holds two shields, a parry with the kept
shield, and the HUD slots on screen (the run is offscreen).
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

Since #144 a pool cell is one the runtime exports as original water (a cell the water fills). The start level's pools are 18 px deep and no longer qualify, so this plan needs `KIRBY_LAB_WATER_LEVEL=SewerDepths` (pools 42 px and deeper in the runs so far); `water.json` also lists every water column's depth (`depths`). Two such runs on 2026-10-13 (`20261013-213902`, `214040`, `repros/water-20261013`) found the same site, a 42 px pool whose bank is three cells above its floor: the Zombie stayed in the exported water and was never source-controlled in the 120 frames before input, and its life did not change, but the inhale timed out with the Zombie out of reach below the bank. Consumption of an enemy in exported water was not repeated live on the new rule.

Every run kept the real saves unchanged and logged no fatal errors. Natural enemies near the pool hurt Kirby in some runs; the probe does not control them. Sword or other attacks on an enemy in water, and a walkway-over-pool site with the fixed bridge, were checked only in the native suite (`proxy_water_sword`, `proxy_water_below`), not live.

## Shallow water (#144)

With `KIRBY_LAB_WATER_WADE=1` the `water` plan puts Kirby on the floor in the middle of the widest stretch of water (exported water if the level has any, otherwise host water of any depth) and holds nothing for 60 source frames, right for 60, left for 60, A for 12, nothing until 300 and right until 360. `water.json` records `depths` (host water cells, how many are exported as original water, and the columns by depth in pixels from the surface to the first solid cell below), `wadeSite`, `wadeActions` (the original's action codes counted in each step, two readings a source frame) and `wadeOrder` (each change of action with its frame; `0815` is swimming). The plan is short because a boss wakes when Kirby appears and attacks about eight seconds later; a death sets `wadeInterrupted` and ends it. A new run in a late level starts with 76 health and no scrolls, and one hit killed Kirby in the cut runs listed below.

```sh
python3 scripts/check.py   # builds; staging copies the existing build
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/water-20261013 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
KIRBY_LAB_WATER_WADE=1 KIRBY_LAB_WATER_LEVEL=SwampHeart python3 scripts/launch_runtime.py \
  --data-root ~/.local/share/kirby-dead-cells/repros/water-20261013 \
  --seconds 45 --start-game --outfit KirbySuperStarUltra --input-plan water --capture-fps 5
```

All runs on 2026-10-13, DCCM 35.13.6, the shared oracle bridge (not changed by this issue).

| Run | Rule | Level, depth at Kirby | Actions in order (frame and code) |
| :--- | :--- | :--- | :--- |
| `20261013-212620` | old (any surface) | `SwampHeart`, 12 px | 0 `0406`, 1 `0815`, 3 `0406`, 61 `060B`, 181 `0815`, 189 `070D`, 221 `070E`, 226 `0815`, 262 `0406`, 301 `060B` |
| `20261013-212720` | new | `SwampHeart`, 12 px | 0 `0406`, 61 `060B`, 63 `060C`, 98 `0509` (the arena's thorn wall), 104 `0406`, 121 `060B`, 123 `060C`, 181 `070D`, 213 `070E`, 216 `0507`, 221 `0406`, 301 `060B`, 303 `060C` |
| `20261013-213551` | new, final probe | `SwampHeart`, 12 px | the same; jump at 183, landing at 216 |
| `20261013-213320` | new | `Shipwreck`, 18 px | 0 `0406`, 62 `060B`, 63 `060C`, 122 `0406`, 123 `060C`, 181 `070D`, 213 `070E`, 215 `0507`, 222 `0406`, 301 `060B`, 303 `060C` |
| `20261013-213650` | new, final probe | `Greenhouse`, 18 px | 0 `0406`, 62 `060B`, 65 `060C`, 99 `0509`, 105 `0406`, 121 `060B`, 123 `060C`, 182 `070D`, 214 `070E`, 217 `0507`, 223 `0406`, 301 `060B`, 303 `060C` |
| `20261013-213017` | new | `SewerShort`, 18 px, toxic | no `0815`; hurt recoil `0F1D` at 18, 112, 211 and 330 between standing, dashing and the jump at 181 |
| `20261013-213406` | new | `Greenhouse`, 42 px (exported) | 0 `0406`, 1 `0815`, 6 `0406`, 65 `060B`, 181 `0815`, 190 `070D`, 222 `070E`, 226 `0815`, 262 `0406`, 301 `060B` |
| `20261013-213103` | new | `SewerDepths`, 114 px (exported), toxic | 0 `0406`, 61 `060B`, 181 `0815`, then `0815` between hurt recoils to the end |

In all of them `source_saves_unchanged` is true and no fatal log was written. On the new rule the arena's 82 water cells and the start level's 5 (run `20261013-212840`) export none. On the old rule Kirby walked on the arena floor at the original's walk (`060B`); on the new one the held direction dashes (`060C`) as on dry floor. The runs before `213551` used the final rule with an earlier probe (the level variable alone chose wading, and a death did not stop it); a `Swamp` run of that probe (`20261013-213149`, 18 px) was cut by a death at frame 158, before the jump, and is not counted. Two earlier baseline runs with a longer plan (`20261013-212122`, `212246`) ended in the boss's first hit. Not run: the fight beyond those seconds, copy abilities or the helper in shallow water, a pool between 18 and 42 px deep, the owner's play launcher.

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
groups, a level saved before this fix, a shop short of gold and mimics.

#### Legendary altars (#107)

The `rewards` plan also creates a `LegendaryAltar` holding a Lightning Whip
with the `Legendary` affix, six cells right of Kirby, and activates it as a
short press does. `rewards.json` gains `legendary` in `spawned`, the altar's
item and floor count after the press, and `legendaryAffix`; `passed` now also
needs the altar's item to be a Legendary ability item, the altar empty and one
floor ability item.

`20261010-145915` (separate root `repros/legendary-altar-20261010`, staged from
this branch with `outfit-build-55` SHA-256 `8e44122d…` and `assets-final`
SHA-256 `1bc2d0b7…`): the Legendary Lightning Whip became `KirbyAbilityBomb`
with `Legendary`, the altar's item was null after the press and one
`KirbyAbilityBomb` floor item lay at its cell, alongside the altar, timed item
and booth results above (1000 gold taken). `passed: true`, saves unchanged, no
fatal log. The captured frame shows only copy-ability icons, the Bomb with a gold
glow. Not run: a legendary altar the level generator places (the owner's report
was Spartan Sandals IV-L), taking one with the real Interact press, a level saved
before this fix, and the locked refusal.
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


## Conjunctivius tentacles (#86)

`--input-plan tentacle` (`LabTentacleProbe`) starts a new run in the real
`BeholderPit`. Once its Beholder exists, Kirby copies Sword (original inhale,
supplied Sword content, Down). The boss's own `createTentacles(3, false)` then
makes a wave of three real `BeholderTtcl`, which keep their natural AI. Kirby
walks to the nearest living tentacle and swings every 16 frames, also while it
is burrowed. Incoming damage to Kirby is cancelled. Every host frame appends each
tentacle's life, `_isInGround`, `canBeHitBy(hero)`, pairing and source control
to `tentacle-trace.jsonl`; burrow and surface events go to
`tentacle-events.jsonl`, and `tentacle.json` holds the result. The probe passes
when all three die to copy hits, at least one burrowed, and no tentacle stays
paired for more than one host frame while the host refuses the hero's hits.

Separate data root `repros/conjunctivius-20261008` with an empty lab save, the
play build's outfit pack (`8e44122d…be7af`) and the shared bridge
(`f4f30c93…6cb49`; this branch does not change `src/nds`):

```sh
python3 scripts/launch_runtime.py \
  --data-root "$HOME/.local/share/kirby-dead-cells/repros/conjunctivius-20261008" \
  --seconds 150 --start-game --outfit KirbySuperStarUltra --input-plan tentacle --capture-fps 5
```

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261008-004843` | Before the fix (`664cde5f…c5044`) | Burrowed tentacles stayed paired for 317 tentacle-frames, up to 165 in a row, and for 47 of them the original recoil controlled a burrowed tentacle. All three still surfaced and died within 1469 frames. `passed: false` |
| `20261008-004529` | Fixed (`7a7f07d0…c610f9`) | 3 of 3 killed in 1139 frames, 18 hits for 3804 damage, 3 burrows and 6 surfacings. Never paired or controlled while burrowed; every life loss happened while surfaced. `passed: true` |

Both runs ended at the expected timeout with no fatal log, and the real saves
were unchanged. The fight in the owner's recording, where a tentacle never
surfaced again, was not reproduced: the run before the fix shows the
mechanism (pairing and original control while burrowed) but not the permanent
stall. Not run live: the boss's own tentacle phases from damage thresholds, a
full Conjunctivius fight, other copy abilities, and other burrowing or hiding
enemies.

Four earlier attempts in this root (exit 255, fatal log, saves unchanged)
were staged with the default `outfit-build/res.pak` (Oct 4, `6c141ee3…`), which
predates the ability items. With a copy of the main lab save, the `tentacle`,
`boss` and `movement` plans all failed while starting the new game with `Null
access .commonProps`. With an empty save, copying Sword failed with `Invalid
ItemKind: KirbyAbilitySword`. The copied save was not retried with the current
pack. Stage the current outfit build instead.

## Copy-attack critical hits (2026-10-08)

Issue #88. Both runs used `python3 scripts/launch_runtime.py --data-root
~/.local/share/kirby-dead-cells/repros/crit-88-20261008 --seconds 90 --start-game
--outfit KirbySuperStarUltra --input-plan ability --probe-ability 18` (original
Sword on a real Zombie) with the staged `config.json` edited only in that root.
`ability-damage-events.jsonl` now also records the host `hitResult`.

- Run `20261008-020113`, `CopyCriticalChance` 1: both Sword hits were host `Critical`, 94 damage for
  original 18 and 26 for original 5 (18 × 3.5 × 1.5 = 94.5 and 5 × 3.5 × 1.5 =
  26.25, truncated). The second hit took the Zombie from 23 to dead in the same
  tick as its source enemy. No fatal log, real saves unchanged.
- Run `20261008-020259`, `CopyCriticalChance` 0: hits were `Normal` at 63 and 52, the previous damage.

Not run: a partial chance, a sure crit, bosses, Ice, spit stars, screen attacks.

### Cooldown mutations (2026-10-08)

Issue #89. `--probe-mutations ID,ID` (`KIRBY_LAB_TEST_MUTATIONS`, with `--input-plan
ability`) gives the hero those mutation items through the normal inventory and
writes the host's description of each, the owned set, and every change of the
crit chance (`critEvents`) to `ability-damage.json`.

- `20261008-020831`: all five cooldown mutations. `ownedMutations` listed all five;
  the host showed the new texts; the hits ran with +10% chance (Master of Arms).
- `20261008-021023`: Killer Instinct and Hunter's Instinct. The copy kill at source
  frame 4376 gave +5% and it ended at 4737, 361 frames (6 s) later. Hunter's
  Instinct gave nothing because the probe copies the ability before it gives the
  mutation, so that trigger is not exercised live.

## Mutations on copy attacks (2026-10-08)

Issue #91. `--probe-affects index:seconds[:value],...` (`KIRBY_LAB_TEST_AFFECTS`,
with `--probe-mutations`) sets host hero status effects that a mutation reads, so a
mutation can be checked without earning the effect in play. `ability-damage.json`
now records each copy hit's `bonusFactor`.

- `20261008-022631`: `P_DmgRevenge,P_NoMobAround,P_DmgKill` with effects 57 and 54.
  Factor 1.87, hit 117 (63 without), then 1.891 and a kill in the same tick.
- `20261008-022811`: `P_Bleed,P_DmgFirstHit,P_InvisibilityOnKill,P_DmgNearRanged,P_DeathShield`.
  Texts rewritten ("Copy attacks inflict bleeding", "Copy attacks at close range
  inflict 30% bonus damage", and so on), Point Blank factor 1.31 (hit 82), an
  Initiative extra hit of 93, a new status effect (89) at the hit.
- `20261008-023052`: `P_DmgParry` with effect 108 = 368. An extra hit of 368.

No fatal log; real saves unchanged. The effects were set by the fixture.
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


## Collector filter (2026-10-08)

Issue #99. `--probe-collector [filtered|unfiltered]` (`KIRBY_LAB_TEST_COLLECTOR`,
with `--input-plan ability`) starts the run in `T_Courtyard`, unlocks Health
Flask I, reveals nine hidden entries (relocked) and five kept ones in the private
profile, records the unlock count and the Random Starter Shield gate with and
without the Kirby count, opens the level's own `CollectorPanelNpc` and writes
`collector.json` when the panel has built its list. All runs used the separate
data root `repros/collector-99-20261008` with the outfit pack
`outfit-build-sync/res.pak`; final runtime DLL
`43bf04ef2622871810ba5289e435251e9ff196b260db7ca749c3cce765f16886`:

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/collector-99-20261008 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/collector-99-20261008 \
  --seconds 45 --start-game --outfit KirbySuperStarUltra --input-plan ability --probe-collector
```

`20261008-195344` (filtered): 43 entries tested, none of the nine hidden listed,
all five kept listed; count 1 (host) and 10 (Kirby); the shield gate closed with
the host count and open with the Kirby count. `20261008-195429` (unfiltered):
all fourteen listed, same counts. Both ended at the time limit with no fatal log
and real saves unchanged. Earlier runs: `20261008-194610` built a Collector NPC
outside a level and failed (`Null access .lib`); `20261008-194731` opened the
real panel but wrote no file because the panel pauses the level;
`20261008-194902`/`194947` were the same pair before the gate check (count 0 and
9, same lists), and `20261008-195058`/`195152` matched the final pair on a build
that differed only in the probe's layout.

## Healing without the flask (2026-10-08)

Issue #101. `--input-plan healing` (`LabHealingProbe`) unlocks Health Flask I
through the host's own unlock path if the profile lacks it, hurts Kirby with two
real host Zombie attacks, holds the real Heal binding (action 7) for 90 frames,
then builds a real `Fountain` at Kirby's cell and presses the real Interact
binding (action 6) once a second while Kirby is focused on it. It writes
`healing.json` and `healing-events.txt`. Separate data root
`repros/healing-101-20261008` (a copy of `repros/collector-99-20261008`), outfit
pack `outfit-build-sync/res.pak`, runtime DLL
`6048c94f10576df329996a0e1c396fa9273ac705483d19c62ccc71660e760ea5`:

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/healing-101-20261008 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/healing-101-20261008 \
  --seconds 55 --start-game --outfit KirbySuperStarUltra --input-plan healing --capture-fps 1
```

`20261008-221759`: life 76 → 16 after the hits; F held, no `HealFlask.onStart`,
still 16; fountain heal asked 76, gave 38, life and source 54; fountain used. The
HUD capture after the flask unlock has no F slot. In the same root, the Collector
probe (`--probe-collector`, now also relocking Health Flask I, Flask II and
Extended Healing, with Gold Reserves I kept) ran as `20261008-221942`, and the
mutation menu pair as `20261008-222204` (unfiltered) and `20261008-222245`
(filtered). Every run ended at its time limit with no fatal log and the source
saves unchanged. Earlier runs `20261008-220743` to `221046` used a stale build
(staging does not rebuild) and pressed Interact on the fountain's first frame, so
it never activated; `221150` and `221251` were the same check before the second
hit and the flask unlock were added.

## F slot (2026-10-09)

Issue #102. `--input-plan fslot` (`LabFSlotProbe`) runs the whole F slot through
real host input: base Kirby holds the inhale for 70 frames beside a floor Crash
item, gets Sword through the original inhale, supplied content and DOWN, then
takes items with the real Interact binding (action 6, R) and fires them with the
real Heal binding (action 7, F). It takes Crash and fires it beside a real
Zombie; takes Mike and fires its three shouts; walks away from the start room's
own floor items, takes Paint, then Cook (Paint must drop back); fires Cook at a
Zombie and fires the returned Paint; takes Mike, writes a normal host save
(`Main.writeSave`), and finally presses F and lands a real host Zombie hit while
the switch loads. With `--resume-game` it records the restored slot and fires it
once. It writes `fslot.json` and `fslot-events.txt`. Separate data root
`repros/fslot-102-20261009` (a copy of `repros/healing-101-20261008`), outfit pack
`outfit-build-sync/res.pak`, runtime DLL
`240d134b2c13350f3cc752c038c8c18993c88696aad5ac77023afa8019aaa334`, bridge
`17420bdc68772f8ea7c442daf91498f1a92ce13108b83a64ce3fefeb8b8d0d0c` (built by
`tools/nds_build.py` with `tools/nds_call_patch.py`):

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/fslot-102-20261009 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/fslot-102-20261009 \
  --seconds 170 --start-game --outfit KirbySuperStarUltra --input-plan fslot --capture-fps 2
python3 scripts/launch_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/fslot-102-20261009 \
  --seconds 45 --resume-game --outfit KirbySuperStarUltra --input-plan fslot
```

`20261009-010357`: the Crash item stayed on the floor through the inhale (never
paired with a proxy). R took Crash (slot Crash, 1 use, floor item gone, main copy
Sword). F fired it: the 119-life Zombie died, Sword came back, the slot emptied.
Mike's three shouts each came back to Sword and emptied the slot after the third.
Taking Cook over Paint returned `replaced: Paint` and put Paint on the floor
beside Kirby. Cook fired from F consumed a Zombie and dropped one host food item;
the returned Paint fired; Sword came back each time. Mike was stored and the save
written. The hit sent while Mike's switch loaded did no damage (76 → 76), so the
lost-press retry was not exercised live; the shout fired and Sword came back.
`20261009-010718` (resume): the slot came back as Mike, 3 uses, its item restored,
main copy Sword; F fired one shout (2 left) and Sword came back. Both ended at the
time limit with no fatal log and source saves unchanged. Captures from the earlier
`20261009-003927` show the HUD F slot with Crash's item icon, empty after it, then
Mike's icon with 3 and 2. Earlier runs of the same probe (`004146` to `005835`)
failed only in probe phases: duplicated spawns (the test input runs twice per host
frame), a focus on other floor items, and a stale Interact press.
`tools/nds_tests.py` passed all 78 cases, including `f_slot` and `f_slot_save`.

## F slot and fountain (2026-10-10)

Issue #109. `--input-plan healing` (`LabHealingProbe`) now owns Health Flask I
from the start of the run (a `hasUnlockedItem` hook, as a profile that already has
it), stores Mike in the F slot, and logs the slot's rules state and the HUD slot's
visibility and icon tile each time they change (`healing-events.txt`, and `slot`
in `healing.json`). It also logs `HUD.setHealings`, `HUD.refreshItems` and the
HUD slot's `setIcon` calls, and calls the host's flask refill
(`Heal.onFountainRefill`) and `HUD.refreshItems` itself. Two switches add
`KIRBY_LAB_HEALING_SWORD=1` (Sword through the original inhale first, so the HUD
overlay is active) and `KIRBY_LAB_HEALING_EXIT=1` (take the level's real exit
150 frames after the heal). Data root `repros/fountain-109-20261010` (a copy of
`repros/fslot-102-20261009`), outfit pack `outfit-build-sync/res.pak`:

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/fountain-109-20261010 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
KIRBY_LAB_HEALING_SWORD=1 KIRBY_LAB_HEALING_EXIT=1 python3 scripts/launch_runtime.py \
  --data-root ~/.local/share/kirby-dead-cells/repros/fountain-109-20261010 \
  --seconds 100 --start-game --outfit KirbySuperStarUltra --input-plan healing --capture-fps 1
```

Results (every run ended at its time limit with no fatal log):

- `20261010-151727`, `152037`: base Kirby with Mike stored; the fountain heal
  (16 → 54) left `stored=Mike uses=2`, the HUD slot visible and its icon tile
  unchanged (flask unlocked mid-run, then owned from the start).
- `20261010-152400`: the same after the host flask refill (`HUD.setHealings(1,1)`,
  `setIcon`); the slot was redrawn in the same frame.
- `20261010-152558` (Sword) and `20261010-152847` (Sword plus a probe
  `HUD.refreshItems`): slot, count and icon intact before and after the heal; the
  capture shows the Mike icon with count 2 at 15/76 and at 53/76.
- `20261010-153057` (Sword, exit, before the fix): after the exit to
  `T_Courtyard` the rules held `Mike` × 2 but the HUD slot was not visible, and
  stayed so for the rest of the run (capture `frame-004097`).
- `20261010-153428` (same, after the fix): after the exit the slot is visible with
  a redrawn icon and the capture shows Mike with count 2 in the new level.

Not run: a fountain placed by the level generator, other stored abilities after a
level change, Collector or merchant HUD refreshes, and normal play with
`./play-kirby`.

## Carnivorous plants (#113)

`LabMovesProbe` has a `plant` scenario for `KIRBY_LAB_MOVES_SETUP`: it takes the
level's first `CarnivorousPlant` (else builds one on open floor) and drops Kirby
onto it. `moves-events.jsonl` logs every `CarnivorousPlant.onTouch` for the hero
(his cell, foot, `dy` and body as `onTouch` sees them, `heroWasInside`,
`announced`), `bumpEntity`, `open` and `close`. Data root
`repros/plant-20261010` (a copy of `repros/elevator-20261006`), outfit pack
`outfit-build-sync/res.pak`. `plan-empty.json` in the data root is `[]`; the lab
reads the plan from inside its sandbox, so it must live there.

```sh
python3 scripts/stage_runtime.py --data-root ~/.local/share/kirby-dead-cells/repros/plant-20261010 \
  --original-runtime --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
KIRBY_LAB_REPRO_PLAN=$HOME/.local/share/kirby-dead-cells/repros/plant-20261010/plan-empty.json \
KIRBY_LAB_MOVES_SETUP=plant@2.5 python3 scripts/launch_runtime.py \
  --data-root ~/.local/share/kirby-dead-cells/repros/plant-20261010 \
  --seconds 40 --start-game --outfit KirbySuperStarUltra --input-plan repro --capture-fps 10
```

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261010-182526` | control: `onTouch` hook removed | 40 logged touches, each with the body projected (`yr` 0.625, radius 6, height 12); `announced` never true, no `close`, no `bump`, no plant damage |
| `20261010-182428` | #113 | Touch with the foot at `yr` 1, `announced` true from the 8th touch, `close` and `bump` fired; two host hits from `CarnivorousPlant` (dmg 22 each), Kirby 76 → 59 → 42 |

Both ran to their time limit with no fatal log and unchanged real saves. The two
runs generated different levels, so the plants differ. A first attempt with the
older `outfit-build/res.pak` died at start on `Invalid ItemKind:
KirbyAbilityYoYo`; use `outfit-build-sync`.

Not run: a plant under a copied attack, Kirby walking onto it, Gardener-boss
plants, and normal play with `./play-kirby`.

#132 (the throw after the bite), same command with a fresh data root
`repros/plant-20261013` (built as in the recipe: `research`, `oracle`, `rom`
and `dotnet` linked from the private data root, then staged). `bump-launch`
is logged in `repro-events.jsonl`, the positions in `source-trace.jsonl`.

| Run | Build | Result |
| :--- | :--- | :--- |
| `20261013-124519` | #132 | `plant-close` f4384, bite 76 → 59; `plant-bump` and `bump-launch` (1.5 cells a step) f4414 from standing: feet 2328 → 2212.5 px at f4428 (115.5 px up), back near the plant at f4452 where a second bump and launch from a full-speed fall carried him 58 px; landed f4482; `plant-open`, second close f4546, 59 → 42. Captures show Kirby above the closed plant |

Ran to its time limit, no fatal log, real saves unchanged. No control run
without the change was made this time; the owner's screencast and the #113 run
(bump fired, Kirby stayed down) are the before state. Not run: a ceiling above
the plant, input during the launch, the Beheaded on the same plant.

#138 (the throw against the Beheaded's). `KIRBY_LAB_MOVES_SETUP` takes
`plant:<px>` to drop the hero that many pixels to the side of the plant's
centre, and the `placed` event logs both bodies (`plantBody`, `heroBody`).
`KIRBY_LAB_MOVES_ANY=1` lets the probe place the Beheaded, in a run without
`--outfit`. Use a fresh data root for him: a root that has run Kirby keeps the
outfit in its save. Jump presses come from the plan (`"actions": [0]`). A Kirby
run boots for about 27 s, so `--seconds` is 27 plus the lab time wanted.
`stage_runtime.py` copies the existing build: run `scripts/check.py` first.

```sh
R=~/.local/share/kirby-dead-cells/repros/plant-138   # Kirby; plant-138-beheaded for the Beheaded
python3 scripts/stage_runtime.py --data-root $R --original-runtime \
  --outfit-pack "$HOME/.local/share/kirby-dead-cells/outfit-build-sync/res.pak"
KIRBY_LAB_REPRO_PLAN=$R/plan-empty.json KIRBY_LAB_MOVES_SETUP=plant@8.5,plant:30@16.5 \
  python3 scripts/launch_runtime.py --data-root $R --seconds 51 --start-game \
  --outfit KirbySuperStarUltra --input-plan repro --capture-fps 10
# The Beheaded: KIRBY_LAB_MOVES_ANY=1, no --outfit, --data-root .../plant-138-beheaded
```

Each level is generated, so plants and coordinates differ between runs; heights
are given from the plant's surface. All plants were hanging ones
(`placedOnGround` false).

| Run | Hero, build | Setup | Result |
| :--- | :--- | :--- | :--- |
| `plant-138/20261013-202552` | Beheaded, mod inactive | centre, no input; then jump at 15.0 and 15.45 | Per-step rise after `plant-bump` 34.44, 30.88, 27.58 … 0.38 px: 242.2 px in 17 steps. Bite hop 34.4 px. Close 0.5–0.85 s after landing, bump 0.5 s after close, open 1.25 s after close, landed about 2.0 s after close. Jump off: plant shut about 26 px up, no bite; air jump 12 px above it gave two `plant-bump`s in one step and a 262 px rise |
| `plant-138-beheaded/20261013-203833` | Beheaded, mod inactive | `plant:13`, `plant:26`, `plant:34` | Bitten and thrown at each offset, his x unchanged by the bite. At 34 px the touch came with his feet between 3.3 and 14.5 px below the surface; while the plant was shut he fell through it |
| `plant-138/20261013-203206` | Kirby, #138 with the host's steps only | centre | 231.3 px: the original's gravity still shortened each step, before the height was taken from the host during a launch |
| `plant-138/20261013-204351` | Kirby, #138 before the recoil hold | centre, 13, 26 | Centre: hop 34 px, throw 901.2 → 659.0 (242.2 px). At 13 and 26 px the hurt recoil carried him 26 px sideways in 0.5 s, he was not thrown and fell through the shut plant |
| `plant-138/20261013-205316` | Kirby, with the recoil hold, body from the saved host body | `plant:34`, `plant:-30` | −30: bitten and thrown. 34: not thrown; the `placed` event shows the saved body was Kirby's (9, 29), not the Beheaded's |
| `plant-138/20261013-205759` | Kirby, body fixed at 16.8 / 54, before the landing-hop change | centre, 13 | Bite cycles alternate with cycles where the plant shuts with Kirby 22.6 px up in his landing hop, no bite, then throws him |
| `plant-138/20261013-205955` | Kirby, #138 final | centre, `plant:30` | Every cycle bite then throw: closes f4754, f4897, f5040 (2.38 s apart), throws 29–33 frames after each close; at 30 px likewise, x unchanged |
| `plant-138/20261013-205511` | Kirby, before the landing-hop change | `plant:34`, `plant:-30` | 34: bitten, hop, thrown with feet 8.9 px below the surface (within the Beheaded's bracket); x unchanged |
| `plant-138/20261013-210402` | Kirby, #138 final | centre, jump at 9.6 and 10.08 | Jumped off after landing: plant shut with Kirby 26.8 px up, no bite; the second press 5 px above the shut plant gave two `plant-bump`s in one step, no float, rise 738.9 → 496.7 (242.2 px) |
| `plant-138/20261013-205136` | Kirby, before the last three changes | centre, jump at 9.6 and 10.05 | The second press 28 px above the shut plant (beyond touch) threw him 242.2 px; the original stayed in its jump action (no float) |
| `plant-138/20261013-210105`, `-210308` | Kirby, #138 final | same, other timings | The press came 47 px above the plant, out of the host's reach: an ordinary float, no throw |

Every run ended at its time limit or the hero's death, `fatal_log` false and
`source_saves_unchanged` true in `report.json`. Kirby has 76 HP and loses 17 a
bite, so a run holds four bites. The plan's times are wall-clock and drift a few
frames against the original's frames, which is why the air-press runs differ.
Not run: a ceiling above the plant, a `placedOnGround` plant, Gardener-boss
plants, a float or a copy ability on a plant, walking onto one, normal play.
