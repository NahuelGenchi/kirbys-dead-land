# Player launcher

[Back to README](../README.md) · Issues #221, #222 · Milestone M4

The launcher is a desktop program for players who do not use a terminal. Each
time it opens it checks Steam, Dead Cells, the player's own Kirby Super Star Ultra
ROM and the sandbox helper. The first time, it sets up the mod from those files.
After that, a Play button starts Dead Cells with Kirby. Source: `launcher/` (Rust
and [Slint](https://slint.dev), software rendering, no GPU needed for the window).

## Download

Releases from v0.2.0 carry two downloads of the launcher with everything it
installs, built by the release workflow ([Player packages](RELEASE.md#player-packages)).
Neither holds a ROM, a game file or any Kirby art: setup makes those from the
player's own files.

- **`KirbysDeadLand-<version>-x86_64.AppImage`**: one file. Allow it to run as a
  program (in the file's Properties, or `chmod +x`), then double-click it. It needs
  `fusermount3` or `fusermount`, which desktop systems have; without them, run it
  with `--appimage-extract-and-run` or use the other download.
- **`KirbysDeadLand-<version>-x86_64.tar.gz`**: unpack it anywhere and double-click
  `kirbys-dead-land` in the folder, keeping the `share` folder beside it.
  `add-to-menu.sh` adds a menu entry for that folder.

Each has a `.sha256` beside it. Both carry the GPL `LICENSE`, `NOTICES.txt` and
`NOTICES-emulator.txt` (together, the text of Settings › Licenses). Until v0.2.0 is out, the launcher is built
from source (see [Building](#building-and-checking)).

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
hold ROM art and stay private); `--write-icon FILE` writes the icon. For checks
only, `--autopilot DIR` presses what a player would in the real window (use the
found ROM, set up, play), saves a window snapshot at each change, stops at any
question, and exits 0 once the game has run and closed. `--smoke-play SECONDS`
makes Play run the offscreen movement check (`--start-game --outfit
KirbySuperStarUltra --input-plan movement`) instead of a normal game.

Settings › Licenses shows `launcher/NOTICES.txt` and then
`packaging/NOTICES-emulator.txt`. The first is generated by
`tools/launcher_notices.py` from `Cargo.lock`, the crates' own license files, and
the fonts' and icons' licenses; a launcher test fails when `Cargo.lock` changes
without it. The second covers the bridge and the oracle runner (see
[Releases](RELEASE.md#player-packages)). Both together are about 230 KB of text;
opening Licenses lays it out once. The window uses Fredoka and Nunito (SIL OFL 1.1) and Lucide icons
(ISC); the two font files are the only binary files the release lint accepts,
pinned by hash.

Tests: `cargo test` in `launcher/` (Steam library and manifest parsing, game
status, ROM verdicts and messages, the ROM search, config and setup records,
bundle discovery, helper pins against the mod scripts, wheel install, check rows,
time left, notices); `python3 -m unittest tests.test_launcher_bundle
tests.test_release_package` (bundle layout and refusals, `.deb` unpacking without
`dpkg-deb`, the font exception).

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

Not run: a person clicking through the window (the autopilot calls the same
handlers as the buttons, not pointer events), normal `--play` from the launcher
with real input and sound, Flatpak or Snap Steam (detected, not tested), other
distributions, a machine without the developer tools, a Steam that was not running
(the start and wait path), and a game in a second Steam library. A clean-machine
test is #223.
