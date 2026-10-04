"""Synthetic attachment regressions; no extracted host or ROM artwork."""
from pathlib import Path
import sys
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from build_outfit import equipment_grip


BODY = (478 % 256, (478 // 256) * 64 + 32, 0, 255)
WEAPON = (20, 32, 0, 255)


class EquipmentGripTests(unittest.TestCase):
    def setUp(self):
        self.pixels = np.zeros((12, 20, 4), dtype=np.uint8)
        self.row = dict(ox=100, oy=200, ow=256, oh=512)

    def grip(self, group, track):
        return equipment_grip(self.pixels, self.row,
                              {group: {'Bip001 Prop2': [*track, 0]}}, group, 0)

    def test_sword_follows_held_hilt_instead_of_distant_off_hand(self):
        # The blade joins one hand while Prop2 names the other, far away.
        self.pixels[5, 4] = BODY
        self.pixels[5, 5:15] = WEAPON
        hand, method = self.grip('atkBackStabberB', [117, 210])
        self.assertEqual(hand, [105, 205])
        self.assertEqual(method, 'body-contact')

    def test_shield_retains_its_valid_off_hand_track(self):
        # A broad shield can touch the torso away from its intended grip.
        self.pixels[5, 4] = BODY
        self.pixels[5, 5:15] = WEAPON
        hand, method = self.grip('blockWoodenShield', [117, 210])
        self.assertEqual(hand, [117, 210])
        self.assertEqual(method, 'shield-track')

    def test_tall_bow_keeps_hand_track_despite_lower_body_contact(self):
        self.pixels[9, 4:7] = BODY
        self.pixels[10, 4:7] = WEAPON
        hand, method = self.grip('atkHeavyBow', [112, 202])
        self.assertEqual(hand, [112, 202])
        self.assertEqual(method, 'bow-track')

    def test_separate_crop_edges_are_not_a_grip_contact(self):
        self.pixels[5, 0] = BODY
        self.pixels[5, -1] = WEAPON
        hand, method = self.grip('atkSwordA', [110, 207])
        self.assertEqual(hand, [110, 207])
        self.assertEqual(method, 'missing-contact-track')

    def test_transparent_body_palette_pixels_do_not_anchor_weapon(self):
        self.pixels[5, 4] = (*BODY[:3], 0)
        self.pixels[5, 5:15] = WEAPON
        hand, method = self.grip('atkSwordA', [110, 207])
        self.assertEqual(hand, [110, 207])
        self.assertEqual(method, 'missing-contact-track')


if __name__ == '__main__':
    unittest.main()
