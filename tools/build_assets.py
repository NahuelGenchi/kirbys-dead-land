#!/usr/bin/env python3
"""Losslessly convert decoded ROM poses into Dead Cells BATL/PAK resources.

This packs *poses*, not timed animation clips or a finished playable outfit.
No invented frame timing or base/cap composition is added. Frames with unresolved
palette parts are omitted and individually recorded in the build manifest.
Outputs are private ROM-derived data, kept outside the source repository.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import os
import shutil
import tempfile
from pathlib import Path
import struct
import zlib

from PIL import Image
from data_root import data_root

DEFAULT_DATA = data_root()
PAGE = 1024
FIELDS = ('index', 'x', 'y', 'w', 'h', 'ox', 'oy', 'ow', 'oh')
BUILDER = 'kirby-rom-pose-assets-v1'
STATUS = 'pose assets only; no gameplay or timing implementation'


def atlas_bytes(rows):
    def string(value):
        raw = value.encode('utf-8')
        if len(raw) >= 65536:
            raise ValueError('Atlas string too long')
        return (bytes([len(raw)]) if len(raw) < 255 else b'\xff' + struct.pack('<H', len(raw))) + raw
    out = bytearray(b'BATL')
    for page in dict.fromkeys(row['page'] for row in rows):
        out += string(page)
        for row in rows:
            if row['page'] == page:
                out += string(row['name']) + struct.pack('<9H', *(row[key] for key in FIELDS))
        out += b'\0'
    return bytes(out + b'\0')


def read_atlas(data):
    stream = io.BytesIO(data)
    if stream.read(4) != b'BATL':
        raise ValueError('Invalid atlas')
    def string():
        n = stream.read(1)
        if not n:
            raise ValueError('Truncated atlas')
        length = n[0]
        if length == 255:
            length = struct.unpack('<H', stream.read(2))[0]
        return stream.read(length).decode('utf-8')
    rows = []
    while page := string():
        while name := string():
            rows.append(dict(zip(FIELDS, struct.unpack('<9H', stream.read(18))), page=page, name=name))
    if stream.read(1):
        raise ValueError('Trailing atlas data')
    return rows


def write_pak(asset_dir, destination):
    """Write an unstamped, mod-local version-0 PAK as specified by DCCM's writer."""
    tree = {}
    payload = bytearray()
    checksums = {}
    for path in sorted(asset_dir.rglob('*')):
        if not path.is_file():
            continue
        name = path.relative_to(asset_dir).as_posix()
        node = tree
        for part in name.split('/')[:-1]:
            node = node.setdefault(part, {})
        data = path.read_bytes()
        node[path.name] = (len(payload), len(data), zlib.adler32(data))
        payload += data
        checksums[name] = hashlib.sha256(data).hexdigest()
    def entry(name, node):
        raw = name.encode('utf-8')
        if len(raw) > 255:
            raise ValueError('PAK name too long')
        if isinstance(node, dict):
            return bytes([len(raw)]) + raw + b'\1' + struct.pack('<I', len(node)) + b''.join(entry(k, v) for k, v in node.items())
        return bytes([len(raw)]) + raw + b'\0' + struct.pack('<III', *node)
    directory = entry('', tree)
    header = b'PAK\0' + struct.pack('<II', 12 + len(directory) + 4, len(payload))
    temporary = destination.with_suffix('.pak.tmp')
    temporary.write_bytes(header + directory + b'DATA' + payload)
    temporary.replace(destination)
    return checksums


def pack_bank(folder, out):
    manifest = json.loads((folder / 'manifest.json').read_text())
    ready, omitted = [], []
    colors = set()
    frames = []
    for group_index, group in enumerate(manifest['groups']):
        for frame in group['frames']:
            frames.append((f'g{group_index}_pose_{frame["index"]:04d}', frame))
            if 'left' in frame:
                frames.append((f'g{group_index}_pose_{frame["index"]:04d}_left', dict(frame['left'], index=frame['index'])))
    for frame in manifest.get('composed_frames', []):
        frames.append((f'composed_pose_{frame["index"]:04d}', frame))
        frames.append((f'composed_pose_{frame["index"]:04d}_left',
                       dict(frame, file=frame['left_file'], origin=frame['left_origin'])))
    for key, frame in frames:
            if frame.get('unresolved_parts'):
                omitted.append({'name': key, 'reason': 'unresolved ROM palette', 'parts': frame['unresolved_parts']})
                continue
            im = Image.open(folder / frame['file']).convert('RGBA')
            box = im.getbbox()
            if box is None:
                omitted.append({'name': key, 'reason': 'empty graphics pose'})
                continue
            im = im.crop(box)
            origin = [frame['origin'][0] + box[0], frame['origin'][1] + box[1]]
            colors.update(c for c in im.get_flattened_data() if c[3])
            ready.append((key, frame, im, origin))
    if not ready:
        return {'bank': folder.name, 'packed': 0, 'omitted': omitted}
    colors = sorted(colors)
    if len(colors) > 1024:
        raise ValueError(f'{folder.name}: more than 1024 palette colors')
    lookup = {color: i for i, color in enumerate(colors)}
    palette = Image.new('RGBA', (256, 4))
    palette.putdata(colors + [(0, 0, 0, 0)] * (1024 - len(colors)))
    stem = f'kirby_rom_{folder.name}'
    palette.save(out / f'{stem}_default_s.png')
    pages = [Image.new('RGBA', (PAGE, PAGE))]
    rows, provenance = [], []
    x = y = 1
    shelf = 0
    for key, frame, im, origin in ready:
        w, h = im.size
        if w + 2 > PAGE or h + 2 > PAGE:
            raise ValueError(f'Pose exceeds atlas page: {key}')
        if x + w + 1 > PAGE:
            x, y, shelf = 1, y + shelf + 2, 0
        if y + h + 1 > PAGE:
            pages.append(Image.new('RGBA', (PAGE, PAGE)))
            x = y = 1
            shelf = 0
        pixels = list(im.get_flattened_data())
        encoded = Image.new('RGBA', im.size)
        encoded.putdata([(lookup[p] % 256, (lookup[p] // 256) * 64 + 32, 0, p[3]) if p[3] else (0, 0, 0, 0) for p in pixels])
        # Round-trip the palette encoding before writing; zero-alpha RGB is irrelevant.
        decoded = [colors[p[0] + (p[1] // 64) * 256] if p[3] else (0, 0, 0, 0) for p in encoded.get_flattened_data()]
        if any(a != b for a, b in zip(pixels, decoded) if a[3]):
            raise ValueError(f'Palette round-trip failed: {key}')
        pages[-1].paste(encoded, (x, y))
        half_w = max(16, math.ceil(max(abs(origin[0]), abs(origin[0] + w)) / 16) * 16)
        half_h = max(16, math.ceil(max(abs(origin[1]), abs(origin[1] + h)) / 16) * 16)
        row = dict(name=key, index=frame['index'], page=f'{stem}{len(pages)-1}.png',
                   x=x, y=y, w=w, h=h, ox=half_w + origin[0], oy=half_h + origin[1], ow=half_w * 2, oh=half_h * 2)
        rows.append(row)
        provenance.append({'name': key, 'rom_file': manifest['source'], 'pose_pointer': frame.get('source_pointer'),
                           'origin': origin, 'source_frame': frame['file'], 'duration': None})
        x += w + 2
        shelf = max(shelf, h)
    for i, page in enumerate(pages):
        page.save(out / f'{stem}{i}.png')
        Image.new('RGBA', page.size, (128, 128, 255, 255)).save(out / f'{stem}{i}_n.png')
    data = atlas_bytes(rows)
    if read_atlas(data) != rows:
        raise ValueError('BATL round-trip failed')
    (out / f'{stem}.atlas').write_bytes(data)
    (out / f'{stem}_poses.json').write_text(json.dumps({'timing': 'not decoded', 'poses': provenance}, indent=2))
    return {'bank': folder.name, 'packed': len(rows), 'colors': len(colors), 'pages': len(pages), 'omitted': omitted}


def validate_output(output):
    """Only replace an empty directory or a complete, unmodified builder output."""
    if not output.exists():
        return
    if not output.is_dir():
        raise ValueError('Output exists and is not a directory')
    names={p.name for p in output.iterdir()}
    if not names:
        return
    if names != {'assets','res.pak','build-manifest.json'}:
        raise ValueError('Refusing to overwrite an output containing unrecognized files')
    report=json.loads((output/'build-manifest.json').read_text())
    if report.get('builder',BUILDER) != BUILDER or report.get('status') != STATUS:
        raise ValueError('Output manifest was not generated by this builder')
    if not isinstance(report.get('asset_sha256'),dict) or not report.get('asset_sha256'):
        raise ValueError('Output manifest has no verified asset ownership list')
    paths=list((output/'assets').rglob('*'))
    if any(p.is_symlink() for p in [output/'assets',output/'res.pak',output/'build-manifest.json',*paths]):
        raise ValueError('Refusing to replace output containing symbolic links')
    actual={p.relative_to(output/'assets').as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
            for p in paths if p.is_file()}
    expected_dirs={'atlas'} | {parent.as_posix() for name in actual for parent in Path(name).parents if parent != Path('.')}
    actual_dirs={p.relative_to(output/'assets').as_posix() for p in paths if p.is_dir()}
    if actual != report['asset_sha256'] or actual_dirs != expected_dirs:
        raise ValueError('Existing generated assets were changed or contain unlisted files/directories; use another output directory')
    if hashlib.sha256((output/'res.pak').read_bytes()).hexdigest() != report.get('pak_sha256'):
        raise ValueError('Existing res.pak was changed; use another output directory')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--decoded', type=Path, default=DEFAULT_DATA / 'rom/decoded')
    parser.add_argument('--output', type=Path, default=DEFAULT_DATA / 'assets-build')
    args = parser.parse_args()
    output = args.output.expanduser().resolve()
    decoded = args.decoded.expanduser().resolve()
    project = Path(__file__).resolve().parents[1]
    if output == project or project in output.parents:
        raise SystemExit('ROM-derived outputs must stay outside the source project.')
    if output == decoded or output in decoded.parents or decoded in output.parents:
        raise SystemExit('Build output and extracted ROM directory must not contain each other.')
    if (output / 'hlboot.dat').exists():
        raise SystemExit('Refusing to build into a Dead Cells installation.')
    validate_output(output)
    folders=[folder for folder in sorted(decoded.iterdir()) if (folder/'manifest.json').is_file()]
    if not folders:
        raise SystemExit('No decoded ROM banks found')
    output.parent.mkdir(parents=True,exist_ok=True)
    lock=output.parent/f'.{output.name}.build.lock'
    descriptor=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
    os.close(descriptor)
    staging=None
    previous=None
    try:
        staging=Path(tempfile.mkdtemp(prefix=f'.{output.name}.build-',dir=output.parent))
        atlas_dir=staging/'assets/atlas'; atlas_dir.mkdir(parents=True)
        banks=[pack_bank(folder,atlas_dir) for folder in folders]
        report={'builder':BUILDER,'status':STATUS,'banks':banks,
                'packed_poses':sum(bank['packed'] for bank in banks),
                'omitted_poses':sum(len(bank['omitted']) for bank in banks),
                'composition':'matching-index composed ability poses included where decoded; movement/cap state selection not reconstructed',
                'normal_maps':'flat host-renderer normals, not extracted ROM graphics'}
        (staging/'assets/kirby-assets.json').write_text(json.dumps(report,indent=2))
        report['asset_sha256']=write_pak(staging/'assets',staging/'res.pak')
        report['pak_sha256']=hashlib.sha256((staging/'res.pak').read_bytes()).hexdigest()
        (staging/'build-manifest.json').write_text(json.dumps(report,indent=2))
        # Catch edits made while the fresh output was being built.
        validate_output(output)
        if output.exists():
            previous=Path(tempfile.mkdtemp(prefix=f'.{output.name}.previous-',dir=output.parent))
            previous.rmdir()
            output.rename(previous)
        try:
            staging.rename(output)
        except BaseException:
            if previous is not None:
                previous.rename(output); previous=None
            raise
        if previous is not None:
            shutil.rmtree(previous); previous=None
        print(json.dumps({'packed_poses':report['packed_poses'],'omitted_poses':report['omitted_poses'],
                          'pak':str(output/'res.pak'),'status':report['status']}))
    finally:
        if staging is not None and staging.exists():
            shutil.rmtree(staging)
        lock.unlink()


if __name__ == '__main__':
    main()
