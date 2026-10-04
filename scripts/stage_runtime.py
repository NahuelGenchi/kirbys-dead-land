#!/usr/bin/env python3
"""Stage a dormant compiled adapter in an isolated lab; never start the game."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import shutil
from setup_runtime import DEFAULT_ROOT, DCCM_VERSION


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-root', type=Path, default=DEFAULT_ROOT)
    parser.add_argument('--game', type=Path, default=Path.home() / '.local/share/Steam/steamapps/common/Dead Cells')
    parser.add_argument('--assets', type=Path, help='Optional locally generated mod-only res.pak to stage.')
    args = parser.parse_args()
    root, game = args.data_root.resolve(), args.game.resolve()
    project = Path(__file__).resolve().parents[1]
    lab = root / 'lab'
    if lab == game or game in lab.parents or lab in game.parents:
        raise SystemExit('Lab and installed game must be disjoint directories.')
    release = root / 'research' / f'dccm-{DCCM_VERSION}'
    built = project / 'src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra'
    if not (built / 'Kirby.Runtime.dll').is_file():
        raise SystemExit('First run scripts/setup_runtime.py --build.')
    if not (game / 'hlboot.dat').is_file() or not (game / 'steam.hdll').is_file():
        raise SystemExit('Expected the owned native Linux Steam game with hlboot.dat and steam.hdll.')
    # The official SDK bundle includes an optional emulator. It is not required
    # for this owned Steam installation and is excluded from the development lab.
    shutil.copytree(release, lab / 'coremod', dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns('goldberg'))
    for filename in ['hlboot.dat', 'res.pak', 'steam.hdll', 'libsteam_api.so']:
        destination = lab / filename
        if destination.exists() or destination.is_symlink():
            if destination.is_symlink() and destination.resolve() == (game / filename).resolve():
                continue
            raise SystemExit(f'Refusing to replace existing lab file: {destination}')
        destination.symlink_to(game / filename)
    mod = lab / 'coremod/mods/KirbySuperStarUltra'
    shutil.copytree(built, mod, dirs_exist_ok=True)
    if args.assets:
        if not args.assets.is_file():
            raise SystemExit(f'Assets do not exist: {args.assets}')
        shutil.copy2(args.assets, mod / 'res.pak')
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
                      'launched': False, 'game_modified': False,
                      'status': 'Dormant: original Kirby controller/ability provider is missing.'}, indent=2))


if __name__ == '__main__':
    main()
