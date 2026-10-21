"""KIRBY_DATA_ROOT moves every tool's data root; unset, the default stays."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

PROJECT = Path(__file__).resolve().parents[1]
# Module attribute that holds each tool's root, and the folder below the data root it names.
ROOTS = {
    ('scripts', 'setup_runtime', 'DEFAULT_ROOT'): '',
    ('scripts', 'install', 'ROOT'): '',
    ('tools', 'build_assets', 'DEFAULT_DATA'): '',
    ('tools', 'build_outfit', 'ROOT'): '',
    ('tools', 'extract_kirby', 'DEFAULT_OUT'): 'rom',
    ('tools', 'nds_probe', 'ROOT'): '',
    ('tools', 'capture_ability_icons', 'ROOT'): '',
    ('tools', 'oracle_build', 'ROOT'): 'oracle',
    ('tools', 'oracle_run', 'ROOT'): 'oracle',
    ('tools', 'verify_native_init', 'ROOT'): '',
}


def roots(environment):
    code = ('import importlib, json, sys\n'
            f'sys.path[:0] = [{str(PROJECT / "scripts")!r}, {str(PROJECT / "tools")!r}]\n'
            f'names = {[list(key) for key in ROOTS]!r}\n'
            'print(json.dumps({m: str(getattr(importlib.import_module(m), a)) for _, m, a in names}))\n')
    output = subprocess.check_output([sys.executable, '-c', code], env=environment, text=True)
    return json.loads(output)


class DataRootTests(unittest.TestCase):
    def test_override_moves_every_root(self):
        with tempfile.TemporaryDirectory() as folder:
            environment = dict(os.environ, KIRBY_DATA_ROOT=folder)
            found = roots(environment)
            for (_, module, _), below in ROOTS.items():
                with self.subTest(module=module):
                    self.assertEqual(Path(found[module]), (Path(folder).resolve() / below) if below else Path(folder).resolve())

    def test_default_is_unchanged(self):
        environment = {key: value for key, value in os.environ.items() if key != 'KIRBY_DATA_ROOT'}
        default = Path.home() / '.local/share/kirby-dead-cells'
        for (_, module, _), below in ROOTS.items():
            with self.subTest(module=module):
                self.assertEqual(Path(roots(environment)[module]), default / below if below else default)


if __name__ == '__main__':
    unittest.main()
