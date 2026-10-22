"""Kirby's audio uses the SDL3 that DCCM loads into the game on Linux and Windows (#248)."""
from pathlib import Path
import re
import unittest

PROJECT = Path(__file__).resolve().parents[1]
AUDIO = (PROJECT / 'src/runtime/NdsAudio.cs').read_text()
BINDINGS = AUDIO[AUDIO.index('private static class Sdl'):]
DECLARATION = re.compile(r'EntryPoint = "(SDL_\w+)"[^\]]*\](\[return: MarshalAs\(UnmanagedType\.I1\)\])? internal static extern (\w+) ')
# DCCM 35.13.6 ships no SDL2, and SDL3 removed these.
SDL2_ONLY = {'SDL_OpenAudioDevice', 'SDL_PauseAudioDevice', 'SDL_QueueAudio', 'SDL_GetQueuedAudioSize',
             'SDL_ClearQueuedAudio', 'SDL_CloseAudioDevice'}
# SDL3's C result type for each function the stream uses.
SDL3 = {
    'SDL_InitSubSystem': 'bool', 'SDL_QuitSubSystem': 'void', 'SDL_GetError': 'nint',
    'SDL_OpenAudioDeviceStream': 'nint', 'SDL_ResumeAudioStreamDevice': 'bool', 'SDL_DestroyAudioStream': 'void',
    'SDL_PutAudioStreamData': 'bool', 'SDL_GetAudioStreamQueued': 'int', 'SDL_ClearAudioStream': 'bool',
}


class SdlAudioTests(unittest.TestCase):
    def test_binds_the_sdl3_stream_functions(self):
        declared = {name: result for name, _, result in DECLARATION.findall(BINDINGS)}
        self.assertEqual(declared, SDL3)
        self.assertFalse(SDL2_ONLY & set(re.findall(r'SDL_\w+', AUDIO)))

    def test_c_bool_results_are_one_byte(self):
        for name, marshal, result in DECLARATION.findall(BINDINGS):
            self.assertEqual(bool(marshal), result == 'bool', name)

    def test_spec_has_the_sdl3_layout(self):
        spec = re.search(r'struct AudioSpec\s*\{(.*?)\}', AUDIO, re.S).group(1)
        # SDL_AudioFormat format; int channels; int freq.
        self.assertEqual(spec.split(), ['public', 'uint', 'Format;', 'public', 'int', 'Channels,', 'Frequency;'])
        self.assertIn('InitAudio = 0x10, DefaultPlayback = 0xFFFFFFFF, SignedLittle16 = 0x8010;', AUDIO)
        self.assertIn('new() { Format = SignedLittle16, Channels = 2, Frequency = SampleRate }', AUDIO)

    def test_stream_starts_and_ends_once(self):
        constructor = AUDIO[AUDIO.index('public NdsAudio('):AUDIO.index('public static void PrepareSource')]
        # The device opens paused, and a failed resume leaves no stream behind.
        self.assertLess(constructor.index('Sdl.Open('), constructor.index('Sdl.Resume(stream)'))
        self.assertIn('{ DeviceError = Error(); Sdl.Destroy(stream); stream = 0; }', constructor)
        dispose = AUDIO[AUDIO.index('public void Dispose()'):AUDIO.index('private uint Queued()')]
        # The audio subsystem reference is the runtime's own, balanced against the host's.
        self.assertLess(dispose.index('Sdl.Destroy(stream)'), dispose.index('if (initialized) { Sdl.Quit(InitAudio)'))
        self.assertEqual(AUDIO.count('Sdl.Init('), 1)
        self.assertEqual(AUDIO.count('Sdl.Quit('), 1)

    def test_queue_failure_reads_as_empty(self):
        # SDL_GetAudioStreamQueued returns -1 on failure.
        self.assertIn('(uint)Math.Max(0, Sdl.Queued(stream))', AUDIO)
        self.assertEqual(AUDIO.count('Sdl.Queued('), 1)

    def test_activation_logs_the_device_result(self):
        provider = (PROJECT / 'src/runtime/KirbyNdsProvider.cs').read_text()
        opened = provider.index('audio = new NdsAudio(nds);')
        self.assertLess(opened, provider.index('"Kirby audio unavailable: " + audioError', opened))


if __name__ == '__main__':
    unittest.main()
