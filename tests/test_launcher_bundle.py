"""The launcher bundle and the dpkg-free TurboJPEG unpacking; synthetic files only."""
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT / 'scripts'))
from build_bundle import project_files
from launch_runtime import extract_deb


def ar_member(name: str, data: bytes) -> bytes:
    header = f'{name:<16}{0:<12}{0:<6}{0:<6}{"100644":<8}{len(data):<10}`\n'.encode()
    return header + data + (b'\n' if len(data) % 2 else b'')


class DebianPackageTests(unittest.TestCase):
    def test_data_files_and_links_unpack_without_dpkg(self):
        files = io.BytesIO()
        with tarfile.open(fileobj=files, mode='w:xz') as tar:
            body = b'\x7fELF library'
            info = tarfile.TarInfo('./usr/lib/x86_64-linux-gnu/libdemo.so.0.2.0')
            info.size = len(body)
            tar.addfile(info, io.BytesIO(body))
            link = tarfile.TarInfo('./usr/lib/x86_64-linux-gnu/libdemo.so.0')
            link.type, link.linkname = tarfile.SYMTYPE, 'libdemo.so.0.2.0'
            tar.addfile(link)
        package = b'!<arch>\n' + ar_member('debian-binary', b'2.0\n') + ar_member('control.tar.xz', b'x') \
            + ar_member('data.tar.xz', files.getvalue())
        with tempfile.TemporaryDirectory() as folder:
            archive = Path(folder) / 'demo.deb'
            archive.write_bytes(package)
            extract_deb(archive, Path(folder) / 'out')
            library = Path(folder) / 'out/usr/lib/x86_64-linux-gnu/libdemo.so.0'
            self.assertEqual(library.read_bytes(), b'\x7fELF library')
            self.assertTrue(library.is_symlink())

    def test_other_files_are_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            archive = Path(folder) / 'demo.deb'
            archive.write_bytes(b'PK\x03\x04 not a package')
            with self.assertRaises(SystemExit):
                extract_deb(archive, Path(folder) / 'out')


class BundleTests(unittest.TestCase):
    def test_bundle_holds_what_setup_and_play_run(self):
        names = project_files()
        for needed in ('scripts/launch_runtime.py', 'scripts/stage_runtime.py', 'scripts/save_backup.py',
                       'scripts/setup_runtime.py', 'tools/oracle_run.py', 'tools/oracle_bootstrap.json',
                       'tools/extract_kirby.py', 'tools/capture_ability_icons.py', 'tools/build_outfit.py',
                       'tools/data_root.py', 'tools/lab_lock.py', 'tools/steam_paths.py', 'scripts/low_integrity.py',
                       'mod.json', 'LICENSE', 'THIRD_PARTY_NOTICES.md'):
            with self.subTest(file=needed):
                self.assertIn(needed, names)
        self.assertFalse(any(name.startswith(('docs/', 'src/', 'launcher/', 'tests/')) for name in names))

    def build(self, folder: Path, runtime_body: bytes, *extra: str,
              programs: dict[str, bytes] = {'libkssu_bridge.so': b'\x7fELF program', 'kssu-oracle-runner': b'\x7fELF program'}) -> subprocess.CompletedProcess:
        runtime, native = folder / 'runtime', folder / 'native'
        runtime.mkdir(exist_ok=True)
        native.mkdir(exist_ok=True)
        for name in ('Kirby.Runtime.dll', 'Kirby.Core.dll'):
            (runtime / name).write_bytes(runtime_body)
        (runtime / 'Kirby.Runtime.pdb').write_bytes(b'symbols')
        (runtime / 'config.json').write_text('{"EnableOriginalRuntime": false}\n')
        (runtime / 'modinfo.json').write_text('{}\n')
        for name, body in programs.items():
            (native / name).write_bytes(body)
        return subprocess.run([sys.executable, str(PROJECT / 'scripts/build_bundle.py'), '--output', str(folder / 'bundle'),
                               '--runtime', str(runtime), '--native', str(native), *extra],
                              capture_output=True, text=True)

    def test_layout_and_record(self):
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            result = self.build(folder, b'MZ adapter')
            self.assertEqual(result.returncode, 0, result.stderr)
            bundle = folder / 'bundle'
            record = json.loads((bundle / 'bundle.json').read_text())
            self.assertEqual(record['version'], json.loads((PROJECT / 'mod.json').read_text())['version'])
            self.assertFalse(record['private'])
            runtime = bundle / 'project/src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra'
            self.assertEqual(sorted(p.name for p in runtime.iterdir()), ['Kirby.Core.dll', 'Kirby.Runtime.dll', 'config.json', 'modinfo.json'])
            self.assertTrue(os.access(bundle / 'bin/kssu-oracle-runner', os.X_OK))
            self.assertTrue((bundle / 'project/scripts/launch_runtime.py').is_file())

    def test_windows_bundle(self):
        # The smallest file pe_machine reads: a DOS header pointing at a PE header.
        image = lambda machine: b'MZ' + bytes(0x3a) + (0x40).to_bytes(4, 'little') + b'PE\0\0' + machine.to_bytes(2, 'little')
        windows = {'kssu_bridge.dll': b'MZ library', 'kssu-oracle-runner.exe': b'MZ program'}
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            result = self.build(folder, image(0x8664), '--target', 'windows', programs=windows)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(sorted(p.name for p in (folder / 'bundle/bin').iterdir()), ['kssu-oracle-runner.exe', 'kssu_bridge.dll'])
            linux_runtime = self.build(folder, image(0xFD1D), '--target', 'windows', programs=windows)
            self.assertIn('not compiled for Windows', linux_runtime.stderr)
            (folder / 'native/kssu_bridge.dll').write_bytes(b'\x7fELF library')
            linux_bridge = self.build(folder, image(0x8664), '--target', 'windows', programs={})
            self.assertIn('kssu_bridge.dll: missing or not a Windows program; run tools/nds_build.py --target windows', linux_bridge.stderr)

    def test_programs_naming_this_home_folder_need_private(self):
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            body = b'MZ ' + os.fsencode(Path.home()) + b'/src/obj/Kirby.Runtime.pdb'
            refused = self.build(folder, body)
            self.assertNotEqual(refused.returncode, 0)
            self.assertIn('home folder', refused.stderr)
            self.assertFalse((folder / 'bundle').exists())
            accepted = self.build(folder, body, '--private')
            self.assertEqual(accepted.returncode, 0, accepted.stderr)
            self.assertTrue(json.loads((folder / 'bundle/bundle.json').read_text())['private'])

    def test_staged_lab_settings_are_refused(self):
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            self.build(folder, b'MZ adapter')
            (folder / 'runtime/config.json').write_text('{"EnableOriginalRuntime": true, "RomPath": "/x.nds"}\n')
            result = subprocess.run([sys.executable, str(PROJECT / 'scripts/build_bundle.py'), '--output', str(folder / 'bundle'),
                                     '--runtime', str(folder / 'runtime'), '--native', str(folder / 'native')],
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('staged lab settings', result.stderr)


if __name__ == '__main__':
    unittest.main()
