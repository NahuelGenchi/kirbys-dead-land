#!/usr/bin/env python3
"""Extract original KSSU NitroFS data and decode its custom PD sprite archives.

Requires Pillow. Output is private, local, derived game data; never redistribute it.
This decodes graphic poses, NOT animation state machines or gameplay physics.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import struct
from pathlib import Path
from PIL import Image, ImageDraw
from data_root import data_root

DEFAULT_OUT = data_root() / 'rom'
SIZES = (((8,8),(16,16),(32,32),(64,64)),
         ((16,8),(32,8),(32,16),(64,32)),
         ((8,16),(8,32),(16,32),(32,64)))


def u32(data, off):
    return struct.unpack_from('<I', data, off)[0]


def nitrofs(data):
    if len(data) < 0x50:
        raise ValueError('ROM header is truncated')
    fnt, fnt_size, fat, fat_size = struct.unpack_from('<4I', data, 0x40)
    if fnt_size < 8 or fat_size % 8 or fnt + fnt_size > len(data) or fat + fat_size > len(data):
        raise ValueError('ROM has an invalid NitroFS table')
    directory_count = struct.unpack_from('<H', data, fnt+6)[0]
    if not 1 <= directory_count <= 4096 or directory_count*8 > fnt_size:
        raise ValueError('Invalid NitroFS directory count')
    found = []
    visited = set()
    def walk(directory, prefix):
        if not 0xf000 <= directory < 0xf000+directory_count:
            raise ValueError('Invalid NitroFS directory ID')
        if directory in visited:
            raise ValueError('Cyclic NitroFS directory')
        visited.add(directory)
        off, file_id, _ = struct.unpack_from('<IHH', data, fnt+(directory & 4095)*8)
        if not directory_count*8 <= off < fnt_size:
            raise ValueError('NitroFS subtable outside filename table')
        off += fnt
        while True:
            if off >= fnt+fnt_size:
                raise ValueError('Unterminated NitroFS subtable')
            length = data[off]; off += 1
            if not length:
                break
            size = length & 127
            if not size or off+size+(2 if length & 128 else 0) > fnt+fnt_size:
                raise ValueError('Invalid NitroFS name length')
            name = data[off:off+size].decode('ascii'); off += size
            if any(c in name for c in ('/', chr(92), chr(0))) or name in ('.', '..'):
                raise ValueError('Unsafe NitroFS name')
            if length & 128:
                child = struct.unpack_from('<H', data, off)[0]; off += 2
                walk(child, prefix+name+'/')
            else:
                if (file_id+1)*8 > fat_size:
                    raise ValueError('NitroFS file ID exceeds allocation table')
                start, end = struct.unpack_from('<II', data, fat+file_id*8)
                if not 0 <= start <= end <= len(data):
                    raise ValueError('Invalid NitroFS file bounds')
                found.append({'id': file_id, 'path': prefix+name, 'offset': start, 'size': end-start})
                file_id += 1
    walk(0xf000, '')
    return found


def rgb555(data):
    colors = []
    for i, (c,) in enumerate(struct.iter_unpack('<H', data)):
        r, g, b = c & 31, (c >> 5) & 31, (c >> 10) & 31
        colors.append(((r << 3) | (r >> 2), (g << 3) | (g >> 2),
                       (b << 3) | (b >> 2), 0 if i % 16 == 0 else 255))
    return colors


def tile_image(graphics, offset, width, height, palette):
    pixels = []
    for y in range(height):
        for x in range(width):
            k = offset + ((y//8)*(width//8) + x//8)*32 + (y % 8)*4 + (x % 8)//2
            if k >= len(graphics):
                raise ValueError(f'OAM tile exceeds graphics buffer: {k}/{len(graphics)}')
            index = (graphics[k] >> (4*(x % 2))) & 15
            pixels.append(palette[index])
    image = Image.new('RGBA', (width, height)); image.putdata(pixels)
    return image


def decode_frame(data, frame_pointer, palette_pointer, palette_offset, palette_count, facing="right"):
    oam, tiles, part_count, block_count, p0, p1 = struct.unpack_from('<IIBBBB', data, frame_pointer)
    pointers = struct.unpack_from('<' + str(block_count) + 'I', data, tiles)
    graphics = b''.join(data[p:p+256] for p in pointers)
    parts, unresolved = [], []
    for i in range(part_count):
        attr0, right, left, attr3 = struct.unpack_from('<4H', data, oam+i*8)
        attr1 = right if facing == "right" else left
        shape, size = attr0 >> 14, attr1 >> 14
        if shape == 3:
            raise ValueError('Prohibited OAM shape')
        width, height = SIZES[shape][size]
        x = attr1 & 511; x = x-512 if x >= 256 else x
        y = attr0 & 255; y = y-256 if y >= 128 else y
        bank = attr3 >> 12
        palette_index = (p0, p1)[bank] if bank in (0, 1) else None
        part = {'index': i, 'x': x, 'y': y, 'width': width, 'height': height,
                'attributes': [attr0,right,left,attr3], 'palette_bank': bank,
                'palette_index': palette_index, 'tile_block': attr3 & 1023}
        if palette_index is None or palette_index == 255:
            unresolved.append(part)
            continue
        start = palette_pointer + palette_index*32
        if not palette_offset <= start < palette_offset+palette_count*32:
            raise ValueError(f'Palette outside archive: {start:#x}')
        palette = rgb555(data[start:start+32])
        sprite = tile_image(graphics, (attr3 & 1023)*256, width, height, palette)
        if attr1 & 0x1000:
            sprite = sprite.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        if attr1 & 0x2000:
            sprite = sprite.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        parts.append((part, sprite))
    # Lower OAM indices have priority; render those last.
    allparts = [p for p,_ in parts] + unresolved
    x0 = min([0]+[p['x'] for p in allparts]); y0 = min([0]+[p['y'] for p in allparts])
    x1 = max([1]+[p['x']+p['width'] for p in allparts]); y1 = max([1]+[p['y']+p['height'] for p in allparts])
    image = Image.new('RGBA', (x1-x0, y1-y0))
    for part, sprite in reversed(parts):
        image.alpha_composite(sprite, (part['x']-x0, part['y']-y0))
    return image, {'source_pointer': frame_pointer, 'origin': [x0,y0], 'pivot': [-x0,-y0],
                   'palettes': [p0,p1], 'parts': [p for p,_ in parts],
                   'unresolved_parts': unresolved, 'graphic_block_pointers': list(pointers)}


def compose_frames(target, frames):
    layers=[(frame,Image.open(target/frame['file']).convert('RGBA')) for frame in frames]
    x0=min(f['origin'][0] for f,_ in layers); y0=min(f['origin'][1] for f,_ in layers)
    x1=max(f['origin'][0]+im.width for f,im in layers); y1=max(f['origin'][1]+im.height for f,im in layers)
    image=Image.new('RGBA',(x1-x0,y1-y0))
    for frame,layer in layers:
        x,y=frame['origin']; image.alpha_composite(layer,(x-x0,y-y0))
    return image,[x0,y0]


def decode_archive(data, target, source):
    target.mkdir(parents=True, exist_ok=True)
    group_count, groups = struct.unpack_from('<2I', data)
    pal_offset, pal_count = struct.unpack_from('<2I', data, 0x10)
    frame_offset, unique_frames = struct.unpack_from('<2I', data, 0x28)
    palette_image = Image.new('RGBA', (16*8, max(1,pal_count)*8))
    drawer = ImageDraw.Draw(palette_image)
    colors = rgb555(data[pal_offset:pal_offset+pal_count*32])
    for i, color in enumerate(colors):
        drawer.rectangle(((i%16)*8,(i//16)*8,(i%16)*8+7,(i//16)*8+7),fill=color[:3]+(255,))
    palette_image.save(target/'palettes.png')
    manifest = {'source': source, 'format': 'KSSU PD .b / Nintendo DS 4bpp OAM',
                'palette_offset': pal_offset, 'palette_count': pal_count,
                'unique_pose_count': unique_frames, 'groups_in_archive': group_count,
                'animation_timing': 'not decoded; indices are graphic poses, not timed animation sequences',
                'groups': [], 'unresolved_palette_parts': 0}
    for group_index in range(group_count):
        count, flags, pointers, palette = struct.unpack_from('<4I', data, groups+group_index*16)
        group = {'index': group_index, 'flags': flags, 'palette_pointer': palette, 'frames': []}
        prefix = '' if group_index == 0 else f'groups/{group_index}/'
        group_root = target/prefix
        paths = group_root/'frames'; paths.mkdir(parents=True, exist_ok=True)
        contact = Image.new('RGB', (16*80, max(1,(count+15)//16)*80), (41,46,54))
        draw = ImageDraw.Draw(contact)
        for i in range(count):
            pointer = u32(data,pointers+i*4)
            image, frame = decode_frame(data,pointer,palette,pal_offset,pal_count)
            frame['index'] = i; frame['file'] = f'{prefix}frames/{i:04d}.png'
            image.save(target/frame['file'])
            left_image, left_frame = decode_frame(data,pointer,palette,pal_offset,pal_count,facing='left')
            left_file=f'{prefix}frames-left/{i:04d}.png'
            (target/left_file).parent.mkdir(parents=True,exist_ok=True)
            left_image.save(target/left_file)
            left_frame['file']=left_file; frame['left']=left_frame
            frame['size'] = list(image.size)
            x,y = frame['origin']
            cx,cy=i%16*80+40+x,i//16*80+44+y
            contact.paste(image,(cx,cy),image)
            draw.text((i%16*80+3,i//16*80+3),str(i),fill='white')
            group['frames'].append(frame)
            manifest['unresolved_palette_parts'] += len(frame['unresolved_parts'])
        extent=max([64]+[max(abs(f['origin'][0]),abs(f['origin'][1]),
                   abs(f['origin'][0]+f['size'][0]),abs(f['origin'][1]+f['size'][1])) for f in group['frames']])
        cell_size=128
        while cell_size < extent*2:
            cell_size*=2
        pivot=cell_size//2
        atlas=Image.new('RGBA',(16*cell_size,max(1,(count+15)//16)*cell_size))
        for frame in group['frames']:
            i=frame['index']; x,y=frame['origin']
            atlas.alpha_composite(Image.open(target/frame['file']),(i%16*cell_size+pivot+x,i//16*cell_size+pivot+y))
            frame['atlas']=[i%16*cell_size,i//16*cell_size,cell_size,cell_size]
            frame['atlas_pivot']=[pivot,pivot]
        atlas.save(group_root/'atlas.png'); contact.save(group_root/'contact.png')
        manifest['groups'].append(group)
    if source.startswith('rom/pd/a') and group_count == 4:
        body, cap = manifest['groups'][0]['frames'], manifest['groups'][2]['frames']
        if body and len(body) == len(cap):
            composite_root = target/'composed'; composite_root.mkdir(exist_ok=True)
            manifest['composed_frames'] = []
            contact = Image.new('RGB', (16*96, ((len(body)+15)//16)*96), (41,46,54))
            draw = ImageDraw.Draw(contact)
            for i, (body_frame, cap_frame) in enumerate(zip(body,cap)):
                image,origin=compose_frames(target,(body_frame,cap_frame))
                filename=f'composed/{i:04d}.png'; image.save(target/filename)
                left_image,left_origin=compose_frames(target,(body_frame['left'],cap_frame['left']))
                left_filename=f'composed-left/{i:04d}.png'
                (target/left_filename).parent.mkdir(parents=True,exist_ok=True)
                left_image.save(target/left_filename)
                missing=body_frame['unresolved_parts']+cap_frame['unresolved_parts']
                manifest['composed_frames'].append({'index':i,'file':filename,'origin':origin,
                    'pivot':[-origin[0],-origin[1]],'size':list(image.size),'source_groups':[0,2],'unresolved_parts':missing,
                    'left_file':left_filename,'left_origin':left_origin,'left_pivot':[-left_origin[0],-left_origin[1]],
                    'composition':'matching-index body and cap layers at original OAM origins'})
                contact.paste(image,(i%16*96+48+origin[0],i//16*96+48+origin[1]),image)
                draw.text((i%16*96+3,i//16*96+3),str(i),fill='white')
            contact.save(target/'composed-contact.png')
    (target/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    return {'source': source, 'directory': str(target), 'frames': sum(len(g['frames']) for g in manifest['groups']),
            'unresolved_palette_parts': manifest['unresolved_palette_parts']}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('rom',type=Path)
    parser.add_argument('--output',type=Path,default=DEFAULT_OUT)
    parser.add_argument('--archive',action='append',help='Only decode named archives, e.g. bkir or cswd')
    parser.add_argument('--extract-all',action='store_true',help='Extract all NitroFS files, including audio/data')
    args=parser.parse_args()
    data=args.rom.read_bytes()
    if data[12:16] != b'YKWE':
        raise SystemExit('This extractor expects Kirby Super Star Ultra USA (game code YKWE).')
    root=args.output.expanduser().resolve()
    project=Path(__file__).resolve().parent.parent
    if root == project or project in root.parents:
        raise SystemExit('Output must be outside the mod project: extracted game assets cannot be packaged.')
    root.mkdir(parents=True,exist_ok=True)
    entries=nitrofs(data)
    (root/'inventory.json').write_text(json.dumps(entries,indent=2)+'\n')
    results=[]
    if args.extract_all:
        system=root/'raw/system'; system.mkdir(parents=True,exist_ok=True)
        for cpu,off in [('arm9',0x20),('arm7',0x30)]:
            start,_,_,size=struct.unpack_from('<4I',data,off)
            if start+size > len(data):
                raise ValueError('Executable outside ROM')
            (system/f'{cpu}.bin').write_bytes(data[start:start+size])
        for cpu,header_offset in [('arm9',0x50),('arm7',0x58)]:
            table,size=struct.unpack_from('<2I',data,header_offset)
            if size%32 or table+size > len(data):
                raise ValueError('Invalid overlay table bounds')
            overlays=[]
            for off in range(table,table+size,32):
                idx,ram,ram_size,bss,init_start,init_end,file_id,packed=struct.unpack_from('<8I',data,off)
                overlays.append({'id':idx,'ram_address':ram,'ram_size':ram_size,'bss_size':bss,
                    'static_init_start':init_start,'static_init_end':init_end,'file_id':file_id,
                    'compressed_size':packed&0xffffff,'flags':packed>>24})
            (system/f'{cpu}-overlays.json').write_text(json.dumps(overlays,indent=2)+'\n')
        fat,fat_size=struct.unpack_from('<2I',data,0x48)
        named={e['id'] for e in entries}
        for file_id in range(fat_size//8):
            if file_id not in named:
                start,end=struct.unpack_from('<2I',data,fat+file_id*8)
                if not 0 <= start <= end <= len(data):
                    raise ValueError('Unnamed file outside ROM')
                (system/f'file_{file_id:04d}.bin').write_bytes(data[start:end])
    for entry in entries:
        is_pd=entry['path'].startswith('rom/pd/') and entry['path'].endswith('.b')
        if not is_pd and not args.extract_all:
            continue
        raw=data[entry['offset']:entry['offset']+entry['size']]
        file=root/'raw'/entry['path'];file.parent.mkdir(parents=True,exist_ok=True);file.write_bytes(raw)
        if is_pd and (not args.archive or Path(entry['path']).stem in args.archive):
            result=decode_archive(raw,root/'decoded'/Path(entry['path']).stem,entry['path'])
            results.append(result)
            print(f"{entry['path']}: {result['frames']} poses; {result['unresolved_palette_parts']} parts need shared palettes",flush=True)
    report={'rom':str(args.rom.resolve()),'title':data[:12].decode('ascii').rstrip('\0'),
            'game_code':data[12:16].decode('ascii'),'sha256':hashlib.sha256(data).hexdigest(),
            'named_files':len(entries),'archives':results,
            'limitations':['Shared fixed-palette banks (2+) are preserved in metadata but not rendered.',
                           'Animation timing, gameplay state machines and physics are not extracted.']}
    (root/'extraction.json').write_text(json.dumps(report,indent=2)+'\n')
    print(f'Report: {root}/extraction.json')

if __name__=='__main__':
    main()
