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
