#!/usr/bin/env python3
"""Build the player packages: an AppImage and a .tar.gz of the launcher with its bundle.

Every program is compiled here from this tree, with the build machine's folders
mapped out of the paths compilers embed, so scripts/build_bundle.py accepts them
without --private:

    runtime adapter (ReadyToRun)   scripts/setup_runtime.py --build --rebuild
    bridge and oracle runner       tools/oracle_build.py and tools/nds_build.py, with
                                   the Cargo.lock files pinned in packaging/ and
                                   melonDS's link-time optimization off
    launcher                       cargo build --release --locked in launcher/

--target windows cross-builds the same programs for Windows with MinGW-w64 (#252):
the runtime precompiled for Windows, kssu_bridge.dll, kssu-oracle-runner.exe and
the launcher for x86_64-pc-windows-gnu, linked with GCC's and MinGW-w64's runtime
libraries, whose licenses the package carries.

Downloads, native sources and their builds stay in --build-root, a data root of
their own, so the development lab's bridge is never replaced. The packages and a
.sha256 for each go to --output:

    KirbysDeadLand-<version>-x86_64.AppImage   bundle at usr/share/kirbys-dead-land
    KirbysDeadLand-<version>-x86_64.tar.gz     bundle at share/kirbys-dead-land, beside the launcher
    KirbysDeadLand-<version>-windows-x64.zip   (--target windows) one folder: the launcher
                                               and the bundle at share/kirbys-dead-land

Each package is read back after it is written: it must hold exactly the files
that passed the package lint (scripts/package_release.py), and its launcher must
find its bundle with --check (the Windows launcher only with --wine). Never reads
the ROM, the game or a save.
"""
import argparse
import gzip
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import tomllib
import zipfile

from build_bundle import MAGIC, RUNTIME, RUNTIME_FILES
from package_release import lint_package
from setup_runtime import download_checked

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from data_root import data_root
from oracle_build import TARGETS, release_dir, windows_environment

PROJECT = Path(__file__).resolve().parents[1]
PACKAGING = PROJECT / 'packaging'
APP = 'kirbys-dead-land'
NAME = 'KirbysDeadLand'
# The AppImage type 2 runtime that starts the package (https://github.com/AppImage/type2-runtime).
APPIMAGE_RUNTIME = 'https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64'
APPIMAGE_RUNTIME_SHA256 = '2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d'
# The dependency versions the lab's bridge and runner were verified with.
LOCKS = {'bridge': PACKAGING / 'bridge.Cargo.lock', 'runner': PACKAGING / 'runner.Cargo.lock'}
# The license and notices every package carries: the launcher's (tools/launcher_notices.py)
# and the bridge's and runner's (tools/emulator_notices.py).
NOTICES = {'LICENSE': PROJECT / 'LICENSE', 'NOTICES.txt': PROJECT / 'launcher/NOTICES.txt',
           'NOTICES-emulator.txt': PACKAGING / 'NOTICES-emulator.txt'}
ELF, MZ, PNG, JSON = b'\x7fELF', b'MZ', b'\x89PNG\r\n\x1a\n', b'{'
# The Windows download's program, named for the person double-clicking it.
WINDOWS_PROGRAM = "Kirby's Dead Land.exe"
# Where the build machine's MinGW-w64 packages record the licenses of the runtime
# libraries linked into every Windows program: the MinGW-w64 C runtime and
# winpthreads, and GCC's libgcc and libstdc++ (GPL with the GCC Runtime Library Exception).
WINDOWS_RUNTIME_LICENSES = {'MinGW-w64 runtime and winpthreads': Path('/usr/share/doc/mingw-w64-x86-64-dev/copyright'),
                            "GCC's runtime libraries": Path('/usr/share/doc/gcc-mingw-w64-base/copyright')}
WINDOWS_RUNTIME_NOTICES = 'NOTICES-windows-runtime.txt'


def run(command: list[str], env: dict[str, str], cwd: Path = PROJECT) -> None:
    print('Running:', ' '.join(command), flush=True)
    subprocess.run(command, cwd=cwd, env=env, check=True)


def git(*args: str) -> str:
    return subprocess.check_output(['git', '-C', str(PROJECT), *args], text=True).strip()


def build_environment(build_root: Path) -> dict[str, str]:
    """The builds' environment: the build root as data root, and the build machine's
    home, build root and checkout mapped to fixed names in every embedded path."""
    maps = sorted({(str(Path.home()), '/build/home'), (str(build_root), '/build/data'), (str(PROJECT), '/build/kirbys-dead-land')},
                  key=lambda item: len(item[0]))
    for folder, _ in maps:
        if folder == '/' or re.search(r'[\s,=]', folder):
            raise SystemExit(f'{folder}: build folders must not be / or hold spaces, commas or "=".')
    env = dict(os.environ, KIRBY_DATA_ROOT=str(build_root))
    # rustc and GCC apply the last mapping that matches, Roslyn the first one.
    env['RUSTFLAGS'] = ' '.join([env.get('RUSTFLAGS', ''), *(f'--remap-path-prefix={a}={b}' for a, b in maps)]).strip()
    for flags in ('CFLAGS', 'CXXFLAGS'):
        env[flags] = ' '.join([env.get(flags, ''), *(f'-ffile-prefix-map={a}={b}' for a, b in maps)]).strip()
    env['PathMap'] = ','.join(f'{a}={b}' for a, b in reversed(maps))
    # Read only by the cmake crate, for the melonDS build inside melon-rs.
    env['CMAKE_TOOLCHAIN_FILE_x86_64_unknown_linux_gnu'] = str(PACKAGING / 'melonds.cmake')
    return env


def build(env: dict[str, str], build_root: Path, target: str = 'linux') -> None:
    for name, lock in LOCKS.items():
        (build_root / 'oracle' / name).mkdir(parents=True, exist_ok=True)
        shutil.copyfile(lock, build_root / 'oracle' / name / 'Cargo.lock')
    windows = ['--target', 'windows'] if target == 'windows' else []
    run([sys.executable, 'scripts/setup_runtime.py', '--build', '--rebuild', *(['--ready-to-run-os', 'windows'] if windows else [])], env)
    run([sys.executable, 'tools/oracle_build.py', *windows], env)
    run([sys.executable, 'tools/nds_build.py', *windows], env)
    for name, lock in LOCKS.items():
        used = build_root / 'oracle' / name / 'Cargo.lock'
        if used.read_bytes() != lock.read_bytes():
            raise SystemExit(f'Cargo changed the pinned {lock.relative_to(PROJECT)}; check {used} and copy it there.')
    if windows:
        run(['cargo', 'build', '--release', '--locked', '--target', TARGETS['windows'][0]], {**env, **windows_environment(env)},
            cwd=PROJECT / 'launcher')
    else:
        run(['cargo', 'build', '--release', '--locked'], env, cwd=PROJECT / 'launcher')


def notice_paths(kind: str) -> list[str]:
    """Where a package keeps the license and notices."""
    if kind == 'zip':
        return [*NOTICES, WINDOWS_RUNTIME_NOTICES]
    return [f'usr/share/doc/{APP}/{name}' if kind == 'appimage' else name for name in NOTICES]


def windows_runtime_notices(sources: dict[str, Path] = WINDOWS_RUNTIME_LICENSES) -> bytes:
    """The licenses of the runtime libraries the Windows programs link in, as the
    build machine's MinGW-w64 packages record them."""
    missing = [str(path) for path in sources.values() if not path.is_file()]
    if missing:
        raise SystemExit('MinGW-w64 license files missing: ' + ', '.join(missing) + ' (install MinGW-w64 from the system packages).')
    out = ["Kirby's Dead Land: notices for the Windows runtime libraries\n\n"
           f"{WINDOWS_PROGRAM}, kssu_bridge.dll and kssu-oracle-runner.exe are linked with\n"
           'the MinGW-w64 C runtime and winpthreads, and with GCC\'s libgcc and libstdc++\n'
           '(GPL version 3 with the GCC Runtime Library Exception), from the MinGW-w64\n'
           'packages of the machine that built them. Their licenses follow, as those\n'
           'packages record them.\n']
    for name, path in sources.items():
        out.append(f'\n\n{name.upper()} ({path.parent.name})\n\n{path.read_text(errors="replace").strip()}\n')
    return ''.join(out).encode()


def layout(kind: str, bundle: Path, launcher: bytes, icon: bytes,
           runtime_notices: bytes = b'') -> tuple[dict[str, tuple[bytes, int]], dict[str, bytes]]:
    """A package's files as {path: (data, mode)}, and the ones the build made, each
    with the bytes it starts with."""
    notices = {name: path.read_bytes() for name, path in NOTICES.items()}
    desktop = (PACKAGING / f'{APP}.desktop').read_bytes()
    target = 'windows' if kind == 'zip' else 'linux'
    if kind == 'zip':
        share = f'share/{APP}/'
        files = {WINDOWS_PROGRAM: (launcher, 0o755), 'README.txt': ((PACKAGING / 'README-windows.txt').read_bytes(), 0o644),
                 WINDOWS_RUNTIME_NOTICES: (runtime_notices, 0o644)}
        files.update({name: (data, 0o644) for name, data in notices.items()})
        built = {WINDOWS_PROGRAM: MZ}
    elif kind == 'appimage':
        share = f'usr/share/{APP}/'
        files = {'AppRun': ((PACKAGING / 'AppRun').read_bytes(), 0o755), f'{APP}.desktop': (desktop, 0o644),
                 f'{APP}.png': (icon, 0o644), '.DirIcon': (icon, 0o644), f'usr/bin/{APP}': (launcher, 0o755)}
        files.update({f'usr/share/doc/{APP}/{name}': (data, 0o644) for name, data in notices.items()})
        built = {f'usr/bin/{APP}': ELF, f'{APP}.png': PNG, '.DirIcon': PNG}
    else:
        share = f'share/{APP}/'
        files = {APP: (launcher, 0o755), f'{APP}.desktop': (desktop, 0o644), f'{APP}.png': (icon, 0o644),
                 'add-to-menu.sh': ((PACKAGING / 'add-to-menu.sh').read_bytes(), 0o755),
                 'README.txt': ((PACKAGING / 'README.txt').read_bytes(), 0o644)}
        files.update({name: (data, 0o644) for name, data in notices.items()})
        built = {APP: ELF, f'{APP}.png': PNG}
    for path in sorted(bundle.rglob('*')):
        if path.is_symlink() or not (path.is_file() or path.is_dir()):
            raise SystemExit(f'{path}: bundles hold only plain files and folders')
        if path.is_file():
            mode = 0o755 if os.access(path, os.X_OK) else 0o644
            files[share + path.relative_to(bundle).as_posix()] = (path.read_bytes(), mode)
    built.update({f'{share}bin/{name}': MAGIC[target] for name in TARGETS[target][1:3]})
    built.update({f'{share}project/{RUNTIME}/{name}': MZ if name.endswith('.dll') else JSON for name in RUNTIME_FILES})
    return files, built


def folders(files: dict[str, tuple[bytes, int]]) -> list[str]:
    return sorted({str(parent) for name in files for parent in PurePosixPath(name).parents if str(parent) != '.'})


def write_tar(path: Path, top: str, files: dict[str, tuple[bytes, int]], mtime: int) -> None:
    """A .tar.gz with one top folder, owned by root, every time the commit time."""
    with path.open('wb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=mtime) as stream, \
            tarfile.open(fileobj=stream, mode='w', format=tarfile.PAX_FORMAT) as tar:
        for name in [''] + folders(files):
            info = tarfile.TarInfo(f'{top}/{name}'.rstrip('/'))
            info.type, info.mode, info.mtime = tarfile.DIRTYPE, 0o755, mtime
            tar.addfile(info)
        for name, (data, mode) in sorted(files.items()):
            info = tarfile.TarInfo(f'{top}/{name}')
            info.size, info.mode, info.mtime = len(data), mode, mtime
            tar.addfile(info, io.BytesIO(data))


def read_tar(path: Path, top: str) -> dict[str, tuple[bytes, int]]:
    files = {}
    with tarfile.open(path) as tar:
        for member in tar.getmembers():
            if not member.name.startswith(f'{top}/') and member.name != top:
                raise SystemExit(f'{path.name}: {member.name} is outside {top}/')
            if member.isfile():
                files[member.name[len(top) + 1:]] = (tar.extractfile(member).read(), member.mode)
            elif not member.isdir():
                raise SystemExit(f'{path.name}: {member.name} is not a plain file or folder')
    return files


def write_zip(path: Path, top: str, files: dict[str, tuple[bytes, int]], mtime: int) -> None:
    """A .zip with one top folder, every time the commit time, members in name order."""
    stamp = time.gmtime(max(mtime, 315532800))[:6]
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in [''] + folders(files):
            info = zipfile.ZipInfo(f'{top}/{name}'.rstrip('/') + '/', stamp)
            info.external_attr = (0o40755 << 16) | 0x10
            archive.writestr(info, b'')
        for name, (data, mode) in sorted(files.items()):
            info = zipfile.ZipInfo(f'{top}/{name}', stamp)
            info.external_attr, info.compress_type = (0o100000 | mode) << 16, zipfile.ZIP_DEFLATED
            archive.writestr(info, data)


def read_zip(path: Path, top: str) -> dict[str, tuple[bytes, int]]:
    files = {}
    with zipfile.ZipFile(path) as archive:
        for member in archive.infolist():
            if not member.filename.startswith(f'{top}/'):
                raise SystemExit(f'{path.name}: {member.filename} is outside {top}/')
            if not member.is_dir():
                files[member.filename[len(top) + 1:]] = (archive.read(member), (member.external_attr >> 16) & 0o777)
    return files


def write_tree(folder: Path, files: dict[str, tuple[bytes, int]]) -> None:
    if folder.exists():
        shutil.rmtree(folder)
    for name in [''] + folders(files):
        (folder / name).mkdir(mode=0o755, exist_ok=True)
    for name, (data, mode) in files.items():
        (folder / name).write_bytes(data)
        (folder / name).chmod(mode)


def read_tree(folder: Path) -> dict[str, tuple[bytes, int]]:
    files = {}
    for path in sorted(folder.rglob('*')):
        if path.is_symlink() or not (path.is_file() or path.is_dir()):
            raise SystemExit(f'{path}: not a plain file or folder')
        if path.is_file():
            files[path.relative_to(folder).as_posix()] = (path.read_bytes(), path.stat().st_mode & 0o777)
    return files


def write_appimage(path: Path, files: dict[str, tuple[bytes, int]], runtime: bytes, mtime: int, work: Path) -> None:
    """The type 2 runtime followed by a squashfs image of the AppDir, as appimagetool writes it."""
    appdir, image = work / 'AppDir', work / 'AppDir.squashfs'
    write_tree(appdir, files)
    for item in [appdir, *appdir.rglob('*')]:
        os.utime(item, (mtime, mtime))
    image.unlink(missing_ok=True)
    # SOURCE_DATE_EPOCH fixes the image's own time in squashfs-tools 4.5 (Ubuntu 22.04) and later alike.
    subprocess.run(['mksquashfs', str(appdir), str(image), '-noappend', '-all-root', '-no-xattrs', '-comp', 'zstd',
                    '-no-progress'], env=dict(os.environ, SOURCE_DATE_EPOCH=str(mtime)), check=True, stdout=subprocess.DEVNULL)
    path.write_bytes(runtime + image.read_bytes())
    path.chmod(0o755)


def read_appimage(path: Path, offset: int, work: Path) -> dict[str, tuple[bytes, int]]:
    folder = work / 'AppImage-contents'
    if folder.exists():
        shutil.rmtree(folder)
    subprocess.run(['unsquashfs', '-offset', str(offset), '-no-xattrs', '-dest', str(folder), str(path)],
                   check=True, stdout=subprocess.DEVNULL)
    return read_tree(folder)


def check_finds_bundle(command: list[str], bundle_id: str, env: dict[str, str] | None = None) -> str:
    """Run the packaged launcher's --check in an empty home; it must name its own bundle."""
    with tempfile.TemporaryDirectory() as home:
        clean = {'PATH': os.environ.get('PATH', '/usr/bin:/bin'), 'HOME': home, 'LANG': 'C.UTF-8', **(env or {})}
        result = subprocess.run([*command, '--check'], env=clean, capture_output=True, text=True, timeout=300)
    found = [line for line in result.stdout.splitlines() if line.startswith('Mod files: ')]
    if not found or not found[0].endswith(f' ({bundle_id})'):
        raise SystemExit(f'{command[0]} --check did not find its bundle {bundle_id}:\n{result.stdout}{result.stderr}')
    return found[0]


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    path.with_name(path.name + '.sha256').write_text(f'{digest}  {path.name}\n')
    return digest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--output', type=Path, default=PROJECT / 'dist', help='Folder for the packages (default dist/).')
    parser.add_argument('--build-root', type=Path,
                        help='Data root for the builds and downloads (default: release-build, or release-build-windows, in the data root).')
    parser.add_argument('--target', choices=sorted(TARGETS), default='linux',
                        help='linux: the AppImage and the .tar.gz; windows: the .zip (default: linux).')
    parser.add_argument('--tag', help='Release tag being built; it must be v<version> and name this commit.')
    parser.add_argument('--skip-build', action='store_true', help='Package the programs a previous run built.')
    parser.add_argument('--wine', help='With --target windows, a wine program to run the packaged launcher\'s --check with.')
    args = parser.parse_args()
    windows = args.target == 'windows'
    default_root = data_root() / ('release-build-windows' if windows else 'release-build')
    build_root = (args.build_root or default_root).expanduser().resolve()
    output = args.output.expanduser().resolve()
    if build_root == PROJECT or PROJECT in build_root.parents:
        raise SystemExit('The build root must be outside the project.')
    version = json.loads((PROJECT / 'mod.json').read_text())['version']
    launcher_version = tomllib.loads((PROJECT / 'launcher/Cargo.toml').read_text())['package']['version']
    if args.tag and (args.tag != f'v{version}' or launcher_version != version):
        raise SystemExit(f'Tag {args.tag}, mod.json {version} and launcher {launcher_version} must name one version.')
    if args.tag and git('rev-parse', 'HEAD') != git('rev-parse', f'{args.tag}^{{commit}}'):
        raise SystemExit(f'{args.tag} does not name the checked-out commit.')
    if args.tag and git('status', '--porcelain', '--untracked-files=no'):
        raise SystemExit('Tracked files differ from the commit; a release is built from the commit alone.')
    if not windows and shutil.which('desktop-file-validate'):
        subprocess.run(['desktop-file-validate', str(PACKAGING / f'{APP}.desktop')], check=True)
    runtime_notices = windows_runtime_notices() if windows else b''

    env = build_environment(build_root)
    build_root.mkdir(parents=True, exist_ok=True)
    if not args.skip_build:
        build(env, build_root, args.target)
    work = build_root / 'packages'
    work.mkdir(exist_ok=True)
    run([sys.executable, 'scripts/build_bundle.py', '--output', str(work / 'bundle'), '--target', args.target,
         '--native', str(release_dir(build_root / 'oracle/vendor/melon-rs', args.target))], env)
    record = json.loads((work / 'bundle/bundle.json').read_text())
    if record['private'] or (args.tag and record['commit'].endswith('-dirty')):
        raise SystemExit(f'The bundle is {"private" if record["private"] else "from a changed tree"}; it cannot be released.')
    bundle_id = f'{record["version"]}+{record["commit"]}'
    mtime = int(git('show', '-s', '--format=%ct', 'HEAD'))
    private = sorted({os.fsencode(Path.home()) + b'/', os.fsencode(build_root), os.fsencode(PROJECT)})
    output.mkdir(parents=True, exist_ok=True)
    report = {'version': version, 'bundle': bundle_id, 'packages': {}}

    if windows:
        launcher = PROJECT / 'launcher/target' / TARGETS['windows'][0] / 'release' / f'{APP}.exe'
        stem = f'{NAME}-{version}-windows-x64'
        path = output / f'{stem}.zip'
        files, built = layout('zip', work / 'bundle', launcher.read_bytes(), b'', runtime_notices)
        problems = lint_package({name: data for name, (data, _) in files.items()}, built, private, notice_paths('zip'))
        if problems:
            raise SystemExit(f'Package lint failed for {path.name}:\n' + '\n'.join(f'- {problem}' for problem in problems))
        write_zip(path, stem, files, mtime)
        if read_zip(path, stem) != files:
            raise SystemExit(f'{path.name} does not hold the files that passed the lint.')
        found = 'not run (no --wine)'
        if args.wine:
            unpacked = work / 'zip-contents'
            if unpacked.exists():
                shutil.rmtree(unpacked)
            with zipfile.ZipFile(path) as archive:
                archive.extractall(unpacked)
            found = check_finds_bundle([args.wine, str(unpacked / stem / WINDOWS_PROGRAM)], bundle_id,
                                       {key: os.environ[key] for key in ('WINEPREFIX', 'WINEDEBUG', 'WINEDLLOVERRIDES') if key in os.environ})
        report['packages'][path.name] = {'files': len(files), 'bytes': path.stat().st_size,
                                         'sha256': sha256_file(path), 'check': found}
        print(json.dumps(report, indent=2))
        return

    launcher = PROJECT / 'launcher/target/release' / APP
    run([str(launcher), '--write-icon', str(work / f'{APP}.png')], env)
    stem = f'{NAME}-{version}-x86_64'
    runtime_file = build_root / 'downloads' / f'appimage-runtime-{APPIMAGE_RUNTIME_SHA256[:12]}'
    runtime_file.parent.mkdir(exist_ok=True)
    download_checked(APPIMAGE_RUNTIME, runtime_file, 'sha256', APPIMAGE_RUNTIME_SHA256)
    runtime = runtime_file.read_bytes()
    for kind, path in (('appimage', output / f'{stem}.AppImage'), ('tar', output / f'{stem}.tar.gz')):
        files, built = layout(kind, work / 'bundle', launcher.read_bytes(), (work / f'{APP}.png').read_bytes())
        problems = lint_package({name: data for name, (data, _) in files.items()}, built, private, notice_paths(kind))
        if problems:
            raise SystemExit(f'Package lint failed for {path.name}:\n' + '\n'.join(f'- {problem}' for problem in problems))
        if kind == 'appimage':
            write_appimage(path, files, runtime, mtime, work)
            if read_appimage(path, len(runtime), work) != files:
                raise SystemExit(f'{path.name} does not hold the files that passed the lint.')
            # Without FUSE (a CI container), the runtime extracts itself to a temporary folder.
            fuse = Path('/dev/fuse').exists() and bool(shutil.which('fusermount3') or shutil.which('fusermount'))
            found = check_finds_bundle([str(path)], bundle_id, None if fuse else {'APPIMAGE_EXTRACT_AND_RUN': '1'})
        else:
            write_tar(path, stem, files, mtime)
            if read_tar(path, stem) != files:
                raise SystemExit(f'{path.name} does not hold the files that passed the lint.')
            unpacked = work / 'tar-contents'
            if unpacked.exists():
                shutil.rmtree(unpacked)
            with tarfile.open(path) as tar:
                tar.extractall(unpacked, filter='data')
            found = check_finds_bundle([str(unpacked / stem / APP)], bundle_id)
        report['packages'][path.name] = {'files': len(files), 'bytes': path.stat().st_size,
                                         'sha256': sha256_file(path), 'check': found}
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
