#!/usr/bin/env python3
"""Build the separate Kirby outfit from the user's ROM and installed game assets.

Writes only to a private output directory. The CDB changes add the outfit's two
rows plus one item row and one weapon row per Kirby ability item (#55); shared UI
sheets retain every existing pixel/entry. Live animation is supplied by
KirbySpriteRenderer from the running original ROM, not these neutral fallbacks.
Ability item icons come from tools/capture_ability_icons.py.
"""
from __future__ import annotations
import argparse
import copy
import hashlib
import io
import json
import math
import os
from pathlib import Path
import shutil
import struct
import tempfile
import zlib

import numpy as np
from PIL import Image

from build_assets import atlas_bytes, read_atlas, write_pak

ROOT = Path.home() / '.local/share/kirby-dead-cells'
MODEL = 'kirbySuperStarUltra'
OUTFIT = 'KirbySuperStarUltra'
BUILDER = 'kirby-original-runtime-outfit-v1'
ABILITY_PREFIX = 'KirbyAbility'
# Same IDs and order as KirbyNdsProvider.Abilities.
ABILITIES = ['Cutter', 'Mirror', 'Beam', 'Fighter', 'Suplex', 'YoYo', 'Wheel', 'Bomb', 'Ice', 'Fire', 'Plasma', 'Ninja',
             'Stone', 'Wing', 'Jet', 'Copy', 'Sword', 'Hammer', 'Parasol', 'Paint', 'Mike', 'Crash', 'Cook', 'Sleep']
# Host item separator 4; the Colorless affix is added when the runtime creates each item.
MELEE_GROUP = 4


class Pak:
    """Bounded read-only reader for native version 0/1 PAK archives."""
    def __init__(self, path):
        self.file = path.open('rb')
        self.length = path.stat().st_size
        if self.file.read(3) != b'PAK':
            raise ValueError('Not a Dead Cells PAK')
        version = self.file.read(1)[0]
        if version not in (0, 1):
            raise ValueError('Unsupported PAK version')
        self.header, self.size = struct.unpack('<II', self.file.read(8))
        if self.header > self.length or self.header < 16:
            raise ValueError('Invalid PAK header bounds')
        if version:
            self.file.read(64)
        self.entries = {}
        self._entry('', 0)
        if self.file.read(4) != b'DATA' or self.file.tell() != self.header:
            raise ValueError('Invalid PAK directory terminator')

    def _read(self, count):
        if self.file.tell() + count > self.header:
            raise ValueError('PAK directory exceeds header')
        data = self.file.read(count)
        if len(data) != count:
            raise ValueError('Truncated PAK directory')
        return data

    def _entry(self, parent, depth):
        if depth > 32:
            raise ValueError('PAK directory too deep')
        name = self._read(self._read(1)[0]).decode('utf-8')
        if any(c in name for c in '/\\\0') or name in ('.', '..'):
            raise ValueError('Unsafe PAK name')
        flags = self._read(1)[0]
        path = '/'.join(p for p in (parent, name) if p)
        if flags & 1:
            for _ in range(struct.unpack('<I', self._read(4))[0]):
                self._entry(path, depth + 1)
        else:
            offset = struct.unpack('<Q' if flags & 2 else '<I', self._read(8 if flags & 2 else 4))[0]
            size, crc = struct.unpack('<II', self._read(8))
            if not path or path in self.entries or offset + size > self.length - self.header:
                raise ValueError('Invalid PAK file bounds or duplicate name')
            self.entries[path] = offset, size, crc

    def read(self, name):
        offset, size, crc = self.entries[name]
        self.file.seek(self.header + offset)
        data = self.file.read(size)
        if len(data) != size or zlib.adler32(data) != crc:
            raise ValueError(f'PAK checksum failure: {name}')
        return data


def json_write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, separators=(',', ':')))


def image_read(pak, name):
    return Image.open(io.BytesIO(pak.read(name))).convert('RGBA')


def palette_encode(image, lookup):
    result = Image.new('RGBA', image.size)
    result.putdata([(lookup[p] % 256, (lookup[p] // 256) * 64 + 32, 0, p[3])
                    if p[3] else (0, 0, 0, 0) for p in image.get_flattened_data()])
    return result


def used_icons(value):
    found = set()
    if isinstance(value, dict):
        if value.get('file') == 'cardIcons.png' and value.get('size') == 24:
            found.add((value.get('x'), value.get('y')))
        for child in value.values():
            found |= used_icons(child)
    elif isinstance(value, list):
        for child in value:
            found |= used_icons(child)
    return found


def ability_items(db, icons, cells, icon_dir):
    """One colorless Melee item and one weapon row per original copy ability.

    The weapon row repeats the starting sword's, so the host's weapon object and
    weapon-data lookups work; KirbyMod never lets that weapon attack. Names and
    descriptions are overridden at runtime from AbilityDescriptions.
    """
    sheets = {s['name']: s for s in db['sheets']}
    if any(str(x.get('id', '')).startswith(ABILITY_PREFIX) for x in sheets['item']['lines']):
        raise ValueError('Source PAK already contains Kirby ability items; use an unmodified source')
    sword = next(x for x in sheets['weapon']['lines'] if x.get('item') == 'StartSword')
    items, weapons, placed = {}, {}, {}
    for name, cell in zip(ABILITIES, cells, strict=True):
        icon = Image.open(icon_dir / f'{name}.png').convert('RGBA')
        if icon.size != (24, 24):
            raise ValueError(f'Ability icon {name} is not 24x24')
        icons.alpha_composite(icon, (cell[0] * 24, cell[1] * 24))
        item_id = ABILITY_PREFIX + name
        items[item_id] = dict(id=item_id, name='Yo-Yo' if name == 'YoYo' else name,
                              gameplayDesc='Kirby copy ability.', icon=dict(file='cardIcons.png', size=24, x=cell[0], y=cell[1]),
                              tier1='Brutality', moneyCost=1500, cellCost=0, droppable=False, castCD=0, legendAffixes=[],
                              tags=[{'tag': 'NoCritical'}], synergy=[], commonProps={}, props={},
                              group=MELEE_GROUP, __separator_group_Name='Melee')
        weapons[item_id] = dict(copy.deepcopy(sword), item=item_id)
        placed[name] = list(cell)
    return items, weapons, placed


def build(pak, decoded, out, icon_dir):
    atlas = out / 'atlas'
    atlas.mkdir(parents=True)
    db = json.loads(pak.read('data.cdb'))
    sheets = {s['name']: s for s in db['sheets']}
    if any(x.get('id') == OUTFIT for x in sheets['item']['lines']):
        raise ValueError('Source PAK already contains Kirby; use an unmodified source')
    manifest = json.loads((decoded / 'bkir/manifest.json').read_text())
    frame = manifest['groups'][0]['frames'][184]
    if frame.get('unresolved_parts'):
        raise ValueError('Source neutral pose has unresolved graphics')
    image = Image.open(decoded / 'bkir' / frame['file']).convert('RGBA')
    box = image.getbbox()
    image = image.crop(box)
    origin = [frame['origin'][0] + box[0], frame['origin'][1] + box[1] - 14.5]
    pal = image_read(pak, 'atlas/beheaded_default_s.png')
    colors = list(pal.get_flattened_data())
    free = [i for i in range(864, 1024) if colors[i][3] == 0]
    added = sorted({p for p in image.get_flattened_data() if p[3]})
    if len(added) > len(free):
        raise ValueError('No unused palette slots for original Kirby colors')
    lookup = dict(zip(added, free))
    for color, index in lookup.items():
        colors[index] = color
    pal.putdata(colors)
    pal.save(atlas / f'{MODEL}_default_s.png')
    pal.save(atlas / f'{MODEL}Weapons_default_s.png')
    encoded = palette_encode(image, lookup)
    encoded.save(atlas / f'{MODEL}0.png')
    Image.new('RGBA', image.size, (128, 128, 255, 255)).save(atlas / f'{MODEL}0_n.png')
    source_rows = read_atlas(pak.read('atlas/beheaded.atlas'))
    rows = [dict(row, page=f'{MODEL}0.png', x=0, y=0, w=image.width, h=image.height,
                 ox=round(row['ow'] / 2 + origin[0]), oy=round(row['oh'] / 2 + origin[1]))
            for row in source_rows]
    (atlas / f'{MODEL}.atlas').write_bytes(atlas_bytes(rows))
    tracks = json.loads(pak.read('atlas/beheaded_tracks.json'))
    fallback_tracks = copy.deepcopy(tracks)
    group_rows = {}
    for row in source_rows:
        group_rows.setdefault(row['name'].rsplit('_', 1)[0], row)
    for group, bones in fallback_tracks.items():
        row = group_rows.get(group)
        if row is None:
            continue
        for name, positions in bones.items():
            offset = (8, -11) if 'Hand' in name or 'Prop2' in name else (0, -24 if name == 'headBone' else -14.5)
            for i in range(0, len(positions) - 2, 3):
                positions[i:i + 2] = [round(row['ow'] / 2 + offset[0]), round(row['oh'] / 2 + offset[1])]
    json_write(atlas / f'{MODEL}_tracks.json', fallback_tracks)
    previews = read_atlas(pak.read('atlas/heroSkins.atlas'))
    if any(r['name'].startswith(MODEL + '_') for r in previews):
        raise ValueError('Source already contains a Kirby preview')
    additions = [dict(r, name=r['name'].replace('beheaded_', MODEL + '_', 1),
                      page=f'{MODEL}0.png', x=0, y=0, w=image.width, h=image.height,
                      ox=round(r['ow'] / 2 + origin[0]), oy=round(r['oh'] / 2 + origin[1]))
                 for r in previews if r['name'].startswith('beheaded_')]
    (atlas / 'heroSkins.atlas').write_bytes(atlas_bytes(previews + additions))
    preview_tracks = json.loads(pak.read('atlas/heroSkins_tracks.json'))
    preview_tracks.update({k.replace('beheaded_', MODEL + '_', 1): copy.deepcopy(v)
                           for k, v in list(preview_tracks.items()) if k.startswith('beheaded_')})
    json_write(atlas / 'heroSkins_tracks.json', preview_tracks)
    # Item rendering uses the shared cardIcons texture, regardless of icon.file.
    icons = image_read(pak, 'cardIcons.png')
    occupied = used_icons(db)
    free = [(x, y) for y in reversed(range(icons.height // 24))
            for x in reversed(range(icons.width // 24)) if (x, y) not in occupied
            and icons.crop((x * 24, y * 24, x * 24 + 24, y * 24 + 24)).getbbox() is None]
    if len(free) < 1 + len(ABILITIES):
        raise ValueError('Not enough free icon cells')
    cell = free[0]
    icon = image.copy()
    icon.thumbnail((24, 24), Image.Resampling.NEAREST)
    icons.alpha_composite(icon, (cell[0] * 24 + (24 - icon.width) // 2, cell[1] * 24 + 24 - icon.height))
    ability_rows, weapon_rows, ability_cells = ability_items(db, icons, free[1:1 + len(ABILITIES)], icon_dir)
    icons.save(out / 'cardIcons.png')
    item = dict(id=OUTFIT, name='Kirby', gameplayDesc='Kirby Super Star Ultra — original Nintendo DS gameplay.',
                icon=dict(file='cardIcons.png', size=24, x=cell[0], y=cell[1]), moneyCost=0, cellCost=0,
                droppable=False, castCD=0, legendAffixes=[], tags=[], synergy=[], commonProps={'revealedAtStart': True},
                props={}, group=13, __separator_group_Name='Skin')
    skin = dict(item=OUTFIT, model=MODEL, colorMap='default', consoleCmdId='kirby', head=False,
                onlyDefaultHead=True, glowData=[], scarfBlendMode=0, scarfs=[], incompatibleHeads=[],
                group=1, __separator_group_Name='PlayableCharacters')
    json_write(out / f'data.cdb_/item/{OUTFIT}.json', item)
    json_write(out / f'data.cdb_/skin/{OUTFIT}.json', skin)
    for item_id, row in ability_rows.items():
        json_write(out / f'data.cdb_/item/{item_id}.json', row)
    for item_id, row in weapon_rows.items():
        json_write(out / f'data.cdb_/weapon/{item_id}.json', row)
    equipment = build_equipment(pak, source_rows, tracks, atlas)
    # Ensure every native name remains available during host animation/equipment handling.
    if {(r['name'], r['index']) for r in rows} != {(r['name'], r['index']) for r in source_rows}:
        raise ValueError('Fallback host animation coverage differs')
    return dict(outfit=OUTFIT, model=MODEL, fallback_pose='bkir group 0 pose 184',
                fallback_origin=origin, fallback_frames=len(rows), preview_frames=len(additions),
                icon_cell=cell, equipment=equipment, ability_item_cells=ability_cells,
                shared_resources='Existing cardIcons pixels and heroSkins rows preserved; the outfit icon and preview and 24 ability item icons appended.',
                live_rendering='Original ROM OAM through KirbySpriteRenderer; fallback atlas has no source timing.')


def build_equipment(pak, rows, tracks, out):
    pages, normal_pages = [], []
    source_pages = {}
    packed, output = {}, []
    page_size = 2048
    x = y = 1
    shelf = 0
    nonempty = 0
    attachments = {}
    for row in rows:
        if row['page'] not in source_pages:
            source_pages[row['page']] = (np.array(image_read(pak, 'atlas/' + row['page'])),
                                        image_read(pak, 'atlas/' + row['page'].replace('.png', '_n.png')))
        source, normals = source_pages[row['page']]
        crop = (row['x'], row['y'], row['x'] + row['w'], row['y'] + row['h'])
        pixels = source[crop[1]:crop[3], crop[0]:crop[2]].copy()
        ids = pixels[:, :, 0].astype(int) + (pixels[:, :, 1] // 64).astype(int) * 256
        body = (ids >= 478) & (ids < 500)
        pixels[:, :, 3][body] = 0
        tile = Image.fromarray(pixels)
        box = tile.getbbox()
        if box:
            nonempty += 1
            tile = tile.crop(box)
            normal = normals.crop(crop).crop(box)
            group, suffix = row['name'].rsplit('_', 1)
            t = int(suffix)
            hand, attachment = equipment_grip(source[crop[1]:crop[3], crop[0]:crop[2]],
                                               row, tracks, group, t)
            attachments[attachment] = attachments.get(attachment, 0) + 1
            origin = [round(row['ox'] + box[0] - hand[0] + 8), round(row['oy'] + box[1] - hand[1] - 11)]
        else:
            tile = Image.new('RGBA', (1, 1))
            normal = Image.new('RGBA', (1, 1), (128, 128, 255, 255))
            origin = [0, 0]
        w, h = tile.size
        key = hashlib.sha256(tile.tobytes() + normal.tobytes() + struct.pack('<II', w, h)).digest()
        if key not in packed:
            if x + w + 1 > page_size:
                x, y, shelf = 1, y + shelf + 2, 0
            if not pages or y + h + 1 > page_size:
                pages.append(Image.new('RGBA', (page_size, page_size)))
                normal_pages.append(Image.new('RGBA', (page_size, page_size), (128, 128, 255, 255)))
                x = y = 1
                shelf = 0
            if w + 2 > page_size or h + 2 > page_size:
                raise ValueError('Equipment tile exceeds page')
            pages[-1].paste(tile, (x, y))
            normal_pages[-1].paste(normal, (x, y))
            packed[key] = len(pages) - 1, x, y
            x += w + 2
            shelf = max(shelf, h)
        p, px, py = packed[key]
        half_w = max(16, math.ceil(max(abs(origin[0]), abs(origin[0] + w)) / 16) * 16)
        half_h = max(16, math.ceil(max(abs(origin[1]), abs(origin[1] + h)) / 16) * 16)
        output.append(dict(row, page=f'{MODEL}Weapons{p}.png', x=px, y=py, w=w, h=h,
                           ox=half_w + origin[0], oy=half_h + origin[1], ow=half_w * 2, oh=half_h * 2))
    for p, page in enumerate(pages):
        page.save(out / f'{MODEL}Weapons{p}.png')
        normal_pages[p].save(out / f'{MODEL}Weapons{p}_n.png')
    (out / f'{MODEL}Weapons.atlas').write_bytes(atlas_bytes(output))
    return dict(frames=len(output), nonempty_frames=nonempty, unique_tiles=len(packed), pages=len(pages),
                body_palette_indices_removed=[478, 499], grip=[8, -11],
                attachments=attachments,
                limitation='Swords use the source body/equipment contact centroid; bow/shield tracks and missing-contact fallbacks use Prop2. The Kirby hand offset is a visual approximation.')


def equipment_grip(pixels, row, tracks, group, frame):
    """Find the source grip without confusing the off hand with the weapon hand.

    Prop2/L Hand are the off hand in several native sword animations. The atlas
    has no R Hand track. Use the interface between original body and equipment
    pixels for swords; bow/shield animation tracks retain their verified grip.
    """
    positions = tracks.get(group, {}).get('Bip001 Prop2', [])
    tracked = positions[frame * 3:frame * 3 + 2]
    if group.lower().startswith(('block', 'parry')) and len(tracked) == 2:
        return tracked, 'shield-track'
    if 'bow' in group.lower() and len(tracked) == 2:
        return tracked, 'bow-track'
    ids = pixels[:, :, 0].astype(int) + pixels[:, :, 1].astype(int) // 64 * 256
    opaque = pixels[:, :, 3] > 0
    body = (ids >= 478) & (ids < 500) & opaque
    weapon = ~body & opaque
    # Padding avoids wrapping the opposite edge of the crop into its neighbors.
    padded = np.pad(body, 1)
    adjacent = np.zeros_like(body)
    for dy in range(3):
        for dx in range(3):
            adjacent |= padded[dy:dy + body.shape[0], dx:dx + body.shape[1]]
    yy, xx = np.where(adjacent & weapon)
    if len(xx):
        return [row['ox'] + float(np.mean(xx)), row['oy'] + float(np.mean(yy))], 'body-contact'
    if len(tracked) == 2:
        return tracked, 'missing-contact-track'
    return [row['ow'] / 2 + 12, row['oh'] / 2 - 27], 'missing-contact-center'


def validate_output(output):
    if not output.exists() or not any(output.iterdir()):
        return
    if {p.name for p in output.iterdir()} != {'assets', 'res.pak', 'build-manifest.json'}:
        raise ValueError('Output contains unrelated files; use a fresh directory')
    report = json.loads((output / 'build-manifest.json').read_text())
    if report.get('builder') != BUILDER:
        raise ValueError('Output belongs to another builder')
    paths = list(output.rglob('*'))
    if any(p.is_symlink() for p in paths):
        raise ValueError('Refusing to overwrite symlinks')
    actual = {p.relative_to(output / 'assets').as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
              for p in (output / 'assets').rglob('*') if p.is_file()}
    expected_dirs = {parent.as_posix() for name in actual for parent in Path(name).parents if parent != Path('.')}
    actual_dirs = {p.relative_to(output / 'assets').as_posix() for p in (output / 'assets').rglob('*') if p.is_dir()}
    if actual_dirs != expected_dirs or actual != report['asset_sha256'] or hashlib.sha256((output / 'res.pak').read_bytes()).hexdigest() != report['pak_sha256']:
        raise ValueError('Generated output changed; use a fresh directory')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-pak', type=Path, default=Path.home() / '.local/share/Steam/steamapps/common/Dead Cells/res.pak')
    parser.add_argument('--decoded', type=Path, default=ROOT / 'rom/decoded')
    parser.add_argument('--output', type=Path, default=ROOT / 'outfit-build')
    parser.add_argument('--ability-icons', type=Path, default=ROOT / 'ability-icons',
                        help='Output of tools/capture_ability_icons.py.')
    args = parser.parse_args()
    if not all((args.ability_icons / f'{name}.png').is_file() for name in ABILITIES):
        raise SystemExit('Run tools/capture_ability_icons.py first (missing ability item icons)')
    output, source, decoded = args.output.resolve(), args.game_pak.resolve(), args.decoded.resolve()
    project = Path(__file__).resolve().parents[1]
    if output == project or project in output.parents or output == source.parent or source.parent in output.parents:
        raise SystemExit('Build output must be outside project and game installation')
    if output == decoded or output in decoded.parents or decoded in output.parents:
        raise SystemExit('Output must not contain or overlap extracted ROM data')
    validate_output(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    lock = output.parent / f'.{output.name}.build.lock'
    os.close(os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600))
    staging = Path(tempfile.mkdtemp(prefix=f'.{output.name}.build-', dir=output.parent))
    previous = None
    try:
        pak = Pak(source)
        report = build(pak, decoded, staging / 'assets', args.ability_icons.resolve())
        pak.file.close()
        hashes = write_pak(staging / 'assets', staging / 'res.pak')
        result = Pak(staging / 'res.pak')
        if set(result.entries) != set(hashes):
            raise ValueError('PAK file set differs from source assets')
        for name, sha in hashes.items():
            if hashlib.sha256(result.read(name)).hexdigest() != sha:
                raise ValueError(f'PAK round-trip differs: {name}')
        result.file.close()
        report.update(builder=BUILDER, asset_sha256=hashes,
                      pak_sha256=hashlib.sha256((staging / 'res.pak').read_bytes()).hexdigest())
        json_write(staging / 'build-manifest.json', report)
        validate_output(output)
        if output.exists():
            previous = Path(tempfile.mkdtemp(prefix=f'.{output.name}.previous-', dir=output.parent))
            previous.rmdir()
            output.rename(previous)
        try:
            staging.rename(output)
        except BaseException:
            if previous is not None:
                previous.rename(output)
                previous = None
            raise
        if previous is not None:
            shutil.rmtree(previous)
        print(json.dumps({k: v for k, v in report.items() if k != 'asset_sha256'}, indent=2))
    finally:
        if staging.exists():
            shutil.rmtree(staging)
        lock.unlink()


if __name__ == '__main__':
    main()
