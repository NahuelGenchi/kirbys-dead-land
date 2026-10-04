#!/usr/bin/env python3
"""Download pinned development dependencies outside the project; never launch/install a mod."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

DCCM_VERSION = '35.13.6'
DCCM_URL = f'https://github.com/dead-cells-core-modding/core/releases/download/v{DCCM_VERSION}/linux-x64.zip'
DCCM_SHA256 = 'c57b1fca88e688d228fe5e0aa2b3b4789117f7ff9b092af5c9fd5ada1d3c14a2'
SDK_VERSION = '10.0.401'
SDK_URL = f'https://builds.dotnet.microsoft.com/dotnet/Sdk/{SDK_VERSION}/dotnet-sdk-{SDK_VERSION}-linux-x64.tar.gz'
SDK_SHA512 = '51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b'
DEFAULT_ROOT = Path.home() / '.local/share/kirby-dead-cells'


def download_checked(url: str, path: Path, algorithm: str, digest: str) -> None:
    def matches() -> bool:
        if not path.is_file():
            return False
        with path.open('rb') as stream:
            return hashlib.file_digest(stream, algorithm).hexdigest() == digest
    if matches():
        return
    temporary = path.with_suffix(path.suffix + '.part')
    with urllib.request.urlopen(url) as source, temporary.open('wb') as target:
        shutil.copyfileobj(source, target)
    with temporary.open('rb') as stream:
        actual = hashlib.file_digest(stream, algorithm).hexdigest()
    if actual != digest:
        raise RuntimeError(f'Unexpected {algorithm} for {url}: {actual}')
    temporary.replace(path)


def environment(root: Path) -> dict[str, str]:
    env = os.environ.copy()
    env.update({
        'DOTNET_ROOT': str(root / 'dotnet'),
        'PATH': str(root / 'dotnet') + os.pathsep + env.get('PATH', ''),
        'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
        'DOTNET_GENERATE_ASPNET_CERTIFICATE': 'false',
        'DOTNET_NOLOGO': '1',
        'DCCM_MDK_ROOT': str(root / 'research' / f'dccm-{DCCM_VERSION}' / 'core/mdk'),
    })
    return env


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-root', type=Path, default=DEFAULT_ROOT)
    parser.add_argument('--build', action='store_true', help='Compile the runtime adapter after setup.')
    args = parser.parse_args()
    root = args.data_root.resolve()
    project = Path(__file__).resolve().parents[1]
    if root == project or project in root.parents:
        raise SystemExit('Runtime dependencies must remain outside the mod source project.')
    installed = (Path.home() / '.local/share/Steam/steamapps/common/Dead Cells').resolve()
    if root == installed or installed in root.parents:
        raise SystemExit('Dependencies must not be installed into the game directory.')
    research = root / 'research'
    research.mkdir(parents=True, exist_ok=True)
    release = research / f'dccm-{DCCM_VERSION}'
    archive = research / 'linux-x64.zip'
    download_checked(DCCM_URL, archive, 'sha256', DCCM_SHA256)
    if not (release / 'core/mdk/ref/GameProxy.dll').is_file():
        with zipfile.ZipFile(archive) as source:
            for entry in source.infolist():
                destination = (release / entry.filename).resolve()
                if not destination.is_relative_to(release.resolve()):
                    raise RuntimeError('Archive contains a path outside its destination.')
            source.extractall(release)
    sdk = root / 'dotnet/dotnet'
    if not sdk.is_file():
        archive = research / f'dotnet-sdk-{SDK_VERSION}-linux-x64.tar.gz'
        download_checked(SDK_URL, archive, 'sha512', SDK_SHA512)
        (root / 'dotnet').mkdir(parents=True, exist_ok=True)
        with tarfile.open(archive) as source:
            source.extractall(root / 'dotnet', filter='data')
    env = environment(root)
    version = subprocess.check_output([str(sdk), '--version'], env=env, text=True).strip()
    if version != SDK_VERSION:
        raise SystemExit(f'Expected SDK {SDK_VERSION}, found {version}. Use another --data-root.')
    if args.build:
        subprocess.run([str(sdk), 'build', str(project / 'src/runtime/Kirby.Runtime.csproj'),
                        '-c', 'Release', '-v', 'minimal'], env=env, check=True)
    print(json.dumps({'data_root': str(root), 'dotnet': str(sdk),
                      'dccm_version': DCCM_VERSION, 'mdk': env['DCCM_MDK_ROOT'],
                      'installed_into_game': False, 'launched_game': False}, indent=2))


if __name__ == '__main__':
    main()
