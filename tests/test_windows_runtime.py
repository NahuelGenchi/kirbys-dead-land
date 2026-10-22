"""The runtime adapter names its native libraries for Linux and Windows from one source (#244)."""
from pathlib import Path
import re
import sys
import unittest

PROJECT = Path(__file__).resolve().parents[1]
RUNTIME = PROJECT / 'src/runtime'
sys.path.insert(0, str(PROJECT / 'tools'))
import oracle_build  # noqa: E402

LIBRARIES = (RUNTIME / 'NativeLibraries.cs').read_text()
IMPORT = re.compile(r'DllImport\(\s*([^,)\s]+)')
# The performance probe returns before any of its imports on other systems.
LINUX_ONLY = {'LabPerfProbe.cs': {'"libhl.so"', '"libc"', '"libGL.so.1"'}}


def mapping(name, windows):
    """The file NativeLibraries.File gives a short name, read from its switch."""
    body = re.search(r'File\(string name, bool windows\) => name switch\s*\{(.*?)\};', LIBRARIES, re.S).group(1)
    for key, on_windows, on_linux in re.findall(r'(\w+) => windows \? "([^"]+)" : "([^"]+)"', body):
        if key == name:
            return on_windows if windows else on_linux
    return None


class WindowsRuntimeTests(unittest.TestCase):
    def test_imports_use_the_short_names(self):
        allowed = {'"kssu_bridge"', 'NativeLibraries.Bridge', 'NativeLibraries.Sdl', 'NativeLibraries.OpenGl', 'Library'}
        for source in sorted(RUNTIME.glob('*.cs')):
            names = set(IMPORT.findall(source.read_text()))
            self.assertLessEqual(names - LINUX_ONLY.get(source.name, set()), allowed, source.name)

    def test_short_names_match_the_constants(self):
        self.assertIn('public const string Bridge = "kssu_bridge", Sdl = "SDL2", OpenGl = "GL";', LIBRARIES)
        self.assertIn('private const string Library = NativeLibraries.Sdl;', (RUNTIME / 'NdsAudio.cs').read_text())

    def test_each_system_gets_its_own_files(self):
        self.assertEqual(mapping('Sdl', windows=False), 'libSDL2-2.0.so.0')
        self.assertEqual(mapping('Sdl', windows=True), 'SDL2.dll')
        self.assertEqual(mapping('OpenGl', windows=False), 'libGL.so.1')
        self.assertEqual(mapping('OpenGl', windows=True), 'opengl32.dll')

    def test_bridge_file_is_the_one_the_build_makes(self):
        linux, windows = oracle_build.TARGETS['linux'][1], oracle_build.TARGETS['windows'][1]
        self.assertIn(f'OperatingSystem.IsWindows() ? "{windows}" : "{linux}"', LIBRARIES)
        provider = (RUNTIME / 'KirbyNdsProvider.cs').read_text()
        self.assertNotIn('libkssu_bridge.so', provider)
        self.assertEqual(provider.count('NativeLibraries.BridgeFile'), 2)

    def test_one_resolver_for_the_assembly(self):
        sources = {source.name: source.read_text() for source in RUNTIME.glob('*.cs')}
        self.assertEqual([name for name, text in sources.items() if 'SetDllImportResolver' in text], ['NativeLibraries.cs'])
        self.assertIn('[ModuleInitializer]', LIBRARIES)
        self.assertIn('NativeLibraries.BridgeHandle = library;', sources['NdsRuntime.cs'])

    def test_performance_probe_stops_off_linux(self):
        probe = (RUNTIME / 'LabPerfProbe.cs').read_text()
        install = probe[probe.index('static void Install()'):probe.index('static void InstallHashlinkStats()')]
        # Nothing the probe tracks, and so none of its Linux imports, starts before the guard.
        self.assertLess(install.index('if (!OperatingSystem.IsLinux())'), install.index('tracking = true'))

    def test_test_projects_link_the_resolver_with_the_runtime(self):
        for project in sorted(PROJECT.glob('tests/*/*.csproj')):
            text = project.read_text()
            if 'src/runtime/NdsRuntime.cs' in text:
                self.assertIn('src/runtime/NativeLibraries.cs', text, project.parent.name)

    def test_ready_to_run_targets_the_chosen_system(self):
        project = (RUNTIME / 'Kirby.Runtime.csproj').read_text()
        self.assertNotIn('--targetos:linux', project)
        self.assertIn('--targetos:$(ModReadyToRunOS);', project)
        self.assertIn("<ModReadyToRunOS Condition=\"'$(ModReadyToRunOS)' == ''\">$(ModBuildOS)</ModReadyToRunOS>", project)
        self.assertIn('Microsoft.NETCore.App.Runtime.win-x64', project)


if __name__ == '__main__':
    unittest.main()
