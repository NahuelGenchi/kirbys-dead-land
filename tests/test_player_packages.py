"""The player packages' layout, lint and archives (scripts/build_packages.py); synthetic files only."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import tomllib
import unittest

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT / 'scripts'))
from build_bundle import RUNTIME, project_files
from build_packages import APP, LOCKS, NOTICES, build_environment, layout, notice_paths, read_appimage, read_tar, \
    write_appimage, write_tar
from oracle_build import CORE, WRAPPER
from package_release import lint_package

HOME = os.fsencode(Path.home()) + b'/'


def fake_bundle(folder: Path) -> Path:
    bundle = folder / 'bundle'
    (bundle / 'project/scripts').mkdir(parents=True)
    (bundle / 'project/scripts/launch_runtime.py').write_text('print("launch")\n')
    (bundle / 'bundle.json').write_text(json.dumps({'version': '0.2.0', 'commit': 'abc', 'private': False}))
    runtime = bundle / 'project' / RUNTIME
    runtime.mkdir(parents=True)
    for name in ('Kirby.Runtime.dll', 'Kirby.Core.dll'):
        (runtime / name).write_bytes(b'MZ adapter')
    for name in ('config.json', 'modinfo.json'):
        (runtime / name).write_text('{}\n')
    (bundle / 'bin').mkdir()
    for name in ('libkssu_bridge.so', 'kssu-oracle-runner'):
        (bundle / 'bin' / name).write_bytes(b'\x7fELF program')
        (bundle / 'bin' / name).chmod(0o755)
    return bundle


class PackageLayoutTests(unittest.TestCase):
    def setUp(self):
        self.folder = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.folder)
        self.bundle = fake_bundle(self.folder)

    def package(self, kind: str):
        return layout(kind, self.bundle, b'\x7fELF launcher', b'\x89PNG\r\n\x1a\nicon')

    def test_appimage_keeps_the_bundle_where_the_launcher_looks(self):
        files, built = self.package('appimage')
        self.assertEqual(files[f'usr/bin/{APP}'][1], 0o755)
        self.assertEqual(files['AppRun'][1], 0o755)
        self.assertIn(f'{APP}.desktop', files)
        self.assertIn(f'usr/share/{APP}/bundle.json', files)
        self.assertEqual(files[f'usr/share/{APP}/bin/kssu-oracle-runner'][1], 0o755)
        self.assertIn(b'$APPDIR/usr/bin/kirbys-dead-land', files['AppRun'][0])
        for name, source in NOTICES.items():
            self.assertEqual(files[f'usr/share/doc/{APP}/{name}'][0], source.read_bytes())
        self.assertEqual(lint_package({name: data for name, (data, _) in files.items()}, built, [HOME], notice_paths('appimage')), [])

    def test_tar_keeps_the_bundle_beside_the_launcher(self):
        files, built = self.package('tar')
        self.assertEqual(files[APP][1], 0o755)
        self.assertIn(f'share/{APP}/project/scripts/launch_runtime.py', files)
        for name in ('LICENSE', 'NOTICES.txt', 'NOTICES-emulator.txt', 'README.txt', 'add-to-menu.sh', f'{APP}.desktop', f'{APP}.png'):
            self.assertIn(name, files)
        self.assertEqual(lint_package({name: data for name, (data, _) in files.items()}, built, [HOME], notice_paths('tar')), [])

    def test_lint_refuses_private_and_unexpected_files(self):
        files, built = self.package('tar')
        data = {name: content for name, (content, _) in files.items()}
        cases = {
            'a sprite': {f'share/{APP}/project/tools/kirby.png': b'\x89PNG sprite'},
            'a ROM': {f'share/{APP}/rom/kssu.nds': b'\0' * 64},
            'a save state': {f'share/{APP}/project/tools/seed.state': b'state'},
            'an unknown program': {f'share/{APP}/bin/extra': b'\x7fELF other'},
            'a key': {'README.txt': b'token sk-' + b'b' * 40},
            'a home path': {f'share/{APP}/bin/libkssu_bridge.so': b'\x7fELF ' + HOME + b'.cargo/registry'},
            'a changed program': {APP: b'#!/bin/sh\n'},
            'a huge program': {APP: b'\x7fELF' + b'\0' * (64 * 1024 * 1024)},
        }
        for label, change in cases.items():
            with self.subTest(label):
                self.assertTrue(lint_package({**data, **change}, built, [HOME]))
        missing = dict(data)
        del missing[f'share/{APP}/bin/kssu-oracle-runner']
        del missing['NOTICES-emulator.txt']
        self.assertEqual(lint_package(missing, built, [HOME], notice_paths('tar')),
                         ['NOTICES-emulator.txt: missing', f'share/{APP}/bin/kssu-oracle-runner: missing'])

    def test_bundle_links_are_refused(self):
        (self.bundle / 'project/link').symlink_to('/etc/hostname')
        with self.assertRaises(SystemExit):
            self.package('tar')

    def test_tar_round_trip_is_reproducible(self):
        files, _ = self.package('tar')
        first, second = self.folder / 'first.tar.gz', self.folder / 'second.tar.gz'
        write_tar(first, 'KirbysDeadLand-0.2.0-x86_64', files, 1_760_000_000)
        write_tar(second, 'KirbysDeadLand-0.2.0-x86_64', files, 1_760_000_000)
        self.assertEqual(first.read_bytes(), second.read_bytes())
        self.assertEqual(read_tar(first, 'KirbysDeadLand-0.2.0-x86_64'), files)

    @unittest.skipUnless(shutil.which('mksquashfs') and shutil.which('unsquashfs'), 'squashfs-tools not installed')
    def test_appimage_round_trip(self):
        files, _ = self.package('appimage')
        runtime = b'\x7fELF runtime stand-in'
        image = self.folder / 'Kirby.AppImage'
        write_appimage(image, files, runtime, 1_760_000_000, self.folder)
        self.assertTrue(image.read_bytes().startswith(runtime))
        self.assertTrue(os.access(image, os.X_OK))
        self.assertEqual(read_appimage(image, len(runtime), self.folder), files)


class NoticesTests(unittest.TestCase):
    def test_emulator_notices_cover_the_pinned_crates_and_revisions(self):
        notices = NOTICES['NOTICES-emulator.txt'].read_text()
        for name, lock in LOCKS.items():
            with self.subTest(program=name):
                digest = hashlib.sha256(lock.read_bytes()).hexdigest()
                self.assertTrue(f'from packaging/{name}.Cargo.lock {digest[:16]}' in notices,
                                f'{lock.name} changed: run tools/emulator_notices.py')
        self.assertTrue(f'melonDS {CORE} ' in notices and f'melon-rs {WRAPPER} ' in notices,
                        'The pinned melonDS or melon-rs revision changed: run tools/emulator_notices.py')


class BundledFilesTests(unittest.TestCase):
    def test_no_bundled_file_names_a_ci_folder(self):
        # The package lint refuses files naming the build machine's folders; on GitHub
        # those are the workspace, the container's home and the runner's home.
        for name in project_files():
            with self.subTest(file=name):
                data = (PROJECT / name).read_bytes()
                for folder in (b'/__w/kirbys-dead-land/kirbys-dead-land', b'/github/home/', b'/home/runner/'):
                    self.assertNotIn(folder, data)


class BuildEnvironmentTests(unittest.TestCase):
    def test_build_folders_are_mapped_out_of_embedded_paths(self):
        root = Path.home() / '.local/share/kirby-dead-cells/release-build'
        env = build_environment(root)
        self.assertEqual(env['KIRBY_DATA_ROOT'], str(root))
        # rustc and GCC use the last matching mapping, Roslyn the first: the deepest folder must win.
        rust = [flag.split('=')[1] for flag in env['RUSTFLAGS'].split()[-3:]]
        self.assertEqual(sorted(rust, key=len), rust)
        roslyn = [item.split('=')[0] for item in env['PathMap'].split(',')]
        self.assertEqual(rust[::-1], roslyn)
        self.assertIn(f'--remap-path-prefix={Path.home()}=/build/home', env['RUSTFLAGS'])
        self.assertIn(f'-ffile-prefix-map={root}=/build/data', env['CXXFLAGS'])
        self.assertTrue(Path(env['CMAKE_TOOLCHAIN_FILE_x86_64_unknown_linux_gnu']).is_file())


class ReleaseWorkflowTests(unittest.TestCase):
    def test_one_version_for_the_mod_the_runtime_and_the_launcher(self):
        # build_packages.py --tag refuses a tag unless these agree.
        mod = json.loads((PROJECT / 'mod.json').read_text())['version']
        launcher = tomllib.loads((PROJECT / 'launcher/Cargo.toml').read_text())['package']['version']
        runtime = re.search(r'<Version>([^<]+)</Version>', (PROJECT / 'src/runtime/Kirby.Runtime.csproj').read_text()).group(1)
        self.assertEqual({mod, launcher, runtime}, {mod})

    def test_local_container_matches_the_workflow(self):
        workflow = (PROJECT / '.github/workflows/release.yml').read_text()
        local = (PROJECT / 'scripts/release_container.sh').read_text()
        image = re.search(r'ubuntu:22\.04@sha256:[0-9a-f]{64}', workflow).group()
        self.assertIn(f"IMAGE='{image}'", local)
        for step in ('bash scripts/release_tools.sh', 'python3 scripts/build_packages.py --output dist ${TAG:+--tag "$TAG"}',
                     'python3 scripts/package_release.py --ref HEAD --output dist'):
            with self.subTest(step=step):
                self.assertIn(step, workflow)
                self.assertIn(step, local)
        # Every action runs at a pinned commit.
        for use in re.findall(r'uses: (\S+)', workflow):
            self.assertRegex(use, r'@[0-9a-f]{40}$')


class ReleaseNotesTests(unittest.TestCase):
    """Every release page is notes.md filled in with that version's parts and pictures."""

    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location('release_notes', PROJECT / '.github/release/notes.py')
        cls.notes = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.notes)

    def test_this_version_has_its_notes_and_pictures(self):
        version = json.loads((PROJECT / 'mod.json').read_text())['version']
        self.assertEqual(self.notes.missing(f'v{version}'), [])

    def test_every_page_is_complete(self):
        for folder in sorted((PROJECT / '.github/release').glob('v*')):
            tag = folder.name
            with self.subTest(tag=tag):
                page = self.notes.render(tag, 'abc123', 'owner/repo', 15_309_304)
                self.assertNotRegex(page, r'\$[a-z]|<!--')
                for name in self.notes.PICTURES:
                    self.assertIn(f'https://raw.githubusercontent.com/owner/repo/abc123/.github/release/{tag}/{name}', page)
                self.assertIn(f'https://github.com/owner/repo/releases/download/{tag}/KirbysDeadLand-{tag[1:]}-x86_64.AppImage', page)
                self.assertIn('One file, about 15 MB', page)
                for heading in ('What you need', 'From download to Kirby in 4 steps', 'Your saves are safe', 'What was tested'):
                    self.assertIn(heading, page)
                # The first launcher release had nothing to update from.
                self.assertEqual('Already playing an older version?' in page, tag != 'v0.2.0')

    def test_missing_parts_stop_the_notes(self):
        with self.assertRaises(SystemExit):
            self.notes.render('v99.0.0', 'abc123', 'owner/repo')

    def test_the_workflow_writes_the_notes(self):
        workflow = (PROJECT / '.github/workflows/release.yml').read_text()
        self.assertIn('python3 .github/release/notes.py "$TAG"', workflow)
        self.assertIn('--notes-file notes/notes.md', workflow)


class MenuEntryTests(unittest.TestCase):
    def test_entry_names_this_folder(self):
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name) / 'Kirby $games 100%'
            folder.mkdir()
            for item in ('add-to-menu.sh', f'{APP}.desktop'):
                shutil.copy(PROJECT / 'packaging' / item, folder / item)
            (folder / f'{APP}.png').write_bytes(b'\x89PNG icon')
            data = Path(name) / 'data'
            subprocess.run(['sh', str(folder / 'add-to-menu.sh')], env=dict(os.environ, XDG_DATA_HOME=str(data)),
                           check=True, capture_output=True)
            entry = (data / f'applications/{APP}.desktop').read_text()
            self.assertIn(f'Exec="{Path(name)}/Kirby \\\\$games 100%%/{APP}"\n', entry)
            self.assertEqual(entry.count('Exec='), 1)
            self.assertTrue((data / f'icons/hicolor/256x256/apps/{APP}.png').is_file())
            if shutil.which('desktop-file-validate'):
                subprocess.run(['desktop-file-validate', str(data / f'applications/{APP}.desktop')], check=True)


if __name__ == '__main__':
    unittest.main()
