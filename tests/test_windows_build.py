"""The Windows cross-build of the bridge and oracle runner (#242); the Linux build stays as it was."""
from pathlib import Path
import re
import shutil
import subprocess
import sys
import unittest
from unittest import mock

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT / 'tools'))
import oracle_build  # noqa: E402

VENDOR = Path('/data/oracle/vendor/melon-rs')
KEY = 'x86_64_pc_windows_gnu'
# The five melonDS Platform callbacks src/nds/melon_windows.rs defines, as
# melonDS 82fdbc78's Platform.h declares them.
CALLBACKS = {
    'melonDS::Platform::WriteGBASave(unsigned char const*, unsigned int, unsigned int, unsigned int, void*)',
    'melonDS::Platform::Addon_KeyDown(melonDS::Platform::KeyType, void*)',
    'melonDS::Platform::Addon_RumbleStart(unsigned int, void*)',
    'melonDS::Platform::Addon_RumbleStop(void*)',
    'melonDS::Platform::Addon_MotionQuery(melonDS::Platform::MotionQueryType, void*)',
}


def windows_environment(env, which=lambda tool: f'/usr/bin/{tool}', rust=True):
    """windows_environment with the compilers and, unless rust is false, Rust's target present."""
    library = '/rust/lib'
    folder = Path(library)
    with mock.patch.object(oracle_build.shutil, 'which', which), \
            mock.patch.object(oracle_build.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, library + '\n', '')), \
            mock.patch.object(Path, 'glob', lambda self, pattern: iter([folder / 'libstd-0.rlib']) if rust and self == folder else iter([])):
        return oracle_build.windows_environment(env)


class WindowsBuildTests(unittest.TestCase):
    def test_linux_outputs_are_unchanged(self):
        self.assertEqual(oracle_build.bridge_binary(VENDOR, 'linux'), VENDOR / 'target/release/libkssu_bridge.so')
        self.assertEqual(oracle_build.runner_binary(VENDOR, 'linux'), VENDOR / 'target/release/kssu-oracle-runner')
        self.assertEqual(oracle_build.provenance_file('bridge', 'linux').name, 'bridge-provenance.json')
        self.assertEqual(oracle_build.provenance_file('build', 'linux').name, 'build-provenance.json')

    def test_windows_outputs_have_their_own_folder_and_records(self):
        release = VENDOR / 'target/x86_64-pc-windows-gnu/release'
        self.assertEqual(oracle_build.bridge_binary(VENDOR, 'windows'), release / 'kssu_bridge.dll')
        self.assertEqual(oracle_build.runner_binary(VENDOR, 'windows'), release / 'kssu-oracle-runner.exe')
        self.assertEqual(oracle_build.provenance_file('bridge', 'windows').name, 'bridge-provenance-windows.json')
        self.assertEqual(oracle_build.provenance_file('build', 'windows').name, 'build-provenance-windows.json')

    def test_tools_load_this_systems_build(self):
        self.assertEqual(oracle_build.HOST, 'linux')
        self.assertEqual(oracle_build.bridge_binary(VENDOR), oracle_build.bridge_binary(VENDOR, 'linux'))

    def test_windows_environment_keeps_a_builds_own_flags(self):
        env = windows_environment({'CFLAGS': '-ffile-prefix-map=/a=/b', 'CXXFLAGS': '-ffile-prefix-map=/a=/b',
                                   'RUSTFLAGS': '--remap-path-prefix=/a=/b'})
        self.assertEqual(env[f'CC_{KEY}'], 'x86_64-w64-mingw32-gcc-posix')
        self.assertEqual(env[f'CXX_{KEY}'], 'x86_64-w64-mingw32-g++-posix')
        self.assertEqual(env['CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER'], 'x86_64-w64-mingw32-gcc-posix')
        self.assertEqual(env[f'CFLAGS_{KEY}'], '-ffile-prefix-map=/a=/b -fno-data-sections')
        self.assertEqual(env[f'CXXFLAGS_{KEY}'], '-ffile-prefix-map=/a=/b -fno-data-sections')
        self.assertEqual(env['RUSTFLAGS'], '--remap-path-prefix=/a=/b -C link-arg=-static')
        self.assertEqual(env[f'CXXSTDLIB_{KEY}'], 'static:-bundle=stdc++')
        self.assertEqual(env[f'CMAKE_TOOLCHAIN_FILE_{KEY}'], str(PROJECT / 'packaging/melonds-windows.cmake'))
        self.assertEqual(windows_environment({f'CMAKE_TOOLCHAIN_FILE_{KEY}': '/x.cmake'})[f'CMAKE_TOOLCHAIN_FILE_{KEY}'], '/x.cmake')
        self.assertEqual(windows_environment({})[f'CXXFLAGS_{KEY}'], '-fno-data-sections')

    def test_windows_environment_names_what_is_missing(self):
        with self.assertRaisesRegex(SystemExit, 'g\\+\\+-mingw-w64-x86-64-posix'):
            windows_environment({}, which=lambda tool: None)
        with self.assertRaisesRegex(SystemExit, 'rustup target add x86_64-pc-windows-gnu'):
            windows_environment({}, rust=False)

    def test_only_windows_builds_name_a_target(self):
        calls = []
        with mock.patch.object(oracle_build.subprocess, 'run', lambda command, **kw: calls.append((command, kw['env']))), \
                mock.patch.object(oracle_build, 'windows_environment', lambda env: {'MARK': '1'}):
            oracle_build.cargo_build(Path('/c/Cargo.toml'), VENDOR, 'linux')
            oracle_build.cargo_build(Path('/c/Cargo.toml'), VENDOR, 'windows')
        (linux, linux_env), (windows, windows_env) = calls
        self.assertNotIn('--target', linux)
        self.assertNotIn('MARK', linux_env)
        self.assertEqual(windows[-2:], ['--target', 'x86_64-pc-windows-gnu'])
        self.assertEqual(windows_env['MARK'], '1')
        self.assertEqual(linux_env['CARGO_TARGET_DIR'], str(VENDOR / 'target'))

    def test_toolchain_file_uses_posix_threads_without_lto(self):
        text = (PROJECT / 'packaging/melonds-windows.cmake').read_text()
        self.assertIn('set(CMAKE_SYSTEM_NAME Windows)', text)
        self.assertIn('set(CMAKE_CXX_COMPILER x86_64-w64-mingw32-g++-posix)', text)
        self.assertIn('set(ENABLE_LTO_RELEASE OFF CACHE BOOL', text)

    def test_callbacks_come_last_and_only_on_windows(self):
        # Last, so the Linux build, which leaves the module out, keeps every line number.
        for path, ending in [('src/nds/lib.rs', ['#[cfg(windows)]', 'mod melon_windows;']),
                             ('tools/oracle_runner.rs', ['#[cfg(windows)]', '#[path = "../src/nds/melon_windows.rs"]', 'mod melon_windows;'])]:
            with self.subTest(path=path):
                lines = (PROJECT / path).read_text().rstrip('\n').splitlines()
                self.assertEqual(lines[-len(ending):], ending)

    @unittest.skipUnless(shutil.which('c++filt'), 'c++filt (binutils) is needed to read the names')
    def test_callback_names_match_melonds_declarations(self):
        names = re.findall(r'#\[export_name = "([^"]+)"\]', (PROJECT / 'src/nds/melon_windows.rs').read_text())
        demangled = subprocess.check_output(['c++filt', *names], text=True).splitlines()
        self.assertEqual(set(demangled), CALLBACKS)


if __name__ == '__main__':
    unittest.main()
