"""Synthetic ability item rows (#55); no extracted host or ROM artwork."""
from pathlib import Path
import re
import sys
import tempfile
import unittest

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from build_outfit import ABILITIES, ABILITY_PREFIX, MELEE_GROUP, ability_items


def database(*items):
    sword = {'item': 'StartSword', 'strikeChain': [{'power': [25]}], 'allowCrouch': False}
    return {'sheets': [{'name': 'item', 'lines': [{'id': i} for i in items]},
                       {'name': 'weapon', 'lines': [sword, {'item': 'BroadSword', 'strikeChain': []}]}]}


class AbilityItemRowTests(unittest.TestCase):
    def setUp(self):
        self.icons_dir = tempfile.TemporaryDirectory()
        self.icon_dir = Path(self.icons_dir.name)
        for index, name in enumerate(ABILITIES):
            Image.new('RGBA', (24, 24), (index, 0, 0, 255)).save(self.icon_dir / f'{name}.png')
        self.sheet = Image.new('RGBA', (24 * 30, 24 * 2))
        self.cells = [(x, 1) for x in range(len(ABILITIES))]

    def tearDown(self):
        self.icons_dir.cleanup()

    def test_one_colorless_melee_row_and_weapon_row_per_ability(self):
        items, weapons, placed = ability_items(database('StartSword'), self.sheet, self.cells, self.icon_dir)
        self.assertEqual(len(ABILITIES), 24)
        self.assertEqual(sorted(items), sorted(ABILITY_PREFIX + name for name in ABILITIES))
        self.assertEqual(sorted(weapons), sorted(items))
        fire = items['KirbyAbilityFire']
        self.assertEqual((fire['id'], fire['name'], fire['group'], fire['__separator_group_Name']),
                         ('KirbyAbilityFire', 'Fire', MELEE_GROUP, 'Melee'))
        # Only the mod creates them: never in the host's own loot pools.
        self.assertFalse(fire['droppable'])
        self.assertEqual(items['KirbyAbilityYoYo']['name'], 'Yo-Yo')
        self.assertEqual(weapons['KirbyAbilityFire']['item'], 'KirbyAbilityFire')
        self.assertEqual(weapons['KirbyAbilityFire']['strikeChain'], [{'power': [25]}])
        self.assertEqual(placed['Cutter'], [0, 1])

    def test_icons_go_to_their_own_cells(self):
        _, _, placed = ability_items(database(), self.sheet, self.cells, self.icon_dir)
        for index, name in enumerate(ABILITIES):
            x, y = placed[name]
            self.assertEqual(self.sheet.getpixel((x * 24 + 5, y * 24 + 5)), (index, 0, 0, 255))
        self.assertIsNone(self.sheet.crop((0, 0, 24 * 30, 24)).getbbox())

    def test_weapon_rows_are_independent_copies(self):
        _, weapons, _ = ability_items(database(), self.sheet, self.cells, self.icon_dir)
        weapons['KirbyAbilityFire']['strikeChain'][0]['power'][0] = 0
        self.assertEqual(weapons['KirbyAbilityIce']['strikeChain'][0]['power'], [25])

    def test_ability_order_matches_the_runtime_and_icon_capture(self):
        source = (ROOT / 'src/runtime/KirbyNdsProvider.cs').read_text()
        runtime = re.search(r'Abilities = \[([^\]]*)\]', source).group(1)
        self.assertEqual([name.strip().strip('"') for name in runtime.split(',')], ABILITIES)
        capture = (ROOT / 'tools/capture_ability_icons.py').read_text()
        self.assertEqual(re.findall(r"'(\w+)'", re.search(r'ABILITIES = \[([^\]]*)\]', capture).group(1)), ABILITIES)
        core = (ROOT / 'src/core/AbilityItems.cs').read_text()
        self.assertIn(f'public const string Prefix = "{ABILITY_PREFIX}";', core)

    def test_refuses_a_source_that_already_has_ability_items(self):
        with self.assertRaises(ValueError):
            ability_items(database('KirbyAbilityFire'), self.sheet, self.cells, self.icon_dir)

    def test_refuses_a_wrong_icon_size(self):
        Image.new('RGBA', (32, 32)).save(self.icon_dir / 'Fire.png')
        with self.assertRaises(ValueError):
            ability_items(database(), self.sheet, self.cells, self.icon_dir)


if __name__ == '__main__':
    unittest.main()
