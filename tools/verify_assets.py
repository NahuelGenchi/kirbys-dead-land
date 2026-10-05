#!/usr/bin/env python3
"""Verify complete atlas contents against original decoded PNGs and packaged bytes."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import zlib

from PIL import Image, ImageChops
from build_assets import DEFAULT_DATA, read_atlas


def require(condition, context):
    if not condition:
        raise ValueError(context)


def read_pak_hashes(data):
    """Independent bounded reader for the unstamped version-0 mod PAK."""
    require(len(data) >= 22 and data[:4] == b'PAK\0', 'Invalid PAK header')
    payload_start, payload_size = struct.unpack_from('<II', data, 4)
    require(payload_start >= 16 and payload_start + payload_size == len(data), 'Invalid PAK payload bounds')
    stream = io.BytesIO(data[12:payload_start-4])
    entries = {}
    spans = []
    def read(size):
        value = stream.read(size)
        require(len(value) == size, 'Truncated PAK directory')
        return value
    def entry(prefix, root=False):
        length = read(1)[0]
        name = read(length).decode('utf-8')
        require((root and name == '') or (name and name not in ('.','..') and '/' not in name and '\\' not in name), 'Unsafe PAK entry name')
        path = prefix + name
        kind = read(1)[0]
        if kind == 1:
            count = struct.unpack('<I', read(4))[0]
            require(count <= len(data), 'Invalid PAK child count')
            for _ in range(count):
                entry(path+'/' if path else '')
        else:
            require(kind == 0 and not root, 'Invalid PAK node type')
            start, size, checksum = struct.unpack('<III', read(12))
            require(start+size <= payload_size and path not in entries, f'Invalid PAK entry: {path}')
            content = data[payload_start+start:payload_start+start+size]
            require(zlib.adler32(content) == checksum, f'PAK Adler checksum mismatch: {path}')
            entries[path] = hashlib.sha256(content).hexdigest()
            spans.append((start,start+size))
    entry('',root=True)
    require(not stream.read(1) and data[payload_start-4:payload_start] == b'DATA', 'Invalid PAK directory terminator')
    end=0
    for start,finish in sorted(spans):
        require(start == end, 'PAK payload overlaps or contains unlisted bytes')
        end=finish
    require(end == payload_size, 'PAK payload has unlisted trailing bytes')
    return entries


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--decoded',type=Path,default=DEFAULT_DATA/'rom/decoded')
    parser.add_argument('--build',type=Path,default=DEFAULT_DATA/'assets-build')
    args=parser.parse_args()
    report=json.loads((args.build/'build-manifest.json').read_text())
    assets=args.build/'assets'
    paths=list(assets.rglob('*'))
    require(not any(p.is_symlink() for p in paths), 'Built assets contain symbolic links')
    actual={p.relative_to(assets).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in paths if p.is_file()}
    require(actual == report['asset_sha256'], 'Built asset file list or hashes differ from manifest')
    pak=(args.build/'res.pak').read_bytes()
    require(hashlib.sha256(pak).hexdigest() == report['pak_sha256'], 'PAK SHA-256 differs from manifest')
    require(read_pak_hashes(pak) == actual, 'PAK entries do not exactly match built assets')
    banks=[p.name for p in args.decoded.iterdir() if (p/'manifest.json').is_file()]
    require(sorted(banks) == sorted(b['bank'] for b in report['banks']), 'Build bank set differs from current decoded input')
    checked=0
    omitted_count=0
    expected_files={'kirby-assets.json'}
    for bank in report['banks']:
        source=args.decoded/bank['bank']
        manifest=json.loads((source/'manifest.json').read_text())
        originals={f'g{i}_pose_{frame["index"]:04d}':frame
                   for i,group in enumerate(manifest['groups']) for frame in group['frames']}
        originals.update({f'g{i}_pose_{frame["index"]:04d}_left':frame['left']
                          for i,group in enumerate(manifest['groups']) for frame in group['frames'] if 'left' in frame})
        for frame in manifest.get('composed_frames',[]):
            originals[f'composed_pose_{frame["index"]:04d}']=frame
            originals[f'composed_pose_{frame["index"]:04d}_left']=dict(frame,file=frame['left_file'],origin=frame['left_origin'])
        expected=set()
        omitted={}
        for name,frame in originals.items():
            if frame.get('unresolved_parts'):
                omitted[name]='unresolved ROM palette'
            else:
                with Image.open(source/frame['file']) as image:
                    if image.convert('RGBA').getbbox() is None:
                        omitted[name]='empty graphics pose'
                    else:
                        expected.add(name)
        reported_omissions={entry['name']:entry['reason'] for entry in bank['omitted']}
        require(len(reported_omissions) == len(bank['omitted']) and reported_omissions == omitted,
                f'{bank["bank"]}: omitted pose set/reasons mismatch')
        omitted_count+=len(omitted)
        require(len(expected) == bank['packed'], f'{bank["bank"]}: complete pose count mismatch')
        if not expected:
            continue
        folder=assets/'atlas'
        stem='kirby_rom_'+bank['bank']
        rows=read_atlas((folder/f'{stem}.atlas').read_bytes())
        require(len(rows) == len(expected) and {row['name'] for row in rows} == expected,
                f'{bank["bank"]}: atlas pose set mismatch')
        expected_files.update({f'atlas/{stem}.atlas',f'atlas/{stem}_default_s.png',f'atlas/{stem}_poses.json'})
        palette=list(Image.open(folder/f'{stem}_default_s.png').convert('RGBA').get_flattened_data())
        pages={}
        for row in rows:
            frame=originals[row['name']]
            name=f'{bank["bank"]}/{row["name"]}'
            require(not frame.get('unresolved_parts'), f'{name}: unresolved pose was packed')
            if row['page'] not in pages:
                pages[row['page']]=Image.open(folder/row['page']).convert('RGBA')
                normal=Path(row['page']).stem+'_n.png'
                expected_files.update({f'atlas/{row["page"]}',f'atlas/{normal}'})
                require(Image.open(folder/normal).size == pages[row['page']].size, f'{name}: normal-map size mismatch')
            page=pages[row['page']]
            require(row['x']+row['w'] <= page.width and row['y']+row['h'] <= page.height, f'{name}: atlas bounds')
            tile=page.crop((row['x'],row['y'],row['x']+row['w'],row['y']+row['h']))
            rgba=Image.new('RGBA',tile.size)
            rgba.putdata([palette[p[0]+p[1]//64*256] if p[3] else (0,0,0,0) for p in tile.get_flattened_data()])
            original=Image.open(source/frame['file']).convert('RGBA')
            box=original.getbbox()
            require(box is not None, f'{name}: empty pose was packed')
            image=original.crop(box)
            require(rgba.size == image.size, f'{name}: visible size mismatch')
            require(rgba.getchannel('A').tobytes() == image.getchannel('A').tobytes(), f'{name}: alpha mismatch')
            black_a=Image.new('RGB',rgba.size); black_a.paste(rgba,mask=rgba.getchannel('A'))
            black_b=Image.new('RGB',image.size); black_b.paste(image,mask=image.getchannel('A'))
            require(ImageChops.difference(black_a,black_b).getbbox() is None, f'{name}: visible pixels mismatch')
            require(row['ox']-row['ow']//2 == frame['origin'][0]+box[0], f'{name}: X origin moved')
            require(row['oy']-row['oh']//2 == frame['origin'][1]+box[1], f'{name}: Y origin moved')
            checked+=1
        require(len(pages) == bank['pages'], f'{bank["bank"]}: atlas page count mismatch')
    require(set(actual) == expected_files, 'Stale or unlisted asset files were packaged')
    require(checked == report['packed_poses'] and omitted_count == report['omitted_poses'], 'Total pose count mismatch')
    print(f'PASS: {checked} atlas poses preserve all visible source pixels, alpha, and original pivots.')
    print('PASS: PAK file list, payload SHA-256, Adler checksums, and exhaustive pose/omission sets match.')
    print(f'{omitted_count} incomplete/empty poses were explicitly omitted. Animation timing and gameplay not verified.')


if __name__=='__main__':
    main()
