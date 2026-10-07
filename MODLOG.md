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
