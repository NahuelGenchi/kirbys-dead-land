#!/usr/bin/env python3
"""Write packaging/NOTICES-emulator.txt for the bridge and the oracle runner.

The player packages ship libkssu_bridge.so and kssu-oracle-runner beside the
launcher. This file covers what is compiled into them: melonDS and melon-rs, the
libraries melonDS bundles (with the license texts read from the pinned melonDS
tree), and every Rust crate resolved from packaging/bridge.Cargo.lock and
packaging/runner.Cargo.lock, listed the way tools/launcher_notices.py lists the
launcher's crates. Run it again after either lockfile or the pinned revisions
change; a test fails until it is.

Reads the vendor checkout tools/oracle_build.py made (default: the data root's
oracle/vendor/melon-rs) and the crates Cargo has already downloaded; it builds
nothing and needs no network.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import tempfile

from data_root import data_root
from launcher_notices import MIT, apache_text, choose, copyrights, metadata, own_text, shipped
from oracle_build import CORE, WRAPPER, bridge_manifest, git_output, runner_manifest

PROJECT = Path(__file__).resolve().parents[1]
PACKAGING = PROJECT / 'packaging'
OUTPUT = PACKAGING / 'NOTICES-emulator.txt'
PROGRAMS = {
    'bridge': (bridge_manifest, PROJECT / 'src/nds/lib.rs', PACKAGING / 'bridge.Cargo.lock'),
    'runner': (runner_manifest, PROJECT / 'tools/oracle_runner.rs', PACKAGING / 'runner.Cargo.lock'),
}
# Covered above the crate list, as the programs' own GPL code.
OWN_CRATES = {'kssu-bridge', 'kssu-oracle-runner', 'melon-rs'}

HEADER = """Kirby's Dead Land: notices for the emulator programs

libkssu_bridge.so (the bridge the game loads to run Kirby's original code) and
kssu-oracle-runner (the program setup uses to read Kirby's sprites and copy
abilities from the player's own game) are built from melonDS and melon-rs with
this project's patches, the libraries melonDS bundles and the Rust crates
listed below.

Both programs are free software, distributed under the GNU General Public
License version 3 (the LICENSE file shipped beside this file, and
https://www.gnu.org/licenses/gpl-3.0.html), WITHOUT ANY WARRANTY. Their complete
corresponding source is this project's source at the release tag,
https://github.com/NahuelGenchi/kirbys-dead-land: tools/oracle_build.py and
tools/nds_build.py fetch the exact melonDS and melon-rs revisions named here and
apply this project's patches, and packaging/bridge.Cargo.lock and
packaging/runner.Cargo.lock pin every crate, whose source is on crates.io at the
version listed.

The programs use the system's own C and C++ runtime libraries; none are shipped.


MELONDS AND MELON-RS

melonDS {core} - GPL-3.0-or-later - https://github.com/melonDS-emu/melonDS
    {melonds_copyright}
melon-rs {wrapper} - GPL-3.0 - https://github.com/benhall-7/melon-rs
    Copyright the melon-rs authors


LIBRARIES BUNDLED WITH MELONDS

Compiled into both programs as part of melonDS {core_short}; their license texts
follow the crate list.

teakra (src/teakra) - MIT
    {teakra_copyright}
FatFs {fatfs_version} (src/fatfs) - FatFs license
    {fatfs_copyright}
FreeBIOS (src/FreeBIOS.cpp, src/FreeBIOS_Data.h) - BSD-2-Clause
    {freebios_copyright}
xxHash {xxhash_version} (src/xxhash) - BSD-2-Clause
    {xxhash_copyright}
tiny-AES-c (src/tiny-AES-c) - Unlicense
SHA-1 in C by Steve Reid (src/sha1) - public domain
blip_buf (src/blip-buf) - LGPL-2.1-or-later
    {blip_copyright}
Dolphin's x64 code emitter (src/dolphin) - GPL-2.0-or-later
    {dolphin_copyright}
glad 0.1.36 OpenGL loader (src/frontend/glad, compiled by melon-rs)
    The generated loader is public domain (glad offers Public Domain, WTFPL or
    CC0); khrplatform.h: {khronos_copyright}

blip_buf and the Dolphin emitter allow later versions of their licenses; as
parts of these programs they are used under GPL version 3.
"""


def text_between(path: Path, start: str, end: str) -> str:
    """The lines of a source comment from the one holding `start` to the one holding `end`."""
    lines = path.read_text(errors='replace').splitlines()
    first = next(i for i, line in enumerate(lines) if start in line)
    last = next(i for i in range(first, len(lines)) if end in lines[i])
    return '\n'.join(re.sub(r'^\s*(\*\*|\*|//)?\s?', '', line).rstrip() for line in lines[first:last + 1]).strip()


def first_line(path: Path, pattern: str) -> str:
    for line in path.read_text(errors='replace').splitlines():
        if re.search(pattern, line):
            return re.sub(r'^\s*(/\*|\*\*|\*|//|/)?\s*', '', line).strip()
    raise SystemExit(f'{path}: no line matching {pattern!r}')


def bundled(melonds: Path) -> tuple[dict[str, str], list[tuple[str, str]]]:
    """The header's fields and the bundled libraries' license texts, read from the melonDS tree."""
    src = melonds / 'src'
    cmake = (src / 'CMakeLists.txt').read_text()
    # Every third-party folder the core compiles must be one listed here.
    folders = set(re.findall(r'^\s+([A-Za-z0-9_-]+)/[^\s)]+\.(?:c|cpp|S)\b', cmake, re.M))
    known = {'fatfs', 'sha1', 'tiny-AES-c', 'xxhash', 'blip-buf', 'dolphin', 'debug',
             'DSP_HLE', 'NDSCart', 'ARMJIT_x64', 'ARMJIT_A64', 'OpenGL_shaders'}
    if folders - known:
        raise SystemExit(f'melonDS compiles folders these notices do not cover: {sorted(folders - known)}')
    if 'add_subdirectory(teakra' not in cmake:
        raise SystemExit('melonDS no longer builds teakra; update these notices.')
    if '100% Public Domain' not in (src / 'sha1/sha1.c').read_text(errors='replace'):
        raise SystemExit('src/sha1/sha1.c no longer says it is public domain.')
    xxh = (src / 'xxhash/xxhash.h').read_text()
    version = '.'.join(re.search(rf'#define XXH_VERSION_{part}\s+(\d+)', xxh).group(1) for part in ('MAJOR', 'MINOR', 'RELEASE'))
    fields = {
        'melonds_copyright': first_line(src / 'NDS.cpp', r'Copyright .*melonDS team'),
        'teakra_copyright': first_line(src / 'teakra/LICENSE', r'^Copyright'),
        'fatfs_version': re.search(r'FatFs - .*?(R\d+\.\d+\w*)', (src / 'fatfs/ff.h').read_text(errors='replace')).group(1),
        'fatfs_copyright': first_line(src / 'fatfs/ff.c', r'Copyright \(C\) \d{4}'),
        'freebios_copyright': first_line(src / 'FreeBIOS.cpp', r'Copyright \(c\)'),
        'xxhash_version': version,
        'xxhash_copyright': first_line(src / 'xxhash/xxhash.h', r'Copyright \(C\)'),
        'blip_copyright': re.search(r'Library (Copyright \(C\) [^.]+)\.', (src / 'blip-buf/blip_buf.c').read_text()).group(1),
        'dolphin_copyright': first_line(src / 'dolphin/x64Emitter.cpp', r'^// Copyright'),
        'khronos_copyright': first_line(src / 'frontend/glad/khrplatform.h', r'Copyright \(c\)'),
    }
    for name in ('x64ABI.cpp', 'x64CPUDetect.cpp', 'x64Emitter.cpp', 'CommonFuncs.cpp'):
        if 'GPLv2+' not in (src / 'dolphin' / name).read_text(errors='replace')[:300]:
            raise SystemExit(f'src/dolphin/{name} no longer says GPLv2+.')
    texts = [
        ('teakra', (src / 'teakra/LICENSE').read_text().strip()),
        ('FatFs', (src / 'fatfs/LICENSE.txt').read_text(errors='replace').strip()),
        ('FreeBIOS', text_between(src / 'FreeBIOS.cpp', 'Copyright (c)', 'POSSIBILITY OF SUCH DAMAGE.')),
        ('xxHash', text_between(src / 'xxhash/xxhash.h', 'Copyright (C)', 'OF SUCH DAMAGE.')),
        ('tiny-AES-c', (src / 'tiny-AES-c/unlicense.txt').read_text().strip()),
        ('khrplatform.h (glad)', text_between(src / 'frontend/glad/khrplatform.h', 'Copyright (c)', 'MATERIALS OR THE USE')),
        ('blip_buf (GNU Lesser General Public License 2.1)', (src / 'blip-buf/license.txt').read_text().strip()),
        ('the Dolphin x64 emitter (GNU General Public License 2)', (src / 'dolphin/license_dolphin.txt').read_text().strip()),
    ]
    return fields, texts


def font_texts(package: dict) -> str:
    """The licenses shipped beside the fonts a crate embeds."""
    folder = Path(package['manifest_path']).parent / 'fonts'
    files = sorted(folder.glob('*.txt')) if folder.is_dir() else []
    if not files:
        raise SystemExit(f'{package["name"]}: font licenses expected in {folder}')
    return '\n\n'.join(f'{path.name}:\n\n{path.read_text(errors="replace").strip()}' for path in files)


def crates(vendor: Path) -> tuple[list[tuple[dict, list[str]]], dict[str, str]]:
    """Every crate built into either program, with the programs it is built into, and the lockfiles' hashes."""
    found: dict[str, tuple[dict, list[str]]] = {}
    hashes = {}
    with tempfile.TemporaryDirectory() as folder:
        for name, (manifest, source, lock) in PROGRAMS.items():
            crate = Path(folder) / name
            crate.mkdir()
            (crate / 'Cargo.toml').write_text(manifest(source, vendor))
            shutil.copyfile(lock, crate / 'Cargo.lock')
            meta = metadata(crate / 'Cargo.toml', offline=True)
            if (crate / 'Cargo.lock').read_bytes() != lock.read_bytes():
                raise SystemExit(f'Cargo changed {lock.relative_to(PROJECT)}; the manifests no longer match it.')
            hashes[name] = hashlib.sha256(lock.read_bytes()).hexdigest()
            for package in shipped(meta):
                if package['name'] not in OWN_CRATES:
                    found.setdefault(package['id'], (package, []))[1].append(name)
    ordered = sorted(found.values(), key=lambda item: (item[0]['name'], item[0]['version']))
    return ordered, hashes


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--vendor', type=Path, default=data_root() / 'oracle/vendor/melon-rs',
                        help='melon-rs checkout from tools/oracle_build.py (default: the data root\'s).')
    args = parser.parse_args()
    vendor = args.vendor.expanduser().resolve()
    if git_output(vendor, 'rev-parse', 'HEAD') != WRAPPER or git_output(vendor / 'melonDS', 'rev-parse', 'HEAD') != CORE:
        raise SystemExit(f'{vendor} is not at the pinned melon-rs and melonDS revisions; run tools/oracle_build.py.')
    fields, library_texts = bundled(vendor / 'melonDS')
    packages, hashes = crates(vendor)
    out = [HEADER.format(core=CORE, core_short=CORE[:12], wrapper=WRAPPER, **fields)]
    out.append(f'\nRUST CRATES\n\nBuilt into the bridge from packaging/bridge.Cargo.lock {hashes["bridge"][:16]}\n'
               f'and into the runner from packaging/runner.Cargo.lock {hashes["runner"][:16]}.\n'
               'The linker leaves out code a program never calls; every crate compiled is listed.\n')
    licensed: dict[str, list[str]] = {}
    nonstandard: dict[str, list[str]] = {}
    for package, programs in packages:
        chosen = choose(package['license'] or '')
        label = f'{package["name"]} {package["version"]}'
        out.append(f'{label} - {" AND ".join(chosen)} (offered: {package["license"]}) - {", ".join(programs)}')
        for license in chosen:
            if license in ('MIT', 'Apache-2.0'):
                licensed.setdefault(license, []).append(label)
                for line in copyrights(package):
                    out.append(f'    {line}')
            elif license.startswith('GPL'):
                raise SystemExit(f'{label}: a GPL crate not covered by these notices')
            elif license in ('OFL-1.1', 'Ubuntu-font-1.0'):
                nonstandard.setdefault(font_texts(package), []).append(f'{label} fonts')
            else:
                if license == 'MPL-2.0':
                    out.append(f'    Used unmodified; source: {package.get("repository") or "crates.io"} and crates.io at {package["version"]}')
                text = own_text(package, license) or f'{license} (no license file shipped with the crate)'
                nonstandard.setdefault(text, []).append(f'{label} ({license})')
    out.append('\n\nLICENSES OF THE LIBRARIES BUNDLED WITH MELONDS')
    for name, text in library_texts:
        out.append(f'\n\nLICENSE OF {name}\n')
        out.append(text)
    out.append('\n\nMIT LICENSE\n\nUsed by the crates marked MIT above, with the copyright lines listed under each.\n')
    out.append(MIT)
    out.append('\n\nAPACHE LICENSE 2.0\n\nUsed by the crates marked Apache-2.0 above.\n')
    out.append(apache_text([package for package, _ in packages]))
    for text, users in sorted(nonstandard.items(), key=lambda item: item[1][0]):
        out.append('\n\nLICENSE OF ' + ', '.join(sorted(set(users))) + '\n')
        out.append(text)
    notice = '\n'.join(out).rstrip() + '\n'
    if str(Path.home()) in notice:
        raise SystemExit('The notices name this machine\'s home folder.')
    OUTPUT.write_text(notice)
    print(json.dumps({'crates': len(packages), 'bytes': len(notice), 'locks': hashes}, indent=2))


if __name__ == '__main__':
    main()
