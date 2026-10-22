#!/usr/bin/env python3
"""Write a release's notes: the one page every release uses (notes.md), filled in.

    python3 .github/release/notes.py v0.3.0 [--ref COMMIT] [--appimage-bytes N] [--zip-bytes N]

Each version has a folder next to this script, v<version>/, with its pictures
(make_media.py) and two short parts written for players: new.md (what is new or
fixed) and tested.md (what was tested). The pictures are linked at --ref, the
commit that holds them (default: the tag), so later commits cannot change a
published page. From the first version with a Windows download, the page offers
both downloads. The release workflow runs this for every new release.
"""
import argparse
import os
import re
import sys
from pathlib import Path
from string import Template

HERE = Path(__file__).resolve().parent
PICTURES = ('banner.png', 'download.png', 'step-1-checks.png', 'step-2-fix.png', 'step-3-setup.png', 'step-4-play.png')
PARTS = ('new.md', 'tested.md')
# The first release with the launcher: nothing to update from before it.
FIRST_LAUNCHER = (0, 2, 0)
# The first release with a Windows download (#252).
FIRST_WINDOWS = (0, 3, 0)
WINDOWS_PICTURES = ('download-windows.png',)


def version_of(tag: str) -> tuple[int, ...]:
    if not re.fullmatch(r'v\d+\.\d+\.\d+', tag):
        raise SystemExit(f'{tag}: expected a tag like v1.2.3')
    return tuple(int(part) for part in tag[1:].split('.'))


def has_windows(tag: str) -> bool:
    return version_of(tag) >= FIRST_WINDOWS


def pictures(tag: str) -> tuple[str, ...]:
    return PICTURES + (WINDOWS_PICTURES if has_windows(tag) else ())


def missing(tag: str) -> list[str]:
    folder = HERE / tag
    return [f'.github/release/{tag}/{name}' for name in pictures(tag) + PARTS if not (folder / name).is_file()]


def block(page: str, name: str, keep: bool) -> str:
    """Keep or drop the lines between <!-- name --> and <!-- /name -->."""
    return re.sub(rf'<!-- {name} -->\n(.*?)<!-- /{name} -->\n', (lambda match: match.group(1)) if keep else '', page, flags=re.S)


def about(bytes_: int | None, what: str) -> str:
    return f'{what}, about {round(bytes_ / 1e6)} MB' if bytes_ else what


def render(tag: str, ref: str, repo: str, appimage_bytes: int | None = None, zip_bytes: int | None = None) -> str:
    version, windows = version_of(tag), has_windows(tag)
    if absent := missing(tag):
        raise SystemExit('Missing for the release notes: ' + ', '.join(absent))
    page = (HERE / 'notes.md').read_text()
    page = block(page, 'windows', windows)
    page = block(page, 'linux-only', not windows)
    page = block(page, 'updating', version > FIRST_LAUNCHER)
    return Template(page).substitute(
        version=tag[1:], repo=repo,
        size=about(appimage_bytes, 'one file' if windows else 'One file'), zip_size=about(zip_bytes, 'one .zip'),
        platforms='Linux and Windows' if windows else 'Linux',
        windows_notices=', plus `NOTICES-windows-runtime.txt` in the Windows one' if windows else '',
        platforms_bold='**Linux** or **Windows 11**' if windows else '**Linux**',
        media=f'https://raw.githubusercontent.com/{repo}/{ref}/.github/release/{tag}',
        download=f'https://github.com/{repo}/releases/download/{tag}',
        new=(HERE / tag / 'new.md').read_text().strip(),
        tested=(HERE / tag / 'tested.md').read_text().strip())


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('tag', help='The release tag, such as v0.2.1.')
    parser.add_argument('--ref', help='Commit that holds the pictures (default: the tag).')
    parser.add_argument('--appimage-bytes', type=int, help="The AppImage's size, shown next to the download button.")
    parser.add_argument('--zip-bytes', type=int, help="The Windows .zip's size, from the first version with one.")
    parser.add_argument('--repo', default=os.environ.get('GITHUB_REPOSITORY', 'NahuelGenchi/kirbys-dead-land'))
    args = parser.parse_args()
    sys.stdout.write(render(args.tag, args.ref or args.tag, args.repo, args.appimage_bytes, args.zip_bytes))


if __name__ == '__main__':
    main()
