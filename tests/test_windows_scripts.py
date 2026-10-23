"""The scripts' Windows choices (#246), and the Linux ones they leave as they were."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

PROJECT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(PROJECT / 'scripts'), str(PROJECT / 'tools')]
import data_root  # noqa: E402
import lab_lock  # noqa: E402
import low_integrity  # noqa: E402
import setup_runtime  # noqa: E402
import stage_runtime  # noqa: E402
import steam_paths  # noqa: E402

LAUNCH = (PROJECT / 'scripts/launch_runtime.py').read_text()


class DataRootTests(unittest.TestCase):
    # data_root's view of the system only: pathlib keeps its own.
    @staticmethod
    def windows(environment):
        return mock.patch.object(data_root, 'os', SimpleNamespace(name='nt', environ=environment))

    def test_windows_root_is_in_local_app_data(self):
        with self.windows({'LOCALAPPDATA': '/users/player/AppData/Local'}):
            self.assertEqual(data_root.data_root(), Path('/users/player/AppData/Local/kirby-dead-cells'))

    def test_override_wins_on_windows(self):
        with tempfile.TemporaryDirectory() as folder, self.windows({'KIRBY_DATA_ROOT': folder, 'LOCALAPPDATA': '/elsewhere'}):
            self.assertEqual(data_root.data_root(), Path(folder).resolve())

    def test_linux_steam_is_unchanged(self):
        self.assertEqual(steam_paths.default_game(), Path.home() / '.local/share/Steam/steamapps/common/Dead Cells')


class LockTests(unittest.TestCase):
    def test_a_second_holder_is_refused_until_the_first_closes(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'lab.runtime.lock'
            first = lab_lock.hold(path)
            self.assertIsNotNone(first)
            self.assertIsNone(lab_lock.hold(path))
            first.close()
            again = lab_lock.hold(path)
            self.assertIsNotNone(again)
            again.close()

    def test_scripts_take_the_shared_lock(self):
        for name in ('stage_runtime.py', 'launch_runtime.py'):
            text = (PROJECT / 'scripts' / name).read_text()
            with self.subTest(script=name):
                self.assertNotIn('fcntl', text)
                self.assertIn("hold(root / 'lab.runtime.lock')", text)


class DownloadTests(unittest.TestCase):
    def test_linux_downloads_are_unchanged(self):
        self.assertEqual(setup_runtime.DCCM_URL, 'https://github.com/dead-cells-core-modding/core/releases/download/v35.13.6/linux-x64.zip')
        self.assertEqual(setup_runtime.DCCM_SHA256, 'c57b1fca88e688d228fe5e0aa2b3b4789117f7ff9b092af5c9fd5ada1d3c14a2')
        self.assertEqual(setup_runtime.SDK_URL, 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz')
        self.assertTrue(setup_runtime.SDK_SHA512.startswith('51c8b999af9e8dd9'))
        self.assertEqual(setup_runtime.sdk_program(Path('/data')), Path('/data/dotnet/dotnet'))

    def test_windows_downloads_are_pinned(self):
        self.assertEqual(set(setup_runtime.DCCM_ARCHIVES), {'linux-x64', 'win-x64'})
        self.assertEqual(len(setup_runtime.DCCM_ARCHIVES['win-x64']), 64)
        kind, digest = setup_runtime.SDK_ARCHIVES['win-x64']
        self.assertEqual((kind, len(digest)), ('zip', 128))

    def test_zip_paths_stay_inside(self):
        import zipfile
        with tempfile.TemporaryDirectory() as folder:
            archive = Path(folder) / 'bad.zip'
            with zipfile.ZipFile(archive, 'w') as out:
                out.writestr('../outside.txt', 'x')
            with self.assertRaises(RuntimeError):
                setup_runtime.extract_zip(archive, Path(folder) / 'out')
            self.assertFalse((Path(folder) / 'outside.txt').exists())


class PlaceTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        root = Path(self.folder.name)
        self.game, self.lab = root / 'game', root / 'lab'
        self.game.mkdir()
        self.lab.mkdir()
        self.source = self.game / 'res.pak'
        self.source.write_bytes(b'pak one')

    def tearDown(self):
        self.folder.cleanup()

    def test_same_drive_gets_a_hard_link_once(self):
        placed = {}
        stage_runtime.place(self.source, self.lab / 'res.pak', placed)
        self.assertTrue(os.path.samefile(self.source, self.lab / 'res.pak'))
        self.assertFalse((self.lab / 'res.pak').is_symlink())
        stage_runtime.place(self.source, self.lab / 'res.pak', placed)
        self.assertEqual(placed['res.pak'], stage_runtime.stamp(self.source))

    def test_another_drive_gets_a_copy(self):
        placed = {}
        with mock.patch.object(stage_runtime.os, 'link', side_effect=OSError(18, 'Invalid cross-device link')):
            stage_runtime.place(self.source, self.lab / 'res.pak', placed)
        copy = self.lab / 'res.pak'
        self.assertFalse(os.path.samefile(self.source, copy))
        self.assertEqual(copy.read_bytes(), b'pak one')
        self.assertEqual(stage_runtime.stamp(copy), stage_runtime.stamp(self.source))
        self.assertFalse((self.lab / 'res.pak.part').exists())

    def test_a_placed_file_follows_a_game_update(self):
        placed = {}
        with mock.patch.object(stage_runtime.os, 'link', side_effect=OSError(18, 'Invalid cross-device link')):
            stage_runtime.place(self.source, self.lab / 'res.pak', placed)
        self.source.unlink()
        self.source.write_bytes(b'pak two, updated')
        stage_runtime.place(self.source, self.lab / 'res.pak', placed)
        self.assertEqual((self.lab / 'res.pak').read_bytes(), b'pak two, updated')

    def test_other_lab_files_are_refused(self):
        (self.lab / 'res.pak').write_bytes(b'something else')
        with self.assertRaises(SystemExit):
            stage_runtime.place(self.source, self.lab / 'res.pak', {})
        (self.lab / 'res.pak').unlink()
        (self.lab / 'res.pak').symlink_to(self.source)
        with self.assertRaises(SystemExit):
            stage_runtime.place(self.source, self.lab / 'res.pak', {'res.pak': stage_runtime.stamp(self.source)})

    def test_linux_game_files_are_unchanged(self):
        self.assertEqual(stage_runtime.GAME_FILES, ['hlboot.dat', 'res.pak', 'steam.hdll', 'libsteam_api.so'])
        self.assertEqual(stage_runtime.OPTIONAL_GAME_FILES, [])


class LaunchTests(unittest.TestCase):
    def test_environment_block(self):
        block = low_integrity.environment_block({'Path': 'a', 'TEMP': 'x', 'PATH': 'b', 'SteamAppId': '588650'})
        self.assertEqual(block, 'PATH=b\0SteamAppId=588650\0TEMP=x\0\0')

    def test_labels(self):
        self.assertEqual(low_integrity.LOW, 'S-1-16-4096')
        self.assertEqual(low_integrity.FOLDER_LABEL, 'S:(ML;OICI;NW;;;LW)')
        self.assertEqual(low_integrity.FILE_LABEL, 'S:(ML;;NW;;;LW)')

    def test_linux_launch_is_unchanged(self):
        self.assertIn("command = ['bwrap', '--die-with-parent', '--unshare-pid', '--ro-bind', '/', '/',", LAUNCH)
        self.assertIn("env['LD_PRELOAD'] = str(turbojpeg_library(root))", LAUNCH)
        self.assertIn("else game.parents[2]", LAUNCH)

    def test_windows_launch_is_sandboxed(self):
        self.assertIn('label_low(lab)', LAUNCH)
        self.assertIn('WindowsProcess(command, env, lab, output, low=not args.normal_integrity)', LAUNCH)
        self.assertIn("('DeadCellsModding.exe' if WINDOWS else 'DeadCellsModding')", LAUNCH)
        self.assertIn('(mod / TARGETS[HOST][1]).is_file()', LAUNCH)

    def test_a_low_integrity_game_reads_controllers_without_directinput(self):
        sandboxed = LAUNCH.index('label_low(lab)')
        hint = LAUNCH.index("env['SDL_JOYSTICK_DIRECTINPUT'] = '0'")
        self.assertLess(LAUNCH.index('if args.normal_integrity:'), sandboxed)
        self.assertLess(sandboxed, hint)
        self.assertLess(hint, LAUNCH.index("command = ['bwrap'"))
        self.assertEqual(LAUNCH.count('SDL_JOYSTICK_DIRECTINPUT'), 1)

    def test_steam_input_is_opened_to_the_game_before_xinput(self):
        # Steam's overlay crashes in XInput when the game cannot open these (#257).
        steam = LAUNCH.index('labeled, refused = label_steam_input()')
        self.assertLess(LAUNCH.index('label_low(lab)'), steam)
        self.assertLess(steam, LAUNCH.index('if refused:'))
        self.assertLess(LAUNCH.index('if refused:'), LAUNCH.index("env['SDL_JOYSTICK_DIRECTINPUT'] = '0'"))
        self.assertIn("            else:\n                env['SDL_JOYSTICK_DIRECTINPUT'] = '0'", LAUNCH)

    def test_only_the_chosen_steam_objects_are_labeled(self):
        names = [name for _, name in low_integrity.STEAM_INPUT_OBJECTS]
        self.assertEqual(len(names), 11)
        self.assertEqual(len(set(names)), 11)
        self.assertTrue(all(name.endswith('-IPCWrapper') and name.startswith(('SteamXInput_', 'SteamController_'))
                            for name in names))
        self.assertEqual({kind for kind, _ in low_integrity.STEAM_INPUT_OBJECTS}, {'mapping', 'mutex', 'event'})
        self.assertIn(('mapping', 'SteamXInput_mem-IPCWrapper'), low_integrity.STEAM_INPUT_OBJECTS)

    def test_the_launcher_stops_a_windows_game_through_the_input(self):
        self.assertIn("if WINDOWS and args.play and sys.stdin is not None:\n            threading.Thread(target=stop_on_request, daemon=True).start()", LAUNCH)
        self.assertIn('not (WINDOWS and stopped)', LAUNCH)
        import launch_runtime
        with mock.patch.object(launch_runtime.sys, 'stdin', ['hello\n', 'stop\n', 'more\n']), \
                mock.patch.object(launch_runtime._thread, 'interrupt_main') as interrupt:
            launch_runtime.stop_on_request()
        interrupt.assert_called_once_with()

    def test_normal_integrity_is_refused_on_linux(self):
        result = subprocess.run([sys.executable, str(PROJECT / 'scripts/launch_runtime.py'), '--play', '--normal-integrity'],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn('--normal-integrity applies to Windows', result.stderr)


if __name__ == '__main__':
    unittest.main()
