"""Synthetic safety checks, not evidence of KSSU gameplay fidelity."""
import struct
import unittest

from oracle_snapshot import ENTITY_HEADS, HIT_HEADS, RAM_SIZE, RAM_START, InvalidDump, inspect_ram


class DumpSafetyTests(unittest.TestCase):
    def setUp(self):
        self.ram = bytearray(RAM_SIZE)
        self.entity = RAM_START + 0x1000
        self.hit = RAM_START + 0x2000
        self.effect = RAM_START + 0x3000

    def put(self, address, fmt, *values):
        struct.pack_into("<" + fmt, self.ram, address - RAM_START, *values)

    def test_read_only_unsigned_negative_and_owner_link(self):
        self.put(ENTITY_HEADS + 4, "I", self.entity)
        self.put(self.entity + 0x80, "iihh", -65536, 131072, -123, 456)
        self.put(HIT_HEADS + 12, "I", self.hit)
        self.put(self.hit + 0x14, "I", self.effect)
        self.put(self.hit + 0x38, "I", self.entity)
        self.put(self.hit + 0x24, "4h", -4, -3, 12, 14)
        before = bytes(self.ram)
        result = inspect_ram(self.ram, self.entity)
        selected = result["selected_entity"]
        self.assertEqual(selected["x_candidate_pixels"], -1)
        self.assertEqual(selected["vx_raw"], -123)
        self.assertEqual(selected["hits"][0]["bounds_raw"], [-4, -3, 12, 14])
        self.assertEqual(self.ram, before)

    def test_rejects_truncated_savestate_or_dump(self):
        with self.assertRaises(InvalidDump):
            inspect_ram(b"not a raw RAM dump")

    def test_rejects_entity_cycles(self):
        self.put(ENTITY_HEADS, "I", self.entity)
        self.put(self.entity, "I", self.entity)
        with self.assertRaisesRegex(InvalidDump, "cycle"):
            inspect_ram(self.ram)

    def test_rejects_pointer_crossing_ram_end(self):
        self.put(ENTITY_HEADS, "I", RAM_START + RAM_SIZE - 4)
        with self.assertRaisesRegex(InvalidDump, "outside"):
            inspect_ram(self.ram)

    def test_rejects_invalid_definition_pointer(self):
        self.put(HIT_HEADS, "I", self.hit)
        self.put(self.hit + 0x14, "I", 0xFFFFFFFF)
        with self.assertRaisesRegex(InvalidDump, "outside"):
            inspect_ram(self.ram)

    def test_does_not_guess_player(self):
        with self.assertRaisesRegex(InvalidDump, "exactly once"):
            inspect_ram(self.ram, self.entity)


if __name__ == "__main__":
    unittest.main()
