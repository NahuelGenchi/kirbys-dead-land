"""Release lint regressions; synthetic file contents only."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from package_release import lint, tracked_files


class ReleaseLintTests(unittest.TestCase):
    def test_source_and_docs_pass(self):
        self.assertEqual(lint({'src/core/KirbyRules.cs': b'class KirbyRules {}\n',
                               'docs/A.md': b'[link](#flask-and-fountains-intentional-deviation-with-a-long-anchor)\n'}), [])

    def test_rom_game_and_generated_files_fail(self):
        for name in ('Kirby Super Star Ultra (USA).nds', 'res.pak', 'hlboot.dat', 'libkssu_bridge.so',
                     'GameRes.Core.dll', 'fmt.hdll', '000.state', 'icons/Sword.png', 'clip.mp4', 'crash-1.dmp'):
            with self.subTest(name=name):
                self.assertEqual(len(lint({name: b'x'})), 1)

    def test_private_folders_fail(self):
        for name in ('evidence/run.json', 'lab/save/user_0.dat.json', 'rom/decoded/manifest.json',
                     'tools/oracle/ready2/notes.txt', 'src/runtime/bin/Release/x.txt'):
            with self.subTest(name=name):
                self.assertEqual(len(lint({name: b'{}'})), 1)

    def test_binary_and_large_files_fail(self):
        self.assertEqual(len(lint({'tools/blob.py': b'a\0b'})), 1)
        self.assertEqual(len(lint({'docs/huge.md': b'a' * (1024 * 1024 + 1)})), 1)

    def test_key_like_strings_fail(self):
        for secret in (b'ghp_' + b'a' * 36, b'token = "sk-' + b'b' * 40 + b'"',
                       b'AKIA' + b'C' * 16, b'-----BEGIN OPENSSH PRIVATE KEY-----',
                       b'FAL_KEY=01234567-89ab-cdef-0123-456789abcdef:' + b'0' * 32):
            with self.subTest(secret=secret[:12]):
                self.assertEqual(len(lint({'scripts/config.py': secret})), 1)

    def test_current_commit_is_releasable(self):
        files = tracked_files('HEAD')
        self.assertIn('LICENSE', files)
        self.assertNotIn('AGENTS.md', files)
        self.assertFalse(any(name.startswith('.github/') for name in files))
        self.assertEqual(lint(files), [])


if __name__ == '__main__':
    unittest.main()
