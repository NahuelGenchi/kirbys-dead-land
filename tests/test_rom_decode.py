"""Original synthetic pixel/bounds cases plus optional private local-ROM checks.

These tests verify extraction behavior, not animation timing or gameplay parity.
No copyrighted fixtures or decoded game images are stored in the repository.
"""
from pathlib import Path
import os
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import extract_kirby as decoder


def tiny_rom():
    rom = bytearray(0x200)
    struct.pack_into("<4I", rom, 0x40, 0x100, 15, 0x140, 8)
    struct.pack_into("<IHH", rom, 0x100, 8, 0, 1)
    rom[0x108:0x10F] = b"\x05a.bin\x00"
    struct.pack_into("<II", rom, 0x140, 0x180, 0x184)
    rom[0x180:0x184] = b"test"
    return rom


def tiny_frame(*, attr0=0, attr1=0, attr3=0, palette_index=0):
    data = bytearray(0x200)
    struct.pack_into("<IIBBBB", data, 0, 0x20, 0x30, 1, 1, palette_index, 255)
    struct.pack_into("<4H", data, 0x20, attr0, attr1, 0, attr3)
    struct.pack_into("<I", data, 0x30, 0x100)
    # Palette index 0 deliberately has a nonblack RGB value; it is still transparent.
    struct.pack_into("<4H", data, 0x40, 0x7FFF, 0x001F, 0x03E0, 0x7C00)
    # Four corners: opaque red, green, blue, and transparent white.
    data[0x100] = 1
    data[0x103] = 2 << 4
    data[0x11C] = 3
    return data


class NitroBoundaryTests(unittest.TestCase):
    def test_reads_file_using_fat_boundaries(self):
        self.assertEqual(decoder.nitrofs(tiny_rom()), [
            {"id": 0, "path": "a.bin", "offset": 0x180, "size": 4}
        ])

    def test_rejects_directory_subtable_outside_fnt(self):
        rom = tiny_rom()
        struct.pack_into("<I", rom, 0x100, 0x80)
        with self.assertRaises(ValueError):
            decoder.nitrofs(rom)

    def test_rejects_file_id_outside_fat_even_when_rom_bytes_exist(self):
        rom = tiny_rom()
        struct.pack_into("<H", rom, 0x104, 1)
        struct.pack_into("<II", rom, 0x148, 0x180, 0x184)
        with self.assertRaises(ValueError):
            decoder.nitrofs(rom)

    def test_rejects_missing_terminator_inside_fnt(self):
        rom = tiny_rom()
        struct.pack_into("<I", rom, 0x44, 14)
        with self.assertRaises(ValueError):
            decoder.nitrofs(rom)

    def test_rejects_child_directory_outside_declared_directory_table(self):
        rom = tiny_rom()
        struct.pack_into("<I", rom, 0x44, 12)
        rom[0x108:0x10C] = b"\x81d\x01\xf0"
        with self.assertRaises(ValueError):
            decoder.nitrofs(rom)

    def test_rejects_filename_path_separator(self):
        for name in (b"../xx", b"a/bbb", b"a\\bbb", b"a\x00bbb"):
            with self.subTest(name=name):
                rom = tiny_rom()
                rom[0x109:0x10E] = name
                with self.assertRaises(ValueError):
                    decoder.nitrofs(rom)


class PixelDecodingTests(unittest.TestCase):
    def test_transparency_depends_on_index_not_rgb(self):
        colors = decoder.rgb555(struct.pack("<16H", 0x7FFF, 0, *([0x7FFF] * 14)))
        self.assertEqual(colors[0], (255, 255, 255, 0))
        self.assertEqual(colors[1], (0, 0, 0, 255))
        self.assertEqual(colors[2], (255, 255, 255, 255))

    def test_nibble_order_and_horizontal_and_vertical_tile_layout(self):
        palette = [(i, 0, 0, 255) for i in range(16)]
        tiles = bytes([0x21] * 32 + [0x43] * 32 + [0x65] * 32 + [0x87] * 32)
        image = decoder.tile_image(tiles, 0, 16, 16, palette)
        expected = {(0, 0): 1, (1, 0): 2, (8, 0): 3, (9, 0): 4,
                    (0, 8): 5, (1, 8): 6, (8, 8): 7, (9, 8): 8}
        for point, index in expected.items():
            self.assertEqual(image.getpixel(point), palette[index])

    def test_non_affine_flip_flags_move_correct_corners(self):
        for flags, red_point, green_point, blue_point in [
            (0, (0, 0), (7, 0), (0, 7)),
            (0x1000, (7, 0), (0, 0), (7, 7)),
            (0x2000, (0, 7), (7, 7), (0, 0)),
            (0x3000, (7, 7), (0, 7), (7, 0)),
        ]:
            with self.subTest(flags=flags):
                image, meta = decoder.decode_frame(tiny_frame(attr1=flags), 0, 0x40, 0x40, 1)
                self.assertEqual(image.getpixel(red_point), (255, 0, 0, 255))
                self.assertEqual(image.getpixel(green_point), (0, 255, 0, 255))
                self.assertEqual(image.getpixel(blue_point), (0, 0, 255, 255))
                self.assertEqual(sum(image.getpixel((x, y))[3] != 0
                                     for y in range(image.height) for x in range(image.width)), 3)

    def test_signed_part_coordinates_preserve_character_pivot(self):
        image, meta = decoder.decode_frame(tiny_frame(attr0=255, attr1=511), 0, 0x40, 0x40, 1)
        self.assertEqual(meta["origin"], [-1, -1])
        self.assertEqual(meta["pivot"], [1, 1])
        self.assertEqual(image.getpixel((0, 0)), (255, 0, 0, 255))

    def test_unknown_shared_palette_is_not_fabricated(self):
        image, meta = decoder.decode_frame(tiny_frame(attr3=0x2000), 0, 0x40, 0x40, 1)
        self.assertEqual(len(meta["unresolved_parts"]), 1)
        self.assertEqual(meta["parts"], [])
        self.assertEqual(image.getbbox(), None)

    def test_left_facing_uses_its_own_oam_position_and_flip(self):
        raw = tiny_frame(attr1=0x1000)
        # Deliberately different left position, and no horizontal flip for left.
        struct.pack_into("<H", raw, 0x24, 510)
        image, meta = decoder.decode_frame(raw, 0, 0x40, 0x40, 1, facing="left")
        self.assertEqual(meta["origin"], [-2, 0])
        self.assertEqual(image.getpixel((0, 0)), (255, 0, 0, 255))
        self.assertEqual(image.getpixel((7, 0)), (0, 255, 0, 255))

    def test_composition_keeps_effects_outside_fixed_128_pixel_canvas(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            decoder.Image.new("RGBA", (8, 8), (255, 0, 0, 255)).save(root / "body.png")
            decoder.Image.new("RGBA", (16, 8), (0, 0, 255, 255)).save(root / "effect.png")
            image, origin = decoder.compose_frames(root, [
                {"file": "body.png", "origin": [-72, -2]},
                {"file": "effect.png", "origin": [70, 6]},
            ])
            self.assertEqual(origin, [-72, -2])
            self.assertEqual(image.size, (158, 16))
            self.assertEqual(image.getpixel((0, 0)), (255, 0, 0, 255))
            self.assertEqual(image.getpixel((157, 15)), (0, 0, 255, 255))


ROM = Path(os.environ.get("KSSU_ROM", Path.home() / "Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds"))


@unittest.skipUnless(ROM.is_file(), "private user ROM not available; synthetic tests still run")
class ActualRomStructureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rom = ROM.read_bytes()
        cls.files = {entry["path"]: entry for entry in decoder.nitrofs(cls.rom)}

    def raw(self, path):
        e = self.files[path]
        return self.rom[e["offset"]:e["offset"] + e["size"]]

    def test_original_inventory_and_known_archive_groups(self):
        self.assertEqual(self.rom[12:16], b"YKWE")
        self.assertEqual(len(self.files), 1886)
        for path, group_count, pose_count in [("rom/pd/bkir.b", 2, 491), ("rom/pd/aswd.b", 4, 42)]:
            raw = self.raw(path)
            count, groups = struct.unpack_from("<II", raw)
            self.assertEqual(count, group_count)
            self.assertEqual(decoder.u32(raw, groups), pose_count)

    def test_all_pd_oam_parts_use_supported_non_affine_4bpp_data(self):
        observed = 0
        for path in self.files:
            if not path.startswith("rom/pd/") or not path.endswith(".b"):
                continue
            raw = self.raw(path)
            count, groups = struct.unpack_from("<II", raw)
            for group in range(count):
                poses, _, pointers, _ = struct.unpack_from("<4I", raw, groups + 16 * group)
                for pose in range(poses):
                    pointer = decoder.u32(raw, pointers + 4 * pose)
                    oam, blocks, parts, block_count, _, _ = struct.unpack_from("<IIBBBB", raw, pointer)
                    self.assertLessEqual(oam + 8 * parts, len(raw), (path, group, pose))
                    for index in range(block_count):
                        graphic = decoder.u32(raw, blocks + 4 * index)
                        self.assertLessEqual(graphic + 256, len(raw), (path, group, pose))
                    for part in range(parts):
                        attr0, attr1, _, _ = struct.unpack_from("<4H", raw, oam + 8 * part)
                        self.assertEqual(attr0 & 0x2300, 0, (path, group, pose))
                        self.assertLess(attr0 >> 14, 3, (path, group, pose))
                        self.assertLess(attr1 >> 14, 4)
                        observed += 1
        self.assertEqual(observed, 42909)


if __name__ == "__main__":
    unittest.main()
