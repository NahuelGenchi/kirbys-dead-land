"""Built-in save backups; synthetic save folders only."""
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import save_backup
from save_backup import back_up


class SaveBackupTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.base = Path(self.folder.name)
        self.game = self.base / 'Dead Cells/save'
        (self.game / 'user_0').mkdir(parents=True)
        (self.game / 'user_0.dat').write_bytes(b'run one')
        (self.game / 'user_0/backup.dat').write_bytes(b'older run')
        self.cloud = self.base / 'userdata/123/588650'
        (self.cloud / 'remote').mkdir(parents=True)
        (self.cloud / 'remote/user_0.dat').write_bytes(b'cloud copy')
        self.root = self.base / 'data'

    def tearDown(self):
        self.folder.cleanup()

    def test_archives_every_existing_folder_and_its_files(self):
        target = back_up([self.game, self.base / 'missing', self.cloud], self.root)
        manifest = json.loads((target / 'manifest.json').read_text())
        self.assertEqual([entry['archive'] for entry in manifest], ['0-save.tar.gz', '2-588650.tar.gz'])
        with tarfile.open(target / '0-save.tar.gz') as tar:
            self.assertEqual(sorted(tar.getnames()), ['save/user_0.dat', 'save/user_0/backup.dat'])
            self.assertEqual(tar.extractfile('save/user_0.dat').read(), b'run one')
        self.assertEqual((self.game / 'user_0.dat').read_bytes(), b'run one')

    def test_keeps_only_the_newest_sets(self):
        for index in range(4):
            with mock.patch.object(save_backup.time, 'strftime', return_value=f'2026010{index}-000000'):
                back_up([self.game], self.root, keep=2)
        self.assertEqual(sorted(path.name for path in (self.root / 'save-backups').iterdir()),
                         ['20260102-000000', '20260103-000000'])

    def test_a_backup_that_does_not_match_stops_and_leaves_nothing(self):
        with mock.patch.object(save_backup, 'archived', return_value={}):
            with self.assertRaises(RuntimeError):
                back_up([self.game], self.root)
        self.assertEqual(list((self.root / 'save-backups').iterdir()), [])


if __name__ == '__main__':
    unittest.main()
