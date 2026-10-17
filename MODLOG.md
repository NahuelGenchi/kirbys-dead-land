# Kirby Super Star Ultra → Dead Cells

New, independent mod requested 2026-10-03 (America/Argentina/Buenos_Aires).

## Required behavior

- Kirby's original Super Star Ultra sprites, animation timing, physics, movement, and ability movesets.
- Absorbing a Dead Cells enemy grants a random copy ability (the user's “evolution”).
- Base Kirby can use swords, bows, and shields.
- Kirby with a copy ability attacks using that ability, while retaining his equipped shield for parrying.
- A sprite replacement alone does not satisfy this request. Approximate movement must not be labeled original NDS physics.

## Recon and boundaries

- Universal Modder local checkout: `/home/rayzza/Games/mods/universal-modder`.
- Read and followed mod-any-game, game-recon, mashup-mods, reverse-engineering workflows.
- `um kb search 'Dead Cells'`: no field note in the toolkit KB. Existing Sonic project has separate useful local findings.
- `um scan 'Dead Cells'`: Steam app 588650, no anti-cheat or loader detected. Scanner's unknown-native label is incomplete: installation uses HashLink `hlboot.dat`.
- Installed host: `/home/rayzza/.local/share/Steam/steamapps/common/Dead Cells`, Linux Steam build 23762174, v35.
- Existing Sonic/Knuckles asset mod is already present. Do not overwrite its archive, source, or saves.
- User ROM: `/home/rayzza/Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds`, 128 MiB.
- Private working data, dependencies and decoded ROM assets: `/home/rayzza/.local/share/kirby-dead-cells/` (outside source project).
- No installed-game mutation, save restoration, input automation, publication, or loader installation performed.

## Chosen route under verification

ROM converter + Dead Cells Core Modding runtime plugin, with an isolated launch environment. DCCM advertises Linux x64 and .NET 10 support. Validate the current game bytecode and Linux runtime before claiming gameplay support. Original NDS behavior requires an independent source/oracle; extracting pictures is not equivalent to porting gameplay code.

## Parallel investigations

- ROM NitroFS/archive decoder and authentic sprite composition.
- DCCM Linux support, exact hooks, and compiled plugin.
- Original Kirby gameplay code/physics source and fidelity verification.

Status: development, not installed or playable yet.

## ROM extraction and host asset conversion

- Exact user ROM SHA256: `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`.
- Decoder: `tools/extract_kirby.py`; findings in `docs/ROM_FORMAT.md`.
- Read 1,886 named NitroFS files; decoded all 61 PD sprite archives, 20,224
  group-indexed poses, both original facing layouts. Base normal Kirby has 491
  complete poses. Same-index body/overlay groups give 630 composed ability poses.
- Shared palette parts remain explicitly unresolved; no guessed colors, timings
  or generated character artwork used.
- Repaired long-effect clipping by deriving diagnostic cell and composition bounds.
- Host converter: `tools/build_assets.py`, guarded generated-output ownership,
  fresh staging, and lossless palette encoding. It builds pose atlases, not an outfit.
- Final private pack: `/home/rayzza/.local/share/kirby-dead-cells/assets-final/res.pak`.
  SHA256 `1bc2d0b700e85167e3d905854f6c0c320b933622cb3ce0f47ce5e9df8ddd39bf`.
- Packed 36,894 complete individual/left-facing/composed poses; 4,814 incomplete
  or empty entries omitted with explicit reasons. These counts include helpers
  and alternate colors, not 36,894 distinct Kirby moves.
- Exhaustive atlas checks preserve every visible source pixel, alpha and pivot.
- Released DCCM `GameRes.Core` independently verified 331 entries, 24,126,968
  payload bytes, all SHA256/Adler checks and no empty files.
- Upstream CLI unpacker has an unawaited asynchronous-write bug; successful exit
  left six files empty. Used the synchronous official archive API to verify instead.

## Runtime and user rules

- Chosen framework: DCCM 35.13.6 Linux x64, source commit
  `31f597bc462e6767f28a62e14ed36d65721e6d46`, .NET SDK 10.0.401.
- `src/core/KirbyRules.cs`: requested random-copy and equipment restrictions,
  26 assertions passing. No physics or original ability code is represented here.
- `src/runtime`: actual released hook signatures compile with zero warnings/errors.
  No gameplay provider exists yet, so hooks pass through without changing gameplay.
- `scripts/setup_runtime.py` sets up private pinned dependencies; `stage_runtime.py`
  stages a dormant plugin and asset pack in a separate lab. Both leave installed
  game files untouched. The optional Steam emulator is excluded and disabled.
- Loader disables achievements; do not assume previous Sonic-mod behavior applies.
- No original-controller implementation, outfit registration, original timed
  animation mapping, working absorption or full ability movesets are supplied.
- No Dead Cells launch or gameplay verification occurred. Original Steam/cloud
  save isolation must still be proven before any future lab launch.

## Original-game oracle

- `tools/oracle_snapshot.py` parses private RAM captures with identity/bounds checks.
- Built and booted the exact user ROM with pinned melon-rs and FreeBIOS, using a
  new private emulated save and virtual DS inputs only. No desktop input or existing
  save was accessed. Reached real Green Greens; measured the candidate player
  entity's response to virtual movement. Detailed trace provenance and limits
  are recorded in `docs/GAMEPLAY_FIDELITY.md`.
- Repeated the 191-frame idle/walk/release/A-jump/land sequence twice from the same
  isolated state. Traces, six top-screen captures and final RAM were byte-identical;
  measured-only results and source hashes are in `docs/ORACLE_RESULTS.md`.
- Emulation is a working observation source. It is not connected to Dead Cells:
  host terrain/collision, enemy lifecycle and character-only rendering bridges
  do not exist yet.

## Reproducible verification

`python3 scripts/check.py --assets /home/rayzza/.local/share/kirby-dead-cells/assets-final`
passed all offline checks: 15 decoder tests, 6 RAM-parser tests, 26 rule assertions,
clean runtime compilation, exhaustive atlas checks, and official-reader PAK checks.
`um kb check FIELD_NOTE.md` passed. `um publish check` found no failures, only local
absolute-path documentation warnings. Nothing was published.

Task outcome remains incomplete: this is a reproducible development foundation,
not the requested playable, original-gameplay Kirby mod.

## 2026-10-04 — original controller running in Dead Cells

The foundation status above is historical. The original ROM now executes inside
the Linux DCCM process through `libkssu_bridge.so`, with a live player-only OAM
renderer and host terrain/input bridge. The new `KirbySuperStarUltra` outfit is
registered separately from the user's existing Sonic outfit.

- Source controller advances at the original `33513982 / 560190` frames per second.
  No reconstructed movement constants or substitute copy movesets are used.
- Native world rebasing preserved position, velocity and pose across a 224-frame
  movement/jump/flight comparison. Original terrain essences were independently
  tested for solid, one-way, both slope directions, and water/swimming.
- Actual isolated Dead Cells run `20261004-001810` boots, enters Prisoners' Quarters,
  renders the correct outfit, walks, and jumps onto a platform. Host feet exactly
  on a floor require `cy - 1, yr = 1`; using `yr = 0` invoked wall-rescue teleports.
- The speedrun clock `isGameTimeRunning()` is not the simulation pause flag: it is
  false in safe rooms. The bridge now uses the actual game/level pause properties.
- Exact runtime OBJ compositor passed 29 source-render comparisons: idle, all 24
  abilities, and four left-facing sequences; 3,068,161 compared pixels matched.
  This does not verify all moves or every background/window effect.
- New private outfit pack SHA256:
  `7c42d5d08a597fa87f8b0896c6ee2e2c04d73ea69b148f77ddf9bb50235ec030`.
- Real source enemy spawn slots now support original suction and capture. A source
  Waddle Dee proxy enters original inhale, travels for about 22 frames, disappears
  into Kirby's mouth, and can give an original Sword transformation after Down.
  Host pairing and damage translation are being exercised in the live lab.
- DCCM sidecar support now captures compressed complete machine state plus the
  chosen ability and retained shield. Save/reload verification is still pending.
- Installed game files, ROM and original Steam saves remain unchanged. Lab file
  access and Steam Cloud save calls are redirected; the launcher backs up and
  hashes original saves around actual runs. Steam ownership checks remain intact.

Remaining verification includes host enemy damage and capture, boss hit behavior,
parries, incoming damage, audio, slot reloads and transitions. Do not interpret
the all-ability rendering tests as full gameplay acceptance.

## 2026-10-04 — combat, persistence and source effects

- Actual game run `20261004-004556` consumed a live Zombie into the original
  mouthful, transformed to random Sword, retained physical shield264, and recorded
  a successful real shield counter while copied. Fresh process `004720` resumed
  the exact saved sourceframe4816 and copy/shield state before advancing4817.
- Incoming host attacks now queue for the next original collision tick. Original
  guard, recoil, copy loss and immunity resolve there; source and host HP agree.
  Full original health is76, not the previous host100 assumption.
- Scoped outgoing damage correction removed the host15% multiplier. Run `005646`
  recorded Wheel23/14/23, identical raw/final/inflicted damage and enemy HP deltas.
- Enemy source physics uses a foot17px below its center, independently of host
  enemy height; source hurtbox top17-height maps the complete host shape.
- Suplex sentinel reset now also resets its original visible clone; regression
  observes exactly50 then2 damage, not repeated50 everyframe. Cook uses its
  original captured/gone flags and creates source food without awarding a copy.
- Original ladder essence0x0E is supported: source Up climbs153.5→84.125,
  release holds, Down returns153.5. Wheel helper creation/riding executes in ROM.
- Multiple-inhale fix: host form remains base while mouthful. Per-enemy random
  copy metadata goes into the original collision group before ingestion, so
  original aggregate/Mix logic survives. Completed captures no longer overwrite
  that aggregate. Native two-enemy test reached original roulette and selected4.
- Boss resistance0x53 blocks source inhale, Suplex and Cook but preserves exact
  original Sword18+16 timing. Host bosses keep their own AI, phase and position.
  The earlier approximate owner/slot boss damage fallback has been removed.
- Scoped host hurtbox projection reads native slot14. Actual run `011928` observed
  standing12×12 and crouch12×6, 1,114 trace samples with every projection restored,
  stable feet and restored host29px body. Original fixed-step position now remains
  authoritative despite the host's Beheaded body-width updates.
- Source renderer/audio and background capture use pinned original machine data.
  All24 forms×300frames matched1,061,683,200 RGB channel comparisons for background
  decoding. Crash's circle is OBJ; its full flash is a separate actual palette
  change. Paint/Mike use measured source transforms. Host RGB blend precision
  differs from source RGB5 quantization and is documented.
- Actual graphics layer bug fixed: h2d.Layers.addChildAt interprets the index as
  layer, not flat child offset. Kirby and equipment now use actual hero layer9;
  background effects use room-back layer. GPU uploads occur once per source frame.
- Fresh combat `012042` consumed a Zombie, randomly became Crash, and parried with
  retained shield257 before Crash naturally expired. All5,259 projected hurtbox
  scopes were restored. Original save hashes remained unchanged.
- Current offline suite passes33 rule assertions,15 ROM decoder and6 parser tests,
  and Release runtime compilation with zero warnings/errors. Native suite and
  live lifecycle/crowd/background checks continue before final acceptance.

## 2026-10-04 — physical geometry, lifecycle, and native startup repairs

- Host `Hero.bodyHei` is measured in 24px cells. Using29 directly produced a696px
  host body after an outfit swap; it now uses29/24, with native29px visual height.
  The original default54px body/radius16.8 returns when leaving Kirby.
- Actual source hurtboxes passed `TargetHelper.filterByArea` inside/outside tests
  for standing12×12 and crouch12×6 in run `20261004-015411`. Query scopes restore
  the host physical body and feet; this verifies hits, not just trace dimensions.
- Run `013050` completed an actual PrisonCourtyard transition. The transition
  exposed a host equipment SpriteLib lifetime issue: `flushCache` releases unused
  atlases despite preventAutoDispose. Rendering now reacquires the manager's
  current atlas and recreates equipment sprites when its identity changes.
- Run `013424` showed original Crash, Paint and Mike effects in actual Dead Cells.
  The HUD remained outside the scenery tint. Native helper/ice-block rendering
  also matches source pixels. Source camera shake is not mapped onto the host camera.
- Fresh Sword `014116` delivered18+16 damage; Wheel `014250` delivered23/14/23/23/2
  and left its117HP target alive at32. Both runs preserved original saves and
  incoming10 damage. Fresh `014414` resumed exact evolved frame4366 before4367.
- Enemy proxies now receive actual host remaining HP, enabling original lethal
  reactions. A defeated source object can detach from host control and persist as
  an original Ice block; native tests cover kicking it and damaging another enemy.
  The live Ice fixture still has a source-state discrepancy under investigation.
- Host breakable objects now participate in source collision; contact damage is
  disabled for props. Live generated-door validation remains pending.
- Review found two health exploits: returning from a different outfit could undo
  damage, and host max-life recomputation could refill source health. The adapter
  now imports the current health fraction when returning to Kirby, preserves the
  fraction across stat recomputation, and gives explicit active-save metadata
  precedence over stale outfit-change flags. A lethal incoming-hit lifecycle test
  has replaced the earlier forced host kill fixture.
- Level changes schedule the original destructor for orphaned Cook food and its
  sparkle children. Native cleanup preserves Kirby, its owned effects and the
  Wheel helper. Exact save restores do not trigger this room cleanup.
- Intermittent native startup SIGSEGV was traced from a real core to
  GPU::SyncAllVRAMCaptures→GPU::SetRenderer→NDS::NDS. Pinned melonDS left
  VRAMCaptureBlockFlags uninitialized before the constructor's SetRenderer call;
  garbage flags dereferenced a null renderer. Checked vendor patch initializes
  this array. `tools/verify_native_init.py` passed64 real open/step/close cycles
  across eight allocator-poison values, including the value that reliably crashed
  the previous binary on its first construction. See NATIVE_INITIALIZATION.md.
- Offline checks after current health and room-boundary edits pass33 user-rule
  assertions,15 ROM-decoder tests,6 oracle-parser tests, and Release compilation
  with zero warnings/errors. Native audio and live Ice/props/lifecycle/boss/crowd
  acceptance are still in progress; no complete-parity claim is made.

## 2026-10-04 — elemental damage and original audio accepted

- Live Ice failure was a real bridge timing bug: source collision-group HP changes
  before the enemy's canonical HP during recoil. Reading only canonical HP let
  the next host synchronization overwrite pending damage. Proxy state now reports
  the minimum valid canonical/active-group HP, including original Suplex clones.
- Live run `20261004-020932` now deals11+5 damage to a16HP Zombie, kills the host
  once, and preserves the original detached Ice block for197 source frames.
  `frame-010858.png` visibly shows the frozen block beside Ice Kirby; inspected
  directly. Native regression also verifies original kick and subsequent damage.
- Live generated-door run `020615` passes: original Sword kills its1HP BasicDoor,
  invokes Door.onDamage once, sets broken=true, and releases the native proxy.
  The initial failed fixture attacked during the original transformation delay;
  waiting for that animation and stepping closer corrected the test.
- Live `020312` absorbs a Zombie into random Jet16, retains physical shield905,
  and records real shield use/start/counter while Jet remains active. An exact
  evolved checkpoint is preserved privately under lab/checkpoints.
- Native audio investigation identified periodic idle SE_ARUKI from the hidden
  Waddle damage carrier. Six source VM walking sound calls are suppressed only
  while stepping paired collision proxies, preserving the real player/helper
  sounds. Production audio test passes with SDL3 preloaded:288836 samples,
  idle/settled0, jump1571, landing5597; gain, queue, pause and disposal all pass.
- The fatal-hit fixture revealed source health0xFFFF (signed−1), previously skipped
  by nonnegative-HP damage handling. Native health now normalizes this lethal
  sentinel to zero. The host immediately enters its death lifecycle and stops
  stepping the dead source scene. Live lifecycle rerun is pending.
- Final native suite:57/57 cases pass. Accepted library SHA256:
  `9dc9fee1b4a944915a9153b2c09d3b3772b945b5e0278fe1444bcdba51db9fc8`.
  Constructor initialization remains the patch proven by64 allocator-poison cases.
- Universal Modder publication lint still reports zero failures, with six local
  path warnings. Nothing has been published or installed into the Steam directory.

## 2026-10-04 — lifecycle accepted and invulnerable health reviewed

- Live lifecycle `20261004-021224` passed with Jet/shield905: original38/76 health
  survived outfit and max-life changes; an actualT_Courtyard transition retained
  state. Source fatal HP reached0 at5395, the host entered Death and removed the
  hero, and normal New Game reset sourceframe4166, baseform1, full76HP and no
  retained shield. Gameplay continued to4665 with correct body29/radius9.
- Independent review caught Cook's intentionally absent active hurt node. Native
  health access now follows the persistent health-group pointer; this preserves
  valid Health/MaxHealth/SetHealth during invulnerability. Original queued damage
  is still rejected by source collision. All24 forms now assert health on every
  sampled frame, and explicit Cook and Wheelie regressions pass.
- Host death responds only to confirmed zero HP. Negative native status is an
  error and is checked explicitly by the managed health wrapper.
- Accepted native suite is now58/58. Current library SHA256:
  `b799d1482911f62f975a1484bfc6e324d2baa246ff1cac05377bb672bde5e690`.
- Existing-bound enemy release now uses the same AABB distance as selection, with
  larger320px/120px release limits. This avoids repeatedly rebinding a wide boss
  whose center is distant but near edge is reachable, and releases enemies left
  far above/below so they cannot consume all31 collision slots.
- Root independently ran the strict audio suite on this final library using the
  default desktop output device, explicitly removing dummy-driver overrides.
  Device/queue/gain/pause/disposal and original PCM assertions all passed; this is
  software output verification rather than an acoustic listening claim.

## 2026-10-04 — final input and host boundary verification

- Live Concierge `20261004-022453` kept real boss AI active, resisted96 original
  inhale frames and took32 original Sword damage. This fixture excluded incoming
  hits; it is outgoing-damage/capture evidence, not a full boss-fight claim.
- Live crowd `022833` kept31 actual Zombies simultaneously paired and alive;
  21 distinct targets took18 Sword damage each. Their AI was locked to measure
  capacity and routing. The exact scope is in docs/HOST_BOUNDARY_TESTS.md.
- Testing the physical binding reader exposed two bugs bypassed by earlier
  direct DS-input fixtures: incorrect Up/Down action IDs, and an absolute
  Controller.suspendTimer treated as a countdown. Input now uses native
  Lib_std.sys_time(), verified10/11/12/13 directions, positive-up analogY,
  matching dead-zone clamping and invertPlayerMovements.
- Host weapon preparation temporarily receives real directional bindings while
  native host movement/jump/skills remain masked. Live `025047` selected actual
  PureNailUp/cycle2, restored5/5 outer scopes, matched all nine input masks and
  inverted masks, moved94.6875px and jumped49.890625px through production Read.
  Shared W mapped both Up and Jump exactly as the host's binding arrays specify.
- Recurrent yellow stars were confirmed original Green Greens exit decorations,
  not Kirby effects. Native World clears only16 cached door-decoration type words
  at0x0209FBEC. Player/helper/ability objects and genuine palette7 effects remain.
  Independent originalSword60-frame rendering matched46,624 pixels with no
  mismatches after the fix. Native source suite passes59/59.
- Final native SHA256:
  `f94997d82877bcdce29ed8d8cb3741545f08044ad813fa61f59f9406c6314b2c`.
- Offline checks pass33 rule assertions,15 ROM-decoder tests and6 parser tests;
  Release runtime builds with zero warnings/errors. Universal Modder lint reports
  zero failures and six intentional private-path warnings. Source notes pass
  its knowledge-base schema check. No assets or game files are committed.
- Final normal-play review found host jump/dodge tutorial completion requires
  suppressed Beheaded action handlers. KirbyTutorials now suppresses only the
  six replaced jump/dodge/Homunculus prompts, and cancels an existing matching
  prompt on activation. Verified Hero.cancelTuto hides and clears current without
  marking the tutorial complete; host healing/equipment/interactions remain.
- Clean showcase run `025145` consumed an actual Zombie into random Fire11 and
  parried with the retained physical shield919. The20s/15fps video contains264
  real captured frames and original48kHz effects, with no source exit-door stars.
  The root agent inspected its contact sheet; original saves were unchanged.
- Final fresh-profile run `025627`, with no automated input plan, advanced original
  frames4166→5063 with base Kirby awake at76HP. Only the normal attackM prompt
  remained; replaced movement prompts were absent. The root inspected its actual
  framebuffer. The bounded launch had no fatal error and left real saves unchanged.
- Preserved test profiles under lab/checkpoints/before-fresh-profile-20261004-025606
  and fresh-profile-smoke-20261004-025743. Final lab/save is empty for the user's
  New Game. No game process remains. `lab/final-stage.json` records staged hashes;
  root compared runtime/core/native/outfit against their exact built originals.
- Final managed runtime SHA256:
  `df41ed4dfb8b5886d41eb4696ecb5c2a42a5c65780912737ebfb91b48e709123`.
  The local executable `play-kirby` starts normal desktop play with separate saves.

## 2026-10-04 — user recording regression fixes

- Reviewed the complete 96.82-second user recording, with denser final-corridor
  and equipment/HP inspection. Created a separate reproduction root from a copy
  of the user's current private profile; the four main profile files are unchanged.
- Fixed pickup HP flicker at its source: active Kirby's updateMaxLife hook keeps
  exact source life/rally/bonus values before the HUD caches them, while saving
  the recalculated host maximum for outfit changes. Activation/deactivation also
  synchronize HUD units. Live 033400 retained 57/76 through actual shield pickup,
  24 recalculations and the 75/100 normal-outfit round trip.
- Corrected equipment's lighting pass and packed normal-map texture. Synchronize
  the full ordered host shader sequence because internal links can change without
  a new head pointer. The normal wooden shield is visibly held; its secondary
  tutorial clears on the first real right-click and seven shield-start callbacks ran.
- Corrected sword grips derived from the wrong off-hand track. Use original
  body/equipment contact for main weapons; verified bow/shield tracks remain intact.
  Only atlas origins changed; original textures, normal maps and palettes are
  identical. All 860 bow-named and 204 shield atlas rows retain their old positions.
- Live 033651 shows the attached starting-sword combo and actual guard/movement.
  Final 034038 verifies the held bow, arrow and 109-damage natural Zombie kill.
  Enemy bitmap-hidden/normal-outfit comparison disproved the suspected canvas
  occlusion. Normal enemy AI remained active. No new successful parry-counter claim.
- Physical keyboard Down+X produces original Down/crouch only; X is a contextual
  menu binding. Controller X instead invokes the main weapon. Original tunnel
  comparison preserved all 600 post-settle frames under world rebasing; source
  slide behavior is unchanged. Actual defaults: E inhale/copy, Q discard, Space jump.
- Added an opt-in normal-binding reproduction probe with explicit copied-profile
  fixture controls, plus five synthetic equipment-grip regressions. Twenty Python
  tests, six source-parser tests and 33 rule assertions pass. Final Release builds
  with zero warnings/errors; Universal Modder reports zero failures and six existing
  private-path warnings; the field note schema passes.
- Reviewed the real 20-second verification clip at
  `showcase/recording-fixes/kirby-fixes-20s.mp4` in the private data root. It includes
  source effects audio and documented cuts around fixture setup.
- Final runtime SHA256:
  `0866aceed61742c333e8fb73dae5384c5b24bfeefe94d50ad3887cdc941b61fe`.
  Final outfit SHA256:
  `6c141ee357dcdb5087acd55e12f79211338e6690de1e235238282b75bc5b8663`.
  Native bridge remains `f94997d82877bcdce29ed8d8cb3741545f08044ad813fa61f59f9406c6314b2c`.
  The last live DLL differs from the final build only by diagnostic event wording
  and a comment; production fixes are identical. Detailed scope and evidence are
  in `docs/RECORDING_REVIEW_2026-10-04.md`.

## 2026-10-04 — second user recording fixes

- Reviewed the complete 105-second recording `09-07-58` frame by frame around
  Kirby, plus play log `20261004-090737`. Diagnosed every flicker, overlay and
  freeze in it; see `docs/RECORDING_REVIEW_2026-10-04_0907.md`. Experiments used
  the separate data root `repros/bugfix-20261004` with a fresh profile.
- The white/pale Kirby during attacks was the host sword's GlowKey blade drawn in
  front of him. Kirby's body now stays above the equipment overlay (`095009`).
  Hit and guard flashes were confirmed as the ROM's own palette flashes and kept.
- Added `KirbyHostVisuals`: the outfit atlas is resolved via the host asset manager
  and never drawn (arrival corpse, body, death corpse, trails); the Beheaded head
  flame is silenced for Kirby. Kirby appears in the arrival flash (`102734`).
- The original death animation now plays after the host destroys the hero, for
  180 source frames from the frame event, with the body anchored (`100337`).
  Host revival reloads the seed machine instead of resuming a dead player.
- Added `FramePacer` (vsync lock within 3% of the DS rate, hitch rounding) and an
  audio queue that drops/repeats 1% of frames to absorb the drift.
- Incoming host damage is scaled to Kirby's 76 HP by the host's current maximum
  (20 → 15 at 100 max, `101442`), so host difficulty and Survival upgrades apply.
- Existing combat probe `101739` still consumed a Zombie, copied Plasma and
  countered with the retained shield. Final acceptance `102834`, repeated as
  `103501` with the exact staged DLL, covered arrival, sword, hit, guard and death.
  Offline checks pass with 39 rule/pacer assertions.
- Staged runtime SHA256 `8e6f423395e52b125d54cfc93cbe4dbfc3d838816a8213c9b6c726731098886c`
  (core `f5288a19115443c705a29711eafc24aee727934a417df88f4cdacd5767a72249`) into the
  play lab; the previous files are in `lab/checkpoints/before-recording-fixes-20261004-103051`.
  Outfit and native bridge are unchanged.

## 2026-10-04 — third user recording fixes

- Reviewed `11-09-21` (99 s) frame by frame with presentation times and play log
  `20261004-110858`; see `docs/RECORDING_REVIEW_2026-10-04_1109.md`.
- Kirby vanished on alternate frames whenever hit: the host's `Hero.blink` alpha
  pulse, reached through the original damage path. It is now skipped there; the
  ROM's own hurt flash remains and the ROM never hides Kirby's objects.
- Slow movement: original walk 82 px/s and dash 123 px/s against the Beheaded's
  251 px/s, seen through a viewport twice the DS screen's height. Added
  `KirbyCamera` (host zoom ×2 with `updateSizes`) and `DashAssist` (a held
  direction becomes the original double tap; a light analog tilt walks).
- Copy damage: original enemies have 16–18 HP; host basic enemies have 60–160
  base life × the host tier scale. Paired original enemies now hold host life in
  original units (÷ 3.5 × tier scale) and damage is multiplied back; Ice freezes
  still work. Parasol and Sword kill a tier-1 Zombie in two or three hits.
- Body bitmap anchored to whole host pixels; minute pacing summary in the log.
- A pale cyan Kirby in darker spots is the level's drifting foreground fog; an
  A/B of alpha versus opaque passes showed no difference, so no change.
- Offline checks pass with 46 assertions. Live lab runs listed in the review.
- Staged runtime SHA256 `47319cd9df8edd8feeb2e82ec47140474987e1c62b3d1682d9ac879d9af8b845`
  (core `725921af13ea9c1419e6f3c47f0c77f2d776d1da15f1909cede456e919585307`),
  byte-identical to the build in final lab runs `124528` and `124658`. Previous
  files: `lab/checkpoints/before-third-recording-fixes-20261004-125106`.

## 2026-10-04 — fourth user recording fixes

- Reviewed `21-08-49` (97 s, 20 fps); see `docs/RECORDING_REVIEW_2026-10-04_2108.md`.
  All 57 single-frame dropouts were the ROM's own hurt/guard palette flash
  (whitened or orange body), not hidden objects; the host blink no longer occurs.
- Added `PaletteFlashSoftener`: Kirby's first two palette banks show a 35% blend
  of a sub-4-frame flash over the stable palette; lasting changes pass through.
  `FlashStrength` 1 restores the original. Lab runs `213157`/`213303` keep Kirby
  solid pink through a real hit and a held guard.
- `CameraZoom` default returned to 1 at the user's request; the option remains.
- Combat probe `213605` passed (Zombie consumed, random Ninja, shield counter).
  Offline checks pass with 52 assertions; Release build has no warnings.
- Staged runtime SHA256 `0ca56aaabc096a57ffb954c4e75dcec42194cf9bf1bd350d394aa2a3c8cf3664`
  (core `aed9fc700956ffb8d438bc0810bc10525f13f62d0bc164da1170a7dec158cfa3`),
  byte-identical to the build in lab runs `213157`, `213303` and `213605`.
  Previous files: `lab/checkpoints/before-fourth-recording-fixes-20261004-213828`.

## 2026-10-04 — fifth user recording fixes

- Reviewed `21-43-49` (60 s, 23 fps); see `docs/RECORDING_REVIEW_2026-10-04_2143.md`.
  The flicker was Kirby drawn one terrain cell off for a single frame at every
  window rebase: hardware OAM shows the previous source frame, and the renderer
  paired it with the newest state. `NdsRuntime.DisplayedOrigin` now pairs them;
  lab runs `224958` and `230948` show 0 jumps where the old pairing drew 36 and 35.
- Roll zones (host collision 65) open while Kirby slides and stay open until he
  leaves one entered by sliding (`RollZoneGate`); the slide's fast part also
  bursts doors and breakable tiles through the host's own roll checks. Lab run
  `225230` walked into a real zone (blocked) and slid through it.
- Down + jump in the air (or the player's dive binding) starts the host
  `DiveAttack`: original fall and exhale, added host descent, host landing with
  shockwave, stun, damage and breakable floors; enemy touch damage is off while
  stomping. Lab runs killed a Zombie, broke a floor with the Ram rune, and were
  refused without it.
- Combat probe `230623` passed. Offline checks pass with 77 assertions; Release
  build has no warnings.
- Staged runtime SHA256 `73e23ac11d0a45dde2693fcaedf56a32ffa25c44bb89870cd939e967433068fd`
  (core `fbdc932f35a3436f59a1a251ac26e3c1f3a5acc4d3cb0aeb2f1a56be441359e2`),
  byte-identical to the build in lab runs `230623`, `230948` and `231121`.
  Previous files: `lab/checkpoints/before-fifth-recording-fixes-20261004-231224`.

## 2026-10-04 — pickup crash fix

- The user's crash when taking a chest item was the host HUD's "unexpected posId":
  the mod refused `Inventory.equip` after `Inventory.add` had listed the item,
  which left it without a slot. Any pickup of an item outside Kirby's rules
  crashed (reproduced in lab run `233522`).
- Kirby now declines those items through `InventItem.isUsefulFor`, which shows the
  host's own "Can't use that right now" message; the equip hook never refuses.
  Lab runs `233628`, `233707`, `234402`, `233904` and `234504` cover a declined
  dagger, an allowed sword, the replace dialog, copy form and the combat probe.
- Staged runtime SHA256 `47f301529d7e4009fcbdc9a7796560687fe08824dcaa35682a5447e875fd1943`
  (core unchanged), byte-identical to the build in runs `234402` and `234504`.
  Previous files: `lab/checkpoints/before-pickup-crash-fix-20261004-234636`.

## 2026-10-05 — chain stomp fix

- Reviewed `23-59-46` (17 s): Down + Space on a chain hopped off and floated, then
  stomps landed in mid-air. The stomp's floor check counted ladder cells and
  one-way platforms from below; the climb action could not start a stomp.
- A climbing Kirby now kicks off the chain with the original A and stomps; ladders
  never count as floor and one-way platforms only from above. Lab run `000547`
  stomped from a real chain to the floor 6.7 cells below; regressions `000659`
  and `000742` pass; offline checks pass with 80 assertions.
- Staged runtime SHA256 `bdb4a5a27cf078aafd9f955ae60b89d788908ea7358c3acd0fecb8720b8ae79f`
  (core `87efbf16d968df66d9b039449b5dec526dd041eec2987c8bd728405107aa1a87`),
  byte-identical to the build in runs `000547`, `000659` and `000742`.
  Previous files: `lab/checkpoints/before-chain-stomp-fix-20261005-000900`.

## 2026-10-05 — climb without jumping first

- Reviewed `00-12-44`: W at a chain jumped, then climbed. W and the Up arrow are
  bound to both Up and jump by default; the original jumped on A first.
- A key bound to both now sends only Up while Kirby holds or overlaps a ladder or
  chain, until released; Space still jumps. Lab runs `001748`, `001935` and
  `002008` cover climbing, jumping on open ground and a stomp from the climb.
  Offline checks pass with 80 assertions.
- Staged runtime SHA256 `8c9ea64bec368de389fac0946cb106df4a95f8fdc256e9ba89ded49bfaf697f2`
  (core unchanged), byte-identical to the build in runs `001935` and `002008`.
  Previous files: `lab/checkpoints/before-chain-climb-fix-20261005-002107`.

## 2026-10-05 — pressure plate fix

- A floor switch (host `PressurePlate`) stayed off under Kirby: the hurtbox
  projection used during `Level.update` lifted his host foot about 9 px, so the
  plate's on-ground test failed. Plates (with trap plates and pulley points) and
  hidden triggers now see the physical body; combat keeps the projection.
- Lab run `004409` triggered a generated plate; `004459` (combat probe) and
  `004617` (host hit) still pass. Offline checks pass with 80 assertions.


## 2026-10-05 — Kirby at the Beheaded's speed

- Request: Kirby felt slow next to the Beheaded; walking, running, dash/roll,
  flying and every other move should match the original Dead Cells character.
- Measured the Beheaded in the lab (runs `005729` and `012018`, 30 host steps a
  second): run and air control 236.5 px/s, stop within ~3 px, roll 131 px at up
  to 339 px/s, fall 403 px/s, dive ~900 px/s, ladders 97.5 px/s both ways, jump
  43 px (47 running) peaking after 0.25 s. Kirby's original speeds: walk 82.5,
  dash 124, air 86, float across 64, fall 210, climb 139 up and 165 down, stomp
  480 px/s; slide 71 px; jump 50 px peaking after 0.33 s; a released dash slid
  33 px and a turn skidded for 0.28 s.
- New `SpeedMatch` (core). The original controller still runs every frame with its
  own states, animation and collision; the host carries each frame's own motion
  on through free space at the Beheaded/original ratio (capped at run speed in
  the air), clears the original horizontal velocity when the direction is released
  or reversed, and steepens the jump after its take-off burst so it keeps its 50 px
  but peaks after 0.25 s. Added motion stops a pixel short of walls and ceilings,
  leaves a full fall step above floors, and follows slopes at the original's
  whole-pixel surface; hits, swimming and ability moves keep original motion.
  `MatchHostSpeed` (default true) keeps the DS speeds when false.
- The stomp now falls 15 px a frame (the dive's ~900 px/s) and straight down: it
  cleared only the reported copy of the horizontal velocity (+0x88), while the
  original moves on +0x98.
- Kirby lab run `014352`: run 236.5 px/s; release stops at once; turn in two
  frames; slide 130.7 px at up to 338 px/s; jump 50 px peaking after 0.25 s and
  landing after 0.58 s; air and float across 236.5 px/s; glide 125 px/s; fall
  403 px/s; stomp 877 px/s; ladders 97.5 px/s; no frame inside a floor or wall.
  New `tests/speed` runs this code against the original controller: its 17
  checks pass (speeds, walls, ceilings, ledges, slopes, one-way platforms, a
  one-cell tunnel). `scripts/check.py` passes, with 105 unit assertions.
- Regressions pass: roll-zone tunnels `014816`, stomp `014900`, breakable floor
  refused without the rune `014937`, door `015015`, plate `015125`, combat probe
  `015157`, chain climb and stomp `015405`.
- Known: walk and dash animations keep their original rate, so Kirby's feet slide
  a little at the higher speed. Climbing is slower than on the DS, matching the
  Beheaded.
- Staged runtime SHA256 `33e14bcb03be6276d844535877bf5e6bf503eaa3432a33f224c1434da45e0be4`
  (core `c042a5c5a2bbd9992a3724fb60692b93dccff11bc5ea3befd75f5eeaa5148b66`),
  byte-identical to the build in runs `015405` and `015441`; the play config gains
  `MatchHostSpeed: true`. Previous files:
  `lab/checkpoints/before-speed-match-20261005-015641`.

## 2026-10-05 — Bomb drag fix (sixth recording)

- Reviewed `02-07-33` (31 s, `docs/RECORDING_REVIEW_2026-10-05_0207.md`): Bomb
  Kirby's lobbed bombs hung behind him and drifted after him as he ran, exploding
  far from where they were thrown; on a chain a bomb ended under his feet.
- Cause: `kssu_world` moves every original actor with Kirby. The speed match and
  the stomp moved Kirby a few pixels every frame through it, dragging his bombs,
  other thrown objects, effects and the helper along.
- New bridge export `kssu_move_player` moves only Kirby (with his terrain probes
  and linked position records). The runtime moves the window by whole cells with
  `kssu_world` and then Kirby alone; host teleports and the first placement after
  a load still move everything, and an older bridge keeps the old behavior.
- A lobbed bomb's path with the speed match is identical to the original's
  (`tests/speed`, 45 frames, 0 px apart). Lab run `022523` and `023137`: bombs
  stay where they are lobbed while Kirby runs or jumps away. Regressions pass:
  Bomb damage `023253`, Cutter `023345`, speeds `023436`, stomp `023528`, roll
  zone `023606`, chain climb and stomp `023649`, combat `023724`. The bridge's 59
  original-ROM cases, `tests/speed` (18 checks) and `scripts/check.py` pass.
- Staged runtime SHA256 `cc77498f6b761e316455bc0ecf74e6d4f43498d680b508e8585d5f30c3da04f3`
  (core `c042a5c5a2bbd9992a3724fb60692b93dccff11bc5ea3befd75f5eeaa5148b66`), bridge
  `a3958db425c90660e1b345bafae8648d82ee1437ec2c119a7e162a47fc541ace`, byte-identical
  to the verified runs. Previous files:
  `lab/checkpoints/before-bomb-drag-fix-20261005-024334`.

## 2026-10-05 — copy abilities that carry Kirby (seventh recording)

- Reviewed `10-13-43` (`docs/RECORDING_REVIEW_2026-10-05_1013.md`): Jet charged and
  fired but stayed in place. Every copy-ability attack runs under action 142C, which
  the speed match treated as the float exhale and braked on a released direction.
  A survey of all 24 abilities found Wheel, Jet, Yo-Yo, Suplex and Fighter broken.
- Ability use now keeps its original motion; only the exhale that directly follows a
  float moves like floating (`SpeedMatch.ActionFilter`). All standing, charged, dash
  and down attacks match the original (125 cases); `tests/speed` checks Wheel
  (321 px), Jet (216 px) and Yo-Yo (124 px) and passes 22 checks; `scripts/check.py`
  passes with 107 unit assertions.
- Lab: Jet dash 216 px `103109`, Wheel roll 288 px `103236`; damage probes Jet 105
  `103328`, Wheel kill `103420`, Bomb 56 `103512`; speeds `103603`, stomp kill
  `103955`, combat `103733` pass.
- Staged runtime SHA256 `f62b8c148d41a9db49d33885412925172e77c73f39f503093c3b5adee7f85846`
  (core `dca9195df071e4d2a4804a0426649705d06bfde85faf87d0339b04ad7d9ad2a8`, bridge
  unchanged), byte-identical to the verified runs. Previous files:
  `lab/checkpoints/before-ability-motion-fix-20261005-104102`.

## 2026-10-05 — attached effects and window moves (eighth recording)

- Reviewed `10-47-27` (`docs/RECORDING_REVIEW_2026-10-05_1047.md`): on a chain
  Fire Kirby's head flame stayed at the foot when he climbed and above him when
  he climbed down; running, the embers trailed further behind than natively.
- Cause: Fire's head emitter is attached to Kirby by an offset in its position
  fields, and `kssu_world` shifted it like a position, 24 px per cell the window
  moved. A sweep of every copy ability found 18 actor classes attached this way
  (Plasma's spark and effects of 15 abilities and base Kirby).
- The bridge records each actor's drawn position and sprite anchor as every source
  frame begins; on a window move it keeps the offsets of attached actors
  (recognized by drawn position = parent point + offset) and moves drawn
  positions with the window. The emitter now stays at 0 px from Kirby (old bridge:
  216 px on the same path); no attached actor jumps with the window any more.
- `tests/speed` adds Fire and Plasma checks (24 pass), the bridge's 59 original-ROM
  cases, 107 unit assertions and `scripts/check.py` pass. Lab run `113838`: Fire
  Kirby climbs a ten-cell chain up and down and runs with the flame on his head;
  Plasma `115618` keeps its spark. Regressions: Jet `114238`, Wheel `114407`, Bomb
  `114459` and damage `114614`, combat `114959`, stomp kill `115313`.
- Staged bridge SHA256 `d872b5221f160e557f44fd6f5002a71f30ed2c5dee4cae16f5cfd3b921f53cb3`
  (runtime and core unchanged), byte-identical to the verified run. Previous files:
  `lab/checkpoints/before-fire-flame-fix-20261005-114149`.

## 2026-10-05 — self-directed sweep: camera framing and walk cycle

- Swept for bugs without a report (`docs/BUG_SWEEP_2026-10-05.md`): frozen versus
  moving source windows for every ability over two scripted plans, an in-game tour
  of all 24 abilities with automatic per-frame checks, the same checks over about
  150 earlier lab runs, and the user's play logs (no errors).
- Fixed: the source camera's scroll (i16 at `0x0209F3A8`) was never rebased. After a
  host placement Kirby could sit at screen x 376, undrawn for six frames, and every
  window cell jumped his screen x by 24. `kssu_world` now moves the scroll with the
  window and frames Kirby after a placement; he is drawn from the first frame and
  stays within x 116–140 while running.
- Fixed: walk and dash cycles kept the original rate at 1.91 times the speed (78.8 px
  per cycle against 41.3). `AnimationPacer` takes extra frames off the pose count at
  `(player+0x40)+0x6C8`: 47.3 px per cycle.
- Lab runs (map dumps added to the lab moves probe for terrain checks): framing and
  walk `125144`; regressions Jet `125311`, Wheel `125439`, Bomb `125531` and damage
  `125648`, speeds `125739`, stomp kill `125831`, Fire chain climb `125908`, combat
  `130029`. `tests/speed` 26 checks, 109 unit assertions, the bridge's 59 cases and
  `scripts/check.py` pass.
- Staged runtime SHA256 `72101b484b292366401d451815477439857ce189ab7e8c4c9b421a905927bd95`
  (core `167a09cb0e48f416a8af5215f8b73a849b610f258330eaeaa2854c38fe8c1226`, bridge
  `18ab44e430047464eb017009d617d107defef6c9cdfe6c4fb53ed39d2b89df65`), byte-identical
  to the verified runs. Previous files:
  `lab/checkpoints/before-camera-walkcycle-20261005-130255`.

## 2026-10-05 — Dead Cells touch rules

- The user noted Kirby is hurt by touching enemies, which the Beheaded is not.
  Measured with an AI-locked Zombie: the Beheaded ran through it four times at
  100/100; Kirby lost 7 of 76 HP on the first touch and the Zombie died from it
  (the original game's body-contact rule, applied through each enemy's proxy).
- New `OriginalTouchDamage` option (default false): the runtime excludes the
  original body-contact category for every paired enemy, so touching hurts neither
  side. Kirby's inhale, swallow, Suplex grab, slide and attacks still hit; enemy
  attacks still hurt him (and Dead Cells' own touch attackers still do).
- Bridge cases `proxy_touch_off`, `proxy_swallow_touch_off` and
  `proxy_suplex_touch_off` added (62 pass). Lab: touch `201306` (Kirby 76/76 and
  the Zombie unharmed after passing through it), active Zombie `201344` (two swipes:
  76 → 69 → 61, like the Beheaded's 100 → 89 → 79 → 69 in `201423`), combat
  `201559` (inhale, copy, parry, counter), Bomb damage `201734` (56).
- Staged runtime SHA256 `32422c774353de9915b9f199bfc1d7ac5248f9c3ea42ced77170357c938bec02`
  (core and bridge unchanged), play config gains `"OriginalTouchDamage": false`.
  Previous files: `lab/checkpoints/before-touch-damage-20261005-201846`.

## 2026-10-05 — Elite Sword (#9)

- Swallowing an Elite now always gives original Sword (ability 18), which deals
  double copy damage until the ability is lost. This replaces the "Ultra
  abilities" idea: Ultra Sword and Flare Beam come from Kirby's Return to Dream
  Land and are not in the KSSU ROM.
- `KirbyRules` tracks a pending empowerment from an Elite capture. Kirby becomes
  empowered only if the original transform yields that ability. Discard, hit
  loss, helper creation, spit and new run all clear it, and saves keep it.
- `CopyDamageScale` scales source HP and host damage together so source/host
  lethality still agree. Its multiplier is a parameter, so it can combine with
  the Boss Cell curve from #7.
- Source tests: `python3 scripts/check.py` passed (20 Python tests, 6
  oracle-parser tests, 58 rule assertions, Release build with 0 warnings).
- Live: `20261005-204132` (separate data root with its own lab copy and a bridge
  built from `main`'s `lib.rs`): an Elite Zombie gave empowered Sword, and a
  second Zombie took 36/36/32. Empowerment loss, resume and biome transitions
  were not run live.

## 2026-10-05 — colorless copy damage (#10)

- Copy attacks now scale like Dead Cells colorless gear, replacing the unmerged
  per-Boss-Cell curve from #7 / PR #8. The host formula came from `hlboot.dat`
  (`59350cad…07ce7e`) with the bundled crashlink disassembler:
  `getRelevantTierFor` (Colorless = highest of the three tiers),
  `computeTiers` (tier = 1 + scrolls) and `computeFinalDmg` →
  `scaleHeroValueToTier` (× 1.15^(tier − 1), truncated). The decompiled output
  stays in the session scratchpad, outside the repository.
- `src/core/ColorlessDamage.cs` holds the tier choice, an independent formula copy
  and the source-HP/host-damage pairing. A tier-2 / life-69 test exposed that
  `ceil(life ÷ multiplier)` can give one spare source HP in floating point. Source
  HP is now the smallest value whose truncated scaled damage is lethal.
- `KirbyEnemies` reads the host tiers each source tick and calls the host's
  `Const.scaleHeroValueToTier(1, tier)`. Base-form weapons, incoming damage,
  parries, boss resistance and source cadence are untouched.
- `LabAbilityProbe` and `launch_runtime.py --probe-scrolls B,T,S` add real scroll
  items through the inventory and call `Hero.computeTiers`.
- Source checks: 20 Python tests, 6 oracle-parser tests, 48 core assertions; Release
  build with 0 warnings/errors.
- Live checks, in the separate `repros/colorless-20261005` data root:
  - `204919` Wheel at tier 5: 23/14 became 40/24, and the host and core
    multipliers were identical.
  - `205107` Ice: a lethal hit of 10 became 17, and the frozen object persisted
    195 frames.
  - `205246` with no scrolls: damage unchanged.
  - Real saves were unchanged in all three runs.

## 2026-10-05 — Cook drops host healing food (#11)

- Each ordinary host enemy consumed by original Cook now gets one host food item
  (`LootGen.getSmallHealingKind`) in its own loot before the existing kill. The
  host death path drops it as a normal `ItemDrop`. Cook damage is unchanged;
  Bosses (original Cook resistance) and Elites get no food. One item per entity ID,
  cleared with level, run and restore (`KirbyRules.ClaimCookFood`).
- Host routines were read from the lab `hlboot.dat` with crashlink: `Mob.onDie`,
  `Mob.dropLoot`, `Level.dropMobLoots`, `Level.updatedDelayedLoots`,
  `LootGen.pickHealing`/`getSmallHealingKind`, `ItemDrop.onActivate` and
  `Hero.updateInteraction` (interact is action 6). No host food drop rule exists
  for Bosses or Elites.
- Core rule test: 44 assertions pass. `scripts/check.py` passes, and the Release
  runtime builds with zero warnings.
- New `--input-plan cook` probe. Live `20261005-205320`: one `SmallMeat` dropped
  from a cooked Zombie and real R pickup healed host/source 48 → 59. The original
  Cook food separately healed 36 → 48. Runs `204940` and `205147` also picked up
  one drop. Run `204739` dropped one item but the fixture lacked a key-press edge.
- Not run: native bridge tests (no native change), boss/elite live checks, and
  save/resume with an uneaten drop.

## 2026-10-05 — enemy projectiles as mouthful hazards (#13)

- Hostile host `Bullet`/`Grenade` entities (`isOpponent(hero)`) now pair with
  copyless, inhalable, contact-free 1 HP source proxies. Original suction holds
  them (host update suspended), the original mouthful removes them without a
  copy, and the spat star damages host enemies through the existing HP-delta path.
  Decisions live in `src/core/MouthfulHazards.cs`.
- Native `hazard_spit`/`hazard_swallow` pass: mouth buffer `[0,1,0,…]`, a
  52-damage star, no copy on swallow, Kirby at 76 HP. Copyless swallow leaves
  the mouth count byte (action bit1) set until the next inhale. Full suite: 61 cases.
- Measured original inhale box: 2px behind to 60px ahead, 34px above to 4px below
  the foot. Archer arrows pass above it on flat ground (live `205347` frames).
- Live `20261005-205827` (separate data root `repros/hazard-20261005`): two
  Grenader grenades and one mid-jump Archer arrow captured at full health; both
  spits hit the shooter for 52; the swallow gave no copy. Saves unchanged.


## 2026-10-06 — left-click inhales; only copy abilities and a shield (#39)

- Owner request: left-click swung the Dead Cells sword in base form; it should
  inhale, and Kirby may use only his copy ability and a shield.
- `KirbyInput.Read` now sends original B for every non-shield weapon button in
  both forms (it was copied form only). `KirbyRules.CanEquip` accepts only
  shields, `CanUse` only shields, and `ResolveAttack` never routes to a host
  weapon (`AttackRoute.HostWeapon` removed), so `Weapon.tryToUse` declines the
  starting sword too. Shield, guard and parry rules are unchanged.
- `LabWeaponAimProbe` (`--input-plan aim`) now checks left-click inhale on the
  starting sword instead of a Pure Nail upward strike, which can no longer happen.
- Core rule tests: 224 assertions; `scripts/check.py` passes; Release build 0
  warnings/errors.
- Live (separate data root `repros/left-click-20261006`, run `20261006-122227`):
  `StartSword` on key 0 read as mask 2, original inhale for 32 frames, no host
  sword attack, mappings/inversion/movement unchanged. Real saves unchanged.
- Not run: `tools/nds_tests.py` (no native change), left-click with a copy ability
  (the copied path is unchanged), controller weapon buttons, a second-slot bow,
  and a real enemy inhaled through left-click.

## 2026-10-06 — Ice uses the host freeze status (#42)

- Owner report: an Ice kill turned the Dead Cells enemy into the original ice
  block instead of using Dead Cells' freeze. The runtime detached the source
  object on the host death, so its original frozen block stayed.
- Host routines read from `hlboot.dat` (`59350cad…07ce7e`) with crashlink:
  `tool.weap.Freeze.onExecute` (affect 23, `props.duration` 3 s from the MDK
  `data.cdb`, `getDiminishingFactor(3, 1, 8)`), `Entity.getDiminishingFactor`,
  `en.Mob.onAffectChange` (thaw: "Slowed", slow 133, stun 8) and
  `MobAttackTarget.preHitResultUpdate` (a hit shatters a freeze unless it is
  `Frost`, `Poison`, `Bleed`, `ExtraDamage`, tag 13 or small and not `Fire`; the
  type is compared by identity with the game's global value).
- `src/core/IceFreeze.cs` and `KirbyEnemies.Complete`: while the source step has
  Ice, copy hits are host `Frost` damage (`KirbyDamage.Frost` finds the game's
  singleton among the module globals), a surviving unfrozen enemy gets affect 23
  for 3 s × the host diminishing factor, and a host death retires the source
  object at once instead of detaching it.
- First live version froze only; the affect log (`125326`) showed every later
  breath hit shattering and refreezing the enemy with a "Slowed" each time. The
  Frost-typed hits fixed that (`125935`: one freeze, six hits, one natural thaw
  after 179 source frames; `130140`: frozen kill with no original block). The
  final build only freezes after a host-resolved hit and repeats both results
  (`130608`, `130738`).
- Native: new `proxy_ice_release` case; `tools/nds_tests.py` passes 74 cases.
  `scripts/check.py` passes (228 assertions) and the Release runtime builds with
  zero warnings. The shared bridge was built from this branch's unchanged
  `lib.rs`.
- Not run: bosses/Elites and freeze resistance, repeated freezes (diminishing
  factor), Ice on breakables and hazards, Ice kills other than the breath.
## 2026-10-06 — copy attacks break gold deposits (#35)

- Owner report: with Wing copied, the host "Break" gold deposit never broke;
  base-form weapons broke it.
- Cause: `KirbyEnemies.Eligible` left out `PileOfGold` and `GoldNugget`, so no
  source proxy existed for original attacks to hit. Both are now eligible.
  Disassembly of `hlboot.dat` (constructors, `onDamage`, `canBeActivated`)
  confirmed host life, targetable and gold drops in `onDamage`.
- `LabBreakableProbe` gained `KIRBY_LAB_BREAKABLE_TARGET=gold`, which spawns a
  real `PileOfGold` beside Kirby. Live (`repros/gold-20261006`): `013937` Sword
  broke it in four hits with four gold drops; control `014052` without the
  change: no damage. `GoldNugget` not run live.

## 2026-10-06 — Kirby rides host elevators (#28)

- Owner recording 2026-10-05 23:51: the elevator left without Kirby, who then
  fell into the shaft. The host `Elevator.fixedUpdate` (read with crashlink) carries
  entities within 2 px of its foot, keeps only the cell row under its foot solid,
  writes its `yr` as that cell's ground height and copies its `cy`/`yr` to them.
  The original saw the floor at the cell row, up to 24 px below the moving
  surface, so Kirby left the 2 px reach after the first carry.
- `src/core/ElevatorRide.cs`: over an elevator (its carry width, from the surface
  to three cells above), lift the source window to the surface and raise Kirby
  onto a rising elevator that passed his feet. The provider places the window
  with Kirby while an elevator moves him and copies the elevator's cell position
  to the hero exactly; enemies use the same lifted origin.
- Live runs in `repros/elevator-20261006` used a spawned and initialized
  `SimpleElevator`. Moving original objects apart from Kirby, then Kirby back
  alone, dropped him for a frame every few frames (`000609`–`001130`). The
  final runs (`002051`, `002312`) ride up and down standing, catch Kirby after a
  jump and leave health at 76/76. The Beheaded comparison (`001557`) had no host
  landings; Kirby keeps one of height 0 at departure.
- Core rule tests: 181 assertions; `scripts/check.py` passes; Release build has
  zero warnings. The bridge was rebuilt from this branch's unchanged `lib.rs`.
  Not run: `tools/nds_tests.py` (no native change), other elevator types,
  walking off a moving elevator, and floating platforms.
## 2026-10-06 — shield swap while copied (#31)

- Owner recording `00-02-14`: Sword Kirby with a wooden shield stood on a dropped
  Cudgel V after the Concierge; every R press gave "Can't use that right now".
  Cause: `KirbyRules.CanEquip` was false for every kind while copied, so
  `IsUsefulFor` declined the shield, and `EquipShield` refused a new shield while
  copied. The "guard pose" is the dropped Cudgel's sprite overlapping Kirby.
- Shields can now be taken in either form; the newly equipped shield becomes the
  retained shield (`EquipShield` in the equip hook), and `FollowEquippedShields`
  keeps the retained ID in step with the host inventory every frame. Swords and
  bows while copied, and skills in any form, are still declined. Guard/parry code
  is unchanged.
- Core rule test: 178 assertions pass. `scripts/check.py` passes and the Release
  runtime builds with zero warnings.
- Live (separate data root `repros/shield-swap-20261006`): `20261006-004147`
  (Sword) and `20261006-004316` (base) each swapped a Warrior's Shield for a Cudgel
  through the host replace dialog; the old shield dropped, the Cudgel became the
  retained shield, held guard gave original guard 512 and the host block, and a
  20-damage hit resolved `Partial` 5 with Kirby at 76/76. Real saves unchanged.
- Not run: native bridge tests (no native change), the owner's exact order (take
  in base form, then swap after copying), a parry-timed counter with the new shield,
  save/resume after a copied swap.
## 2026-10-06 — enemies in water (#27)

- Owner recording 2026-10-05 23:45: a Zombie standing in a shallow pool could not
  be inhaled or hit and stayed frozen, with small yellow stars around it.
- Cause: the paired original Waddle Dee is defeated by source water (enter-water
  handler `0x02159440` → defeat `0x02158FC0`). The dying proxy reported source
  control (flags 3), so the adapter pinned the host enemy; it ignored inhale and
  attacks and was rebound in the same water about 60 frames later. The damage
  carrier also died beside a swimming Kirby, so host hits missed him in water.
- Fix: `keep_proxies_alive_in_water` in `src/nds/lib.rs` points Waddle Dee's
  enter-water entry at the original return-zero stub `0x020CF36C`. An empty entry
  is not enough: the recoil handler `0x02156540` falls back to the defeat.
- Native: new `proxy_water`, `proxy_water_sword`, `proxy_water_below` and
  `hurt_water` fail on the previous bridge and pass; the full suite passes 68
  cases on this branch's build.
- Live (`--input-plan water`, `repros/water-20261005`): before the fix the pool
  Zombie was claimed for every idle frame and the inhale timed out; with it, five
  runs never claimed it before input and consumed it 37–38 frames into the inhale.
  One run then showed no copy after DOWN (unexplained). Details in
  `docs/RUNTIME_LAUNCH.md`.
- Another session rebuilt the shared oracle bridge from its own branch during
  this work; this branch's bridge for tests and live runs was then built into the
  repro root's own `target/`.

## 2026-10-05 — climbing Dead Cells vines (#25)

- Owner recording 22:43: Kirby hopped and floated beside a grown vine but never
  grabbed it. Host bytecode (crashlink): `VineLadder.buildLadder` only calls
  `LevelMap.addSpot` (spots 0 and 18 per cell, 1 and 18 above the top block);
  `Hero.preUpdate` climbs on spot 0, which `initSpots` derives from collision
  bit 16 for ordinary ladders. The terrain export read collision bits only.
- `KirbyVines` hooks `buildLadder`, records the vine's column and re-reads its
  spots on each query. Vine cells become source ladders (134); solid cells the
  vine passes through or ends under become one-way ladder tops (131). `AtLadder`
  includes vine cells, so W climbs instead of jumping.
- New bridge case `ladder_top`: a solid top stops the original climb at y 52.5,
  a one-way top lets it reach y 20. New lab scenario `vine@t` grows the level's
  first natural vine through `VineLadder.activate` and stands Kirby at its seed.
- Live (separate data root `repros/vine-20261005`): `232657` climbs at ~99 px/s
  and stands on the ledge; `232901` climbs, hangs, descends, reaches the ledge
  and climbs back down from it; `233002` ordinary ladder unchanged. Saves
  unchanged in all three. Not run: hanging vines, stomp from a vine, a grown
  vine after loading a save.
## 2026-10-05 — spat and discarded stars as host projectiles (#20)

- Measured in the bridge, the original stars are:
  - the spit star, `0x020BD718`: 52 damage, bursts on its first enemy and on walls;
  - the big star, `0x020BDD08`: 60 damage, passes through enemies, bursts on walls;
  - the discarded ability star, `0x02166A54`: 0 damage, passes through enemies,
    bounces off walls and floors.

  Kind 60 is shared with copy attacks, so the update routine identifies each star.
- `src/runtime/KirbyStars.cs` reads the stars from the native hit list after every
  source frame. Paired enemies keep the original star damage (proxy HP delta), and
  a star hit now also staggers them. Unpaired enemies are struck by the host with
  the star's damage word. The discarded star strikes every enemy for 52, an owner
  request and a deviation from the ROM. The host strikes an enemy once per star;
  for paired enemies the original collision decides how often. The stagger is the
  host dive landing's affect 16 for 0.7 s, a bump of 0.2–0.3 and Stun (affect 8)
  for 1 s with diminishing returns, read from `hlboot.dat`.
  `src/core/StarProjectiles.cs` holds the rules.
- Native `star_spit`, `star_spit_wall`, `star_big` and `star_ability` pass. The
  full suite has 68 cases; the docs previously said 61, but main already had 64.
  Core rule test: 188 assertions.
- Live `20261005-221418` (separate root `repros/stars-20261005`) passed all five
  phases at 76/76 HP:
  - Each of the paired spit, unpaired spit and discarded-star hits dealt 182
    damage, stunned the Zombie for about a second and set affect 16.
  - A spit star burst on a real wall face, and a discarded star bounced off it.
- Found: the bridge's facing byte read 0 in all 7151 traced frames, including
  left-facing ones, so `hero.dir` may not follow Kirby's turns. This was not fixed
  here.

## 2026-10-05 — Kirby walks through exit doors (#17)

- Owner recording: after Leave at an exit door, Kirby glided into the doorway
  and faded in one frozen standing frame. `OnFrame` stopped the source during any
  host cinematic and only redrew the last frame.
- While a cinematic moves the hero sideways, `CinematicWalk` (`src/core/`) holds
  the matching DS direction, and `StepCinematicWalk` steps the original
  controller. The host keeps position and fade. The door's `stairs` phase lifts
  the hero where the host has no floor, so the original walks on a flat floor
  at the starting height instead. The level-change cleanup moved into `EnterLevel`
  for both step paths.
- Lifecycle probe: starts 1.5 cells beside the exit, records `door-cinematic`,
  and accepts `KIRBY_LAB_EXIT`.
- Source checks: `python3 scripts/check.py` passes (177 core assertions, 7 new),
  and the Release runtime builds with 0 warnings.
- Live (`repros/door-walk-20261005`): `214244` with host terrain showed Walk, then
  Fall on the doorway steps. `214713` with the flat floor stayed in original Walk
  for 148 of 149 polls, and the frames show walk poses until the fade. Saves were
  unchanged.
- Not run: native bridge tests (no native change), drop-type exits, leftward
  cinematic walks, and normal play with real input.

## 2026-10-05 — HUD ability slot (#18)

- While copied, the HUD weapon slots that fire the ability (non-shield, as in
  `KirbyInput`) show the original bottom-screen ability icon in a host colorless
  slot. Base form and discard restore the real equipment icon.
- Icon: found in the live sub engine as a 32×32 4bpp OBJ at (4, 4) (Wheel state:
  OAM 20, tile 52, palette 8). `src/core/AbilityIcon.cs` decodes it.
  `KirbyHud` accepts it after 30 unchanged source frames, which skips the original
  copy flash and hurt icon seen in live run `215110`. It is drawn in whole screen
  pixels near the host's `Skill.ICON_SIZE`.
- Frame and colors: host `Skill.useItem` with a cached `StartSword` + `Colorless`
  stand-in (decompiled `setBgColors`, `useItem`, `onResize`), then the real item is
  restored as the slot item. Colorless backgrounds follow the highest scroll stat;
  ties show Brutality red.
- Source: decoder tests (OBJ lookup, 2D/8bpp rejection, flips, palette,
  transparency). `python3 scripts/check.py`: 20 Python, 6 oracle-parser and 177
  core assertions; Release build 0 warnings/errors.
- Live checks: `215606` (scrolls 4/0/2) and `215830` (no scrolls, discard). Both
  confirmed the colorless Sword slot and an untouched shield slot. `215830` also
  confirmed the restored weapon slot. Real saves were unchanged.
## 2026-10-05 — shield parries in Kirby's facing direction (#19)

- Owner recording: the host shield and parry arc stayed on Kirby's right while he
  faced left. `hero.dir` came from player+0x15, which never changes with facing,
  so the host always saw Kirby facing right.
- `hero.dir` now follows bit 31 of player+0x6C (`NdsState.FacesLeft`). That bit
  matched the walking direction on every moving frame of live trace `214458`.
  The native bridge test now walks left and right and checks the bit.
- Live (separate data root `repros/left-parry-20261005`): run `215025` mirrored
  the host block for left-facing guards; run `215320` parried a real Zombie hit
  from the left (counter callback, `Block`, no damage); run `215401` let a hit
  from behind through. Saves unchanged.
- Not resolved: raising the shield while holding the opposite direction. The
  original guard keeps Kirby's facing while the host turns its shield.

## 2026-10-06 — Kirby stays on descending elevators (#33)

- Owner recording 2026-10-06 01:15: after #28, Kirby left descending elevators
  once they sped up and fell behind them. Lab run `20261006-011803` (no inputs)
  reproduced it: at about 10 px a tick the host carry missed him for one tick,
  and the original's 3.5 px a frame fall never caught up.
- `ElevatorRide.Over` now carries Kirby itself, up or down, when his feet were
  within the host's 2 px of the surface on the previous original frame and the
  original is not moving him upward (`lastVy < 0` is a jump or float).
- Live `012037`: full descents and climbs at up to 12.4 px a frame, all frames
  standing, feet exactly on the surface; the runtime carried him 74 times.
  Live `012131`: jumps still leave a moving elevator in both directions.
- Core rule tests: 225 assertions; `scripts/check.py` passes; Release build has
  zero warnings. Not run: `tools/nds_tests.py` (no native change), other
  elevator types and generated elevator levels.
- Owner recording 2026-10-06 12:26 (play build with this branch): Kirby sank into
  the platform at the bottom stop, then rebounded and landed with stars. A copy
  of the owner's save in `repros/elevator-real-20261006`, with the host frame
  rate stalled to 25–100 fps (`KIRBY_LAB_FRAME_SLEEP_MS`), reproduced it on a real
  64-cell elevator (`124646`, `124727`). Its stop sits in level floor; a full-speed
  overshoot took the surface 4.9 px into that solid row, the lifted window put
  Kirby inside it, and the original pushed him out a cell above the elevator.
  `ElevatorRide.Effective` now gives the floor's top while the surface is sunk
  into solid floor under Kirby; 17 later runs covered 27 such steps with Kirby
  standing throughout. 229 core assertions.
- Owner recording 2026-10-06 19:50: rides worked, but Kirby was drawn above the
  platform going up and sunk into it going down. The host draws the platform
  up to a tick behind its physical surface (13 px measured, run `195232`).
  Kirby standing on an elevator, with his equipment, is now drawn with the same
  lag (`SetDrawOffset`, set from an `Elevator.postUpdate` hook); drawn feet stay
  0.5 px from the drawn platform in four jittered real-level runs.
## 2026-10-06 — Kirby climbs at the original speed (#48)

- Owner recording (19:53, Sword Kirby on a Vine-rune vine): climbing is too
  slow. Its camera scroll put Kirby at about 95 px/s up and down: `SpeedMatch`
  scaled the original climb (139 px/s up, 165 px/s down) down to the Beheaded's
  97.5 px/s average, the only motion it slowed. The Beheaded climbs in
  front-loaded 26 px pulses (13 px in the first host step), so his average
  feels quicker than a steady 97.5 px/s.
- `SpeedMatch` now leaves the climb (`0x916`) at the original's own motion. The
  hand-over-hand cycle matches the movement again. Recorded as a deviation from
  the host speeds in `docs/GAMEPLAY_FIDELITY.md`.
- Source: `python3 scripts/check.py` passes (221 core assertions, Release build
  0 warnings/errors); `tests/speed` passes all 26 checks, including the climb
  at 139 px/s up and 165 px/s down, and Fire's and Plasma's head effects staying
  on a climbing Kirby.
- Live (separate data root `repros/climb-20261006`, `vine` setup, W then Down
  twice): run `20261006-200031` climbed at 138.8 px/s up and 165.0 px/s down per
  original frame, stood on the ledge at the vine's top and climbed back down.
  Run `195931` hit its 30 s limit during boot, 2 s into play (139 px/s up).
  Saves unchanged, no fatal log. Not run: ordinary ladders and chains at the new
  speed, hanging vines.
## 2026-10-06 — carrying ability moves keep their lead over the run (#40)

- Owner recordings 12:19 (Wing) and 12:30 (Wheel): Condor Head and Wheel's roll
  moved slower than Kirby's matched run. Every ability move runs under action
  142C, which the speed match left at its original speed, while the run is 1.91
  times the original dash.
- `SpeedMatch.ActionFilter` marks an ability move that starts from a dash, or
  becomes faster than the original dash (528), as carrying Kirby until the
  action ends; its motion in its own direction is scaled like the run. Floor
  frames follow the surface, hops stay in the air, walls stop it a pixel short.
- Survey of all 24 abilities (standing, charged, Down+B, mid-air, dash) on the
  original controller: every dash attack keeps its original duration and its
  original peak-to-dash ratio (Wing 1.94, Cutter 2.73, Suplex 3.33); Wheel,
  Jet's bursts and Suplex's grab lunge scale from any start; nothing else
  changes. `tests/speed` passes 32 checks; `scripts/check.py` passes (225 unit
  assertions, runtime build).
- Live, separate data root `repros/dash-attack-20261006`: Wing `124027` dash
  3.94 then Condor Head 7.64 px a frame; Wheel `124135` dash 3.94 then roll
  6.69 px a frame, stopping outside a wall. No fatal logs. Not run live: other
  abilities, slopes, ledges, Wheel turning. Details in `docs/GAMEPLAY_FIDELITY.md`.

## 2026-10-06 — Ability descriptions (#45)

- The owner asked whether the pause card says what each ability does; it did not.
  The owner chose mod-written text over ROM text, because ROM text names DS
  buttons.
- `src/core/AbilityDescriptions.cs`: a short summary for each of the 24 abilities,
  naming only well-established original moves in the mod's controls, plus a
  shared "Skill 1 or this button: attack. Skill 2: discard." line. Tests cover all
  24 IDs, length and host-markup characters.
- `KirbyHud` hooks `InventItem.getGameplayDesc` for the colorless stand-in, so
  the host's own card layout shows the text.
- `python3 scripts/check.py` passes (226 core assertions; Release build 0
  warnings/errors).
- Live checks used a local-only merge with #41's branch: `125345` Fire, `125617`
  Yo-Yo, `125747` Stone and `125923` Fire (final text). Real saves were unchanged.
  Not every listed move has been checked live.
## 2026-10-06 — Pause equipment card (#41)

- The owner's recording showed the pause screen listing "Rusty Sword I" while
  Kirby had Fire.
- `ui.pause.DefaultPause` builds each card as a `ui.NewItemDesc` from the slot
  item (DefaultPause.hx:210–218). The card's icon is its own `ui.hud.Skill`; its
  name and main info are its first two direct `ui.Text` children.
- `KirbyHud` hooks `NewItemDesc.onResize` for pause cards of an equipped
  non-shield weapon while copied:
  - It builds the card from the colorless stand-in, then restores the real item.
    This gives the host colorless card and affix line, and the original icon
    through the `Skill.useItem` hook.
  - It renames the card in the host markup (`{Fire@CL}`) and replaces the DPS
    line with "Copy ability".
- Lab: `--probe-pause` opens the pause screen while copied and dumps each card's
  tree.
- `python3 scripts/check.py` passes (221 core assertions; Release build 0
  warnings/errors).
- Live checks:
  - `124012`: Fire card in the colorless frame; shield and collar cards unchanged.
  - `124145`: base form keeps "Rusty Sword I / 138 DPS".
  - Real saves were unchanged.
## 2026-10-06 — HUD slot stat color (#36)

- The owner reported a red colorless ability slot while Survival was highest.
- Cause: the host's `Skill.useItem` same-item-kind path (Skill.hx:540–545) calls
  `setBgColors` before it assigns the new item, and `setBgColors` takes the tier
  from the slot's current item. The `StartSword` stand-in matched the real starting
  sword, so the colors came from that Brutality sword. The #18 runs could not show
  this.
- Fix: `KirbyHud` clears the slot item before passing the stand-in, so the host
  takes its full path (item assigned first).
- The HUD fixture now records the background shader colors.
- `python3 scripts/check.py` passes (221 core assertions; Release build 0
  warnings/errors).
- Live checks:
  - `013806` (tiers 1/1/5): Survival green `17ce75` + gray `b2c0dd`.
  - `013956` (tiers 1/5/1): Tactics purple `a051ff` + gray.
  - In both, discard restored the sword's red. Real saves were unchanged.
## 2026-10-06 — Ability items replace weapon and skill loot (#55)

- Owner request (#54): Kirby uses only his own moves, copy abilities and a
  shield, so host weapons and skills on the floor are useless to him. Step 1 of
  four: they become colorless Kirby ability items, taken only by the original
  inhale and swallow. Owner decisions for later steps: enemy copies are base
  items, Elites give high quality, a dropped star keeps its item for 7 s (#56).
- Host findings (crashlink on the lab `hlboot.dat`): `ItemDrop`'s constructor
  calls `initItem` before `super`; `Weapon.create` throws for an unknown item ID;
  `HUD.refreshItems` only redraws each slot's current item, while
  `Hero.hudInitItems` re-reads the inventory; Dead Cells rarity is affix-based
  (`Colorless`, `QualityUp`, `Legendary`, rolled by `LootGen.finalizeItem`);
  shops are `ShopBooth` entities; `Assets.getItem` honors the CDB icon size.
- Build: `tools/capture_ability_icons.py` captures the 24 original bottom-screen
  ability icons headlessly; `tools/build_outfit.py` adds 24 `KirbyAbility<Name>`
  item rows, starting-sword `weapon` rows and 24×24 icons to the private outfit
  pack.
- Runtime: `KirbyAbilityItems` (replacement in `ItemDrop.initItem`, the weapon
  object, no pickup, the item card's text, slot sync); `KirbyEnemies` pairs floor
  ability items with proxies carrying their copy byte in base form and moves them
  only with the original inhale (flag 32); `MouthfulItems` in `src/core` settles
  the mouthful (the swallow takes the copied ability's item, a spit returns them
  where they lay).
- Source: `python3 scripts/check.py` passes (26 Python tests including 6 new for
  the item rows, 267 core assertions, Release build). `tools/nds_tests.py` was
  not run (no native change).
- Live (separate data root `repros/ability-items-20261006`, `--input-plan items`):
  final runs `223826` (Paint), `223958` (Beam) and `224129` (Ice) passed every
  check; saves unchanged, no fatal log. Iterations fixed the HUD refresh
  (`215522`), the spat item's bounce (`221819`) and a spit star's recoil pushing
  a returned item (`223514`). `221525` captured the floor item tip.
- Not run: the short-press hint, long-press recycle, an enemy copy filling the
  slot, mixed item and enemy mouthfuls, save/resume with an ability item, other
  biomes, chests, timed doors, shops and the forge.
## 2026-10-06 — Suplex's dash attack bursts doors (#50)

- Owner recording 21:05: Suplex Kirby's dash grab bounced off a closed door and
  left it intact.
- Cause: doors pair with original objects that take the boss resistance (mask
  `0x53`: no grab, Cook or inhale). Suplex's grab only captures, so it never hit.
- Fix: `HostStrikes.Grab` reports Suplex's grab lunge (carrying Kirby at 2 px a
  frame or more) to the host's `Hero.canBreakDoors`, as the slide already does.
  Doors keep their grab resistance. `HostStrikes.Slide` now holds the slide rule.
- `python3 scripts/check.py` passes (248 core assertions; Release build 0
  warnings/errors). `tests/speed` passes, with two new checks: the strike covers
  the dash grab's 24 lunge frames only, and holds until the lunge is a pixel from
  a wall.
- Live (`repros/suplex-door-20261006`): `211833` broke a real door on the lunge's
  sixth frame; control `211959` without the hook bounced off a door and left it
  shut. Not run live: other Suplex lunges, doors on Kirby's left.
## 2026-10-06 — Kirby climbs as much faster as he runs (#51)

- Owner recording (21:07, base Kirby on a chain): still too slow after #48. Its
  camera scroll put Kirby at about 137 px/s up, the original speed.
- `SpeedMatch` now speeds the climb (`0x916`) up by the run's factor
  (`ClimbScale` = `GroundScale`, 1.91×): 265 px/s up and 315 px/s down. The
  original climbs up a little faster than it dashes; the matched climb keeps
  that ratio to the matched run. The added climb stops a full step short of
  the ladder's ends (a one-way top counts as ladder), ceilings and floors,
  looking four cells ahead, so the original makes its last steps itself.
- Source: `python3 scripts/check.py` passes (248 core assertions, Release build
  0 warnings/errors). `tests/speed` passes all 36 checks. The new ones compare
  the matched climb with the original controller at a one-way top, a ladder
  under a solid block, a ladder ending in open air (the original stands for a
  frame and falls off) and a hanging ladder's bottom; each lets go within one
  original step of where the original does and ends in the same place.
- Live (separate data root `repros/climb-20261006`): vine run
  `20261006-211757` and ordinary ladder run `20261006-211905` climbed at
  265.2 px/s up and 315.3 px/s down per original frame, stood on the top and
  climbed down to the floor. In the vine run a natural Grenader's grenade
  (9.6 s) and its recoil overlapped the second Down. Saves unchanged, no fatal
  log. The movement row in `docs/GAMEPLAY_FIDELITY.md` had two copies after
  merges; they are now one.
- Not run: chains live, hanging vines, the climb animation's pace (its
  hand-over-hand cycle keeps the original tempo, now slower than the motion).

## 2026-10-07 — Far shots stay drawn past the DS screen edge (#63)

- Owner screencasts (2026-10-06 23:33 and 23:36): how Plasma charges, then
  "why does the firing cut abruptly?" The charged ball burst into sparks a
  short way from Kirby in open air.
- Native bridge: the ball is not removed. On an open source floor it kept
  flying (still moving 336 px out), but the original OAM builder `0x020100A0`
  drops OBJ pieces outside screen x −63…256, the DS's own screen, and the
  renderer draws only OAM. Uncharged Plasma shots end at 76 px on the ROM's
  own stage too (original). The charged ball passes through walls in the ROM.
- `widen_obj_cull` in `src/nds/lib.rs` patches the builder's two horizontal
  compares in emulated RAM to keep screen x −127…380, and `NdsObjRenderer`
  decodes OAM x 384–511 as the left side. Survey of all 24 copy abilities
  (tap, hold, dash attack, left; charged Plasma): cut before and now drawn
  further were charged Plasma (128 → 252 px), Ninja's star (128 → 248),
  Beam's wave (126 → 171) and Sword's beam (125 → 146).
- Source: `python3 scripts/check.py` passes (278 core assertions, two new for
  the renderer's x decoding; Release build 0 warnings/errors).
  `python3 tools/nds_tests.py`: 75/75, with new `far_shot_draw` (pieces to
  252 px; fails at 128 px on the previous bridge). `python3 tools/verify_renderer.py`:
  29 sequences, 3,046,706 pixels, 0 mismatches, 0 unsupported. A/B of the old
  and new bridge over 12,954 frames of all 24 abilities: game state, actors,
  sub OAM and in-screen OBJ pieces identical. All 31 proxies spread across
  ±250 px used at most 57 of 128 OAM entries.
- Live (separate data root `repros/far-shots-20261006`): run `20261006-235639`
  drew a charged Plasma ball 246 px left of Kirby. Fired right, the ball sparked
  at a level wall about 190 px away and went on hidden behind its tiles. Saves
  unchanged, no fatal log.
- Not run: Ninja, Beam and Sword far shots live, spit stars, an enemy hit beyond
  128 px. Limit: about 252 px of reach; a wider host view still loses a shot
  near its edge.
## 2026-10-06 — Challenge rewards and shops offer ability items (#61)

- Owner reports (Promenade of the Condemned and a kill-count room): altars,
  timed rewards and shops still offered host weapons and skills. They are
  `ItemAltar`, `TimedItem` and `ShopBooth`, which take their item in their own
  constructors, not through `ItemDrop`.
- Their item is replaced in `initGfx`. Taking one follows the host's own step
  (`ItemAltar.disable(true)` from `cine.TierItemFound.destroyItem`, the timed
  item's take callback, `ShopBooth.buy`) and drops the ability item to be
  inhaled. `isUsefulFor` is true for Kirby so a shop short of gold says so.
- Live `20261006-235600` (`rewards` plan): all three converted, taken and
  dropped; 1000 gold charged. Saves unchanged.
## 2026-10-06 — Pause card shows the real ability item (#62)

- Owner report: a forge reroll's grenade passive on Plasma disappeared from the
  pause card in the next level. The #41 card path drew any copied slot from a
  colorless stand-in sword, hiding the real ability item's level and affixes.
- `KirbyHud` now draws a slot holding an ability item from that item.
- Live (`repros/ability-items-20261006`): `234721` saved Wheel with the host
  `Grenade` affix; `235012` resumed with the affix still on the slot item, and
  its pause card listed it. Saves unchanged, no fatal log.
- Source: `python3 scripts/check.py` passes.
## 2026-10-07 — Ability item quality scales copy damage (#56)

- Owner report: "Beam II" and "Suplex III" showed no stat. Host research: an
  item's name is its level as a numeral plus one "+" per `QualityUp`;
  `InventItem.getDamageBonus` turns level, "+" and `Legendary` into the weapon
  damage bonus (checked against the owner's "Rusty Sword I+ / +33% damage").
- Floor ability items now keep the replaced item's "+" and `Legendary`; copy
  damage is multiplied by 1 + the slot item's host bonus; the card shows
  "+N% copy damage". An Elite's copy is Legendary (×2.25 at level I), replacing
  the separate ×2 (`KirbyRules.EmpoweredDamageMultiplier` removed).
- The 7-second dropped star moved to #68 (needs ROM work).
- Source: `python3 scripts/check.py` passes (284 core assertions).
- Live: `20261007-003043` (++ item, +67%), `20261007-003221` (Elite, ×2.25).
## 2026-10-07 — Crash hits every enemy on the Dead Cells screen (#69)

- Owner recording 00-25-03: Crash left a Zombie and an archer lower on screen
  untouched. Only enemies with a source object near Kirby could be hit, and the
  original only collides objects within 32 px of its 256×192 screen.
- Measured on the bridge: Crash, Paint and Mike each make one Kirby-owned
  slot-2 record, 512×512 around him, for one frame (160, 70, 40/60 damage).
- `ScreenAttacks` (core) and `KirbyScreenAttacks` (runtime): the host strikes
  every enemy in its viewport that the original cannot reach, with the record's
  own damage converted as usual. New `screen` lab plan.
- Live (`repros/screen-attack-20261007`): Crash `005031`, Paint `004842`,
  Mike `004639` killed all four Zombies in view; the off-screen one was
  untouched. Saves unchanged, no fatal log. A Zombie spawned just outside the
  view died ~90 frames later from a sourceless host hit in every run (Mike
  too); not caused by this change, not investigated.
- Source: `python3 scripts/check.py` passes (286 assertions).

## 2026-10-07 — Far shots drawn from the original sprite records (#67)

- Owner screencast (00:22): after #63 the range improved, but the charged
  Plasma ball was still cut in open air. From the clip, the owner's 1080p view
  shows about 425 host px either side of Kirby; #63's 9-bit OAM limit drew to
  about 252 px (about 300 px with the DS camera's lead).
- Removed #63's `widen_obj_cull`. `build_far_objects` in `src/nds/lib.rs`
  reruns the original OAM builder's (`0x020100A0`) assembly over the same
  sprite records after each frame, accepts it only when its on-screen entries
  equal the ROM's shadow OAM, and keeps the culled pieces with full positions;
  `kssu_far_objs` reports them when the hardware OAM equals that shadow OAM.
  `NdsObjRenderer` draws them in original OAM order on a 1024×512 canvas and
  reads OAM over the builder's own range (x −63…256, y −63…192).
- Source: `python3 scripts/check.py` passes (282 core assertions, six for the
  renderer's OAM range, filler, far pieces and order; Release build 0
  warnings/errors). `python3 tools/nds_tests.py`: 75/75; `far_shot_draw` now
  requires culled pieces at least 380 px out on at least 40 frames with the
  original OAM cull (390 px, 43 frames). `python3 tools/verify_renderer.py`:
  29 sequences, 3,046,706 pixels, 0 mismatches. Bridge pieces equal a Python
  port of the builder on all frames of all 24 abilities; A/B against the
  bridge before #66 identical over 12,954 frames.
- Live (`repros/far-shots-20261006`, run `20261007-004025`): the whole ball
  drawn 300 px left of Kirby, then cut only by the lab view's edge at 354 px.
  Same gameplay rate as the #63 run. Saves unchanged, no fatal log.
- Not run: the other far attacks live, spit stars, an enemy hit beyond 128 px.
  Limit: 512 px horizontally and 256 px vertically from Kirby.

## 2026-10-07 — Ability item affixes act on copy attacks (#57)

- Owner report (screencast): an ability item's arrow passive never fired.
- Host flow: `Weapon.prepare` clears `affixesDone` and throws the Grenade affix,
  `Weapon.onExecute` runs `executeAffixes` (arrows, on-use shield); hits carry
  their item's affixes through `AttackData.useItemAffixes`.
- `KirbyDamage.Hit` makes the copy's ability item each hit's source item; each
  copy attack press runs the item's weapon prepare/execute affix steps; floor
  ability items keep the replaced item's affixes the host allows.
- Live `004739`, `004958`, `005200`: arrows and bleed on copy attacks, affix
  carry-over. Saves unchanged.
- Source: `python3 scripts/check.py` passes.
## 2026-10-07 — Stomp from the top of a vine lands (#74)

- Owner recording 01:24: S then Space on the ledge at a vine's top started the
  stomp, but Kirby slid down the vine in the climb pose and hung at its foot
  until the 3 s safety cancel.
- Cause (bridge, ready2 seed): Down on a ladder top starts the original climb
  inside the top cell, which ignores A for its first frames. The stomp's single
  kick-off press was lost and the host descent carried the still-climbing Kirby
  to 4.5 px above the floor, short of any landing.
- The stomp now passes the ladder top in the row Kirby's feet climb in, so the
  original lets go at once; while it still climbs, A is pressed every other
  frame and no host descent is added. New bridge case `ladder_top_stomp`.
- Live (separate data root `repros/vine-stomp-20261007`, `vine` setup):
  pre-fix `20261007-013859` reproduced the hang twice; fixed `014012` landed
  both vine-top stomps (12.6 and 10.2 cells). Saves unchanged, no fatal log.
- Not run: stomps from ordinary ladder or chain tops live, hanging vines.
## 2026-10-07 — Shift bound to Dodge starts Kirby's slide (#75)

- Owner request: Shift and Ctrl both made Kirby guard; Shift should do the
  slide Down + jump (S + Space) does. The recorded binding arrays bind Dodge
  (action 1) to Shift (16) and Ctrl (17).
- `KirbyInput` splits Dodge by key: Shift slides, every other Dodge binding
  guards. `SlideKey` sends Down + A for one source frame per press on the
  ground; the original controller starts the slide.
- ROM measurement: one Down + A frame starts the full 36-frame slide whether
  Down stays held or not, and same-frame Down + A slides from standing and
  running.
- Source: `python3 scripts/check.py` passes. Native: `tests/speed` 42/42 pass,
  including four new Shift checks.
- Live (`repros/shift-slide-20261007`, run `20261007-014554`): Shift slid
  130.7 px right and, after turning, left; Ctrl guarded (`0x0A17`); Shift held
  in the air did nothing; held standing it slid once. Saves unchanged.

## 2026-10-07 — Zombies no longer bite after the original recoil (#78)

- Owner report: Kirby sometimes took a hit with the original knockback right as
  he killed a Zombie, with no visible attack.
- Cause (bytecode review of `hlboot.dat`): the Zombie's leap keeps cooldown key
  `1226833920` set, and `Zombie.fixedUpdate` applies its jump hit while the key
  is set. The original recoil's control skipped the Zombie's updates without
  ending the leap, so the leap resumed on release and bit Kirby.
- `KirbyEnemies` now unsets the leap key while the original holds an enemy.
  Record: [Leaps held by the original recoil](docs/GAMEPLAY_FIDELITY.md#leaps-held-by-the-original-recoil-78).
- Headless original-ROM scratch checks ruled out the source side (detached
  enemies, all 24 abilities' defeat reactions, projectile proxies, health sync,
  hurtbox union).
- Source: `python3 scripts/check.py` passes. Not run live.
- Later the same day the owner was hit again just after killing a Zombie, on a
  staged integration build that included the leap change. The cause is not yet
  known. Added `KirbyDamageLog` (`Kirby damage:` lines in the DCCM log) for
  host hits, original health drops, host-only health loss and copy-hit kills.
  Its runtime DLL was swapped into the owner's staged integration build
  (`de34d4f` + this commit). The previous DLL was backed up to
  `lab/checkpoints/before-damage-log-20261007-122317`. Not run live by the agent.
## 2026-10-07 — Claiming a scroll no longer crashes (#80)

- Owner report: claiming a green (Survival) scroll ended the game with
  `NDS bridge returned -3` from `KirbyEnemies.Prepare` (play run
  `20261007-114618`).
- Cause: claiming a scroll is a host cinematic that walks the hero onto it, and
  `StepCinematicWalk` advances the original with enemies still paired. An inhale
  under way finished its swallow there. Only `KirbyEnemies.Complete`, after a
  normal step, acknowledges an ended source object; the next normal step's
  `Prepare` rebound the slot first, and the bridge refused.
- Fix: `Prepare` skips a slot whose source object is already swallowed or gone;
  `Complete` acknowledges it after that step as usual (host enemy removed, copy
  kept).
- New `--input-plan scroll` probe (`LabScrollProbe`, `KIRBY_LAB_SCROLL_INHALE`).
  Live (`repros/scroll-claim-20261007`): the inhale variant reproduced the crash
  before the fix (`115757`) and passed after it (`115950`); the walk variant
  passed (`120114`). Saves unchanged.
- Source: `python3 scripts/check.py` passes. `tools/nds_tests.py` not run
  (`src/nds` unchanged).

## 2026-10-07 — Legendary ability items get the legendary HUD frame (#82)

- Owner report (two screenshots): "Parasol II-L" had the gold legendary card on
  the pause screen but a gray colorless HUD slot.
- Cause: `KirbyHud` drew every copied HUD slot from a Colorless-only
  `StartSword` stand-in. Host `Skill.setBgColors` (`ui/hud/Skill.hx:395-404`,
  read from the lab `hlboot.dat` with crashlink) uses `skillSlotLegendary` for
  the `Legendary` affix before `skillSlotColorless`.
- Fix: a slot holding a real ability item is drawn from that item (as its pause
  card since #62); the stand-in stays for other weapons and empty slots. New lab
  option `--probe-legendary` (`KIRBY_LAB_TEST_LEGENDARY`).
- Source: `python3 scripts/check.py` passes (278 core assertions; Release build
  0 warnings/errors).
- Live (separate root `repros/legendary-hud-20261007`): `20261007-124255`
  copied Sword with a Legendary `KirbyAbilitySword`; the HUD slot showed the
  original icon in `skillSlotLegendary` with Survival green and gold. Saves
  unchanged, no fatal log.
- Not run: a non-Legendary ability item after this change (the code path is the
  same `useItem` call), the owner's Parasol, resolutions other than 1024×768.

## 2026-10-08 — Copy attacks open the Scratch the wall secret (#84)

- Owner report: a copied ability's attack on the "Scratch the wall" reward did
  nothing; only R opened it.
- Cause: the wall is Dead Cells `en.inter.HiddenBlock` (`hlboot.dat` string
  table: "Gratter le mur", `wallSecretBroken`, `wallSecretRune`). Copied attacks
  paired only with the gold-deposit objects (`KirbyEnemies.Eligible`), so the
  wall never got a proxy and no attack reached it.
- Fix: `HiddenBlock` joins `Eligible`. A copy attack that reaches it calls the
  host's `canBeActivated` and `onActivate` with Kirby, the R key's path, and the
  wall drops its reward. Base-form contact does nothing. Live probe:
  `KIRBY_LAB_BREAKABLE_TARGET=hidden` (and `hidden-base`) in `LabBreakableProbe`.
- Source: `python3 scripts/check.py` passes (302 core assertions; runtime Release
  build 0 warnings, 0 errors). `tools/nds_tests.py` not run (`src/nds` unchanged).
- Live (separate root `repros/hidden-block-20261008`, staged with
  `outfit-build-55` and `assets-final`): control with no pairing change, copy
  Sword beside a real wall, no hits and no activation; final code, one
  activation and a `SmallMeat` dropped beside the wall; base form, no activation,
  wall at 3 life. Saves unchanged, no fatal log. See `docs/RUNTIME_LAUNCH.md`
  "Secret walls (#84)".
- Not run: an R-key press beside a wall as a parity control, copy forms other
  than Sword, rewards other than food, and the owner's own play session.

## 2026-10-08 — Conjunctivius tentacles unpair while burrowed (#86)

- Owner report (recording and play log `20261008-000039`): Conjunctivius kept
  its shield; tentacle `BeholderTtcl#599447` never died, and Sword sparks hit an
  invisible spot by the wall.
- Cause (crashlink, `hlboot.dat`): a burrowed `BeholderTtcl` is unhittable
  (`_isInGround`, affect 5, zero size), but `KirbyEnemies.Prepare` checked
  `canBeHitBy` only when pairing. The burrowed tentacle stayed a 1×1 original
  enemy; copy hits were rejected by the host, and the original recoil could hold
  its AI, which is what makes it surface.
- Fix: `Prepare` releases a paired enemy once the host stops letting the hero
  hit it (not projectiles, floor ability items or swallowed objects); it pairs
  again when hittable.
- New `--input-plan tentacle` probe (`LabTentacleProbe`). Live
  (`repros/conjunctivius-20261008`, empty save): before the fix `004843` showed
  317 paired-while-burrowed tentacle-frames, 47 of them under original control;
  fixed `004529` killed all three with none. The permanent stall itself was not
  reproduced. Saves unchanged.
- Found while staging: the default `outfit-build/res.pak` (Oct 4) predates the
  ability items and fails new runs; the play build uses `8e44122d…`.
- Source: `python3 scripts/check.py` passes (302 core assertions; Release build
  0 warnings/errors). `tools/nds_tests.py` not run (`src/nds` unchanged).
## 2026-10-08 — critical hits for copy attacks (#88)

- Design (owner): the cooldown mutations do nothing in a Kirby run, so they become
  timed crit bonuses (#89); every copy attack counts as melee (#91); weapon
  mutations leave the choice list (#90). All need Kirby to crit first.
- `CopyCritical` (`src/core/`) holds chance, factor, timed bonuses and a sure
  crit. `KirbyEnemies.Prepare` rolls per paired enemy each source frame and folds
  the factor into the source-HP pairing, so lethality stays in agreement without
  native changes. `KirbyDamage` upgrades a surviving `Normal` hit to the host's
  `Critical` through `AttackTargetImpl.updateHitResult`: `AttackUtils.hitInit`
  resets `hitResult` to `Normal`, so it cannot be set beforehand. Host bytecode
  read with crashlink (`updateDamages` applies `critMul` only on `Critical`; the
  mod's `applyAttackResult` hook already replaces the damage, so ×1.5 lives only in
  the mod multiplier). Decompiled output stayed in the session scratchpad.
- Config: `CopyCriticalChance` (0) and `CopyCriticalDamage` (1.5).
- Source: `python3 scripts/check.py` passes (core assertions incl. the new crit
  pairing; Release build 0 warnings/errors).
- Live (separate root `repros/crit-88-20261008`): chance 1 gave host `Critical`
  hits of 94 and 26; chance 0 gave the old `Normal` 63 and 52.
- Not run: partial chance, sure crit, bosses, Ice, stars, screen attacks.

## 2026-10-08 — cooldown mutations become crit bonuses (#89)

- Read the host's mutation code (crashlink, outputs kept in the scratchpad): the
  five cooldown mutations act in `HeroAttackSource.postHitResultUpdate` (parry, crit,
  Heart of Ice: affects 15/8/23/133 within 4 cells) and `Mob.onDie` (kills).
  `PerkSelect.addPerk` and the pause screen read `InventItem.getGameplayDesc`.
- `CriticalMutations` and `MutationDescriptions` (`src/core/`) hold the rules and
  texts; `CopyCritical` gained keyed stacks, an owned chance, a timed sure crit
  and a per-target extra chance. `KirbyMutations` reads the host inventory, hooks
  `getGameplayDesc` and `Hero.onPerfectParry`, and tests Heart of Ice's target.
  Numbers are in `config.json` (`CriticalMutations`).
- Source: `python3 scripts/check.py` passes (340 core assertions; Release build 0
  warnings/errors).
- Live (`repros/crit-88-20261008`): texts, ownership and Killer Instinct's +5% and
  6 s ending verified; Blind Faith, Heart of Ice, Hunter's Instinct and the menu not run.

## 2026-10-08 — mutations on copy attacks (#91)

- Host reading (crashlink): `Mob.onDamage` calls `onDirectHitFromHero` for a hero
  hit with no carrier and no ranged tag, so a copy hit is already a melee hit for
  Combo, kill rewards and heals; Initiative and Counterattack run in
  `HeroAttackSource.extraDamageEffects` as separate extra hits (Counterattack reads
  effect 108). What was lost is the multiplier: `KirbyDamage` replaces `finalDmg`.
  `computePerksBonusMul` and `computeAspectsBonusMul` are pure; `computeBonusMul` is
  not (it removes effects 50 and 83), so it is left alone.
- Plan change: the earlier idea of hand-written Support/Parting Gift/Networking
  rules was dropped. Support, Parting Gift and Networking are hidden in #90.
- `KirbyHostBonuses` reads `computeExtraBonusMul` for a throwaway attack with the
  close-range tag, folded into the source-HP pairing. `CopyAttackWording` rewrites
  the host's melee/ranged text.
- Source: `python3 scripts/check.py` passes (348 core assertions; Release build 0
  warnings/errors).
- Live (`repros/crit-88-20261008`): `20261008-022631`, `-022811`, `-023052` as in
  RUNTIME_LAUNCH.md. Effects were set by the fixture, not earned.
- Not run: Gastronomy, Extended Healing, Tainted Flask, Demon Strength, bosses, the
  menu, effects earned in play.
## 2026-10-08 — mutations Kirby cannot use leave the menu (#90)

- Host: both `PerkSelect.isVisible` and `PerkMaster.isVisible` call
  `itemMeta.hasUnlockedItem` (plus the custom-game lock); `PerkMaster.getAvailablePerks`
  counts with the NPC's call, so both are filtered.
- `MutationMenu.Hidden` (`src/core/`) lists nine mutations with reasons;
  `KirbyMutationMenu` filters them while a Kirby session is active.
- Source: `python3 scripts/check.py` passes (302 core assertions; Release build 0
  warnings/errors).
- Live (`repros/crit-88-20261008`): `20261008-021537` unfiltered listed all nine,
  `20261008-021638` filtered listed none; ordinary mutations stayed. Not run: the
  NPC hook, the pause screen, a mutation already held.
- Scope note: Point Blank moves to #91 with its effect, so no text promises more
  than the code does. Support (needs a deployed skill; the helper's position is not
  readable), Parting Gift and Networking (ranged) were added to the hidden list
  once the host code showed copy hits are already handled as melee: twelve in all.
  Live reruns `20261008-022159` (unfiltered, all twelve listed) and
  `20261008-022301` (filtered, none listed, ordinary ones kept).

## 2026-10-08 — Original enemies the host killed alone no longer linger (#78)

- The #79 damage log caught the owner's unexplained hits (Archer kill in the
  Prisoners' Quarters, Grenadier kill in the Promenade): original body contact
  from a detached proxy with the original 24×24 box, 0.8–1.3 s after the kill.
- `KirbyEnemies` detached a living original enemy when its host enemy died in
  the same tick without an original defeat. Now only a defeated (zero HP)
  original object is detached, keeping its touch exclusion. Others are retired
  and logged (`host death without an original defeat`).
- Headless: a detached lethal Sword victim reported its defeat at frame 5 and
  was gone at frame 14 without hurting Kirby. Source: `python3 scripts/check.py`
  passes. Not run live.
- Seen, not changed: host poison ticks play the original hurt reaction.


## 2026-10-08 — Ninja's wall cling holds while a direction is held (#97)

- Owner recording: Ninja Kirby on a wall flickered between the cling and a
  fall and slid down instead of holding on.
- Cause: `DashAssist` (`AlwaysDash`) counted any still frame as ground. After
  two still cling frames (original 0x712) it released the direction for one
  frame to re-tap a dash; the original lets go of the wall on release (0x70E),
  clings again on the next press, and the cycle repeated every five frames.
- Landing frames now count only in original ground actions
  (`KirbyActions.OnGround`). The landing re-tap after a fall is unchanged.
- Headless (bridge, Ninja, scratch script, not committed): before, right held
  gave `712 712 712 70E 70E` repeating with the feet sinking 0.5 px a cycle;
  after, 0x712 held for 100 frames. Source: `python3 scripts/check.py` passes
  with a new cling assertion. `tests/speed` does not build on main
  (`FarObject` missing), so it was not run. Not run live.
## 2026-10-08 — the Collector hides upgrades Kirby cannot use (#99)

- Owner request: the Collector offered weapon, bow and skill upgrades that do
  nothing for Kirby. Decision: hide them and keep the unlock gates moving.
- Host bytecode (build 23762174): the panel lists what `CollectorPanel.baseFilter`
  accepts; gates read `ItemMetaManager.countUnlockedItems` (unlocked entries with a
  cell cost) after requiring Health Flask I. `LootGen.generate` uses `RandomBow`
  and `RandomCC` only to choose the first room's weapon, which Kirby's loot hook
  already replaces, so both upgrades are hidden too, with `BackpackUnlock`.
- `CollectorMenu` (`src/core/`) and `KirbyCollector` hide host groups 0–5 except
  shields, those three upgrades and the mutations #90 hid, and count found hidden
  entries as unlocked. No save writes.
- Source: `python3 scripts/check.py` passes (375 core assertions).
- Live (`repros/collector-99-20261008`, `--probe-collector`): `20261008-195344`
  filtered listed none of nine hidden and all five kept; `20261008-195429`
  unfiltered listed all. Count 1 → 10; the Random Starter Shield gate opened. Not
  run: a real blueprint pickup, other transition levels, skin/head rewards.

## 2026-10-08 — Kirby has no health flask; fountains heal half (#101)

- Owner request: F stops healing; Kirby heals from food and fountains only, and
  flask upgrades and mutations are hidden. F is kept for the stored limited-use
  ability (#102).
- Host bytecode (`hlboot.dat`, crashlink): `HealFlask.isEnabled` already turns
  the flask off for Captain Chicken; `Fountain.onActivate` marks the fountain used
  and plays `cine.HealFountain`, whose end calls `Fountain.applyEffect` →
  `Hero.heal(maxLife)`; `ItemMetaManager.canInvestOnItem` needs Health Flask I
  for every investment.
- `KirbyHealing` disables the flask and turns the fountain's heal into half of
  maximum health (`Healing.FountainHeal`). `MutationMenu` hides Extended Healing,
  Emergency Triage, Tainted Flask and Cursed Flask; `CollectorMenu` hides Health
  Flask I–IV, and a hidden entry now satisfies the Collector's prerequisites. The
  flask's "heal" tutorial is suppressed. New `--input-plan healing` probe.
- Source: `python3 scripts/check.py` passes (384 core assertions).
- Live (`repros/healing-101-20261008`): `20261008-221759` F held without healing,
  fountain 16 → 54 of 76; Collector `20261008-221942` and mutation menu
  `20261008-222204`/`222245` passed. Not run: a generated fountain, flask
  recharge items, a profile that owned the flask at run start, normal play.

## 2026-10-09 — F slot for limited-use abilities (#102)

- Owner request: Crash, Paint, Mike and Cook are never swallowed; Interact (R)
  takes their item into the F slot (a new one replaces the stored one); F fires
  it while Kirby keeps his copy; Mike keeps three shouts.
- Research (private trace build of the bridge: CPU write watch, execution log and
  call injection; never shipped): the only writer of the copy byte `0x020BAF7B`
  is the field setter called from `SetAbility(actor, record, ability)` at
  `0x0211B734`, used by the swallow, each limited ability's revert and the
  original give-ability routine `0x020AF020` (whose precondition — action group
  4, 6, 7 or 8 — the bridge copies). Calling it directly fires each limited
  ability exactly like a swallowed one (reverts on frames 266/404/135/445) with no
  swallow; it loads graphics for 2–5 frames. Called mid-attack or with a mouthful
  it leaves Kirby stuck. Mike's shouts are at `0x0205B6B1`.
- `tools/nds_call_patch.py` adds a checked hook to the pinned ARM9 interpreter
  that runs one original routine at the player update entry `0x020B2714`; the
  bridge adds `kssu_set_ability`, `kssu_ability_change_pending`,
  `kssu_set_mike_shouts` and a `KSSUHK01` savestate trailer.
- Core `LimitedAbilities`, `FSlot` and `FSequence`; enemies no longer roll limited
  abilities. Runtime `KirbyFSlot` (Interact take, F press, HUD `skillPotion`
  icon and count, save), limited floor items are not paired for the inhale, the
  stored item's damage bonus applies while it fires. New `--input-plan fslot`.
- Source: `python3 scripts/check.py` passes (408 core assertions);
  `python3 tools/nds_tests.py` passes 78 cases including `f_slot` and `f_slot_save`.
- Live (`repros/fslot-102-20261009`): `20261009-010357` took and fired all four,
  replacement and save; `20261009-010718` restored the slot from the save. Not run:
  a press lost to a hit, stomp/ladder/water, helper, room change mid-attack, normal play.

## 2026-10-10 — helper reacts quickly and keeps pace with Kirby (#105)

- Owner report: the Homunculus (X) helper took too long to detect and attack
  enemies and was much slower than Kirby.
- Research (private oracle bridge, ARM disassembly): the ROM brain's notice timer
  is clamped by the getter at `0x020B53C0` (60 to 120+ frames); the helper got no
  host speed match, Kirby does. Widening the helper's vision was tried and
  dropped: enemy proxies only spawn ~150 px from Kirby.
- Bridge: `quicken_helper_reaction` (wait capped at 6 frames, validated RAM
  patch), `kssu_helper_state`, `kssu_move_helper`. Runtime: `KirbyHelperMatch`
  applies `SpeedMatch` to the helper after Kirby's own match.
- Source: `python3 scripts/check.py` passes; `tests/speed` passes with the new
  helper checks; `python3 tools/nds_tests.py` passes 80 cases including `helper_reaction` and
  `helper_move`. `tests/nds` does not build (missing `FarObject` include, same
  before this change; not touched).
- Live: none run. Not verified: the feel in the running game, a helper in water or
  on a ladder, and the Wheelie helper (left at the original speed).

## 2026-10-10 — Legendary altars offer ability items, not weapons (#107)

- Owner report (two screenshots): a gold "Spartan Sandals IV-L" (+187% damage,
  Take / Recycle 600) on an altar in a Kirby run. Dead Cells weapons must never
  appear (#54).
- Cause (type scan of `GameProxy.dll`, `hlboot.dat` bytecode via crashlink):
  `en.inter.LegendaryAltar` is its own `Interactive`, not an `ItemAltar`, and
  takes its item in its constructor; #61 hooked only `ItemAltar`, `TimedItem`
  and `ShopBooth`, and the fidelity record listed legendary altars as "not
  converted". Its `onActivate` hands a short press to the hero's pickup and
  calls `onPick` to empty the altar. Other item holders checked: `VaniaAltar`
  calls the hooked `ItemAltar` methods, `HiddenBlock`/`HiddenGroundBlock` later
  spawn an `ItemDrop`; neither needs a change.
- `Hook_LegendaryAltar.initGfx` replaces the item (the replaced item's `Legendary`
  and "+" carry over); `onActivate` runs the host's `onPick` and drops the ability
  item, a long press still recycles. `LabRewardItemsProbe` covers it.
- Source: `python3 scripts/check.py` passes (408 core assertions; the runtime
  builds with one existing warning in `LabAbilityProbe.cs`).
- Live (`repros/legendary-altar-20261010`): `20261010-145915` passed (see
  [RUNTIME_LAUNCH](docs/RUNTIME_LAUNCH.md#legendary-altars-107)). Not run: a
  generator-placed legendary altar, a real Interact press, a level saved before
  this fix, the locked refusal.
## 2026-10-10 — the helper no longer attacks key-locked doors (#110)

- Owner report (recording): the Homunculus helper attacks key-locked doors
  endlessly.
- Cause: `KirbyEnemies.Eligible` pairs every `Door`; the helper's AI attacks any
  proxy. `LockedDoor : Door` (and `CardLockedDoor`, `MariaDoor`) refuse hits.
- Change: `KirbyEnemies.Refuses` keeps `LockedDoor` and `Door.locked` out of the
  pairing and releases one that becomes locked. Breakable doors unchanged (#50).
- Source: `python3 scripts/check.py` passes (the runtime builds). No test
  exercises the rule: it needs host entities.
- Live: none run. Not verified: the recording's door class, other special doors.
## 2026-10-10 — F slot icon lost after a level change (#109)

- Owner report: the F ability seems to disappear as soon as health is recovered at
  the fountain. Repro with the healing probe (Mike stored; real fountain, host
  flask refill, `HUD.refreshItems`, with and without a main Sword): the slot, its
  count and its icon are unchanged by the heal. Taking the level exit after it
  hid the F slot in the next level's HUD while the rules still held Mike × 2; the
  host does that without any hooked call (no `setHealings`, `setIcon` or
  `refreshItems`), and `KirbyFSlot` only redrew on `HUD.setHealings`.
- Fix: `KirbyFSlot.DrawHud` checks the host slot as drawn each frame (same skill
  object, visible, same icon tile) and redraws it when it changed.
- Healing probe extended (flask owned from the start, Mike stored, optional
  Sword and exit); `RUNTIME_LAUNCH.md` lists the runs.
- Source: `python3 scripts/check.py` passes. Live (`repros/fountain-109-20261010`):
  `20261010-153057` before the fix lost the slot after the exit, `20261010-153428`
  after it kept it. Not run: a generated fountain, other abilities across levels,
  normal play.
## 2026-10-10 — Kirby rolls through a whole roll-zone tunnel (#115)

## 2026-10-10 — carnivorous plant ignored Kirby standing on it (#113)

- Owner report (screenshot): Kirby and the helper stood on a host carnivorous
  plant and it never attacked.
- Cause (host bytecode of `CarnivorousPlant.onTouch`): the plant closes only on
  an entity that is on the ground. The `KirbyHurtbox` projection lifts Kirby's
  host foot about 9 px for the whole `Level.update`, so the test failed; the same
  cause as the 2026-10-05 pressure plate fix.
- Fix: `Hook_CarnivorousPlant.onTouch` runs with the physical body
  (`KirbyHurtbox.PhysicalHero`); the hit-area test keeps the projection.
- Lab: new `plant` scenario in `LabMovesProbe`.
- Source: `python3 scripts/check.py` passes (the runtime builds). Live
  (`repros/plant-20261010`): `20261010-182526` without the hook never closed,
  `20261010-182428` with it announced, closed and hit Kirby twice, 76 → 59 → 42. Not run: a
  copied attack on a plant, Kirby walking onto one, Gardener-boss plants, normal
  play.
- Owner report (recording): in a long tube the roll makes the same stop it does on
  open floor; one roll should reach the end of the tube.
- Cause: roll zones were open to a slide but the original slide is a fixed 36
  frames (131 px matched), so it ended inside a longer tunnel.
- Change: `SlideHold` / `KirbySlideHold` write back the slide controller's two
  counters (+0x6CB steps, +0x6D8 frames) and full velocity while Kirby's body is
  in a roll zone; leaving it starts the usual slowdown; a wall or hit lets go.
  Offsets found with a frame trace of the original slide.
- Source: `python3 scripts/check.py` and `tests/speed` pass (the runtime builds),
  including a new tunnel case, a left slide and a wall inside the tunnel.
- Live: none run. The owner's game was running, so the staged root
  `repros/tube-roll-20261010` was not launched.
## 2026-10-10 — Kirby's body hidden by solid terrain art (#116)

- Owner report (recording `Screencast From 2026-10-10 18-31-28.mp4`): Kirby appears
  and disappears. Frame by frame: over the room's brick background the body is
  drawn; at the black area at the room's edge only a sliver of it, and past the
  platform end none. Play log: pacing locked 93-98%, no errors.
- Cause: the body bitmap and equipment were attached beside the hero sprite, in
  `DP_ROOM_MAIN_HERO` (9). The solid terrain art is a tile group in
  `DP_FOREGROUND` (15) and covers the hero layer. Live ghosts of the bitmap in
  every layer: only layers 15-17 showed over the mass; the opaque (`None`) blend
  did not help; with Kirby inside the mass the whole actor was hidden.
- Change: `KirbySpriteRenderer.AttachSibling` appends the body and equipment to
  `DP_FOREGROUND`. New lab diagnostics `KIRBY_LAB_GHOSTS` and
  `KIRBY_LAB_TELEPORT` (see `RUNTIME_LAUNCH.md`).
- Source: `python3 scripts/check.py` passes (408 rule assertions, the runtime
  builds). Live (`repros/body-void-116-20261010`, `--input-plan repro`): before the
  change the ghosts and a held Kirby were hidden by the mass; after it Kirby is
  fully visible inside it. `--input-plan combat` (`20261010-184759`): Ice
  absorbed, shield parry, base and copy frames normal; no fatal log, saves
  unchanged. Not run: the recording's biome, host props that overlap the hero,
  other abilities' effects in the layer, normal play.
## 2026-10-10 — flip platforms flip under Kirby (#119)

- Owner report (screenshot, first Castlevania level): an iron platform does not
  flip with Kirby on it.
- Cause, from the host bytecode: `FlipPF.init` listens to `heroSignals.landSignal`
  and `onHeroLand` needs the platform closed, the hero's column in
  `xLeft..xRight` and `hero.cy == cy - 1`. Only `Hero.onLand` raises the signal, and
  only host physics calls it; the mod holds `dx`, `dy` and gravity at zero and
  places Kirby from the original, so the host never lands him. The other
  listeners (`DiveAttack`, `DodgeRoll`, `pow.Wings`) are left alone: the stomp ends
  through `onOwnerLand`, and `Hero.onLand` adds a stun and fall damage.
- Change: `HostLanding` (core) reports the source frame where the original
  reaches the ground after moving downward since it left it (a ladder or chain
  resets it). `KirbyFlipPlatforms.Land` then calls `onHeroLand` on each live
  `FlipPF` after Kirby's host position is set.
- Source: `python3 scripts/check.py` passes (the runtime builds; new landing
  assertions in `tests/Program.cs`). Live: none run. Not verified: the flip in
  a real Castlevania level, how long the platform stays open, the platform
  re-closing under Kirby, other trap entities that wait on the land signal.
## 2026-10-11 — source review findings applied (#121)

- A high-effort source review of `main` (77aa909) gave ten findings; the owner
  asked for them to be applied with the smallest edits.
- Changed:
  - `NdsRuntime.Health`/`MaxHealth` return the bridge's -2 (no player health
    group) instead of throwing, so the provider's existing guards run.
  - `KirbyMod.Synchronize` drops a session whose activation threw (ROM, bridge,
    seed state or snapshot), logs it once and leaves that hero to the host.
  - The `Kirby pacing` line counts only normally stepped frames: its clock
    stops for loading, pauses, cinematics, deaths and deactivation.
  - An F press in progress carries on in a new session on the same machine; a
    new run resets it.
  - `RuntimeConfig.RomPath` and `SeedStatePath` have no default; enabling the
    original runtime without them fails at startup with a message.
  - `KirbySpriteRenderer` keeps the uploaded texture when no decoder input
    changed. The per-byte VRAM read in `kssu_read_gpu` is unchanged.
  - The bridge refreshes proxy records once per change, not on every read
    (see `docs/NDS_BRIDGE.md`).
  - `KirbyEnemies.Controls` and the pairing loops use plain loops; the mutation
    attack is made only when an enemy is paired.
- Not changed: screen attacks keep no mutation factor or crit (already recorded
  in `docs/GAMEPLAY_FIDELITY.md`); the proxy birth frame needs no contact fix
  (`proxy_birth_contact` passes).
- Source: `python3 scripts/check.py` passes (26 + 6 Python tests, 433 rule
  assertions, the runtime builds). Bridge: built under another crate name
  against the pinned vendor tree; all 80 `tools/nds_tests.py` cases pass and
  match the previous bridge's results case by case. Live: none run. Not
  verified: any of these changes in the game, the failed-activation fallback
  with a real missing ROM or bad snapshot, a level change during an F press,
  the pacing line in play, the time saved by the bridge and renderer changes.

## 2026-10-11 — the mod's per-frame cost (#123)

- The owner asked for the mod to be as cheap as possible: steady 60 fps, even
  frames, no flicker and no new bugs. Measured first with a new opt-in lab
  probe (`KIRBY_LAB_PERF=1`, `LabPerfProbe`), then changed what the numbers
  pointed at. Record: `docs/PERFORMANCE.md`.
- Changed:
  - `kssu_read_gpu` copies engine A's OBJ VRAM as a block (1.28 ms to 0.04 ms a
    frame); a second stage of the checked vendor patches adds `Copy_ObjVram`.
  - New `kssu_main_screen_only`: the lower screen, which the mod never shows,
    is not drawn (`kssu_step` 2.77 ms to 2.26 ms in the bridge alone).
  - `KirbyEnemyHooks` installs its six host hooks only while the original
    engine holds an enemy, projectile or item, and removes them 30 host frames
    after. A hooked host function costs about 2 µs and 920 bytes of garbage a
    call with a handler installed; the host made 616 such calls a frame.
  - `NdsObjRenderer` draws into the texture's own pixels and clears only what
    the last drawing covered (a decode 0.15 ms to 0.015 ms).
  - Enabling fixes: `tests/background` links `NdsObjRenderer.cs` (it did not
    build on `main`), and `tools/nds_tests.py` starts a case's result on its
    own line.
- Lab, offscreen and software-drawn, `main` against this branch (which also
  holds #121): the mod's work for one original frame 5.56 ms to 3.20 ms,
  garbage 54.9 MB to 16.0 MB a second, time in collections 2.3% to 0.6%, host
  63.6–66.6 fps to 89.4–99.6 fps on the movement plan; `crowd` 36.8 to
  57.7 fps. #121 alone is 4.87 ms and 68.3 fps.
- Source: `python3 scripts/check.py` passes (26 + 6 Python tests, 435 rule
  assertions, the runtime builds). Bridge, built against a private copy of the
  vendor tree: all 82 `tools/nds_tests.py` cases pass, the 80 older ones equal
  to the previous bridge's recorded results. `verify_renderer.py` 29 cases and
  3,046,706 pixels, `verify_background.py` 4 cases, `verify_native_init.py`
  64 cycles: no mismatch. Live: 15 lab runs (`movement`, `combat`, `crowd`,
  `hazard`, `items`) in private data roots, with equal probe outcomes on
  `main` and the branch. The shared bridge binary, its evidence files and the
  shared vendor checkout were not replaced.
- Not verified: play in a window on the GPU, 60 fps in play, long sessions,
  the hooks next to another mod's hooks on the same functions. Left as they
  are: the interpreter's 2.6 ms, the 6 ms original frame with 30 stacked
  enemies, the `HSprite.draw` hook, the whole-texture upload, the host's
  first-use shader link.

## 2026-10-11 — camera looks far below Kirby after arriving in the Ossuary (#126)

- Report: the camera is wrong on arrival in the Ossuary, then corrects itself;
  only that level. A screenshot showed Kirby at the top edge of the screen.
- Cause (recorded, `--input-plan arrival`, Ossuary): the level starts a host
  `ForcedDiveAttack` as the hero arrives. The host camera leads 150 px below a
  diving hero, and the host ends the dive from the hero's landing, which never
  comes while the source machine owns Kirby's physics. The view stayed 134
  host pixels below Kirby for the whole 25 s run, in two separate levels.
- Fix: `KirbyNdsProvider.FinishForcedDive` delivers `onOwnerLand` when the
  source machine puts Kirby on the ground. Live, three Ossuary runs: the dive
  ends, the lead returns to −5 and the offset settles at +18 px; the movement
  plan ran 45 s without a fatal log.
- Source: `python3 scripts/check.py` passes. Not run: arrival through the
  `T_Ossuary` passage in a played run, other biomes that start a forced dive,
  the landing shockwave against enemies near the arrival.
- Lab: `arrival` input plan and `KIRBY_LAB_ARRIVAL_LEVEL`, run from
  `repros/ossuary-camera-20261011`; the shared lab was not restaged.

## 2026-10-12 — Kirby's damage −25%, the helper's −50% (#128)

- Request: Kirby and the ally (the Homunculus CPU helper) are too strong.
- `CopyDamageScale` 3.5 → 2.625 for Kirby; new `HelperCopyDamageScale` 1.75.
  The helper's hits are told apart by owner in the frame's original collision
  records (new bridge export `kssu_helper_owns`; `kssu_player_owns` counts the
  helper as Kirby's), so a hit that only the helper's records touch is scaled
  at the helper's scale; a frame with both is Kirby's. Helper screen attacks use
  it too.
- Found while testing: Kirby's and the helper's Sword records are both in
  hit-list slot 7, and their bodies in slot 14 with the same damage word, so
  the owner, not the slot, is what tells them apart.
- Known limit: a source-lethal helper hit still kills (source HP is set from
  Kirby's scale before the hitter is known), so its chip damage is half but
  its kills come one hit sooner for about half of all enemy life values;
  enemies up to 47 life still die to one helper Sword hit
  (docs/GAMEPLAY_FIDELITY.md, Damage balance).
- Source: `python3 scripts/check.py` passes (443 rule assertions, the runtime
  builds). Bridge built in an isolated crate, shared binary untouched:
  `helper_owns` (new), `helper_move`, `helper_reaction`, `proxy_sword`,
  `proxy_lethal` and `wheelie_ride` pass. Not run: the rest of the
  `nds_tests.py` set, any live game check, the helper against a host enemy in
  play.

## 2026-10-13 — controller layout (#130)

- The owner reported Kirby's controls as not suited to an Xbox controller and
  asked the agent to choose the layout. The adapter read a controller like the
  keyboard and no controller run is recorded.
- Controller bindings only (`ControllerLayout`, default true): the movement
  stick is 8-way (`PadStick`), so a sideways stick drifting down no longer
  holds Down; Dodge slides; Skill 1 guards; Skill 2 discards the copy ability
  only after a 0.3 s hold (`HeldButton`). Keyboard unchanged
  (docs/GAMEPLAY_FIDELITY.md, Controller layout).
- Source: `python3 scripts/check.py` passes (449 rule assertions, the runtime
  builds). Not run: any live game check, any controller, `nds_tests.py`
  (bridge untouched). The default Xbox button names are from memory, not a
  recorded binding dump.

## 2026-10-13 — carnivorous plant did not throw Kirby (#132)

- Owner report (screencast): the plant closes on Kirby and bites him, then he
  is dropped in place instead of being thrown upward.
- Cause (host bytecode): `CarnivorousPlant.bumpEntity` calls `bump(0, -1.5)`
  on the hero; the provider zeroes the host hero's velocities every step, as the
  original machine owns Kirby's motion.
- Fix: `KirbyBumps` reads the bump the host left on the hero and
  `KirbyNdsProvider.Launch` replays it as decaying ascent before each original
  frame (`BumpLaunch`, 200 px before gravity, stopped by ceilings). Intentional
  deviation, recorded in docs/GAMEPLAY_FIDELITY.md ("The plant's throw").
- Source: `python3 scripts/check.py` passes (455 rule assertions, the runtime
  builds). Live (`repros/plant-20261013`, run `20261013-124519`): bitten 76 → 59, launched
  115.5 px; with no input he fell back within the plant's bump window and was
  bumped a second time. Not run: a ceiling above the plant, input during the
  launch, the Beheaded's height on the same plant, `nds_tests.py` (bridge
  untouched), normal play.

## 2026-10-13 — Plasma's charge after an F-slot ability (#135)

- Owner recording `Screencast From 2026-10-13 12-45-59.mp4`: Plasma main copy,
  Crash fired from F, then every direction tap drew Kirby as a white and orange
  ball and a row of scrambled tiles for one frame.
- Reproduced headless from the machine in the owner's Kirby save and from the
  seed; the original's own screen shows it. Cause: the original's charge-level
  routine creates Plasma's charge aura whenever it is missing, and a Plasma pose
  handler still asks for it after `SetAbility` has switched copies, so a stray
  aura survives and Plasma gets a second one when it comes back. Only Plasma is
  affected; Paint, Mike and Cook gave ARM9 data aborts instead of visible
  garbage.
- Fix in the bridge (`src/nds/lib.rs`): before each source frame a stray aura is
  removed with the original's own removal call through the call hook
  (docs/NDS_BRIDGE.md, Plasma's charge aura after a change). A save that already
  holds the extra aura is repaired on its first frame.
- Source: new bridge case `f_slot_plasma`; all 84 `nds_tests.py` cases passed
  one at a time against the bridge built in an isolated crate (the shared
  binary and its evidence file were not replaced). `python3 scripts/check.py`
  passes. Not run: any live game check, `tools/nds_build.py`, a restage of the
  owner's lab.

## 2026-10-13 — Kirby flickers a cell off beside a helper (#134)

- The owner's recording of 12:39 shows Kirby a terrain cell (24 px) off his
  path for single frames, six times in 12.9 s, with a helper beside him; the
  play log shows steady 60 fps (docs/RECORDING_REVIEW_2026-10-13_1239.md).
- Cause: an emulator frame ended 71 scanlines after VBlank, inside the
  original's logic pass whenever a helper made it longer; the terrain window
  was then moved under a pass that had moved Kirby and the camera and not yet
  built the sprites.
- A source frame now ends as VBlank begins (`tools/nds_step_patch.py`, a
  checked addition to the pinned melonDS frame loop applied by both build
  scripts; the machine runs the same). A state saved at the end of a frame is
  brought there once when it is loaded. While the original takes longer than
  a frame over a load the window and Kirby wait (`NdsRuntime.PassUnfinished`,
  `KirbyNdsProvider.HoldWindow`); `kssu_world` moves the original's drawing
  copy of the camera with the window; `kssu_unfinished_passes` feeds a count
  in the `Kirby pacing` log line (docs/NDS_BRIDGE.md, Where a source frame
  ends).
- Tests follow the new boundary: `step_between_passes` is new; `f_slot` sees
  Crash's switch after 1 frame and its revert on frame 267 (2 and 266 before,
  the same 268 after arming); `f_slot_save` saves during a Mike change, since
  Crash's has returned by the first frame; `tests/render` steps once after a
  load and `tests/background` compares each capture with the next framebuffer.
- Private build under `repros/flicker-134`, shared bridge untouched:
  `nds_tests.py` 84 cases, `verify_renderer.py` 29 cases, `verify_background.py`
  4, `verify_native_init.py`, `tests/speed` 47 checks and `scripts/check.py`
  pass. Live, isolated profile, helper beside Kirby: 5 frames ended inside a
  pass in a minute and no capture with Kirby a cell off (run
  `20261013-135009`); the old boundary on the same build: 118 and 10 captures
  (`20261013-192904`).
- Not run: the owner's play launcher, a controller, many host enemies, an
  elevator during a load. `tests/nds` and `tests/audio` do not build on `main`
  (their projects lack `NdsObjRenderer.cs`); private copies with that file
  pass on the new bridge. The shared bridge needs `tools/nds_build.py` and a
  new staging after the merge.

## 2026-10-13 — ROM internals (#146)

- Read the whole program, not one feature: a private capstone index of the
  5,996 functions loaded during play, and a private instrumented copy of the
  bridge (`repros/re-debug`: instruction histogram, watchpoints, a register
  breakpoint, stage timers, ARM9 clock, 2D renderer and recompiler switches).
  Record: `docs/ROM_INTERNALS.md`.
- Found: the overlays present in play and the five addresses enemy overlays
  share (one species a slot); the frame's call chain and its 34.7% idle load;
  Kirby's action table `0x02131688`, the move pointer `R+0x808` and the
  per-ability move tables behind `SetAbility`; actions `0E1B` (ability change)
  and `1021` (death); the vitals record every hit group is built from; the
  object type table `0x0209E10C`, file codes and the spawner; health, copy
  ability and touch damage of 38 enemy species; the 12-bit random routine
  `0x0200B8A4`; 566 named sound sequences.
- Measured, headless: with 24 to 31 proxies the original ends 2 to 5 frames in
  360 inside its logic pass, none with the emulated ARM9 clock doubled;
  `kssu_step` 2.41 ms idle and 3.71 ms in that crowd on the interpreter, 1.31
  and 1.71 ms on the recompiler with fast memory off, with byte-identical
  600-frame traces of Kirby.
- Changed: documentation only. No bridge, runtime or test change; the fix
  candidates are listed in the record for their own issues.
- Not run: `tools/nds_tests.py` or any verifier on the recompiler or the
  doubled clock, any live game check, the owner's play launcher. Enemy numbers
  are stored values read statically; only Waddle Dee's were seen applied.

## 2026-10-13 — Kirby holds one shield (#139)

- Owner report with a screenshot: base Kirby with a shield in each weapon slot
  after buying a second one. Owner decision: one shield only.
- Cause: base form has an empty weapon slot once the copy's item leaves, and the
  host equips a taken or bought shield into an empty slot instead of swapping.
  Nothing limited shields to one; the fidelity record listed two as allowed.
- Fix: `AbilityItems.SpareShields` (core) names every shield slot but the
  retained one, and `KirbyAbilityItems.Sync` drops those shields to the floor
  each host frame, before it places the copy's item. The newly taken shield is
  the retained one, so it stays and the old one drops, as in the host's swap.
- `LabItemProbe` gained `state@seconds` (records inventory, retained shield and
  level drops without dropping anything).
- `scripts/check.py` passes (459 core assertions, runtime build). Live, isolated
  root `repros/one-shield-139`, run `20261013-203959`: discard Sword, take two
  shields in base form; the second stayed equipped and the first lay on the
  floor.
- Not run: a shop purchase, a resumed save with two shields, a parry with the
  kept shield, the HUD on screen, the owner's play launcher.

## 2026-10-13 — the Mix roulette gave Cook as the main copy (#141)

- Owner screenshot (play log `20261013-201446`): Kirby wears Cook's hat and
  the main copy slot shows Cook after swallowing enemies. Limited abilities
  (#102) must only come from a floor item and go to the F slot.
- Cause, reproduced headless on the shared bridge: no enemy carries a limited
  ability, but a mouthful of two or more runs the original Mix roulette, whose
  wheel (25 bytes at `0x020BAA8C`, read by `0x020AF1C8`) has Mike, Crash and
  Cook among its faces. With Sword and Beam proxies, B on frames 111, 139 and
  167 gave Crash, Cook and Mike.
- Fix in the bridge (`keep_limited_abilities_off_mix_wheel`, validated and
  applied each step): each of the three faces shows the next one (Yo-Yo,
  Stone, Fighter). Order, speed, the 25 positions and the roulette code are
  the original's. Side effect: left alone on a doubled face the original's
  turn count stops the roulette after two turns instead of four.
- New case `mix_no_limited` in `tools/nds_tests.py`; it fails on the unchanged
  bridge. Bridge built in an isolated crate, shared binary untouched: 86 cases
  passed one at a time.
- Not run: a live game check, `scripts/check.py` (it does not cover the bridge
  or its tests). A Cook already held as the main copy stays until used. The
  shared bridge needs `tools/nds_build.py` and a new staging after the merge.

## 2026-10-13 — the plant throws Kirby as it throws the Beheaded (#138)

- Owner report (screencast) after #132: the throw is not the Beheaded's. Kirby
  was thrown high, hopped, or sat on the closed plant.
- Measured the Beheaded on generated plants with the mod inactive
  (`KIRBY_LAB_MOVES_ANY=1`): 242.2 px in 17 fixed steps from `bump(0, -1.5)`,
  reproduced to five decimals by gravity 0.065, gravity friction 0.9 and bump
  friction 0.94 a step; a 34 px hop from the bite; thrown 0.5 s after the bite
  anywhere on the plant; a shut plant does not hold him; an air jump just above
  a shut plant throws him.
- #132 used the wrong decay (0.82) on top of the original's own speed (115 px,
  58 px from a fall), tested the touch with Kirby's small hurtbox, kept the
  shut plant solid and dropped the bite's hop.
- Fix: `BumpLaunch` replays the host's steps with the original's vertical speed
  held; `KirbyHurtbox.AsBeheaded` gives the plant's `fixedUpdate` and `close`
  the Beheaded's body at Kirby's foot, on the floor during the original's
  landing hop; `KirbyBumps` adds the closed plant's touch, the bite's hop and
  the air-press throw; the bite's hurt recoil is held to the vertical; `TileAt`
  needs a solid bit for a one-way platform. Deviations and remaining
  differences: docs/GAMEPLAY_FIDELITY.md ("The plant's throw").
- Source: `python3 scripts/check.py` passes (463 rule assertions, the runtime
  builds). Live (`repros/plant-138`, runs in docs/RUNTIME_LAUNCH.md): hop 34 px,
  throw 242.2 px, bite then throw every cycle at 0, 13, 26, 30 and 34 px from
  the centre, jump-off and air-press throws. Not run: a ceiling above the plant,
  ground plants, Gardener-boss plants, a float or copy ability on a plant,
  normal play.

## 2026-10-13 — Kirby swam in shallow water (#144)

- Owner report (screenshot, Mama Tick): Kirby swims for the whole fight because
  the arena floor is under water.
- Cause: `KirbyNdsProvider.TileAt` exported every free cell with a host water
  surface as an original water tile. The host surface is in pixels
  (`getWaterSurfaceY`, marker `cy + yr`), so a partly filled top row counted as
  a whole tile. The arena has 12 px of water over 82 columns; for the host it
  is decoration and the Beheaded never swims.
- Owner decision: Kirby swims only where the water would cover him.
- Fix: a cell is original water only when the surface is at or above its top
  (`SpeedMatch.Submerged`). Intentional deviation, recorded in
  docs/GAMEPLAY_FIDELITY.md ("Shallow host water").
- Lab: the `water` plan takes `KIRBY_LAB_WATER_LEVEL` and, with
  `KIRBY_LAB_WATER_WADE=1`, wades and counts the original's actions; it also
  records every water column's depth. Its Zombie check now needs a level with
  deep pools (docs/RUNTIME_LAUNCH.md).
- Source: `python3 scripts/check.py` passes (458 rule assertions, 3 new; the
  runtime builds).
- Live (`repros/water-20261013`): old rule in the arena, `0815` after the jump
  press and the landing (run `20261013-212620`); new rule, none, and the jump
  starts on the press (`212720`, `213551`). No `0815` in 18 px of water in
  `Shipwreck`, `Greenhouse` and `SewerShort`; still `0815` in a 42 px pool
  (`Greenhouse`, `213406`) and a 114 px one (`SewerDepths`, `213103`).
- The Zombie check in `SewerDepths` (`213902`, `214040`): the Zombie in a
  42 px pool was never source-controlled; the inhale timed out because the
  only bank found was three cells above it.
- Not run: the fight beyond its first seconds, copy abilities and the helper in
  shallow water, a pool between 18 and 42 px, an inhale of an enemy in deep
  water on the new rule, `nds_tests.py` (bridge untouched), normal play.

## 2026-10-13 — upper screen left undrawn on frames with no effect (#148)

- Engine A was still drawn on every scanline of every original frame, and the
  host read it only to find a screen-wide palette or colour effect, which
  almost no frame has (candidate from the ROM study, #146).
- The bridge leaves a scanline's backgrounds and composition undrawn when its
  layer capture would read as "no effect" (unit on, not blanked, no colour
  effect control, the seed palette), keeps sprites and any scanline a display
  capture reads, and reports each frame as drawn, undrawn or mixed
  (`kssu_skip_idle_main`, `kssu_main_frame`; third stage of
  `tools/nds_graphics_patch.py` and `tools/nds_vendor.py`). The provider turns
  it on; `NdsBackgroundEffects` and `LabVisualProbe` decode only drawn frames.
  `KSSU_DRAW_EVERY_FRAME=1` keeps every scanline drawn.
- Private build under `repros/skip-148`, shared bridge untouched: `kssu_step`
  2.77 → 2.44 ms from Python; in the lab the original frame 2.58 → 2.15 ms
  and the background capture 0.07 → 0.003 ms a frame (two runs each way).
- Same machine and same effect on every frame: new case `skip_idle_main`
  (480 frames with Crash), `verify_background.py` replaying its four cases
  with the switch on (1,200 frames, 0 different), and the lab's `visual` plan
  both ways (2,093 common frames, 0 different). `nds_tests.py` 86 cases,
  `verify_renderer.py` 29, `verify_native_init.py` and `scripts/check.py`
  pass.
- Not run: windowed play, a controller. No run produced a mixed frame or a
  display capture. The shared bridge needs `tools/nds_build.py` and a new
  staging after the merge.

## 2026-10-14 — the original's processor at twice the DS clock (#150)

- Owner decision of 2026-10-13: a deliberate departure from the DS, on by
  default (`DoubleArm9Clock`), off in tools and tests.
- `tools/nds_step_patch.py` second stage: `RunFrame` moves the ARM9 to the
  clock the bridge asks for (timestamps rescaled, bus timings recomputed).
  `kssu_arm9_clock`; a state saved at twice the clock ends with
  `[2]["KSSUCK01"]`, a DS-clock state is unchanged.
- Bridge, private build `repros/clock-150`: same 240-frame trace at both
  clocks without a crowd; 31 proxies, 4 → 0 of 300 frames ended inside a pass;
  a state saved at twice the clock continues as the run did (`arm9_clock`).
  The 85 older cases at twice the clock: 83 the same, `f_slot` a frame sooner
  (B is taken the frame after `SetAbility`), `main_screen_only` differs only
  in the sound capture buffers at `0x02048BE0`.
- Live, isolated profile, one run per clock: `movement` 0 and 0 late frames,
  step 2.81 → 2.82 ms; 20 Zombies in one swing 7 → 5, 3.60 → 3.69 ms.
- The 31-Zombie crowd fixture showed no change (3,252 and 3,257) because the
  original is dead in it: one attack that hits 24 or more proxies makes the
  ARM9 abort at `0x02156BA4` (a creator's null result, used unchecked) and
  spin in the BIOS at either clock. Filed as #151. `--crowd-size` added to the
  launcher so the fixture can stay under it.
- Not run: windowed play, a controller, the sound by ear, the shared bridge
  build. On the private build `nds_tests.py` (86 cases), the three verifiers
  and `scripts/check.py` pass.

## 2026-10-14 — the original on the emulator's recompiler (#153)

- Owner decision of 2026-10-13: the recompiler last of the three speed fixes
  of #146, on by default (`Recompiler`), the interpreter kept behind the
  switch and in tools and tests.
- `tools/nds_jit_patch.py` builds the pinned core with its recompiler, fast
  memory off, and without the process-wide SIGSEGV handler (the library
  imports neither `sigaction` nor `signal`). `tools/nds_call_patch.py` second
  stage: each ARM9/ARM7 slice runs on the interpreter unless the bridge sets
  `kssu_recompile`; the ARM9 also while a call is armed; prefetch refilled on
  the way back; compiled blocks dropped after the bridge's RAM patches change
  code. Exports `kssu_recompiler`, `kssu_code_changed`; `KSSU_RECOMPILER=1`
  starts every machine on it. Runtime logs `Kirby original machine:`.
- Private build `repros/jit-153`, shared bridge untouched: `nds_tests.py` 86
  cases, the 85 older equal to the recorded results; new case `recompiler`;
  each of the 85 on the recompiler: 83 equal, 2 different and passing (load
  timing, sound capture buffers). Renderer and background verifiers 0
  mismatches on both paths; `verify_native_init.py` 64 cycles;
  `scripts/check.py` passes.
- Bridge, median step: 2.85 → 1.80 ms alone, 3.71 → 2.06 ms with 31 proxies,
  same traces. Some loads end a frame later (Sword swallow 3 → 4, Wheel
  2 → 3). Live, one run each: `movement` 2.82 → 1.81 ms, 103.1 → 110.3 fps;
  `crowd` 6.42 → 2.37 ms, 61.8 → 91.2 fps.
- Found: the recompiler skips the protection unit's data aborts. #151's null
  write then lands in six bytes of the original's ITCM (a routine that did
  not run in 1,003 frames of play) and play continues; the interpreter
  freezes. Recorded in docs/NDS_BRIDGE.md and docs/GAMEPLAY_FIDELITY.md; #151
  still needs its fix.
- Not run: the owner's play launcher, a controller, a window on the GPU,
  bosses, a long session, listening, and the recompiler with #148 or #150.
  The shared bridge needs `tools/nds_build.py` and a new stage after the
  merge. The native-init verifier's shared record was rewritten through the
  private root's `research` link and restored by rerunning it on the shared
  bridge.

## 2026-10-14 — the recompiler with the doubled clock and the idle-screen skip (#153)

- The open work merged together for the owner (#141, #144, #138, #139,
  #146, #148, #150, #153). One interaction found: a compiled block counts
  its cycles at the ARM9 clock it was compiled at, so a clock change left
  stale costs. `kssu_arm9_clock` now sets `kssu_recompile_flush` when the
  asked clock changes. Case `recompiler`: 31 proxies at twice the clock,
  then the DS clock from the same frame, end in the state a state saved at
  the switch reaches; without the flush the same trace but other states.
- Private build of the merged tree `repros/merge-20261014` (bridge
  `82e81174…`), shared bridge untouched: 89 cases pass; 85 equal to recorded
  results; `main_screen_only` and `skip_idle_main` digests change only
  through #141's wheel patch (equal with it skipped). On the recompiler at
  twice the clock: 85 equal, 2 digests, `f_slot` as at twice the clock
  alone, `arm9_clock`'s byte-exact save/load fails (loading drops the
  compiled blocks; equal when the straight run drops them too). Verifiers 0
  mismatches, `verify_native_init.py` 64 cycles (private report),
  `scripts/check.py` passes.
- Live, all three on, one run each: `movement` 1.45 ms, 117.8 fps, 0 late
  frames; 20 Zombies 1.84 ms, 99.2 fps, 5; 31 Zombies 2.08 ms, 92.7 fps, 5.
  #144's wade (`SwampHeart`): no swimming. #138's plant: four bites, each
  followed by a throw; the three not cut short rose 242.2 px.
- Not run: the owner's play launcher, a controller, a window on the GPU,
  bosses, a long session, listening.

## 2026-10-14 — the native and audio test projects build again (#155)

- `tests/nds` and `tests/audio` linked `NdsRuntime.cs` but not
  `NdsObjRenderer.cs`, where `FarObject` has lived since #67, so neither
  built. Both now link it, and `scripts/check.py` builds every C# project
  under `tests/`, so a missing link fails the offline check.
- `tests/nds` against the shared bridge, the ROM and `oracle/ready2/000.state`:
  passes (ABI sizes, seed, walking, facing, RAM bounds, 3 live hits).
  `tests/audio` with `SDL_AUDIODRIVER=dummy`: passes (288,836 samples, jump
  peak 1,571, landing peak 5,597, as `docs/AUDIO.md` records).
  `python3 scripts/check.py` passes with the six new builds.
- Not run: any live check; nothing in the game changed.

## 2026-10-14 — one attack into 24 or more enemies no longer stops the original (#151)

- Cause: the original takes every collision group from one pool of 48
  (`0x02076F00`, set up at each stage load by `0x02071C00`). A proxy holds
  one; each hit makes the recoil routine `0x02156ACC` create a companion
  that holds one more for about 7 frames, and then writes the enemy's
  health into it unchecked. Thirty-one proxies and 24 hits in one swing
  emptied it: a null write, a data abort on the interpreter, six bytes of
  ITCM on the recompiler.
- `kssu_world` links 64 more groups, two per proxy slot, from the scene
  heap after the original's free ones and sets the count to 112, its
  marker; no original instruction changes. It returns -6 when the pool is
  not the one expected. Only the pool's setup reads the count or the base.
- Private build `repros/fix-151`: 90 cases pass with the new `hit_pool`; the
  other 89 equal the shared record except the memory digests of
  `main_screen_only` and `skip_idle_main`. A margin probe (every copy
  ability, four attack patterns, 31 stacked proxies) kept at least 44 of
  111 groups free; up to 189 of 300 frames ended inside a pass (Mirror,
  mashed). Verifiers 0 mismatches, 64 init cycles; `scripts/check.py`
  passes.
- Live 31-Zombie `crowd`: interpreter 7 frames inside a pass after the
  crowd appeared (3,264 on `main`) and 96 hits; the defaults 2 and 66. The
  crowd probe now decides `passed` once, 300 frames after the crowd
  appears, so a death's new run no longer overwrites it.
- Not run: the helper's attacks, bosses, windowed play, the owner's
  launcher. The shared bridge was not rebuilt. Two scratch probes that
  leaked one emulator per run filled the machine's memory; probes now run
  one machine per process.

## 2026-10-14 — read Kirby's move; no F switch under a mouthful (#158)

- A mouthful keeps the action groups of standing, walking and jumping, so
  `kssu_set_ability`'s group test and the F slot's "free" check could not see
  it: a change went through under the mouthful, and a swallow during the
  press replaced the stored ability (headless: Crash set, Down, Sword).
- The original tells a mouthful by player field 4 (`R+0x14`): the stand
  selector `0x0210EA60` picks the mouthful stand from it, and the
  give-ability routine's `0x020AEF70` resets it after `SetAbility`. New
  export `kssu_move` (action, field 4, copy byte, move descriptor `R+0x808`,
  move table `R+0x800`); `kssu_set_ability` refuses while field 4 is 2;
  `KirbyFSlot` neither starts nor keeps waiting a press in a mouthful.
- Case `move_mouthful`: field 4 through a catch, the five mouthful moves, a
  spit and a swallow, the refusal, and a change after the swallow. Private
  root `repros/move-158`, shared bridge untouched: 90 cases pass, the other
  89 equal to the recorded results.
- Live `fslot` with `KIRBY_LAB_FSLOT_MOUTHFUL=1` (`20261014-125300`,
  `125544`): F with a real Zombie held started nothing and kept the mouthful
  and Crash's use; after the spit F fired Crash as usual.
- Not run: the drop of a press already waiting when a catch completes, the
  swallow path live, verifiers (no rendering change), the owner's launcher.

## 2026-10-14 — cheaper per-step bookkeeping (#160)

- `KirbyEnemies.Prepare` tested every entity of the level with host calls
  before its reach; it now tests the reach first, keeps its candidate list
  and one roll delegate between steps. `KirbyMutations` no longer allocates
  an array or a closure per check. Same filters, same pairs.
- The bridge keeps its walk of the actor lists (which object is in which
  proxy slot) until a call steps, writes, loads or moves something; the
  proxy setters keep it, and `kssu_proxy_set` walks again only after its
  first call enlarges the scene's record table. New case `proxy_cache`:
  960 reads through births, inhales, swallows, kills and releases, all
  equal to a forced fresh walk; a build that never drops the walk fails it.
- Private build `repros/perf-160` (bridge `5f88a1ae…`), shared bridge
  untouched: 90 cases pass, the 89 older ones equal to the shared record.
  Python-driven, 31 proxies: proxy calls 0.109 → 0.072 ms a frame, same
  900-frame trace. `scripts/check.py` passes.
- Live, one 120 s run each, `main` → managed alone → both: `Prepare`
  0.206 → 0.139 → 0.122 ms moving and 0.506 → 0.447 → 0.346 ms beside 31
  Zombies; host fps 107.2 → 111.6 and 86.5 → 94.3. The three crowd runs
  played the same fight (66 hits, 2,046 damage, 31 destroyed).
- Left: the host's own per-target damage queries (most of the crowd's
  `Prepare` and its 35 KB a step) and the per-byte OAM and palette reads.
  Timed and left: `kssu_world` 1.7 µs a call, `kssu_state` 0.3 µs (a bare
  Python call is 0.4 µs), under 2 µs a step together.
  Not run: the owner's play launcher, a window on the GPU, a long session.

## 2026-10-14 — OAM and palettes read as blocks (#165)

- `kssu_read_gpu` read OAM and the palettes one bus byte at a time. A
  fourth wrapper stage (`tools/nds_graphics_patch.py`, `tools/nds_vendor.py`,
  `src/nds/melon-rs-gpu.patch`) adds `Copy_GpuMemory`: the palettes or OAM
  as `ARM9Read8` returns them, a powered-off engine's half as zeros. Reads
  aligned to 8 bytes inside either 2 KiB use it; the far-object check reads
  engine A's OAM through it too.
- Case `gpu_block`: both whole areas and a partial read equal the bus read
  one byte off. Private root `repros/gpu-165`, shared bridge untouched: 93
  cases pass, the 92 older ones equal to the shared record. Verifiers: 29
  renderer cases and 4 background cases with 0 mismatches, 64 native-init
  cycles.
- Python, new build: OAM 1 KiB 3.62 → 0.41 µs, palettes 512 B 2.02 →
  0.40 µs; a frame's reads 0.036 → 0.028 ms. Live `movement`, 120 s
  (`20261014-214926`): `Read` 0.0421 → 0.0387 ms a step against #160's
  run; everything else within noise.
- Not run: the owner's play launcher, a window on the GPU, a long session.

## 2026-10-14 — read the move, not only the action (#166)

- The wade: on the bottom of water the walk action `060B` runs move
  `0x02131160`, 0.9375 px a frame at most against the dry walk's 1.375. The
  speed match scaled it as a dry walk (1.79 px a frame); it now keeps the
  original's motion and walk cycle. `tests/speed` pool check: 177 wade frames
  at exactly the original's velocity, failing with the old rule.
- Player field 4 is 4 in water and 5 with a mouthful in water. The F check and
  `kssu_set_ability`'s refusal now read 2 and 5. New bridge case `move_wade`;
  it fails at the refusal on the bridge from before #166.
- The exhale is known by its move (`0x021313D0`), not by the float before it.
- Copy attacks: a headless survey of all 24 copies' moves (tables from
  `0x02129DE8` to `0x02130B44`). The copy item's affixes are now used when a
  copy move starts for a waiting press, not on every press. A first rule (a
  press expires 10 frames after it is made) missed Sword's third slash live (6
  uses for 15 presses); the final one keeps a press while a copy move runs.
- Private build `repros/move-166` (bridge `03784873…`), shared bridge
  untouched: 93 cases pass, the 92 older ones equal to the shared record. Core
  tests 484 assertions, `tests/speed` 46 checks, `scripts/check.py` pass.
- Live: `20261014-221157` waded 117 frames at a median 0.9375 px a frame in a
  42 px `Greenhouse` pool; `20261014-222720` used Sword's item 9 times for 15
  presses (9 slashes), with the arrow and bleed, and `20261014-225817` did the
  same with frames captured.
- Not run: copy abilities or the helper wading, F with a water mouthful live,
  other copies' affix uses live, the owner's launcher.

## 2026-10-14 — a real species in the free enemy slot, tried (#161)

- `0x020936A4(type, unused, parameter)` loads a species' overlay and graphics
  during play, as the room loader does. A headless probe ran it through the
  call hook for the five species of the empty slot 2 (Knuckle Joe, Gim, Jungle
  Bomb, Birdon, Tac) and spawned each as a proxy. Each one loaded in 1 or 2
  frames, matched the ROM's file, and was taken by the bridge as a proxy.
- Its own program runs: Knuckle Joe took Kirby from 76 to 50 health in 300
  frames and could not be inhaled; Gim's yo-yo hit for 10. The other three
  were inhaled and gave their copy. With Waddle Dee's update routine, Knuckle
  Joe made no attack and gave Fighter, but his touch still hurt Kirby twice
  for 8, which the Waddle Dee proxy did not from the same place.
- Not adopted: one free slot, never drawn, and nothing gained once its program
  is off. Recorded in `docs/ROM_INTERNALS.md` (Enemy slots). No code changed.

## 2026-10-14 — the ROM notes say where each fix candidate went (#163)

- `docs/ROM_INTERNALS.md`, Fix candidates, has a column that points each
  row to its issue, pull request and record: the recompiler (#153), the
  doubled ARM9 clock (#150) and the undrawn upper screen (#148) are in and
  on by default; reading the move is in for the mouthful (#158) and for
  wading, the mouthful in water, the exhale and copy attacks (#166); the
  bookkeeping is in for the pairing and the actor-list walk (#160) and the
  OAM and palette reads (#165), with `kssu_world` and `kssu_state` timed and
  left; real species were tried headless and not adopted (#161).
- Not established: the line saying the recompiler and the clock never met
  the bridge suite, the verifiers or the game is gone (#150 and #153 ran
  all three), and the collision pool joins what was checked in play.
- Not run: anything live; documentation only.

## 2026-10-15 — the dropped star keeps the copy's item for 7 seconds (#68)

- Owner decision on #54: losing the copy (a hit or a discard) puts its ability
  item on the original ability star, which lasts 7 s; swallowing the star again
  gives back the same item, and when the star runs out both are gone.
- The original star keeps its copy byte at +0x49, a 480-frame countdown at
  +0x4C and a state at +0x60 (0 once the inhale has caught it). The runtime
  writes each new star's countdown 60 lower, pairs the lost item with the star
  of that copy (`StarItems`, `KirbyStarItems`), and puts a caught star's item in
  the mouthful once the original mouth fills. A spat star's item is gone, never
  back on the floor. Core tests 501 assertions; `scripts/check.py` passes; the
  bridge case `copy_drop_reabsorb` now checks the star fields (caught at 57,
  record gone at 58, mouth full at 69).
- Live (`repros/star-68-20261015`): two catch runs got the same Sword item back
  (level, "+", `Grenade`) and saw the second star run out 433 frames after its
  discard, taking the item; a spit run lost the item with the copy. Earlier runs
  showed a star leaving early when it bounces into a drop deeper than the
  original's 9-row window, and Sleep dropping no star.
- Not run: a hit knocking the copy off, a helper, a star in a mouthful with
  something else, an Elite's Legendary item on a star, a save while a star is
  out, a level change with a star out, the owner's launcher.

## 2026-10-15 — the forge rerolls and upgrades the copy's item (#58)

- The passage forge already forges the copy's ability item with its own code:
  the reroll keeps its level, "+", Legendary and `Colorless` (kept on reroll),
  the upgrade adds a "+", and copy damage reads the new bonus at once. It also
  listed the starting Rusty Sword before the first copy. `KirbyForge` lets it
  list only ability items and shields in a Kirby run (`AbilityItems.ForgeLists`).
- Core tests 505 assertions; `scripts/check.py` passes.
- Live (`repros/forge-58-20261015`): with the filter the forge was empty before
  the first copy; with the copy it listed only the Sword item, rerolled it (700
  gold, `BleedOnHit` to `DeathExplosion`) and upgraded it to I++ (1500 gold,
  +33% to +67% copy damage). With the filter off it listed "Rusty Sword I".
- Not run: a forge NPC in a passage, a shield or a Legendary item in the forge,
  `ForgeRefine1`, the owner's launcher.

## 2026-10-16 — inside a Dead Cells frame, against the Beheaded's (#174)

- Dead Cells v35 read the way the ROM was, read-only: `hlboot.dat` (SHA-256
  `59350cad…`) disassembled with crashlink into a private index (37,448
  functions), DCCM 35.13.6 read as IL. Disassembly, index and notes stay in
  `research/dc-re` in the private data root. docs/HOST_INTERNALS.md records
  the frame loop, the 30 Hz entity loop, level generation and loading, the two
  collectors, DCCM's hook layer and what each kind of mod call costs.
- Findings: a hooked host function costs about 2 µs and 920 B a call through
  DCCM's managed path and leaves a finalizable object, so nearly every .NET
  collection is a blocking generation-1 one of 6–8 ms; removing a handler
  leaves the function detoured into .NET for the session; the game runs the
  workstation collector, not the server one this record claimed.
- Lab tooling: `KIRBY_LAB_PERF=quiet`, `_HOST`, `_HOOKS`, `_ALLOC`,
  `KIRBY_LAB_BARE`, `KIRBY_LAB_SEED`, `KIRBY_LAB_REAL_KEYS`, and
  `launch_runtime.py --gpu`, `--shader-cache` and `--crash-dump`.
- Live (`repros/perf-174-kirby`, `repros/perf-174-beheaded`, seed 174, one run
  each): Kirby moving, software drawing, 108.1 fps, 126.5 KB a frame, 108.7
  hooked calls a frame (`HSprite.draw` 78.9), 47 collections of 7.4 ms; the
  Beheaded 125.8 fps and 134.6 KB; Dead Cells alone 137.6 fps and 39.3 KB. On
  the GPU 1,092.9, 1,272.7 and 1,853.4 fps. docs/PERFORMANCE.md has the tables
  and the fixes ranked: #175, #178, #176, #177.
- The four fixes together (`measure/all2`, two runs each unless said): moving
  in software 126.3 → 43.9 KB a frame, 108.6 → 24.7 hooked calls, 9.11 →
  8.37 ms; in the crowd 250.4 → 68.5 KB, 481.7 → 32.0 calls, 11.69 → 9.77 ms,
  frames over 20 ms 1.0 → 0.19 a second; on the GPU 0.90 → 0.61 ms moving and
  1.76 → 0.96 ms wading in the Greenhouse (569 → 1,037 fps).
- The bridge alone, driven from Python like the runtime drives it: 0.94 ms a
  step at the median, half of it recompiled ARM code, a sixth timers and the
  event scheduler, an eighth the SPU. No bridge change: fast memory stays off.
- Not run: windowed play, level load times, the owner's launcher.

## 2026-10-16 — no hook on every host sprite draw (#175)

- `KirbyHostVisuals` hooked `HSprite.draw`, the host's draw of every sprite,
  to hide the outfit atlas's fallback pose: 78.9 hooked calls a frame moving,
  135 in the crowd, about 200 wading in the Greenhouse, the Beheaded included.
- The outfit's in-game page is now blank (`tools/build_outfit.py`); the skin
  preview reads `kirbySuperStarUltraPreview0.png`. The hook and `OnFrame` are
  gone. `scripts/stage_runtime.py --original-runtime` refuses an older pack.
- Live (`repros/perf-174-kirby`, seed 174, quiet, software, two or three runs
  each): moving, 126.3 → 53.7 KB a frame, 51 → 22 collections a minute,
  108.6 → 29.8 hooked calls; in the crowd 250.4 → 123.0 KB, 96 → 47
  collections, 481.7 → 340.8 calls, the mean frame 11.69 → 10.65 ms. Traces
  matched the #174 build in all 2,907 shared original frames; the arrival and
  the first moments of play look the same, with nothing of the outfit drawn.
- Not run: the skin selector's preview, the owner's launcher, a window on the
  GPU.

## 2026-10-16 — DCCM's detour taken off unhooked host functions (#178)

- Removing a DCCM handler leaves the function detoured into .NET for the
  session (0.77 µs and 56 B a call): 280 calls a frame with no handler in the
  crowd. Turning DCCM's direct route back on crashed the game after the held
  enemies were let go in 3 of 4 crowd runs, so that version was dropped.
- `DccmDetours` undoes the detour (`NativeHooks.HookHandle.Disable`) when
  `KirbyEnemyHooks.Relax` leaves a function with no handler, writes it again
  (`Enable`) before the next `Hold`, and remakes the manager's closure over
  the new trampoline. Reflection; nothing changes if DCCM's fields are
  missing.
- Live (`repros/perf-174-kirby`, seed 174, crowd, quiet, software): 250.4 →
  233.3 KB a frame, 96 → 79 collections a minute, 481.7 → 201.6 hooked calls
  (280.0 → 0 with no handler), the mean frame 11.69 → 10.82 ms. No crash in
  the 8 crowd runs with it, each the same fight (66 hits, 2,046 damage).
- Not run: another mod hooking the same functions, a hold across a level
  change, the owner's launcher.

## 2026-10-16 — the controls read once per input read (#176)

- `KirbyInput` asked the host for the bindings again for every action and
  tested keys through `dc.hxd.Key.Class.isDown`, a new delegate on every
  access. A read now takes the bindings, the modes and `hxd.Key.keyPressed`
  once (`isDown(code)` is `keyPressed[code] > 0` in the bytecode).
- Live (`repros/perf-174-kirby`, seed 174, quiet, software, two or three runs
  each): with the lab's input a read went 20 µs, 1,704 B → 11 µs, 14 B; with
  keys through the host's key table (`KIRBY_LAB_REAL_KEYS=1`) 73 µs,
  13,948 B → 19 µs, 8 B, and 150.8 → 122.2 KB a frame. With real keys two #174
  runs differed in 331 of 2,968 original frames (presses timed in host
  milliseconds) and this branch's runs from them in 221 to 227; with the lab's
  input all four fixes matched #174 in every frame.
- Not run: a controller, the owner's launcher.

## 2026-10-16 — two hurtbox hooks dropped (#177)

- `Hero.postUpdate` and `Entity.spriteUpdate` run from `Level.postUpdate`,
  after the hurtbox projection has ended, so their `Physical` handlers never
  found it on. They are gone, and `KirbyMod`'s own handlers on
  `Hero.preUpdate` and `Hero.fixedUpdate` run their work inside `Physical`
  instead of a second handler each (same order: the hurtbox handler was added
  last, so it already ran outermost).
- Live (`repros/perf-174-kirby`, seed 174, quiet, software, two or three runs
  each): 108.6 → 103.7 hooked calls a frame moving, 481.7 → 450.9 in the
  crowd; 126.3 → 120.3 and 250.4 → 217.8 KB a frame. Traces matched the #174
  build in all 2,912 shared original frames.
- Not run: the owner's launcher, every hurtbox case of the projection's own
  record.

## 2026-10-17 — a live check of main after the four speed fixes (#190)

- `main` at `28f4086` (#179–#183 merged) built, staged in a new isolated
  profile (`repros/main-28f4086`) and run with the settings of "The four
  fixes together (#174)": seed 174, quiet, hook counter, software drawing.
  `movement` (`20261017-001113`) and `crowd` (`20261017-001241`) ended by
  their time limit with no fatal log and the real saves unchanged.
- Against `measure/all2`, which has the same source: 42.6 and 68.0 KB a frame
  (43.9 and 68.5), the same kinds of slow frames and the same crowd fight;
  109.5 and 96.1 fps (119.5 and 102.4), the difference outside the mod's own
  work and within the spread of earlier runs of one build.
- Not run: a second run of each, the GPU, the Greenhouse, real keys, traces
  with frame captures, the owner's launcher.

## 2026-10-17 — the mod compiled ahead of time (#184)

- The first host frame in which the original steps, at the end of the arrival,
  took 53.6–54.6 ms: 30.8–31.3 ms of it the .NET JIT compiling 261 methods,
  12 ms the first original step. The lab's new arrival record
  (`perf-arrival.json`) times every frame from Kirby's activation to his 600th
  counted frame, with the JIT in each stage and host phase.
- The build compiles the packed `Kirby.Core` and `Kirby.Runtime` ahead of time
  (ReadyToRun) with crossgen2 10.0.12, restored from NuGet;
  `-p:ModReadyToRun=false` packs the IL alone.
- Live (`repros/perf-184`, seed 174, movement, quiet, software, two runs
  each): the first Kirby frame 53.6–54.6 → 38.3–39.3 ms, its JIT 30.8–31.3 →
  14.7–14.9 ms (261 → 135 methods); played frames, garbage and collections
  unchanged. The arrival captures looked the same, and Kirby's original state
  matched the IL build in all 866 shared original frames. Crossgen2's
  cross-module options gained 1–3 ms more and were left out.
- Not run: GPU drawing, a window with vsync, a first launch after a reboot,
  the owner's launcher.

## 2026-10-17 — the movement sticks through the host's own setting (#185)

- The hook on `ControllerAccess.getMovementSticks` is gone. The masked hero
  update sets each stick whose function is movement to none and puts the
  options back after it, and back for the player while a charging weapon
  reads its aim (`WithHostDirections`).
- Live (`repros/perf-185-kirby`, seed 174, quiet, software, three `before`
  runs on `main` 28f4086 and two with the change): 24.9 → 17.6 hooked calls a
  frame moving, 32.3 → 24.9 in the crowd; 44.6 → 37.5 and 69.5 → 62.0 KB a
  frame. The `aim` plan saw no movement stick inside the masked update and
  the player's left stick outside it; its key checks fail on `main` too since
  #176 (it presses keys through `Key.isDown`).
- Not run: a real controller stick, the owner's launcher.

## 2026-10-17 — the health flask out of Kirby's skills (#186)

- The hook on `HealFlask.isEnabled` is gone. A Kirby session takes the flask
  out of the hero's main skills with the host's `unregisterSkill` and puts it
  back in its place when it ends; a handler on `HeroMainSkillsManager.serialize`
  writes it into a save made in between.
- Live (`repros/perf-185-kirby`, seed 174, quiet, software, three `before`
  runs on `main` 28f4086 and two with the change): 24.9 → 21.6 hooked calls a
  frame moving, 32.3 → 28.7 in the crowd; 44.6 → 41.8 and 69.5 → 65.6 KB a
  frame. The `healing` plan gave the same lives, fountain heal and F slot as
  `main`, through a level exit, with the flask out of the skills.
- Not run: a run save during a Kirby session (the `serialize` handler), the

## 2026-10-17 — the Collector's unlock check hooked only while it runs (#187)

- `KirbyCollector`'s handler on `ItemMetaManager.hasUnlockedItem` is added by
  the first `canInvestOnItem` check of a Kirby run and taken off between host
  frames 30 frames after the last one, with DCCM's detour, instead of staying
  on for the whole session.
- Live (`repros/perf-185-kirby`, seed 174, quiet, software, three `before`
  runs on `main` 28f4086 and two with the change): 24.9 → 22.8 hooked calls a
  frame moving, 32.3 → 30.2 in the crowd; 44.6 → 42.1 and 69.5 → 66.9 KB a
  frame. The Collector check gave `main`'s counts, gates and lists, with the
  handler on only from Kirby's first check.
- Not seen: the handler coming off after a Collector visit. Not run: the
  owner's launcher.

## 2026-10-17 — the weapon update hooked only while the shield is held (#188)

- `KirbyInput`'s handler on `HeroWeaponsManager.updateWeapon`, which lets a
  charging weapon read the real directions inside the masked hero update, is
  added while the retained shield's binding is down or the assist mode attacks
  by itself, and taken off 30 host frames later once no weapon charges, with
  DCCM's detour. New `shield` lab plan (`LabShieldHoldProbe`).
- Live (`repros/perf-185-kirby`, seed 174, quiet, software, three `before`
  runs on `main` 28f4086 and two with the change): 24.9 → 22.8 hooked calls a
  frame moving, 32.3 → 30.2 in the crowd; 44.6 → 42.4 and 69.5 → 66.8 KB a
  frame. The `shield` plan passed: the handler on in every held, charging
  frame, the host reading the real directions and turning the shield, and off
  after the release.
- Not run: the assist mode's automatic attack, the owner's launcher.
