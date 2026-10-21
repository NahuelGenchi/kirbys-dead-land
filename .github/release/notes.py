#!/usr/bin/env python3
"""Write a release's notes: the one page every release uses (notes.md), filled in.

    python3 .github/release/notes.py v0.2.1 [--ref COMMIT] [--appimage-bytes N]

Each version has a folder next to this script, v<version>/, with its pictures
(make_media.py) and two short parts written for players: new.md (what is new or
fixed) and tested.md (what was tested). The pictures are linked at --ref, the
commit that holds them (default: the tag), so later commits cannot change a
published page. The release workflow runs this for every new release.
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


def version_of(tag: str) -> tuple[int, ...]:
    if not re.fullmatch(r'v\d+\.\d+\.\d+', tag):
        raise SystemExit(f'{tag}: expected a tag like v1.2.3')
    return tuple(int(part) for part in tag[1:].split('.'))


def missing(tag: str) -> list[str]:
    folder = HERE / tag
    return [f'.github/release/{tag}/{name}' for name in PICTURES + PARTS if not (folder / name).is_file()]


def render(tag: str, ref: str, repo: str, appimage_bytes: int | None = None) -> str:
    version = version_of(tag)
    if absent := missing(tag):
        raise SystemExit('Missing for the release notes: ' + ', '.join(absent))
    page = (HERE / 'notes.md').read_text()
    if version <= FIRST_LAUNCHER:
        page = re.sub(r'<!-- updating -->.*?<!-- /updating -->\n\n', '', page, flags=re.S)
    else:
        page = page.replace('<!-- updating -->\n', '').replace('<!-- /updating -->\n', '')
    size = f'One file, about {round(appimage_bytes / 1e6)} MB' if appimage_bytes else 'One file'
    return Template(page).substitute(
        version=tag[1:], repo=repo, size=size,
        media=f'https://raw.githubusercontent.com/{repo}/{ref}/.github/release/{tag}',
        download=f'https://github.com/{repo}/releases/download/{tag}',
        new=(HERE / tag / 'new.md').read_text().strip(),
        tested=(HERE / tag / 'tested.md').read_text().strip())


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('tag', help='The release tag, such as v0.2.1.')
    parser.add_argument('--ref', help='Commit that holds the pictures (default: the tag).')
    parser.add_argument('--appimage-bytes', type=int, help="The AppImage's size, shown next to the download button.")
    parser.add_argument('--repo', default=os.environ.get('GITHUB_REPOSITORY', 'NahuelGenchi/kirbys-dead-land'))
    args = parser.parse_args()
    sys.stdout.write(render(args.tag, args.ref or args.tag, args.repo, args.appimage_bytes))


if __name__ == '__main__':
    main()
