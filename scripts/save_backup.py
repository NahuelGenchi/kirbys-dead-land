"""Back up the real Dead Cells save folders before a launch, and prove the copy.

Each launch writes one set under <data root>/save-backups/<time>/: a .tar.gz per
existing save folder and a manifest of every file's SHA-256. Each archive is read
back and compared with the files before the launch may continue. The newest sets
are kept; older ones are removed. The save folders themselves are only read.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import shutil
import tarfile
import time

KEEP = 10


def digests(folder: Path) -> dict[str, str]:
    result = {}
    for path in sorted(folder.rglob('*')):
        if path.is_file() and not path.is_symlink():
            with path.open('rb') as stream:
                result[path.relative_to(folder).as_posix()] = hashlib.file_digest(stream, 'sha256').hexdigest()
    return result


def archived(archive: Path) -> dict[str, str]:
    result = {}
    with tarfile.open(archive, 'r:gz') as tar:
        for member in tar.getmembers():
            if member.isfile():
                name = member.name.split('/', 1)[1]
                result[name] = hashlib.sha256(tar.extractfile(member).read()).hexdigest()
    return result


def back_up(saves: list[Path], root: Path, keep: int = KEEP) -> Path:
    """Write and verify one backup set of the existing save folders; return its folder."""
    sets = root / 'save-backups'
    target = sets / time.strftime('%Y%m%d-%H%M%S')
    suffix = 1
    while target.exists():
        suffix += 1
        target = sets / f'{time.strftime("%Y%m%d-%H%M%S")}-{suffix}'
    target.mkdir(parents=True)
    manifest = []
    try:
        for index, folder in enumerate(saves):
            if not folder.is_dir():
                continue
            files = digests(folder)
            archive = target / f'{index}-{folder.name}.tar.gz'
            with tarfile.open(archive, 'w:gz') as tar:
                for name in files:
                    tar.add(folder / name, arcname=f'{folder.name}/{name}', recursive=False)
            if archived(archive) != files or digests(folder) != files:
                raise RuntimeError(f'The backup of {folder} does not match the save files.')
            manifest.append({'folder': str(folder), 'archive': archive.name, 'sha256': files})
        (target / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    except BaseException:
        shutil.rmtree(target, ignore_errors=True)
        raise
    complete = sorted(path for path in sets.iterdir() if (path / 'manifest.json').is_file())
    for old in complete[:-keep]:
        shutil.rmtree(old)
    return target
