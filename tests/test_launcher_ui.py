"""The launcher's Slint sources; read as text, nothing is built."""
from pathlib import Path
import re
import unittest

UI = Path(__file__).resolve().parents[1] / 'launcher/ui'


class ClickTests(unittest.TestCase):
    def test_keyboard_scopes_leave_clicks_to_the_touch_area(self):
        # A FocusScope that takes focus on a click accepts that press, so the TouchArea
        # beneath it would need a second click (#258).
        scopes = 0
        for path in sorted(UI.glob('*.slint')):
            for match in re.finditer(r'FocusScope \{(.*?)\n    \}', path.read_text(), re.S):
                scopes += 1
                self.assertIn('focus-on-click: false;', match.group(1), path.name)
        self.assertEqual(scopes, 5)


if __name__ == '__main__':
    unittest.main()
