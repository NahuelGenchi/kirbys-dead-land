# Releases

[Back to README](../README.md) · [Third-party notices](../THIRD_PARTY_NOTICES.md) · [Work tracking](TRACKING.md)

A release is a source archive built from one commit. It holds no ROM, Dead Cells
file, binary, save, state, recording or generated pack; players build everything
with `scripts/install.py` (see [Install](../README.md#install)). From v0.2.0 a
release also carries the [player packages](#player-packages): the launcher with
programs built from the same commit, still without any ROM, game or Kirby art.

## Building the archive

```sh
python3 scripts/package_release.py --ref v0.1.0
```

The script reads tracked files from the commit with git (never the working tree),
leaves out the repository workflow files (`.github/`, `AGENTS.md`, `CLAUDE.md`,
`.gitignore`), and writes `dist/kirbys-dead-land-<version>.tar.gz` and its
`.sha256`. The version comes from `mod.json` at that commit. Executable bits are
kept and file times are the commit time.

Before writing anything it runs the release lint, which fails on:

- ROM, save, state, game, binary, archive, image, audio, video and dump suffixes
  (`.nds`, `.sav`, `.state`, `.pak`, `.dat`, `.hdll`, `.dll`, `.so`, `.png`, `.wav`,
  `.mp4`, `.dmp` and others in `FORBIDDEN_SUFFIXES`);
- any file inside a private data folder (`evidence`, `lab`, `rom`, `oracle`,
  `research`, `repros`, `outfit-build`, `ability-icons`, build outputs and others);
- files over 1 MiB or with NUL bytes;
- key-like strings (GitHub, OpenAI-style, AWS, Slack and fal keys, private key
  blocks).

`tests/test_release_package.py` covers each rule and checks that the current
commit passes.

## Player packages

`scripts/build_packages.py` makes the downloads described in
[Player launcher](LAUNCHER.md#download), each with a `.sha256`: the two Linux
ones, and with `--target windows` the Windows one (#252):

| File | Layout |
| :--- | :--- |
| `KirbysDeadLand-<version>-x86_64.AppImage` | `AppRun`, the desktop entry and icon at the top; `usr/bin/kirbys-dead-land`; the bundle at `usr/share/kirbys-dead-land`, found through `$APPDIR`; `LICENSE`, `NOTICES.txt` and `NOTICES-emulator.txt` in `usr/share/doc/kirbys-dead-land/` |
| `KirbysDeadLand-<version>-x86_64.tar.gz` | One folder, `KirbysDeadLand-<version>-x86_64/`: `kirbys-dead-land`, the bundle at `share/kirbys-dead-land` beside it, `LICENSE`, `NOTICES.txt`, `NOTICES-emulator.txt`, `README.txt`, the desktop entry, the icon and `add-to-menu.sh` |
| `KirbysDeadLand-<version>-windows-x64.zip` | One folder, `KirbysDeadLand-<version>-windows-x64/`: `Kirby's Dead Land.exe`, the Windows bundle (`build_bundle.py --target windows`) at `share/kirbys-dead-land` beside it, `LICENSE`, `NOTICES.txt`, `NOTICES-emulator.txt`, `NOTICES-windows-runtime.txt` and `README.txt` (`packaging/README-windows.txt`) |

The bundle is `scripts/build_bundle.py`'s ([Player
launcher](LAUNCHER.md#building-and-checking)): the tracked scripts and tools, the
ReadyToRun runtime adapter, the bridge and the oracle runner. `NOTICES.txt` is
`launcher/NOTICES.txt` (the launcher's crates, fonts and icons, the downloaded
helpers, and the release tag as the source); `NOTICES-emulator.txt` is
`packaging/NOTICES-emulator.txt` (melonDS, melon-rs, the libraries melonDS bundles
and every crate in the pinned bridge and runner lockfiles). The package lint fails
when either is missing, and the launcher shows both in Settings › Licenses. Run
`python3 tools/emulator_notices.py` after changing a pinned lockfile or the
melonDS or melon-rs revision; a test fails until it is. The icon is drawn by the
launcher's own code (`--write-icon`), not taken from the ROM.

```sh
python3 scripts/build_packages.py --output dist [--tag v0.2.0]
python3 scripts/build_packages.py --target windows --output dist [--wine WINE]
```

`--target windows` (#252) builds in a root of its own (`release-build-windows`):
the runtime precompiled for Windows (`setup_runtime.py --build --ready-to-run-os
windows`), `kssu_bridge.dll` and `kssu-oracle-runner.exe` (`--target windows`,
#242) and the launcher for `x86_64-pc-windows-gnu`, all with MinGW-w64's POSIX
compilers and linked statically (`oracle_build.windows_environment`), so they need
only Windows' own DLLs. It writes `NOTICES-windows-runtime.txt` from the license
records of the build machine's MinGW-w64 packages
(`/usr/share/doc/mingw-w64-x86-64-dev/copyright`, the MinGW-w64 C runtime and
winpthreads, and `/usr/share/doc/gcc-mingw-w64-base/copyright`, libgcc and libstdc++
under the GCC Runtime Library Exception), lints the package like the others (every
program must start with `MZ`), writes the `.zip` (file times the commit time,
members in name order) and reads it back. The launcher's `--check` runs only when
`--wine` names a Wine program. The runtime's in-tree build is replaced by the
Windows one; run `setup_runtime.py --build` again before a Linux lab run.
`tools/emulator_notices.py` lists the bridge's and runner's crates for both systems.

The script:

1. Builds every program from the working tree in a data root of its own
   (`--build-root`, default `release-build` in the data root), so the lab's bridge is
   never replaced: `scripts/setup_runtime.py --build --rebuild`,
   `tools/oracle_build.py` and `tools/nds_build.py` with the `Cargo.lock` files in
   `packaging/` (the dependency versions the lab's bridge and runner were built
   with; the build stops if Cargo changes them), and `cargo build --release
   --locked` for the launcher.
2. Maps the build machine's folders out of every path the compilers embed: the
   home folder to `/build/home`, the build root to `/build/data` and the checkout to
   `/build/kirbys-dead-land`, through `--remap-path-prefix` in `RUSTFLAGS`,
   `-ffile-prefix-map` in `CFLAGS` and `CXXFLAGS`, and MSBuild's `PathMap`. So
   `build_bundle.py` accepts the programs without `--private`, on any machine.
3. Lays out each package and runs the package lint (`lint_package` in
   `scripts/package_release.py`): the files the build made (launcher, bridge,
   runner, the two runtime DLLs and their two settings files, the icon) are
   accepted only at their own paths, must start with the right bytes (ELF, `MZ`,
   PNG, `{`) and stay under 64 MiB; every other file must pass the release lint
   above; no file may name the build machine's home folder, build root or checkout,
   or hold a key-like string.
4. Writes the `.tar.gz` (file times the commit time, owner root) and the AppImage:
   the AppImage type 2 runtime (`type2-runtime` release 20251108, pinned by SHA-256)
   followed by a zstd squashfs image of the AppDir, which is what `appimagetool`
   writes, made with `mksquashfs` and `SOURCE_DATE_EPOCH`.
5. Reads each package back (the tar members, and the squashfs image with
   `unsquashfs` at the runtime's length) and stops unless it holds exactly the files
   that passed the lint. Then it runs each package's launcher with `--check` in an
   empty home folder, which must print `Mod files: <folder> (<version>+<commit>)`
   for its own bundle. Without FUSE the AppImage runs with
   `APPIMAGE_EXTRACT_AND_RUN=1`.

`--tag` is for releases: the tag, `mod.json` and the launcher's `Cargo.toml` must
name one version, the tag must name the checked-out commit, and the tree must be
unchanged. `--skip-build` packages what an earlier run built.

Run directly, the script needs Python 3.11 or later, Rust, CMake, a C++ compiler,
pkg-config, the ALSA and fontconfig headers and squashfs-tools, and the programs need the glibc of
the machine that built them. Releases use the workflow below instead.

### Release workflow

`.github/workflows/release.yml` runs when a `v*` tag is pushed, or by hand: with an
existing tag it builds that tag and attaches the files to its release; without one
it only builds the chosen branch and keeps the files as a workflow artifact.

- **Build** (read-only token), once per target (`linux`, `windows`): an
  `ubuntu:22.04` container pinned by digest on an `ubuntu-24.04` runner, so the
  Linux programs need only glibc 2.35 and the libstdc++ of Ubuntu 22.04.
  The Windows leg runs in `ubuntu:24.04`, also pinned by digest: Ubuntu 22.04's
  MinGW-w64 8 lacks the Windows memory calls melonDS's JIT uses
  (`MEM_RESERVE_PLACEHOLDER` and the others), and Windows programs do not depend on
  the build machine's glibc. `release_tools.sh windows` adds Ubuntu's MinGW-w64 11
  (GCC 13) with POSIX threads and Rust's `x86_64-pc-windows-gnu` target. `scripts/release_tools.sh` installs Ubuntu's compilers, CMake,
  ALSA and fontconfig headers, ICU, OpenSSL and squashfs-tools, Python 3.14.8
  (python-build-standalone 20261009, the launcher's own pin) and rustup 1.29.1, both
  checked by SHA-256, and Rust 1.99.0. The .NET SDK 10.0.401, DCCM, melon-rs and
  melonDS are pinned by the build scripts. Then `build_packages.py --target <target>
  --tag`, and on Linux `package_release.py`; the packages, the source archive and
  the `.sha256` files are uploaded as `packages-linux` and `packages-windows`.
- **Notes**: before the build, each job stops if the tag has no
  [release page](#release-page-239) parts or pictures.
- **Attach** (the only job that can write): checks out `.github/release` at the
  tag (no credentials kept), writes the page with the AppImage's and the `.zip`'s
  sizes and the pictures linked at the tag's commit, and runs `gh release upload
  --clobber` to the tag's release, or to a new draft pre-release with those notes
  when the tag has none yet. An existing release keeps its notes.

Actions are pinned by commit, the checkout keeps no credentials, and the tag
reaches shell commands only through environment variables.

`scripts/release_container.sh [vX.Y.Z]` runs the same steps on this machine, in
the same container image, from the committed `HEAD` (or the tag), one container
per target (`RELEASE_TARGETS`, default `linux windows`), and copies the packages,
the source archive and their `.sha256` files to `dist/`. It needs
Docker; `RELEASE_DOCKER_ARGS` passes extra `docker run` options such as
`--memory 4g`. A test checks that it and the workflow use one image and the same
build commands.

### Release page (#239)

Every release page is one design: `.github/release/notes.md`, filled in by
`.github/release/notes.py vX.Y.Z`. It holds the banner, the download button, how
to update from an older version (from 0.2.1 on), what the mod is, what players
need, the four steps with pictures, their saves, how Kirby plays, help, and the
files and licenses. Each version adds a folder, `.github/release/vX.Y.Z/`, with
two short parts in players' words, `new.md` (what is new or fixed) and
`tested.md` (what was tested), and its pictures. From v0.3.0 (#252) the page
offers the Windows download beside the Linux one, with the Windows first step and
a `download-windows.png` button; earlier versions render as before:

```bash
cargo build --manifest-path launcher/Cargo.toml
python3 .github/release/make_media.py
```

`make_media.py` runs the launcher's `--screenshot --scale 2` (stand-in art, never
`--art-from`) and writes the banner, the download button and the four step
pictures, all showing the launcher's version, to that version's folder. Commit the
folder with the version change, before the tag: a test fails while the current
version's folder is incomplete, and the workflow will not build a tag without it.
The pictures hold no game art; pictures from the game itself (Kirby, his
abilities, Dead Cells) stay out of the repository and the releases.

To rewrite a published release's notes, render them with `--ref` set to a commit
that holds the folder and `--appimage-bytes` set to the AppImage's size, then
update the release's body.

### Verification (#222)

2026-10-20, this machine (Ubuntu 26.04, Docker 29.1.3): `scripts/release_container.sh`
on this branch, which runs the workflow's build steps in its container. On 2026-10-21
it was repeated on the branch's code commit (`9b35427`) in a new container: the whole
build took 3 min 17 s, and the checksum and host `--check` results below held again.

- The container (limited to 3.5 GB and 8 CPUs, `CARGO_BUILD_JOBS=6`) built the
  runtime, the runner and the bridge (the pinned `Cargo.lock` files unchanged) and the
  launcher. `build_bundle.py` accepted every program without `--private`. Both
  packages passed the lint and were read back, and each launcher's `--check` named its
  own bundle (the AppImage through `APPIMAGE_EXTRACT_AND_RUN`: the container has no
  FUSE).
- AppImage 15.2 MB and `.tar.gz` 15.3 MB, 55 files each. No file names a home, build
  or checkout folder; the compilers' paths read `/build/home`, `/build/data` and
  `/build/kirbys-dead-land`. The launcher needs glibc 2.35 and fontconfig; the bridge
  and the runner need glibc 2.34 and GCC 11's libstdc++ (`GLIBCXX_3.4.29`). The
  bridge exports the same 252 symbols as the lab's, built from the same
  `src/nds/lib.rs`, with the recompiler.
- On the host, the AppImage started through FUSE and `--check` in an empty home found
  `/tmp/.mount_…/usr/share/kirbys-dead-land`; the unpacked `.tar.gz` found its
  `share/kirbys-dead-land`.
- Live, offscreen: the AppImage's `--autopilot` with `--smoke-play 72`, in a new data
  root and config folder. Checks, setup from the found ROM (1 min 16 s), home with
  Kirby and the 24 icons, then Play. The game loaded the packaged bridge (same SHA-256
  as in the AppImage): `dc.pr.Game`, live hero, Kirby outfit, original ROM provider,
  recompiler, 14,577 frames, normal timeout, save isolation installed, real saves
  unchanged, no fatal log; the last frame shows Kirby in the starting cell. The
  autopilot exited 0.

Two problems turned up and are fixed here. melonDS turns on GCC link-time
optimization when CMake supports it, as Ubuntu 22.04's does; that leaves bytecode
objects Rust's linker cannot read, so `packaging/melonds.cmake` keeps it off, as in
the lab's builds. The launcher also needs the fontconfig headers to build.

Not run: the workflow on GitHub (Actions is stopped by billing on this repository),
the attach job, a tag build (`mod.json` says 0.1.0 and the launcher 0.2.0 until #223
sets the version), the packaged programs on a system older than this one, and a
desktop without FUSE.

## GPL

The archive is the complete source of this project, including the melon-rs and
melonDS patches. Source archives attach no built bridge. The player packages do,
so each release that carries them also carries the source archive of the same
commit, and each package holds `LICENSE`, `NOTICES.txt` and
`NOTICES-emulator.txt`, which name the release tag as the source (see the
[notices](../THIRD_PARTY_NOTICES.md#modified-gpl-source)).

## Going public

Checked on 2026-10-20 (#216) before the repository was made public: a credential
scan of all 354 commits (main, every branch still on GitHub and every pull
request's `refs/pull/*/head`, 1544 file versions), commit and tag messages, all 215
issues and pull requests with their comments, edit histories and reviews, and the
release notes found no key, token, password, private key or credential file. The
only matches are the synthetic strings in `tests/test_release_package.py`. The
repository has no Actions secrets, webhooks or deploy keys. Owner-specific
absolute paths were replaced with `~` or `$HOME`. Five early commits carry the
owner's email address, which their older public repositories already show; the
history was not rewritten.

The project-tracking workflow is safe to run on forks' pull requests: it uses
`pull_request_target` with issue and pull request write access only, checks out no
code and reads issue and pull request text as data, never as a command.

After switching the visibility (**Settings → General → Danger Zone → Change
visibility**), turn on in **Settings → Advanced Security**: private vulnerability
reporting (used by [`SECURITY.md`](../SECURITY.md)), Dependabot alerts, secret
scanning and push protection. Then add a branch ruleset for `main` in **Settings →
Rules → Rulesets** that requires a pull request and blocks force pushes and
deletion (see [work tracking](TRACKING.md#enforcement-boundary)). Public
repositories run standard Actions runners at no cost, so the tracking workflow,
stopped by billing on the private repository, runs again.

## v0.3.1 (2026-10-23, pre-release)

The second Windows test release (#271), with what the owner's first tries of
v0.3.0 on Windows 11 found and fixed. Published as a pre-release, as v0.3.0 was:
Latest stays v0.2.1 until the owner checks this Windows download on Windows 11
(#241).

- One version, 0.3.1, in `mod.json`, the runtime adapter's project and the launcher
  (`Cargo.toml`, its `Cargo.lock` entry and `launcher/NOTICES.txt`).
- Since v0.3.0: Kirby's textures in the host's RGBA value (#262), the controller
  and Steam's overlay in the sandboxed Windows game (#257), Kirby drawn between
  original frames and kept on elevators on displays the pacer does not lock to
  (#265), his canvas 3 px lower so he stands on host floors (#267), one click per
  launcher button (#258) and controller navigation in the launcher (#261).
- Its page is the [release page](#release-page-239) with both downloads; the
  pictures and the two parts are in `.github/release/v0.3.1/`. The page says the
  fixes were played on Windows 11 from a test build, and that this download has
  not run there.

Release record (2026-10-23): before the tag, `RELEASE_DOCKER_ARGS="--memory 5g"
bash scripts/release_container.sh` on the branch (`c8dff50`) built both legs, the
launcher with no warnings (the bridge keeps its four `static mut` warnings). PR
#272 merged as `b302297`; the annotated tag `v0.3.1` on it ran the release
workflow (run 39903879443): both legs and the attach job passed and made a draft
pre-release with the rendered notes and eight files (the `.zip` is 29,216,050
bytes, 58 files; the AppImage 15,251,960 bytes, the same size as the local build).

- All four checksums matched after downloading the draft. The `.zip`'s only
  `/home` strings are the build container's `/build/home/.cargo` paths.
- Linux, the released AppImage on a new data root (`--autopilot --smoke-play
  72`): checks passed, setup took 73 s, Play ran Kirby's session (`Kirby audio:
  SDL3 playback stream open.`) and the game closed after 72 s; exit 0, no fatal
  log. The 17 backed-up normal save files matched the live ones by hash.
- Windows, the released `.zip` under Wine (the #250 prefix with stand-ins):
  `--version` reported 0.3.1, and `--check` passed all four checks and found its
  own bundle (`0.3.1+b302297…`).
- Published as a pre-release with `make_latest=false`; `releases/latest` stayed
  v0.2.1. The page showed all seven pictures.
- Not run: this download on Windows 11 (the owner's check, #241), a controller in
  the launcher, the Steam Deck.


The first release with a Windows download (#253, from #252):
`KirbysDeadLand-0.3.0-windows-x64.zip` beside the two Linux downloads. Published
as a pre-release, so Latest stays v0.2.1 and v0.2.x launchers are not told about
it, until a Windows 11 check (#241) shows it works; then it is published as Latest.

- One version, 0.3.0, in `mod.json`, the runtime adapter's project and the launcher
  (`Cargo.toml`, its `Cargo.lock` entry and `launcher/NOTICES.txt`).
- For Linux players it carries the Windows port's shared changes since v0.2.1:
  Kirby's audio on the SDL3 that DCCM ships (#248), and the scripts and launcher
  changes of #246 and #250.
- Its page is the [release page](#release-page-239) with both downloads; the
  pictures and the two parts are in `.github/release/v0.3.0/`. The page says the
  Windows download has not run on Windows 11.

Release record (2026-10-22): PR #255 merged as `4c6b894`; the annotated tag
`v0.3.0` on it ran the release workflow (run 39856233490): both package legs and
the attach job passed and made a draft pre-release with the rendered notes and
eight files (the AppImage, the Linux folder, the Windows `.zip`, the source and
their checksums; the `.zip` is 29,190,967 bytes, 58 files).

- All four checksums matched after downloading the draft. The `.zip`'s only
  `/home` strings are the build container's `/build/home/.cargo` paths, and its
  only e-mail addresses are third-party authors in the license lists.
- Linux, the released AppImage on a new data root (`KIRBY_DATA_ROOT` and
  `XDG_CONFIG_HOME` under the private repros folder, `--autopilot --smoke-play
  72`): checks passed, the seven setup steps took 74 s, Play ran Kirby's session
  (`Kirby audio: SDL3 playback stream open.` in `play.log`) and the game closed
  after 72 s; exit 0. The 17 backed-up normal save files matched the live ones by
  hash afterwards.
- Windows, the released `.zip` under Wine (the #250 prefix with stand-ins for
  Steam and Dead Cells): `--version` reported 0.3.0, and `--check` passed all four
  checks and found its own bundle (`0.3.0+4c6b894…`). Setup and Play under Wine
  were run on the #254 container build of the same packaging, not on this file.
- Published as a pre-release with `make_latest=false`; `releases/latest` stayed
  v0.2.1. The page showed all seven pictures.
- Not run: anything on Windows 11 (#241), the Steam Deck.

## v0.2.1 (2026-10-21)

A fix release for one bug (#236): in v0.2.0 Kirby could not break the Collector's
exit door, so a player holding unspent cells with nothing to buy was trapped in the
transition level (#233, fixed by PR #234). Same files as v0.2.0, with 0.2.1 in their
names.

- One version, 0.2.1, in `mod.json`, the runtime adapter's project and the launcher
  (`Cargo.toml`, its `Cargo.lock` entry and `launcher/NOTICES.txt`).
- The fix was checked live before tagging: the probe and control runs in
  [RUNTIME_LAUNCH.md](RUNTIME_LAUNCH.md#collector-door-233), and the owner's own
  stuck v0.2.0 save opened with main's build (see
  [GAMEPLAY_FIDELITY.md](GAMEPLAY_FIDELITY.md#the-collectors-door-takes-hits-again-233)).
- v0.2.0 launchers that look for a new version see this one as newer.
- Built by the release workflow from the `v0.2.1` tag on `b01ec50` (run
  39628515140, success), which attached the files to a draft. The downloaded files
  matched their checksums; the AppImage's `--check` named bundle
  `0.2.1+b01ec505ba666f98c04ef51655ea222efdb07473`.
- Update from 0.2.0, on copies of the owner's 0.2.0 launcher folder and settings
  (`KIRBY_DATA_ROOT`, `XDG_CONFIG_HOME`): `--autopilot` kept the helpers, game and
  sprite steps, re-ran the outfit and staging steps (about 5 s) and recorded the
  0.2.1 bundle. With `--smoke-play 72` the movement check reached a live Kirby hero
  (15,692 frames, no fatal log, real saves unchanged). Without it, normal Play
  opened the game, which was closed at its menu: the Kirby save was byte-identical
  before and after, and the real saves unchanged.
- Published 2026-10-21 as a regular release, shown as Latest, with notes for
  players: the fix, how to update from 0.2.0, and what was tested. The download
  button reused the v0.2.0 picture and the notes sent new players to the v0.2.0
  notes, because those screenshots show "0.2.0".
- Rewritten the same day with the [release page](#release-page-239) (#239): its
  own pictures showing 0.2.1, and the whole guide for new players.

## v0.2.0 (2026-10-21)

The first release with the player launcher, published as a regular release (shown
as Latest) with `KirbysDeadLand-0.2.0-x86_64.AppImage`,
`KirbysDeadLand-0.2.0-x86_64.tar.gz`, `kirbys-dead-land-0.2.0.tar.gz` and their
`.sha256` files. Its notes are written for players: what they need, the steps from
download to Play, what happens to their saves, what was tested, and the files and
licenses.

- One version, 0.2.0, in `mod.json` (status `release`), the runtime adapter's
  project and the launcher; `tests/test_player_packages.py` keeps them equal, and
  `build_packages.py --tag` refuses a tag that names another.
- Checked before tagging: a [clean setup](LAUNCHER.md#clean-setup-223) from the
  packaged launcher on this machine, and the packages'
  [checks on other distributions](LAUNCHER.md#other-distributions-223).
- Built by the release workflow from the `v0.2.0` tag (run 39544728624, success),
  which made the draft and attached the five files. The bridge in that AppImage
  has the same SHA-256 as a local build of the same commit
  (`3c7562e9e2628d21a85c86d6bd3071f7a735ec663364ab12a9f55f19155efd3b`).
- Checked before publishing: the draft's files were downloaded, matched their
  `.sha256` files and held no personal data or credentials; the downloaded
  AppImage, with new data and config folders, took the offered ROM, ran setup in
  69 s, and its 72-second movement check reached `dc.pr.Game` with a live hero in
  the Kirby outfit (14,560 frames), saves backed up and unchanged, no fatal log.
- The notes' pictures are in `.github/release/v0.2.0/`, made by
  `.github/release/make_media.py` from `--screenshot --scale 2` with stand-in art:
  a banner, a download button and one picture per step. They hold no game art;
  pictures from the game itself (Kirby, his abilities, Dead Cells) stay out of
  the repository and the release. The notes link the pictures at the commit that
  added them, so later changes cannot alter a published release.

## v0.1.0 (2026-10-20, pre-release)

The first release, tagged `v0.1.0` and published as a GitHub pre-release on the
private repository, with `kirbys-dead-land-0.1.0.tar.gz` and its `.sha256` attached.

- Linux x64 only. Needs Steam Dead Cells (tested with v35, build 23762174) and the
  player's own Kirby Super Star Ultra USA revision 0 ROM (SHA-256
  `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`).
- Verified install: the archive installed from an empty data root and launched
  once into a playable Kirby run (#210, see
  [Runtime launch](RUNTIME_LAUNCH.md#clean-install-from-a-release-archive-210)).
- Gameplay verified live is listed under
  [Verified behavior](../README.md#verified-behavior); complete parity for every
  host interaction is not established. Known limits and deliberate deviations are in
  [Gameplay fidelity](GAMEPLAY_FIDELITY.md) and
  [Compatibility and limits](../README.md#compatibility-and-limits).
