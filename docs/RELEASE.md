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

`scripts/build_packages.py` makes the two downloads described in
[Player launcher](LAUNCHER.md#download), each with a `.sha256`:

| File | Layout |
| :--- | :--- |
| `KirbysDeadLand-<version>-x86_64.AppImage` | `AppRun`, the desktop entry and icon at the top; `usr/bin/kirbys-dead-land`; the bundle at `usr/share/kirbys-dead-land`, found through `$APPDIR`; `LICENSE`, `NOTICES.txt` and `NOTICES-emulator.txt` in `usr/share/doc/kirbys-dead-land/` |
| `KirbysDeadLand-<version>-x86_64.tar.gz` | One folder, `KirbysDeadLand-<version>-x86_64/`: `kirbys-dead-land`, the bundle at `share/kirbys-dead-land` beside it, `LICENSE`, `NOTICES.txt`, `NOTICES-emulator.txt`, `README.txt`, the desktop entry, the icon and `add-to-menu.sh` |

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
```

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

- **Build** (read-only token): an `ubuntu:22.04` container pinned by digest on an
  `ubuntu-24.04` runner, so the programs need only glibc 2.35 and the libstdc++ of
  Ubuntu 22.04. `scripts/release_tools.sh` installs Ubuntu's compilers, CMake,
  ALSA and fontconfig headers, ICU, OpenSSL and squashfs-tools, Python 3.14.8
  (python-build-standalone 20261009, the launcher's own pin) and rustup 1.29.1, both
  checked by SHA-256, and Rust 1.99.0. The .NET SDK 10.0.401, DCCM, melon-rs and
  melonDS are pinned by the build scripts. Then `build_packages.py --tag` and
  `package_release.py`, and the two packages, the source archive and the `.sha256`
  files are uploaded as the `packages` artifact.
- **Attach** (the only job that can write): `gh release upload --clobber` to the
  tag's release, or to a new draft pre-release when the tag has none yet; its notes
  are written by hand.

Actions are pinned by commit, the checkout keeps no credentials, and the tag
reaches shell commands only through environment variables.

`scripts/release_container.sh [vX.Y.Z]` runs the same steps on this machine, in
the same container image, from the committed `HEAD` (or the tag), and copies the
packages, the source archive and their `.sha256` files to `dist/`. It needs
Docker; `RELEASE_DOCKER_ARGS` passes extra `docker run` options such as
`--memory 4g`. A test checks that it and the workflow use one image and the same
build commands.

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
