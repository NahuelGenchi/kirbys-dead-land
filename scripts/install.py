#!/usr/bin/env python3
"""Set up Kirby's Dead Land from source with the player's own ROM and Dead Cells.

Runs the setup steps in order beneath the data root (~/.local/share/kirby-dead-cells/
unless KIRBY_DATA_ROOT names another), skipping
those already done: the pinned .NET SDK and DCCM with the runtime build, the
pinned melon-rs/melonDS oracle and bridge, the seed state from the original
menus, the ROM extraction and ability icons, the outfit pack, and the private
lab. The ROM and the installed game are only read. Nothing is written to the
Steam game directory or the project.
"""
import argparse
import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT / 'tools'))
from data_root import data_root
from oracle_snapshot import ROM_SHA256

ROOT = data_root()
GAME = Path.home() / '.local/share/Steam/steamapps/common/Dead Cells'
COMMANDS = ['git', 'cargo', 'cmake', 'bwrap', 'dpkg-deb']


def missing_prerequisites(game: Path) -> list[str]:
    problems = []
    if sys.version_info < (3, 12):
        problems.append('Python 3.12 or newer')
    problems += [f'`{name}` on PATH' for name in COMMANDS if not shutil.which(name)]
    if not (shutil.which('c++') or shutil.which('g++') or shutil.which('clang++')):
        problems.append('a C++ compiler (`c++`, `g++` or `clang++`)')
    problems += [f'Python module `{name}`' for name in ('PIL', 'numpy') if not importlib.util.find_spec(name)]
    if not (game / 'hlboot.dat').is_file() or not (game / 'res.pak').is_file():
        problems.append(f'Steam Dead Cells (hlboot.dat and res.pak) in {game}')
    return problems


def rom_digest(rom: Path) -> str:
    with rom.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def run(step: str, *command, env=None) -> None:
    print(f'\n== {step}\n$ {" ".join(map(str, command))}', flush=True)
    subprocess.run([str(part) for part in command], cwd=PROJECT, env=env, check=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rom', type=Path, required=True, help='Kirby Super Star Ultra (USA) revision 0 ROM.')
    parser.add_argument('--game', type=Path, default=GAME, help='Steam Dead Cells installation (read only).')
    args = parser.parse_args()
    rom, game = (path.expanduser().resolve() for path in (args.rom, args.game))
    problems = missing_prerequisites(game)
    if problems:
        raise SystemExit('Missing prerequisites:\n' + '\n'.join(f'- {problem}' for problem in problems))
    if not rom.is_file() or rom_digest(rom) != ROM_SHA256:
        raise SystemExit(f'{rom} is not the Kirby Super Star Ultra USA revision 0 ROM (sha256 {ROM_SHA256}).')
    python = sys.executable
    env = dict(os.environ, KSSU_ROM=str(rom))
    oracle = ROOT / 'oracle'

    run('Runtime: .NET SDK, DCCM and the mod build', python, 'scripts/setup_runtime.py', '--build', env=env)
    if not (oracle / 'vendor/melon-rs/target/release/kssu-oracle-runner').is_file():
        run('Emulator: pinned melon-rs and melonDS', python, 'tools/oracle_build.py', env=env)
    seed = oracle / 'ready2/000.state'
    if not seed.is_file():
        # The bootstrap plan walks the original menus to Green Greens with a fresh
        # save; its last state is the seed the bridge starts from.
        boot = oracle / f'install-boot-{time.strftime("%Y%m%d-%H%M%S")}'
        run('Seed state: original menus to Green Greens', python, 'tools/oracle_run.py',
            '--rom', rom, '--plan', 'tools/oracle_bootstrap.json', '--output', boot, env=env)
        states = sorted(boot.glob('*.state'))
        if not states:
            raise SystemExit(f'The bootstrap wrote no state in {boot}.')
        seed.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(states[-1], seed)
    run('Bridge: original-ROM execution library', python, 'tools/nds_build.py', env=env)
    if not (ROOT / 'rom/decoded').is_dir():
        run('ROM: decode Kirby graphics', python, 'tools/extract_kirby.py', rom, env=env)
    if not (ROOT / 'ability-icons/manifest.json').is_file():
        run('ROM: capture the 24 ability icons', python, 'tools/capture_ability_icons.py', env=env)
    run('Outfit: Kirby outfit pack', python, 'tools/build_outfit.py', '--game-pak', game / 'res.pak', env=env)
    run('Lab: stage the private profile', python, 'scripts/stage_runtime.py', '--original-runtime',
        '--game', game, '--rom', rom, '--outfit-pack', ROOT / 'outfit-build/res.pak', env=env)
    print('\nInstalled. Keep Steam running, then play with:\n  ./play-kirby')


if __name__ == '__main__':
    main()
