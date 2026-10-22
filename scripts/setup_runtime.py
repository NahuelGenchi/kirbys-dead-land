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
import sys
import tarfile
import urllib.request
import zipfile

# This system's builds of DCCM and the .NET SDK (#246). The SDK hashes are those
# of Microsoft's release metadata for 10.0.401.
SYSTEM = 'win-x64' if os.name == 'nt' else 'linux-x64'
DCCM_VERSION = '35.13.6'
DCCM_ARCHIVES = {  # SHA-256 of each release archive
    'linux-x64': 'c57b1fca88e688d228fe5e0aa2b3b4789117f7ff9b092af5c9fd5ada1d3c14a2',
    'win-x64': '9ceb86e7c79f882ffdb9a7d7a3d39bc0bcc3a16b7bd8a3bd3dc6248bdce131ba',
}
SDK_VERSION = '10.0.401'
SDK_ARCHIVES = {  # archive type and SHA-512
    'linux-x64': ('tar.gz', '51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b'),
    'win-x64': ('zip', '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'),
}
DCCM_URL = f'https://github.com/dead-cells-core-modding/core/releases/download/v{DCCM_VERSION}/{SYSTEM}.zip'
DCCM_SHA256 = DCCM_ARCHIVES[SYSTEM]
SDK_FILE = f'dotnet-sdk-{SDK_VERSION}-{SYSTEM}.{SDK_ARCHIVES[SYSTEM][0]}'
SDK_URL = f'https://builds.dotnet.microsoft.com/dotnet/Sdk/{SDK_VERSION}/{SDK_FILE}'
SDK_SHA512 = SDK_ARCHIVES[SYSTEM][1]
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from data_root import data_root
from steam_paths import default_game

DEFAULT_ROOT = data_root()


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


def extract_zip(archive: Path, destination: Path) -> None:
    with zipfile.ZipFile(archive) as source:
        for entry in source.infolist():
            if not (destination / entry.filename).resolve().is_relative_to(destination.resolve()):
                raise RuntimeError('Archive contains a path outside its destination.')
        source.extractall(destination)


def sdk_program(root: Path) -> Path:
    """The SDK's dotnet program under a data root."""
    return root / 'dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')


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
    parser.add_argument('--rebuild', action='store_true', help='With --build, compile everything again instead of reusing earlier output.')
    parser.add_argument('--ready-to-run-os', choices=('linux', 'windows'),
                        help="With --build, the system the precompiled code is for (default: this machine's).")
    args = parser.parse_args()
    root = args.data_root.resolve()
    project = Path(__file__).resolve().parents[1]
    if root == project or project in root.parents:
        raise SystemExit('Runtime dependencies must remain outside the mod source project.')
    installed = default_game().resolve()
    if root == installed or installed in root.parents:
        raise SystemExit('Dependencies must not be installed into the game directory.')
    research = root / 'research'
    research.mkdir(parents=True, exist_ok=True)
    release = research / f'dccm-{DCCM_VERSION}'
    archive = research / f'{SYSTEM}.zip'
    download_checked(DCCM_URL, archive, 'sha256', DCCM_SHA256)
    if not (release / 'core/mdk/ref/GameProxy.dll').is_file():
        extract_zip(archive, release)
    sdk = sdk_program(root)
    if not sdk.is_file():
        archive = research / SDK_FILE
        download_checked(SDK_URL, archive, 'sha512', SDK_SHA512)
        (root / 'dotnet').mkdir(parents=True, exist_ok=True)
        if archive.suffix == '.zip':
            extract_zip(archive, root / 'dotnet')
        else:
            with tarfile.open(archive) as source:
                source.extractall(root / 'dotnet', filter='data')
    env = environment(root)
    version = subprocess.check_output([str(sdk), '--version'], env=env, text=True).strip()
    if version != SDK_VERSION:
        raise SystemExit(f'Expected SDK {SDK_VERSION}, found {version}. Use another --data-root.')
    if args.build:
        subprocess.run([str(sdk), 'build', str(project / 'src/runtime/Kirby.Runtime.csproj'),
                        '-c', 'Release', '-v', 'minimal', *(['--no-incremental'] if args.rebuild else []),
                        *([f'-p:ModReadyToRunOS={args.ready_to_run_os}'] if args.ready_to_run_os else [])],
                       env=env, check=True)
    print(json.dumps({'data_root': str(root), 'dotnet': str(sdk),
                      'dccm_version': DCCM_VERSION, 'mdk': env['DCCM_MDK_ROOT'],
                      'installed_into_game': False, 'launched_game': False}, indent=2))


if __name__ == '__main__':
    main()
