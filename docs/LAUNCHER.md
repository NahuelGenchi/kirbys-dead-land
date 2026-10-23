# Player launcher

[Back to README](../README.md) · Issues #221, #222, #223 · Milestone M4 · Windows: #250, controllers: #261 (M5)

The launcher is a desktop program for players who do not use a terminal. Each
time it opens it checks Steam, Dead Cells, the player's own Kirby Super Star Ultra
ROM and the sandbox helper. The first time, it sets up the mod from those files.
After that, a Play button starts Dead Cells with Kirby. Source: `launcher/` (Rust
and [Slint](https://slint.dev), software rendering, no GPU needed for the window).

## Download

Releases from v0.2.0 carry two downloads of the launcher with everything it
installs, built by the release workflow ([Player packages](RELEASE.md#player-packages));
the [latest release](https://github.com/NahuelGenchi/kirbys-dead-land/releases/latest)
has them. Neither holds a ROM, a game file or any Kirby art: setup makes those
from the player's own files.

- **`KirbysDeadLand-<version>-x86_64.AppImage`**: one file. Allow it to run as a
  program (in the file's Properties, or `chmod +x`), then double-click it. It needs
  `fusermount3` or `fusermount`, which desktop systems have; without them, run it
  with `--appimage-extract-and-run` or use the other download.
- **`KirbysDeadLand-<version>-x86_64.tar.gz`**: unpack it anywhere and double-click
  `kirbys-dead-land` in the folder, keeping the `share` folder beside it.
  `add-to-menu.sh` adds a menu entry for that folder.

From v0.3.0 a third download is for Windows (#252):

- **`KirbysDeadLand-<version>-windows-x64.zip`**: extract it anywhere and
  double-click `Kirby's Dead Land` in the folder, keeping the `share` folder beside
  it. The program is not signed, so Windows SmartScreen may ask first (*More info*,
  then *Run anyway*). See [Windows](#windows-250) for what differs; it has not run
  on a Windows 11 PC yet (#241).

Each has a `.sha256` beside it. All carry the GPL `LICENSE`, `NOTICES.txt` and
`NOTICES-emulator.txt` (together, the text of Settings › Licenses); the Windows one
also carries `NOTICES-windows-runtime.txt`, the licenses of the MinGW-w64 and GCC
runtime libraries linked into its programs.

Both are built on Ubuntu 22.04, so they need a 64-bit Linux with glibc 2.35 or
later, plus the fontconfig library and at least one installed font, which every
desktop has and Steam needs anyway. Without fontconfig the launcher
does not start; with fontconfig but no font at all (a bare Arch install), Slint's
font lookup stops it before the window opens (see [Verification](#other-distributions-223)).

## For players

1. **Checks.** Four rows, each with what to do when it is not ready:
   - **Steam**: found in the usual places for the native package
     (`~/.local/share/Steam`, `~/.steam/steam` and its links), Flatpak
     (`~/.var/app/com.valvesoftware.Steam/…`) or Snap (`~/snap/steam/common/…`).
   - **Dead Cells**: read from each Steam library in `libraryfolders.vdf` and the
     game's `appmanifest_588650.acf`. The Linux build must be complete
     (`hlboot.dat`, `res.pak`, `steam.hdll`, `libsteam_api.so`). A Windows build
     (installed through Proton) or an unfinished download is explained, with a
     button that opens the game in Steam. A build other than 23762174 (v35) is a
     warning only.
   - **Your Kirby Super Star Ultra**: the launcher looks for a 128 MiB file in
     Downloads, Documents, Desktop, ROMs, Games and Emulation (four folders deep)
     and offers it, or the player chooses a file. Only the USA revision 0 ROM is
     accepted (SHA-256 `b2573313…3027`); the message says when a file is a zip or
     7z, not a DS game, another game, another region or revision, trimmed, or
     modified. The ROM stays where it is and is only read.
   - **Sandbox helper**: bubblewrap must be able to make the sandbox the game runs
     in. See [bubblewrap](#bubblewrap).

   The ROM's place is remembered, and every check runs again each time the launcher
   opens, so it always uses the games the player has now.
2. **Set up Kirby.** Once, about two minutes on a recent desktop plus the downloads
   (165 MB). Seven steps with progress, time left, Cancel, and a log behind *Show
   details*. A failed step says why and offers *Try again*, which resumes: steps
   already done are kept.
3. **Play.** Starts Steam first when it is not running, then the game. While the
   game runs, the button reads *Playing* and *Close the game* stops it. Before Play,
   the launcher asks when less than 6 GiB of memory is free.
4. **Settings** (gear, top right): change the ROM, open Kirby's saves, the save
   backups or the last game log, repair the setup, open the launcher's files, check
   for updates (and turn off *Look for a new version when the launcher opens*),
   licenses and the source code.

An Xbox controller works in the window too: the D-pad or left stick moves between
the buttons, A presses, B closes Settings or a question ([Game
controllers](#game-controllers-261)).

Changing the ROM later, or Steam moving the game, sets up only what depends on it.
A new launcher version installs its own mod files the next time Setup runs.

## Bubblewrap

The game runs inside a bubblewrap sandbox in which the Steam game folder and
everything else outside the launcher's data folder are read-only. Install the
`bubblewrap` package once; the launcher shows the command for the distribution
it finds:

| Distribution | Command |
| :--- | :--- |
| Ubuntu, Debian, Mint, Pop!_OS | `sudo apt install bubblewrap` |
| Fedora | `sudo dnf install bubblewrap` |
| Arch, Manjaro, SteamOS | `sudo pacman -S bubblewrap` |
| openSUSE | `sudo zypper install bubblewrap` |

If bubblewrap is installed but *your system blocks its sandbox*, the system does
not let programs make user namespaces. On Ubuntu 24.04 and later, use the
distribution's `bubblewrap` package (its AppArmor profile allows it) rather than a
copy from elsewhere. Some hardened systems turn user namespaces off with
`kernel.unprivileged_userns_clone=0` or `user.max_user_namespaces=0`; the game
cannot run there.

## The one-time setup

Everything is written to the launcher's data folder,
`~/.local/share/kirbys-dead-land/` (or `$XDG_DATA_HOME/kirbys-dead-land`;
`KIRBY_DATA_ROOT` moves it). It never writes to the Steam game folder or the ROM.

| Step | What it does | 2026-10-20 |
| :--- | :--- | ---: |
| Download the helpers | DCCM 35.13.6, the .NET 10.0.12 runtime, Python 3.14.8 (python-build-standalone), Pillow 12.1.1, NumPy 2.3.5 and Ubuntu's `libturbojpeg0` 2.1.5, each checked against a pinned SHA-256 or SHA-512, three tries each | 0:25 |
| Install the mod | Copies the bundled scripts, tools, runtime adapter, bridge and runner | 0:00 |
| Start Kirby's adventure | `tools/oracle_run.py` plays the original menus once to make the seed state | 0:13 |
| Read Kirby's sprites | `tools/extract_kirby.py` decodes Kirby's graphics from the ROM | 0:23 |
| Collect the copy abilities | `tools/capture_ability_icons.py` captures the 24 ability icons | 0:23 |
| Sew Kirby's outfit | `tools/build_outfit.py --game-pak` builds the outfit pack from the game's `res.pak` | 0:03 |
| Put it all together | `scripts/stage_runtime.py --original-runtime` stages the private lab; `launch_runtime.py --prepare` unpacks TurboJPEG | 0:00 |

The scripts are the same ones the source install uses, run with the launcher's
own Python and `KIRBY_DATA_ROOT`. Each runs in its own process group; Cancel
interrupts the group and kills it after six seconds. The full log is
`logs/setup.log`, and `setup.json` records the bundle, game folder and ROM that
the finished setup used. Setup runs again by itself when one of those changes.
Time estimates are half again the times above.

## Play

Play runs `scripts/launch_runtime.py --play --steam-root <Steam folder>`, the
same launch as `./play-kirby`. It backs up the real saves (the game's `save/` and
Steam's `userdata/*/588650`) to `save-backups/`, keeps Kirby's saves separate in
`lab/save/`, and checks after the game closes that the real saves did not change.
`--steam-root` names the Steam installation that holds the cloud saves, which
differs from the game's library when Dead Cells is in a second library. The
launcher's output is in `logs/play.log`; Settings › Last game log opens the
game's own log.

## Files, network and privacy

- Settings: `~/.config/kirbys-dead-land/config.json` (the ROM's path and the
  update switch).
- Data: the data folder above. Settings › Launcher files opens it; deleting it removes
  everything the launcher made, including Kirby's saves (backups are inside it
  too).
- Network: the setup downloads above, from their publishers; Play starts nothing
  online beyond the game and Steam. With *Look for a new version when the launcher
  opens* on, the launcher asks `api.github.com` for the newest release once when it opens. It sends
  no information about the player, the ROM or the system, and installs nothing
  itself: *Get it* opens the release page.

## Building and checking

```sh
cd launcher && cargo build --release
python3 scripts/setup_runtime.py --build
python3 scripts/build_bundle.py --output dist/bundle
launcher/target/release/kirbys-dead-land --bundle dist/bundle
```

`scripts/build_bundle.py` assembles the files the launcher installs:
`bundle.json` (version and commit), `project/` (the tracked `scripts/`, `tools/`,
`mod.json`, README, LICENSE and notices, checked by the release lint, plus the
built runtime without debug symbols) and `bin/` (the bridge and the oracle runner
from `tools/nds_build.py` and `tools/oracle_build.py`). Programs built on a
personal machine name its home folder in their debug paths, so the script refuses
them unless `--private` marks the bundle as a local test. The launcher finds the
bundle through `--bundle`, `KIRBYS_DEAD_LAND_BUNDLE`, an AppImage's
`usr/share/kirbys-dead-land`, or `share/kirbys-dead-land` beside the program.
`scripts/build_packages.py` builds every part with the build machine's folders
mapped out of their paths and makes both downloads ([Player
packages](RELEASE.md#player-packages)).

Other options: `--check` prints the checks and the bundle it found (`Mod files:
<folder> (<version>+<commit>)`), then exits (0 when ready to play);
`--screenshot DIR` renders every screen to PNG files with a fixed clock, with
stand-in art unless `--art-from DATA_ROOT` names a finished setup (those images
hold ROM art and stay private), and `--scale 2` draws them at twice the pixel
density for sharp pictures; `--write-icon FILE` writes the icon. For checks
only, `--autopilot DIR` presses what a player would in the real window (use the
found ROM, set up, play), saves a window snapshot at each change, stops at any
question, and exits 0 once the game has run and closed. With it, `--yes ID`
agrees once to the question with that id (such as `sandbox`), and
`--close-after SECONDS` presses Close once the game has run that long. `--smoke-play SECONDS`
makes Play run the offscreen movement check (`--start-game --outfit
KirbySuperStarUltra --input-plan movement`) instead of a normal game.

Settings › Licenses shows `launcher/NOTICES.txt` and then
`packaging/NOTICES-emulator.txt`. The first is generated by
`tools/launcher_notices.py` from `Cargo.lock` (the crates of both the Linux and the
Windows build, so both show the same file), the crates' own license files, and
the fonts' and icons' licenses; a launcher test fails when `Cargo.lock` changes
without it. The second covers the bridge and the oracle runner (see
[Releases](RELEASE.md#player-packages)). Both together are about 230 KB of text;
opening Licenses lays it out once. The window uses Fredoka and Nunito (SIL OFL 1.1) and Lucide icons
(ISC); the two font files are the only binary files the release lint accepts,
pinned by hash.

Tests: `cargo test` in `launcher/` (Steam library and manifest parsing, game
status, ROM verdicts and messages, the ROM search, config and setup records,
bundle discovery, helper pins against the mod scripts for both systems, wheel
install, check rows, time left, notices, the Windows game files and sandbox
messages, and the controller mapping: directions, repeat, the stick's two
thresholds, A and B, XInput and Linux event decoding); `python3 -m unittest tests.test_launcher_bundle
tests.test_release_package` (bundle layout and refusals for both systems, `.deb`
unpacking without `dpkg-deb`, the font exception). The same `cargo test` builds
for Windows with `--target x86_64-pc-windows-gnu`, and its test program runs under Wine.

## Windows (#250)

The launcher builds for Windows from the same source, cross-compiled on Linux
with MinGW-w64 as the bridge is (#242):

```sh
cd launcher && cargo build --release --target x86_64-pc-windows-gnu
python3 scripts/build_bundle.py --target windows --output dist/bundle-windows
```

The program is a Windows GUI program that needs only Windows' own DLLs. With an
option such as `--check`, it prints to the terminal that started it. What differs:

- **Folders.** Data in `%LOCALAPPDATA%\kirbys-dead-land`, settings in
  `%APPDATA%\kirbys-dead-land`. Paths are shown in full, with backslashes.
- **Steam.** The installation the Steam client records in the registry
  (`HKCU\Software\Valve\Steam`, `SteamPath`), or `%ProgramFiles(x86)%\Steam`, and
  its other libraries from `libraryfolders.vdf`. Steam counts as running when a
  `steam.exe` is; Play starts it with `steam.exe -silent`.
- **The game.** The Windows Dead Cells is ready with `deadcells_gl.exe` and
  `res.pak`, the files `scripts/stage_runtime.py` places on Windows (#246).
- **Setup.** The same helpers in their Windows builds, each pinned by hash: DCCM
  35.13.6 `win-x64.zip`, the .NET 10.0.12 runtime `win-x64.zip`, Python 3.14.8
  `x86_64-pc-windows-msvc`, and Pillow 12.1.1 and NumPy 2.3.5 `cp314-win_amd64`
  wheels. No TurboJPEG: the Windows game needs none, and the last step skips
  `--prepare`. The bundle's `kssu_bridge.dll` and `kssu-oracle-runner.exe` are
  installed where the scripts look on Windows
  (`oracle/vendor/melon-rs/target/x86_64-pc-windows-gnu/release`). Setup's
  scripts run with no console window, each in a job object; Cancel ends the job
  at once. The scripts' locks end with them, and setup redoes an unfinished step.
- **Sandbox.** The fourth check reads whether the data folder's drive keeps
  Windows permissions (NTFS does), which the low-integrity labels need. A drive
  that cannot only warns.
- **Play and Close.** Play runs the same `launch_runtime.py --play`, which starts
  the game with low integrity (#246). That script has no console window and no
  job, so, as on Linux, the game keeps running if the launcher closes. Close writes `stop` to the script's input,
  and the script closes the game and checks the saves, as Ctrl+C does on Linux. A
  game closed this way ends with exit code 1, which the script accepts. When the
  script reports that Windows refused the sandbox, the launcher asks before
  playing with the player's normal rights, protected by the save backup and check
  only (the #241 fallback, a deviation). The answer holds until the launcher closes.
- **Other calls.** Free memory from `GlobalMemoryStatusEx`, folders and links
  opened by the Windows shell, the Windows file picker, and Consolas for logs.
  `--smoke-play` stays on Linux.

## Game controllers (#261)

The window also works with an Xbox controller, or another controller the system
reads as one. `src/gamepad.rs` reads the controllers on a thread every 16 ms and
turns what it reads into the window's keys:

| On the controller | In the window | Same as |
| :--- | :--- | :--- |
| D-pad or left stick down or right | the next button | Tab |
| D-pad or left stick up or left | the previous button | Shift+Tab |
| A | presses the focused button | Return |
| B | closes the question, Licenses or Settings, whichever is on top | a click beside it |

A held direction moves again after 450 ms, then every 140 ms. The stick counts once
it is pushed past half way, and lets go below 35 %, so a wobble at the edge does
not move twice. A button held when the launcher opens waits for its release. Two
controllers count as one. While Dead Cells is starting, running or closing, the
launcher leaves the controller to the game.

- **Windows.** XInput (`XInputGetState` in `xinput1_4.dll`, slots 0 to 3), which
  reads Xbox controllers and the ones that present themselves as one; pads that
  only speak DirectInput are not read. Empty slots are asked every 2 s, as asking
  them is slow. XInput reads a controller whichever program is in front, so the
  launcher acts on it only while its own window is in front.
- **Linux.** The kernel's event devices `/dev/input/event*` that have an A button
  (`BTN_SOUTH`), read without blocking and looked for again every 2 s. The D-pad
  counts as a hat (the xpad driver for Xbox controllers) or as four buttons.
  Reading them needs the access that logind gives the active session for
  joysticks (systemd's `uaccess` rule); a controller the player cannot open is not
  read, and the window does not say so. The launcher reads it in the background
  too, unlike on Windows.
- **No new crate.** gilrs, the usual Rust choice, would add its crates to
  `Cargo.lock` (and libudev on Linux), which the release builds use with
  `--locked` and the licenses are generated from, and the PC this was written on
  had no Rust to update them. The two reads need only what the launcher already
  pins: `windows-sys` 0.61.2 with the `Win32_UI_Input_XboxController` feature, and
  `libc` for two `ioctl` calls. `Cargo.lock` and `NOTICES.txt` are unchanged.

**Focus.** Each screen's main button takes the focus whenever it can be pressed:
*Set up Kirby* or *Continue* on the checks, *Continue setup* or *Try again* after
setup stops, *Play* at home. The gold focus ring shows on the focused button. An
overlay takes the focus when it opens: Settings and Licenses on their close
button, a question on its cancel answer (or its only answer). Tab, and so the
controller, visits only the overlay on top: `FocusLayer` in `ui/widgets.slint`
holds its number, set by `AppWindow`, and each button's `FocusScope` takes Tab only
on its own layer (`focus-on-tab-navigation`). When the overlay closes, the screen's
main button has the focus again. By Slint 1.18.1's rule (`ItemRc::is_visible`
counts only clipping parents), Tab could also stop on the closed Settings sheet
beyond the window's edge and on the hidden text field the clipboard goes through;
the layers keep it off the first, and the field is now clipped to nothing.

Escape on the keyboard still does not close overlays; only B does.

## Verification

2026-10-20, this machine (Ubuntu 26.04, native Steam, Dead Cells build 23762174),
launcher debug build from branch `feat/221-launcher`, a private bundle of the same
tree, and no `~/.local/share/kirbys-dead-land` or `~/.config/kirbys-dead-land`
beforehand.

- `--check`: Steam and Dead Cells found, the ROM in Documents offered and
  verified, sandbox ready, setup needed.
- `--autopilot` with `--smoke-play 60`: the checks pressed *Use this file* then
  *Set up Kirby*; all seven steps ran (1 min 34 s in all, times above, 897 MB on
  disk); the home screen showed Kirby and the 24 icons from the player's ROM. Play
  stopped at the low-memory question (6.0 GiB free), as designed.
- Second run, 6.4 GiB free: home at once (setup recognized), Play, 60 s offscreen
  run, launcher back to idle. Report: save isolation installed, real saves
  unchanged, no fatal log, but the run stayed on the title screen because this mode
  did not yet start a game.
- Third run, `--smoke-play 72` with the movement check: `dc.pr.Game` with a live
  hero and the `KirbySuperStarUltra` outfit, original ROM provider enabled, 14,909
  frames, normal timeout, save isolation installed, real saves unchanged, no fatal
  log; the last frame shows Kirby in the starting cell. The launcher returned to
  idle and the autopilot exited 0.
- Final run with the committed launcher code, a second new data root
  (`KIRBY_DATA_ROOT`) and config folder (`XDG_CONFIG_HOME`), `--smoke-play 72`:
  checks, setup (1 min 22 s; downloads 0:17, the other steps as above) and Play in
  one go. The movement check reached `dc.pr.Game` with a live hero and the Kirby
  outfit (15,067 frames), original ROM provider enabled, save isolation installed,
  real saves unchanged, no fatal log; the autopilot exited 0.
- Each screen was also rendered with `--screenshot` and reviewed (#221).
- Packaged launcher (#222): the AppImage built in the release container ran the same
  autopilot with `--smoke-play 72` on a new data root: setup 1 min 16 s, then the
  movement check with the packaged bridge reached `dc.pr.Game` with a live hero and
  the Kirby outfit (14,577 frames), real saves unchanged, no fatal log. Details in
  [Releases](RELEASE.md#verification-222).

### Clean setup (#223)

2026-10-21, this machine (Ubuntu 26.04, native Steam running, Dead Cells build
23762174, 6.0 GiB free), with `KirbysDeadLand-0.2.0-x86_64.AppImage` built by
`scripts/release_container.sh` from `808016b` (bundle `0.2.0+808016b`), started
through FUSE with a data folder (`KIRBY_DATA_ROOT`) and a config folder
(`XDG_CONFIG_HOME`) that did not exist, and `--autopilot <dir> --smoke-play 72`:

- Checks: Steam, Dead Cells and the sandbox ready at once; the ROM in Documents
  offered, *Use this file* pressed, verified.
- *Set up Kirby*: all seven steps in 69 s (downloads 0:05, start 0:13, sprites
  0:23, abilities 0:23, outfit 0:03), 970 MB on disk afterwards; the home screen
  showed Kirby, *Ready to play* and the 24 ability icons.
- Play: the 72-second movement check reached `dc.pr.Game` with a live hero in the
  `KirbySuperStarUltra` outfit (14,976 frames), original ROM provider enabled,
  recompiler, normal timeout, saves backed up first, save isolation installed, real
  saves unchanged, no fatal log. The game loaded the packaged bridge (the same
  SHA-256 as in the AppImage). The launcher went back to idle and the autopilot
  exited 0 after 146 s in all.

The window snapshots hold the player's ROM art and stay private. Not covered here:
choosing the ROM in the file chooser (the autopilot takes the offered file; the
chooser is the desktop's own portal dialog) and downloading the package from the
release page, which is checked when the release is published.

### Other distributions (#223)

2026-10-21, Docker on this machine (no display, no FUSE: the AppImage ran with
`APPIMAGE_EXTRACT_AND_RUN=1`), the same 0.2.0 packages, an empty home folder:
`--check` from both packages, `--screenshot` (all 14 screens rendered without a
display) and `ldd` on the launcher, the bridge and the runner.

| Image | glibc | As shipped | With fontconfig |
| :--- | :--- | :--- | :--- |
| `fedora:latest` (Fedora 44) | 2.43 | launcher does not start: `libfontconfig.so.1` missing | checks and screenshots work |
| `archlinux:latest` | 2.44 | same | checks work; screenshots stop in Slint's font lookup (`fontique` `NoMatch`): Arch's `fontconfig` package brings no font; with `ttf-dejavu`, all 14 screens |
| `debian:stable` (Debian 13) | 2.41 | same | checks and screenshots work |

- With fontconfig, each check said Steam was missing, Dead Cells needs Steam, the
  ROM is to be chosen, and bubblewrap is to be installed with that distribution's
  command (`sudo dnf install bubblewrap`, `sudo pacman -S bubblewrap`, `sudo apt
  install bubblewrap`), and found its own bundle. With bubblewrap installed, the row
  said the system blocks its sandbox (Docker blocks user namespaces), as described
  under [Bubblewrap](#bubblewrap).
- The bridge and the runner found every library they need in all three images;
  `libfontconfig.so.1` is the only library any package program lacked.
- Recorded rather than fixed: fontconfig and at least one font are on every
  desktop, and Steam itself needs them (Arch's `steam` package depends on a font).
  A system with neither has no desktop to show the launcher on. Images pinned by
  digest: Fedora `sha256:43b29f65…5d80`, Arch `sha256:e43f1fd8…a8f4`, Debian
  `sha256:defb09e1…8bd8`.

Not run: a person clicking through the window (the autopilot calls the same
handlers as the buttons, not pointer events), normal `--play` from the launcher
with real input and sound, Flatpak or Snap Steam (detected, not tested), the game
on another distribution or a Steam Deck (only the checks ran there, in
containers), a Steam that was not running (the start and wait path), and a game in
a second Steam library.

### Windows, and Linux again (#250)

2026-10-22, this machine, Proton Experimental's Wine in a new prefix, the launcher
and its test program cross-built from branch `feat/250-windows-launcher`, and a
private Windows bundle (`--target windows`) of the same tree. Steam and the game
were stand-ins: a `steam.exe` that only waits, recorded in the registry; a second
library in its `libraryfolders.vdf` holding `Dead Cells` with a stand-in
`deadcells_gl.exe` and the Linux `res.pak` (read only); and saves of their own.

- `cargo test --target x86_64-pc-windows-gnu`: the 36 tests passed under Wine,
  among them the process list, the drive check, free memory, and a job ending the
  program it holds.
- `--check`: Steam from the registry, Dead Cells in the second library (tested
  build), the ROM verified, the sandbox row ready (Wine's drive reports
  permissions), the data folder in `C:\users\steamuser\AppData\Local`, every path
  with backslashes; exit 0.
- `--autopilot --yes sandbox`: setup with the Windows helpers in 88 s (downloads
  0:06, start 0:13, sprites 0:39, abilities 0:23, outfit 0:03, stage 0:01); the
  home screen showed Kirby and the 24 ability icons. Play started the stand-in
  Steam; the script reported that Windows refused the low-integrity labels, the
  launcher asked the sandbox question, and *Play anyway* ran the script with normal
  rights. The saves were backed up, the game files hard-linked into the lab, and
  DCCM loaded `Kirby.Core` and `Kirby.Runtime`, then stopped reading `hlboot.dat`
  from the stand-in game; the launcher asked the crash question.
- Second run, a stand-in in place of DCCM's program that waits once save isolation
  is ready, with `--close-after 10`: Close pressed at 14 s; the script closed it and
  checked the saves (exit code 1, save isolation installed, saves unchanged, no
  fatal log); the launcher went back to idle with no game process left, and the
  autopilot exited 0.

The same day on Linux (native Steam running, Dead Cells build 23762174, 7.1 GiB
free), the launcher release build from the same tree and a private Linux bundle of
it, with a new data folder and config folder and `--autopilot <dir> --smoke-play
72`: setup in 83 s (downloads 0:18, start 0:12, sprites 0:23, abilities 0:22,
outfit 0:03), then the movement check reached `dc.pr.Game` with a live hero in the
`KirbySuperStarUltra` outfit (14,498 frames) in bubblewrap, save isolation
installed, real saves unchanged, no fatal log, Kirby's SDL3 audio stream open; the
launcher went back to idle and the autopilot exited 0 after 159 s.

Not run: Windows 11 or the Windows Dead Cells; the low-integrity sandbox itself
(Wine refuses the labels and ignores integrity levels); the file picker; opening
folders and links with the shell; Cancel during setup from the window (the job is
covered by its unit test only); and a person using the window.

### One click per button (#258)

On the owner's Windows 11 PC (v0.3.0 download), every button needed two clicks.
Each button and the settings switch has a keyboard `FocusScope` above its
`TouchArea`. In Slint 1.18.1 (`FocusScope::input_event` in
`internal/core/items/input_items.rs`) an enabled scope that may take focus on a
click (`focus-on-click`, true by default) and does not have it takes the press
to focus itself and accepts it, so the `TouchArea` beneath never saw the first
click; the second found the scope focused and passed. This does not depend on the
system. The five scopes now have `focus-on-click: false`: a click goes to the
`TouchArea`, and Tab, Return and Space work as before.
`python -m unittest tests.test_launcher_ui` checks that every scope in
`launcher/ui` says so.

`shots::tests::one_click_runs_each_button` (in `launcher/src/shots.rs`) draws a
`PrimaryButton`, `SecondaryButton`, `LinkButton` and `IconButton` in an
offscreen software window and sends each one pointer press and release through
Slint's own input path: each runs once. With `focus-on-click: true` put back in
`widgets.slint` it fails with none run (`[0, 0, 0, 0]`), the fault seen on
Windows. The settings `Switch` is not exported, so only the source check covers it.

2026-10-23, Linux build machine, branch merged with `main` (`a6e493a`):
`cargo test` in `launcher/`, 37 passed; the same tests built with `--target
x86_64-pc-windows-gnu` and run under Wine (Proton Experimental), 37 passed;
`cargo build --release` and `cargo build --release --target
x86_64-pc-windows-gnu` both built. Not run: a click in a real window on either
system (no input tool on this machine's Wayland desktop); the owner's check on
Windows 11 follows a build.

### Game controllers (#261)

2026-10-22, the release workflow run by hand on branch `feat/261-launcher-gamepad`
at `69230d6`, with no tag, so it kept the files as workflow artifacts only ([run
39873165461](https://github.com/NahuelGenchi/kirbys-dead-land/actions/runs/39873165461)).
Both legs passed. In each, `cargo build --release --locked` compiled the launcher
with no warnings: for Linux in `ubuntu:22.04` (packages 31.4 MB), and for
`x86_64-pc-windows-gnu` with MinGW-w64 in `ubuntu:24.04` (packages 29.1 MB). That
covers the XInput and evdev code paths. A build is not a test.

Not run: `cargo test` in `launcher/` (the 10 tests in `src/gamepad.rs`), on Linux
or for Windows under Wine; the Python tests; and any controller in the window. The
change was written on the owner's Windows 11 PC, which has neither Rust nor Python.
The owner still has to check it with a real controller on Windows 11: the D-pad
and stick move the ring through each screen's buttons, A presses, B closes
Settings, Licenses and a question, and the controller does nothing while the game
runs or another window is in front. The same check is open on Linux with the xpad
driver.
