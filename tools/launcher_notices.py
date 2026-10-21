#!/usr/bin/env python3
"""Write launcher/NOTICES.txt from launcher/Cargo.lock and the fonts' and icons' licenses.

The launcher shows this file in Settings › Licenses, followed by
packaging/NOTICES-emulator.txt (tools/emulator_notices.py). It covers the launcher's own
license, the programs a launcher package ships or setup downloads, the fonts and
icons, and every Rust crate built into the launcher with the license text that
applies to it. Run it again after launcher/Cargo.lock changes; a launcher test
fails until it is.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
import subprocess

PROJECT = Path(__file__).resolve().parents[1]
LAUNCHER = PROJECT / 'launcher'
TARGET = 'x86_64-unknown-linux-gnu'
# When a crate offers a choice, the first of these it offers is used.
PREFERRED = ['MIT', 'Apache-2.0', 'BSD-3-Clause', 'BSD-2-Clause', 'ISC', 'Zlib', 'Unicode-3.0',
             'CDLA-Permissive-2.0', '0BSD', 'Unlicense', 'CC0-1.0', 'MPL-2.0', 'GPL-3.0-only', 'GPL-3.0-or-later',
             'OFL-1.1', 'Ubuntu-font-1.0']
# The system's copies of standard license texts, for crates that ship none.
COMMON_LICENSES = Path('/usr/share/common-licenses')
# A copyright line, not license wording such as "copyright license" or "(c) You must".
COPYRIGHT = re.compile(r'(Copyright|COPYRIGHT)\s+(\(c\)|\(C\)|©|\d{4}|[A-Z])|(\(c\)|\(C\)|©)\s*\d{4}')
MIT = """Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE."""

HEADER = """Kirby's Dead Land launcher {version}

Kirby's Dead Land is free software: you can redistribute it and/or modify it
under the terms of the GNU General Public License as published by the Free
Software Foundation, either version 3 of the License, or (at your option) any
later version. It is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
FITNESS FOR A PARTICULAR PURPOSE. The full license is the LICENSE file shipped
with the launcher, and https://www.gnu.org/licenses/gpl-3.0.html.

The launcher draws its window with Slint, used under the GNU GPL version 3, so
the launcher program as a whole is distributed under GPL version 3.

Source code: https://github.com/NahuelGenchi/kirbys-dead-land
The source for any release is the repository at that release's tag.

Kirby Super Star Ultra and Dead Cells are not included. Players use their own
copies. This project is not affiliated with or endorsed by Nintendo, HAL
Laboratory, Motion Twin or Evil Empire.


PROGRAMS SHIPPED WITH THE LAUNCHER

libkssu_bridge.so and kssu-oracle-runner are built from melonDS
(GPL-3.0-or-later, https://github.com/melonDS-emu/melonDS) and melon-rs
(GPL-3.0, https://github.com/benhall-7/melon-rs) with this project's patches.
Their complete corresponding source is this project's source at the release
tag, which names the exact melonDS and melon-rs revisions and patches in
THIRD_PARTY_NOTICES.md, tools/oracle_build.py and tools/nds_build.py. The
libraries and Rust crates built into them are listed in NOTICES-emulator.txt,
shipped beside this file and shown after it in the launcher's Licenses.

The Kirby runtime adapter (Kirby.Runtime.dll, Kirby.Core.dll) is this project's
own code, GPL-3.0-or-later.


DOWNLOADED BY SETUP (each pinned by hash, from its publisher)

Dead Cells Core Modding 35.13.6 - MIT - https://github.com/dead-cells-core-modding/core
.NET runtime 10.0.12 - MIT - https://github.com/dotnet/runtime
Python 3.14.8 (python-build-standalone) - PSF-2.0 and others - https://github.com/astral-sh/python-build-standalone
Pillow 12.1.1 - MIT-CMU - https://github.com/python-pillow/Pillow
NumPy 2.3.5 - BSD-3-Clause and others - https://github.com/numpy/numpy
libjpeg-turbo 2.1.5 (Ubuntu libturbojpeg0) - BSD-3-Clause, IJG and Zlib - https://libjpeg-turbo.org


FONTS

Fredoka and Nunito, used under the SIL Open Font License 1.1:
"""


def metadata(manifest: Path = LAUNCHER / 'Cargo.toml', offline: bool = False) -> dict:
    output = subprocess.check_output(
        ['cargo', 'metadata', '--format-version', '1', '--locked', *(['--offline'] if offline else []),
         '--filter-platform', TARGET, '--manifest-path', str(manifest)])
    return json.loads(output)


def shipped(meta: dict) -> list[dict]:
    """The crates compiled into the launcher: normal dependencies, not build or test ones."""
    nodes = {node['id']: node for node in meta['resolve']['nodes']}
    packages = {package['id']: package for package in meta['packages']}
    root = meta['resolve']['root']
    seen, stack = set(), [root]
    while stack:
        current = stack.pop()
        if current in seen:
            continue
        seen.add(current)
        for dep in nodes[current]['deps']:
            if any(kind['kind'] is None for kind in dep['dep_kinds']):
                stack.append(dep['pkg'])
    seen.discard(root)
    return sorted((packages[i] for i in seen), key=lambda p: (p['name'], p['version']))


def choose(expression: str) -> list[str]:
    """The licenses a crate is used under: one choice from each part that must all apply."""
    chosen = []
    for part in re.split(r'\s+AND\s+', expression.replace('/', ' OR ')):
        options = [o.strip(' ()') for o in re.split(r'\s+OR\s+', part.strip(' ()'))]
        best = next((p for p in PREFERRED if p in options), None)
        if best is None:
            raise SystemExit(f'No known license in "{expression}"')
        chosen.append(best)
    return chosen


def license_files(package: dict) -> list[Path]:
    folder = Path(package['manifest_path']).parent
    found = []
    for path in sorted(folder.iterdir()):
        name = path.name.lower()
        if name.startswith(('license', 'licence', 'copying', 'notice')):
            found.extend(sorted(p for p in path.iterdir() if p.is_file()) if path.is_dir() else [path])
    return found


def copyrights(package: dict) -> list[str]:
    lines = []
    for path in license_files(package):
        for line in path.read_text(errors='replace').splitlines():
            text = line.strip()
            if (COPYRIGHT.match(text) and not re.search(r'[<\[]\s*(year|yyyy|copyright|name)', text, re.I)
                    and 'copyright notice' not in text.lower() and 'copyright holder' not in text.lower()
                    and 'copyright owner' not in text.lower() and len(text) < 200 and text not in lines):
                lines.append(text)
    if not lines:
        authors = [re.sub(r'\s*<[^>]*>', '', author).strip() for author in package.get('authors') or []]
        lines.append(f'Copyright the {package["name"]} authors' + (f': {", ".join(authors)}' if authors else ''))
    return lines


def own_text(package: dict, license: str) -> str | None:
    """The crate's own text for a license that names its holders inside it."""
    keys = {'BSD-3-Clause': ('bsd', 'whatwg'), 'BSD-2-Clause': ('bsd',), 'ISC': ('isc',), 'Zlib': ('zlib',),
            'Unicode-3.0': ('unicode',), 'CDLA-Permissive-2.0': ('cdla',), 'MPL-2.0': ('mpl',)}.get(license, ())
    files = license_files(package)
    named = [f for f in files if any(k in f.name.lower() for k in keys)]
    generic = [f for f in files if f.name.lower() in ('license', 'license.txt', 'license.md', 'copying')]
    for path in named or generic:
        return path.read_text(errors='replace').strip()
    if (COMMON_LICENSES / license).is_file():
        return (COMMON_LICENSES / license).read_text().strip()
    return None


def apache_text(packages: list[dict]) -> str:
    for package in packages:
        for path in license_files(package):
            text = path.read_text(errors='replace')
            if 'apache' in path.name.lower() and 'TERMS AND CONDITIONS FOR USE' in text and 'LLVM' not in path.name:
                return text.strip()
    raise SystemExit('No Apache-2.0 license text found among the crates.')


def main() -> None:
    meta = metadata()
    version = next(p['version'] for p in meta['packages'] if p['id'] == meta['resolve']['root'])
    packages = shipped(meta)
    lock = hashlib.sha256((LAUNCHER / 'Cargo.lock').read_bytes()).hexdigest()
    out = [HEADER.format(version=version)]
    for font in ('Fredoka', 'Nunito'):
        out.append((LAUNCHER / f'ui/fonts/{font}-OFL.txt').read_text().splitlines()[0])
    out.append('\n' + (LAUNCHER / 'ui/fonts/Nunito-OFL.txt').read_text().split('\n', 1)[1].strip())
    out.append('\n\nICONS\n\nLucide 1.49.0, https://lucide.dev\n')
    out.append((LAUNCHER / 'ui/icons/Lucide-LICENSE.txt').read_text().strip())
    out.append(f'\n\nRUST CRATES\n\nBuilt into the launcher, from Cargo.lock {lock[:16]}.\n')
    texts: dict[str, list[str]] = {}
    nonstandard: dict[str, list[str]] = {}
    for package in packages:
        chosen = choose(package['license'] or '')
        label = f'{package["name"]} {package["version"]}'
        out.append(f'{label} - {" AND ".join(chosen)} (offered: {package["license"]})')
        for license in chosen:
            if license in ('MIT', 'Apache-2.0'):
                texts.setdefault(license, []).append(label)
                for line in copyrights(package):
                    out.append(f'    {line}')
            elif license.startswith('GPL'):
                out.append('    Copyright SixtyFPS GmbH (Slint); GPL-3.0, see the top of this file.')
            else:
                text = own_text(package, license) or f'{license} (no license file shipped with the crate)'
                nonstandard.setdefault(text, []).append(f'{label} ({license})')
    out.append('\n\nMIT LICENSE\n\nUsed by the crates marked MIT above, with the copyright lines listed under each.\n')
    out.append(MIT)
    out.append('\n\nAPACHE LICENSE 2.0\n\nUsed by the crates marked Apache-2.0 above.\n')
    out.append(apache_text(packages))
    for text, users in sorted(nonstandard.items(), key=lambda item: item[1][0]):
        out.append('\n\nLICENSE OF ' + ', '.join(users) + '\n')
        out.append(text)
    notice = '\n'.join(out).rstrip() + '\n'
    (LAUNCHER / 'NOTICES.txt').write_text(notice)
    print(json.dumps({'crates': len(packages), 'bytes': len(notice), 'cargo_lock': lock}, indent=2))


if __name__ == '__main__':
    main()
