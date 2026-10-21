#!/usr/bin/env python3
"""Build a source release archive from a commit and refuse anything private in it.

The archive holds tracked files only, read from the commit with git, never the
working tree. The release lint fails on ROMs, game files, binaries, saves, states,
recordings, generated packs, private data folders and key-like strings.
`lint_package` applies the same lint to a player package's file list.
"""
import argparse
from collections.abc import Iterable
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile

PROJECT = Path(__file__).resolve().parents[1]
NAME = 'kirbys-dead-land'
# Repository workflow files; a player needs none of them.
EXCLUDED = ('.github/', 'CLAUDE.md', 'AGENTS.md', '.gitignore')
FORBIDDEN_SUFFIXES = {
    '.nds', '.srl', '.sav', '.dsv', '.state', '.mst', '.pak', '.dat', '.hdll', '.dll', '.so', '.exe',
    '.dmp', '.zip', '.7z', '.rar', '.tar', '.gz', '.xz', '.deb', '.nupkg',
    '.png', '.jpg', '.jpeg', '.gif', '.bmp', '.ppm', '.rgba', '.atlas',
    '.wav', '.ogg', '.mp3', '.flac', '.sdat', '.mp4', '.mkv', '.webm', '.mov',
}
FORBIDDEN_FOLDERS = {
    'evidence', 'lab', 'save', 'saves', 'repros', 'research', 'oracle', 'rom', 'decoded',
    'outfit-build', 'ability-icons', 'captures', 'smoke-runs', 'play-runs', 'dist', 'build', 'bin', 'obj',
}
MAX_BYTES = 1024 * 1024
# A player package's largest program, the launcher, is far below this; a ROM is 128 MiB.
MAX_PROGRAM_BYTES = 64 * 1024 * 1024
# The open-licensed fonts the launcher draws with (launcher/NOTICES.txt), pinned
# by hash so that no other binary file passes.
ALLOWED_BINARIES = {
    'launcher/ui/fonts/Fredoka.ttf': '2ba02e68b152868aef9ba28e24b3648c7d457fe6f25c761f2c2c53fb61a73fc8',
    'launcher/ui/fonts/Nunito.ttf': 'bb55a5ca5c2042335b3991af27c4d0705d0ef41cac6164ac737fd8f2a1e85207',
}
SECRETS = [re.compile(pattern) for pattern in (
    rb'gh[pousr]_[A-Za-z0-9]{36}', rb'github_pat_[A-Za-z0-9_]{40,}', rb'(?<![A-Za-z0-9_-])sk-[A-Za-z0-9_-]{32,}',
    rb'AKIA[0-9A-Z]{16}', rb'xox[abprs]-[A-Za-z0-9-]{10,}', rb'-----BEGIN [A-Z ]*PRIVATE KEY-----',
    rb'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{32}',
)]


def lint(files: dict[str, bytes]) -> list[str]:
    """Return one problem per file that must not be released."""
    problems = []
    for name, data in sorted(files.items()):
        path = PurePosixPath(name)
        if ALLOWED_BINARIES.get(name) == hashlib.sha256(data).hexdigest():
            continue
        if path.suffix.lower() in FORBIDDEN_SUFFIXES:
            problems.append(f'{name}: {path.suffix} files are not released')
        elif FORBIDDEN_FOLDERS.intersection(part.lower() for part in path.parts[:-1]):
            problems.append(f'{name}: inside a private data folder')
        elif len(data) > MAX_BYTES:
            problems.append(f'{name}: {len(data)} bytes, over the {MAX_BYTES} byte limit')
        elif b'\0' in data:
            problems.append(f'{name}: binary content')
        elif any(pattern.search(data) for pattern in SECRETS):
            problems.append(f'{name}: contains a key-like string')
    return problems


def lint_package(files: dict[str, bytes], built: dict[str, bytes], private: list[bytes],
                 required: Iterable[str] = ()) -> list[str]:
    """Return one problem per file that must not be in a player package (scripts/build_packages.py).

    `built` names the files the build made (the programs, the runtime's settings and
    the icon), each with the bytes it must start with; every other file must pass the
    release lint. `required` names other files that must be there, such as the license
    and notices. No file may name one of the `private` folders of the build machine.
    """
    problems = [f'{name}: missing' for name in sorted((set(built) | set(required)) - set(files))]
    for name, data in sorted(files.items()):
        if name not in built:
            problems.extend(lint({name: data}))
        elif not data.startswith(built[name]):
            problems.append(f'{name}: not the program or settings file expected here')
        elif len(data) > MAX_PROGRAM_BYTES:
            problems.append(f'{name}: {len(data)} bytes, over the {MAX_PROGRAM_BYTES} byte limit for programs')
        elif any(pattern.search(data) for pattern in SECRETS):
            problems.append(f'{name}: contains a key-like string')
        if any(folder in data for folder in private):
            problems.append(f'{name}: names a folder of the build machine')
    return problems


def git(*args: str) -> bytes:
    return subprocess.check_output(['git', '-C', str(PROJECT), *args])


def tracked_files(ref: str) -> dict[str, bytes]:
    names = git('ls-tree', '-r', '-z', '--name-only', ref).decode().split('\0')
    return {name: git('show', f'{ref}:{name}') for name in names
            if name and not any(name == item or name.startswith(item) for item in EXCLUDED)}


def executable(ref: str) -> set[str]:
    rows = git('ls-tree', '-r', '-z', ref).decode().split('\0')
    return {row.split('\t', 1)[1] for row in rows if row.startswith('100755 ')}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ref', default='HEAD', help='Commit or tag to package (default HEAD).')
    parser.add_argument('--output', type=Path, default=PROJECT / 'dist')
    args = parser.parse_args()
    version = json.loads(git('show', f'{args.ref}:mod.json'))['version']
    files = tracked_files(args.ref)
    problems = lint(files)
    if problems:
        raise SystemExit('Release lint failed:\n' + '\n'.join(f'- {problem}' for problem in problems))
    commit = git('rev-parse', f'{args.ref}^{{commit}}').decode().strip()
    timestamp = int(git('show', '-s', '--format=%ct', commit).decode())
    modes = executable(args.ref)
    prefix = f'{NAME}-{version}'
    output = args.output.expanduser().resolve()
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f'{prefix}.tar.gz'
    with tarfile.open(archive, 'w:gz', format=tarfile.PAX_FORMAT) as tar:
        for name, data in sorted(files.items()):
            info = tarfile.TarInfo(f'{prefix}/{name}')
            info.size, info.mtime = len(data), timestamp
            info.mode = 0o755 if name in modes else 0o644
            tar.addfile(info, io.BytesIO(data))
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (output / f'{archive.name}.sha256').write_text(f'{digest}  {archive.name}\n')
    print(json.dumps({'archive': str(archive), 'version': version, 'commit': commit,
                      'files': len(files), 'sha256': digest}, indent=2))


if __name__ == '__main__':
    main()
