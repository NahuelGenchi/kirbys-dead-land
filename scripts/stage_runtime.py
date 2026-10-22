#!/usr/bin/env python3
"""Stage a compiled adapter in an isolated lab; never start the game."""
from __future__ import annotations
import argparse
import json
import hashlib
import os
from pathlib import Path
import shutil
import sys
from setup_runtime import DEFAULT_ROOT, DCCM_VERSION
from lab_lock import hold
from oracle_build import bridge_binary
from steam_paths import default_game

WINDOWS = os.name == 'nt'
# The game files the lab's game root holds. DCCM reads the Windows game's code from
# deadcells_gl.exe; steam.hdll and steam_api64.dll are placed when the game has them.
GAME_FILES = ['deadcells_gl.exe', 'res.pak'] if WINDOWS else ['hlboot.dat', 'res.pak', 'steam.hdll', 'libsteam_api.so']
OPTIONAL_GAME_FILES = ['steam.hdll', 'steam_api64.dll'] if WINDOWS else []


def blank_outfit_page(pack: Path, project: Path) -> bool:
    """Whether an outfit pack's in-game page is blank, as the runtime expects (#175)."""
    sys.path.insert(0, str(project / 'tools'))
    from build_outfit import MODEL, Pak, image_read
    pak = Pak(pack)
    try:
        return image_read(pak, f'atlas/{MODEL}0.png').getbbox() is None
    finally:
        pak.file.close()


def stamp(path: Path) -> list[int]:
    status = path.stat()
    return [status.st_size, status.st_mtime_ns]


def place(source: Path, destination: Path, placed: dict) -> None:
    """Put a game file in the lab without a symbolic link, which Windows grants only in
    Developer Mode (#246): a hard link on the same drive, otherwise a copy. A lab file
    this script placed is replaced once the game's file changes; any other is refused."""
    if destination.exists() or destination.is_symlink():
        if not destination.is_symlink() and (os.path.samefile(source, destination) or stamp(source) == stamp(destination)):
            placed[destination.name] = stamp(destination)
            return
        if destination.is_symlink() or placed.get(destination.name) != stamp(destination):
            raise SystemExit(f'Refusing to replace existing lab file: {destination}')
        destination.unlink()
    try:
        os.link(source, destination)
    except OSError:
        print(f'Copying {source.name} ({source.stat().st_size / 1e6:,.0f} MB) into the lab: the game is on another drive.', flush=True)
        partial = destination.with_name(destination.name + '.part')
        shutil.copy2(source, partial)
        partial.replace(destination)
    placed[destination.name] = stamp(destination)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-root', type=Path, default=DEFAULT_ROOT)
    parser.add_argument('--game', type=Path, default=default_game())
    parser.add_argument('--original-runtime', action='store_true', help='Enable the original NDS bridge in this private lab only.')
    parser.add_argument('--rom', type=Path, default=Path.home() / 'Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds')
    parser.add_argument('--seed-state', type=Path, help='Private original-ROM seed state (defaults to oracle/ready2/000.state).')
    parser.add_argument('--outfit-pack', type=Path, help='Generated mod-only outfit PAK to stage as outfit.pak.')
    parser.add_argument('--assets', type=Path, help='Optional locally generated mod-only res.pak to stage.')
    args = parser.parse_args()
    root, game = args.data_root.resolve(), args.game.resolve()
    root.mkdir(parents=True, exist_ok=True)
    runtime_lock = hold(root / 'lab.runtime.lock')
    if runtime_lock is None:
        raise SystemExit('The private lab is running or being staged. Close it before staging another build.')
    project = Path(__file__).resolve().parents[1]
    lab = root / 'lab'
    if lab == game or game in lab.parents or lab in game.parents:
        raise SystemExit('Lab and installed game must be disjoint directories.')
    release = root / 'research' / f'dccm-{DCCM_VERSION}'
    built = project / 'src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra'
    if not (built / 'Kirby.Runtime.dll').is_file():
        raise SystemExit('First run scripts/setup_runtime.py --build.')
    mod = lab / 'coremod/mods/KirbySuperStarUltra'
    outfit = args.outfit_pack or mod / 'outfit.pak'
    # The runtime no longer hides the outfit's host sprites itself; an older pack
    # would draw its static fallback pose over Kirby.
    if args.original_runtime and outfit.is_file() and not blank_outfit_page(outfit, project):
        raise SystemExit(f'{outfit} predates the blank in-game outfit page (#175). '
                         'Rebuild it with tools/build_outfit.py and stage it with --outfit-pack.')
    if WINDOWS and not all((game / name).is_file() for name in GAME_FILES):
        raise SystemExit('Expected the owned Windows Steam game with deadcells_gl.exe and res.pak.')
    if not WINDOWS and (not (game / 'hlboot.dat').is_file() or not (game / 'steam.hdll').is_file()):
        raise SystemExit('Expected the owned native Linux Steam game with hlboot.dat and steam.hdll.')
    # The official SDK bundle includes an optional emulator. It is not required
    # for this owned Steam installation and is excluded from the development lab.
    shutil.copytree(release, lab / 'coremod', dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns('goldberg'))
    if WINDOWS:
        # launch_runtime.py finds the game, and its saves, from this record.
        record = lab / 'game-files.json'
        placed = json.loads(record.read_text())['files'] if record.is_file() else {}
        for filename in GAME_FILES + [name for name in OPTIONAL_GAME_FILES if (game / name).is_file()]:
            place(game / filename, lab / filename, placed)
            record.write_text(json.dumps({'game': str(game), 'files': placed}, indent=2) + '\n')
    else:
        for filename in GAME_FILES:
            destination = lab / filename
            if destination.exists() or destination.is_symlink():
                if destination.is_symlink() and destination.resolve() == (game / filename).resolve():
                    continue
                raise SystemExit(f'Refusing to replace existing lab file: {destination}')
            destination.symlink_to(game / filename)
        # Python ZIP extraction does not preserve executable permissions.
        for relative in ['core/host/startup/DeadCellsModding', 'core/native/linux-x64/hl']:
            executable = lab / 'coremod' / relative
            executable.chmod(executable.stat().st_mode | 0o111)
    shutil.copytree(built, mod, dirs_exist_ok=True)
    if args.assets:
        if not args.assets.is_file():
            raise SystemExit(f'Assets do not exist: {args.assets}')
        shutil.copy2(args.assets, mod / 'res.pak')
    if args.outfit_pack:
        if not args.outfit_pack.is_file():
            raise SystemExit(f'Outfit pack does not exist: {args.outfit_pack}')
        shutil.copy2(args.outfit_pack, mod / 'outfit.pak')
    runtime_config = mod / 'config.json'
    settings = json.loads(runtime_config.read_text())
    settings['EnableOriginalRuntime'] = args.original_runtime
    if args.original_runtime:
        rom = args.rom.resolve()
        seed = (args.seed_state or root / 'oracle/ready2/000.state').resolve()
        native = bridge_binary(root / 'oracle/vendor/melon-rs')
        if not rom.is_file() or not seed.is_file() or not native.is_file():
            raise SystemExit('Original runtime requires the owned ROM, private seed state, and tools/nds_build.py output.')
        with rom.open('rb') as stream:
            if hashlib.file_digest(stream, 'sha256').hexdigest() != 'b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027':
                raise SystemExit('The ROM must match Kirby Super Star Ultra USA revision 0.')
        shutil.copy2(native, mod / native.name)
        settings['RomPath'] = str(rom)
        settings['SeedStatePath'] = str(seed)
    runtime_config.write_text(json.dumps(settings, indent=2) + '\n')
    config = lab / 'coremod/config/modcore.json'
    config.parent.mkdir(parents=True, exist_ok=True)
    if config.exists():
        data = json.loads(config.read_text())
        if data.get('EnableGoldberg') is not False:
            raise SystemExit('Existing lab config must explicitly disable EnableGoldberg.')
    else:
        config.write_text(json.dumps({'EnableGoldberg': False, 'AllowLockCursor': False,
                                     'DCCMWarningPopup': False}, indent=2) + '\n')
    (lab / 'save').mkdir(exist_ok=True)
    print(json.dumps({'lab': str(lab), 'mod': str(mod),
                      'launched': False, 'game_modified': False, 'original_runtime': args.original_runtime,
                      'status': 'Staged. Use launch_runtime.py for a save-isolated offscreen smoke test.'}, indent=2))


if __name__ == '__main__':
    main()
