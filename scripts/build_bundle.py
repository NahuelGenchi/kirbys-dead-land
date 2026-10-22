#!/usr/bin/env python3
"""Assemble the files the player launcher ships with; never reads the ROM, the game or a save.

The bundle holds the mod's tracked scripts and tools (checked by the release
lint), the compiled runtime adapter and the two native programs:

    bundle.json                      version and commit
    project/                         scripts/, tools/, mod.json, notices, and the
                                     runtime at src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra
    bin/libkssu_bridge.so            the melonDS bridge (tools/nds_build.py)
    bin/kssu-oracle-runner           the original-ROM runner (tools/oracle_build.py)

--target windows assembles the Windows launcher's bundle (#250): the same
project files, a runtime built with -p:ModReadyToRunOS=windows, and
bin/kssu_bridge.dll and bin/kssu-oracle-runner.exe from --target windows builds.

Build the runtime and native programs from the same tree first. Programs built on
a personal machine embed its home folder in their debug paths, so they are only
accepted with --private, which marks the bundle as not for release.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from package_release import lint

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from data_root import data_root
from oracle_build import TARGETS, release_dir

PROJECT = Path(__file__).resolve().parents[1]
# What the launcher's setup and play steps run, and what a player is owed.
INCLUDED = ('scripts/', 'tools/', 'mod.json', 'LICENSE', 'README.md', 'THIRD_PARTY_NOTICES.md')
RUNTIME = 'src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra'
RUNTIME_FILES = {'Kirby.Runtime.dll', 'Kirby.Core.dll', 'config.json', 'modinfo.json'}
# Each system's program files start with its executable format's magic bytes.
MAGIC = {'linux': b'\x7fELF', 'windows': b'MZ'}
# The Linux bundle's programs, as scripts/build_packages.py checks them.
NATIVE = {name: MAGIC['linux'] for name in TARGETS['linux'][1:3]}
# The PE machine of a ReadyToRun image compiled for Windows x64; Linux ones differ.
WINDOWS_READY_TO_RUN = 0x8664


def pe_machine(path: Path) -> int | None:
    data = path.read_bytes()
    if data[:2] != b'MZ' or len(data) < 0x40:
        return None
    header = int.from_bytes(data[0x3c:0x40], 'little')
    return int.from_bytes(data[header + 4:header + 6], 'little') if data[header:header + 4] == b'PE\0\0' else None


def git(*args: str) -> str:
    return subprocess.check_output(['git', '-C', str(PROJECT), *args], text=True)


def project_files() -> list[str]:
    names = git('ls-files', '-z', '--', *INCLUDED).split('\0')
    return sorted(name for name in names if name and (PROJECT / name).is_file())


def personal_paths(path: Path) -> bool:
    """Whether a file names this machine's home folder."""
    return os.fsencode(Path.home()) + b'/' in path.read_bytes()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--output', type=Path, required=True, help='Bundle folder to create (replaced if it is an earlier bundle).')
    parser.add_argument('--runtime', type=Path, default=PROJECT / RUNTIME, help='Built runtime folder (default: this tree\'s build).')
    parser.add_argument('--target', choices=sorted(TARGETS), default='linux', help='The system the bundle is for (default: linux).')
    parser.add_argument('--native', type=Path,
                        help='Folder with the bridge and kssu-oracle-runner for --target (default: the data root\'s build).')
    parser.add_argument('--private', action='store_true', help='Accept programs that name this machine\'s home folder; the bundle is for local tests only.')
    args = parser.parse_args()
    native_names = TARGETS[args.target][1:3]
    output = args.output.expanduser().resolve()
    if output == PROJECT or PROJECT in output.parents and not output.is_relative_to(PROJECT / 'dist'):
        raise SystemExit('Write the bundle outside the project, or under dist/.')
    if output.exists() and not (output / 'bundle.json').is_file():
        raise SystemExit(f'{output} exists and is not a bundle; choose an empty folder.')

    names = project_files()
    files = {name: (PROJECT / name).read_bytes() for name in names}
    problems = lint(files)
    runtime = args.runtime.expanduser().resolve()
    present = {path.name for path in runtime.iterdir()} if runtime.is_dir() else set()
    if not RUNTIME_FILES <= present:
        problems.append(f'{runtime}: missing {", ".join(sorted(RUNTIME_FILES - present))}; run scripts/setup_runtime.py --build')
    # Debug symbols are left out: they only name source files, and the game does not need them.
    runtime_files = sorted(name for name in present if not name.endswith('.pdb'))
    for name in runtime_files:
        if Path(name).suffix not in ('.dll', '.json'):
            problems.append(f'{runtime / name}: only the compiled adapter and its settings are bundled')
    if args.target == 'windows':
        for name in sorted(name for name in present if name.startswith('Kirby.') and name.endswith('.dll')):
            if pe_machine(runtime / name) != WINDOWS_READY_TO_RUN:
                problems.append(f'{runtime / name}: not compiled for Windows; build the runtime with -p:ModReadyToRunOS=windows')
    settings = json.loads((runtime / 'config.json').read_text()) if 'config.json' in present else {}
    if settings.get('EnableOriginalRuntime') or 'RomPath' in settings or 'SeedStatePath' in settings:
        problems.append(f'{runtime / "config.json"}: holds staged lab settings; rebuild the runtime')
    native = (args.native or release_dir(data_root() / 'oracle/vendor/melon-rs', args.target)).expanduser().resolve()
    magic = MAGIC[args.target]
    for name in native_names:
        path = native / name
        if not path.is_file() or path.read_bytes()[:len(magic)] != magic:
            build = '' if args.target == 'linux' else f' --target {args.target}'
            problems.append(f'{path}: missing or not a {args.target.capitalize()} program; run tools/nds_build.py{build} and tools/oracle_build.py{build}')
    if problems:
        raise SystemExit('Bundle check failed:\n' + '\n'.join(f'- {problem}' for problem in problems))
    shipped = [runtime / name for name in runtime_files] + [native / name for name in native_names]
    local = sorted(str(path) for path in shipped if personal_paths(path))
    if local and not args.private:
        raise SystemExit('These programs name this machine\'s home folder, so they cannot be released:\n'
                         + '\n'.join(f'- {path}' for path in local)
                         + '\nBuild them on a release machine, or pass --private for a local test bundle.')

    if output.exists():
        shutil.rmtree(output)
    partial = output.with_name(output.name + '.partial')
    if partial.exists():
        shutil.rmtree(partial)
    for name, data in files.items():
        target = partial / 'project' / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        target.chmod(0o755 if os.access(PROJECT / name, os.X_OK) else 0o644)
    (partial / 'project' / RUNTIME).mkdir(parents=True)
    for name in runtime_files:
        shutil.copy2(runtime / name, partial / 'project' / RUNTIME / name)
    (partial / 'bin').mkdir()
    for name in native_names:
        shutil.copy2(native / name, partial / 'bin' / name)
        (partial / 'bin' / name).chmod(0o755)
    version = json.loads(files['mod.json'])['version']
    commit = git('rev-parse', 'HEAD').strip()
    if subprocess.run(['git', '-C', str(PROJECT), 'diff', '--quiet', 'HEAD', '--', *INCLUDED]).returncode:
        commit += '-dirty'
    record = {'version': version, 'commit': commit, 'private': bool(local)}
    (partial / 'bundle.json').write_text(json.dumps(record, indent=2) + '\n')
    partial.rename(output)
    print(json.dumps({'bundle': str(output), **record, 'project_files': len(files), 'runtime_files': runtime_files}, indent=2))


if __name__ == '__main__':
    main()
