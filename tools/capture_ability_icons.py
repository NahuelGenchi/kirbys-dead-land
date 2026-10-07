#!/usr/bin/env python3
"""Capture the 24 original copy-ability icons for the Kirby ability items.

Each ability runs in its own private headless bridge instance from the isolated
ready2 seed: Kirby is fed the ability, swallows it, and the bottom-screen icon
OBJ (the same one KirbyHud reads live) is decoded once it has been steady for
30 frames. Writes 32x32 originals and 24x24 item icons (box-averaged, then
snapped to the original's own colors) to a private directory, never the project.
"""
import argparse
import ctypes as C
import hashlib
import json
from pathlib import Path
import struct

import numpy as np
from PIL import Image

from nds_probe import Bridge, bind_proxy_api, ROOT

# Same IDs and order as KirbyNdsProvider.Abilities (original abilities 2..25).
ABILITIES = ['Cutter', 'Mirror', 'Beam', 'Fighter', 'Suplex', 'YoYo', 'Wheel', 'Bomb', 'Ice', 'Fire', 'Plasma', 'Ninja',
             'Stone', 'Wing', 'Jet', 'Copy', 'Sword', 'Hammer', 'Parasol', 'Paint', 'Mike', 'Crash', 'Cook', 'Sleep']
STEADY_FRAMES = 30


def gpu(bridge, address, length):
    out = C.create_string_buffer(length)
    assert bridge.lib.kssu_read_gpu(bridge.handle, address, out, length) == 0
    return out.raw


def find_icon(display, oam):
    """The 32x32 OBJ at (4, 4) of sub-engine OAM, as AbilityIcon.Find."""
    if not display & 0x10:
        return None
    unit = 32 << ((display >> 20) & 3)
    for index in range(128):
        a0, a1, a2 = struct.unpack_from('<3H', oam, index * 8)
        affine = a0 & 0x100
        if (not affine and a0 & 0x200) or affine or a0 & 0x2000 or ((a0 >> 10) & 3) >= 2:
            continue
        if a0 >> 14 != 0 or a1 >> 14 != 2 or (a0 & 255) != 4 or (a1 & 511) != 4:
            continue
        return (a2 & 1023) * unit, a2 >> 12, bool(a1 & 0x1000), bool(a1 & 0x2000)
    return None


def decode(sprite, tiles, palette):
    """1D-mapped 4bpp tiles and a BGR555 bank to RGBA, as AbilityIcon.Decode."""
    _, _, flip_x, flip_y = sprite
    colors = [struct.unpack_from('<H', palette, i * 2)[0] for i in range(16)]
    image = Image.new('RGBA', (32, 32))
    for y in range(32):
        for x in range(32):
            tx, ty = (31 - x if flip_x else x), (31 - y if flip_y else y)
            offset = ((ty // 8) * 4 + tx // 8) * 32 + (ty & 7) * 4 + (tx & 7) // 2
            index = (tiles[offset] >> ((tx & 1) * 4)) & 15
            if index:
                c = colors[index]
                image.putpixel((x, y), ((c & 31) * 255 // 31, ((c >> 5) & 31) * 255 // 31, ((c >> 10) & 31) * 255 // 31, 255))
    return image


def capture(ability_byte):
    bridge = Bridge()
    bind_proxy_api(bridge)
    bridge.lib.kssu_read_gpu.argtypes = [C.c_void_p, C.c_uint32, C.c_void_p, C.c_size_t]
    try:
        # The original mouthful-and-swallow path, as tools/nds_tests.py transform().
        assert bridge.feed(ability_byte) == -2
        bridge.step(2, 20)
        assert bridge.action_flags() & 1
        assert bridge.feed(ability_byte) == 0
        bridge.step(2, 1); bridge.step(0, 1); bridge.step(128, 1); bridge.step(0, 180)
        assert bridge.state()['ability'] == ability_byte
        previous, steady = None, 0
        for _ in range(240):
            bridge.step(0, 1)
            display = struct.unpack('<I', gpu(bridge, 0x04001000, 4))[0]
            sprite = find_icon(display, gpu(bridge, 0x07000400, 1024))
            if sprite is None:
                previous, steady = None, 0
                continue
            pixels = decode(sprite, gpu(bridge, 0x06600000 + sprite[0], 512), gpu(bridge, 0x05000600 + sprite[1] * 32, 32)).tobytes()
            steady = steady + 1 if pixels == previous else 0
            previous = pixels
            if steady >= STEADY_FRAMES:
                return Image.frombytes('RGBA', (32, 32), pixels)
        raise RuntimeError(f'Ability {ability_byte} icon never became steady')
    finally:
        bridge.close()


def item_icon(original):
    """24x24: area-average the 32x32 icon, keep pixels at least half covered, snap to its own colors."""
    source = np.array(original)
    palette = np.unique(source[source[:, :, 3] > 0][:, :3], axis=0).astype(float)
    small = np.array(original.resize((24, 24), Image.Resampling.BOX)).astype(float)
    out = np.zeros((24, 24, 4), np.uint8)
    for y in range(24):
        for x in range(24):
            if small[y, x, 3] < 128:
                continue
            out[y, x, :3] = palette[((palette - small[y, x, :3]) ** 2).sum(1).argmin()]
            out[y, x, 3] = 255
    return Image.fromarray(out, 'RGBA')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'ability-icons')
    args = parser.parse_args()
    output = args.output.resolve()
    project = Path(__file__).resolve().parents[1]
    if output == project or project in output.parents:
        raise SystemExit('Icons are ROM-derived; write them outside the project')
    output.mkdir(parents=True, exist_ok=True)
    manifest = {}
    for index, name in enumerate(ABILITIES):
        original = capture(index + 2)
        original.save(output / f'{name}-32.png')
        item_icon(original).save(output / f'{name}.png')
        manifest[name] = {size: hashlib.sha256((output / f'{name}{suffix}.png').read_bytes()).hexdigest()
                          for size, suffix in (('32', '-32'), ('24', ''))}
    (output / 'manifest.json').write_text(json.dumps({'abilities': ABILITIES, 'sha256': manifest}, indent=2) + '\n')
    print(f'Wrote {len(ABILITIES)} ability icons to {output}')


if __name__ == '__main__':
    main()
