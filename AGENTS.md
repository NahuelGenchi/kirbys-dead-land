# Agent instructions

## The project

Kirby's Dead Land is a Linux mod that makes Kirby playable in Steam Dead Cells. The owner's Kirby Super Star Ultra USA revision 0 ROM runs in an embedded melonDS instance and drives Kirby's movement, physics, copy abilities, sprites and sound. Dead Cells supplies the level, enemies, equipment and progression through Dead Cells Core Modding (DCCM). `README.md` lists the requested rules and verified behavior; `MODDING_PLAN.md` and `docs/GAMEPLAY_FIDELITY.md` record acceptance evidence and limits. Complete parity for every host interaction is not established.

Never describe something as working or complete unless a recorded check establishes it. A source test, a build, or a sprite comparison is not proof of live gameplay.

## Layout

| Path | Contents |
| :--- | :--- |
| `src/core/` | Platform-independent rules (`KirbyRules.cs`) |
| `src/runtime/` | DCCM adapter: input, enemies, damage, hurtboxes, saves, rendering, audio and `Lab*` live probes |
| `src/nds/` | Rust bridge to the pinned melon-rs/melonDS, plus its patch |
| `launcher/` | Player launcher (Rust and Slint): checks, one-time setup, Play (`docs/LAUNCHER.md`) |
| `scripts/` | Runtime setup, staging, launch and `check.py` |
| `tools/` | ROM extraction, outfit build, oracle and verification scripts |
| `tests/` | .NET and Python source tests |
| `docs/` | One record per component or decision; the README links each one |
| `MODLOG.md` | Dated work journal |

## Commands

- `python3 scripts/check.py` runs the main source checks; `python3 tools/nds_tests.py` runs the native bridge tests.
- `python3 tools/verify_renderer.py`, `tools/verify_background.py` and `tools/verify_native_init.py` compare against original output.
- `python3 scripts/setup_runtime.py --build`, `python3 tools/nds_build.py` and `python3 scripts/stage_runtime.py` build and stage the runtime. `python3 scripts/launch_runtime.py` runs an isolated live check (see `docs/RUNTIME_LAUNCH.md`).
- `./play-kirby` is the owner's normal play launcher. Do not use it for automated checks.

Run builds and live checks only when the task changes what they cover.

## Working well here

- Start from the active issue, then read only the code and doc sections involved. Docs are long; search for the section you need.
- The ROM and installed game are read-only. Never modify the Steam game directory, install a loader there, or touch the owner's real saves. Live checks use the isolated private profile under `~/.local/share/kirby-dead-cells/`.
- Original behavior comes from the ROM. Prefer driving original code over reimplementing it on the host, and record any intentional deviation in `docs/GAMEPLAY_FIDELITY.md`.
- Record results in the doc for that component, in the same PR as the code. State what is implemented, blocked or unknown, and what was not run. Add a dated `MODLOG.md` entry for meaningful work.

## Private data

ROMs, Dead Cells files, saves, save states, extracted or generated asset packs, recordings and credentials stay out of commits, issues and PRs. Write derived output only under ignored paths or the private data root. ROM hashes used for verification may be cited.

## Tracking and delivery

Every change (code, bug fixes, enhancements, docs, research and repository operations) belongs to an issue with a milestone in `NahuelGenchi/kirbys-dead-land`, reused or created before editing. Every new feature, and every other work item, gets its own new branch created from the latest `origin/main` for that issue: `<type>/<issue>-<slug>` (`feat`, `fix`, `enh`, `docs`, `research`, `ops`). Never reuse a branch for a second feature, never mix two issues on one branch, and never push to `main`. Use `type: summary (#N)` commits. Delete the branch after its PR merges. PRs follow `.github/pull_request_template.md`: a standalone `Closes #N` line, the issue's milestone, and `Refs #N` for unfinished parent work. Decisions, exact changed paths and blockers go in the issue or PR. See `CONTRIBUTING.md` and `docs/TRACKING.md`.

## Final report

Report the issue, the PR and what changed. Keep these separate: source tests (exact commands and outcomes, or "none run"), live game checks (the same), builds, and source/diff review scope.
