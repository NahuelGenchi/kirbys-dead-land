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
