# Original sound effects

The native bridge retains stereo PCM produced by the original ROM's sound driver. `kssu_read_audio` drains signed 16-bit interleaved samples; capacity and returned count are sample counts, not stereo frame counts. The export never separates a stereo pair. The queue is bounded at 65,536 samples and reset when a source state is loaded.

melonDS already resamples its hardware output to 48,000 Hz. The bridge sets output skew to 1.0 because the host steps the original 59.826098 Hz frame clock. Treating the export as 32,768 Hz would slow and pitch-shift the effects. The measured 180-frame sequence returned 288,836 interleaved samples, consistent with 48 kHz stereo at the original frame rate.

`NdsAudio` opens an independent SDL3 playback stream on the default device in that format (`SDL_OpenAudioDeviceStream`); SDL converts the stream to whatever the device uses. It binds the SDL3 that DCCM ships and loads into the game on Linux and Windows (`libSDL3.so.0`, `SDL3.dll`; see [SDL3 on both systems](#sdl3-on-both-systems-248)). It drains the native queue after source stepping, applies the host effect gain, bounds device backlog, clears pending effects on pause and disposes its device when the outfit deactivates. Its SDL audio subsystem reference is balanced independently of the host's reference. Device failure is exposed through `DeviceError`, logged once per activation (`Kirby audio unavailable: …`), and does not prevent original gameplay stepping. Physical playback still depends on the selected audio device.

Source stage music is muted through the original driver, rather than filtering the mixed PCM. The supplied ROM's SDAT symbol table names sequence player 0 `BGM_Player`; the jump player is separately named `PLAYER_JUMP`. Reverse inspection of the original ARM9 routines identifies the NitroSystem player table at `0x0205EBC4`, with 0x24-byte records and a volume byte at +0x20. Therefore writing zero at `0x0205EBE4` mutes the source music player while preserving the effect players. The table layout agrees with the independently reconstructed [NitroSystem player implementation](https://github.com/pret/pokediamond/blob/master/arm9/asm/NNS_SND_player.s). The driver applies this volume during its normal update. No ROM file is modified.

The hidden collision carrier and host-enemy proxies use Waddle Dee's original movement maintenance. Its walking animation also emits sequence295 `SE_ARUKI` on `PLAYER_TR1` every31 source frames, even when that internal object is parked outside the scene. This was identified from live sound-driver records, then isolated with a separate no-carrier build. The production bridge validates and suppresses only six original walking-script sound calls in emulated RAM; the remaining animation, AI, physics, collision maintenance, Kirby sounds and other effects keep running. Private RAM probes and the production build both recovered exactly silent idle/settled intervals while retaining the original jump and landing peaks. The strict audio assertions were retained.

Verification uses an isolated flat source map so original stage enemies do not contribute their own effects. With the music player muted, source idle frames 10–59 and settled frames 120–179 contain exactly zero PCM. The original jump and landing remain audible in the source data, with measured sample peaks 1,571 and 5,597. With music enabled, the same idle interval has RMS 1,427.68. A C# test also validates the ABI, stereo sample rate, SDL queue, gain path, pause clearing and disposal using SDL's dummy output driver:

```sh
dotnet build tests/audio/Kirby.Audio.Tests.csproj -c Release
# DCCM's own libSDL3.so.0, the one the game loads.
LD_LIBRARY_PATH=~/.local/share/kirby-dead-cells/research/dccm-35.13.6/core/native/linux-x64 \
SDL_AUDIO_DRIVER=dummy dotnet tests/audio/bin/Release/net10.0/Kirby.Audio.Tests.dll \
  ~/.local/share/kirby-dead-cells/oracle/vendor/melon-rs/target/release/libkssu_bridge.so \
  "$HOME/Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds" \
  ~/.local/share/kirby-dead-cells/oracle/ready2/000.state
```

Dummy-device verification establishes the playback code and queue behavior, not physical speaker output. The source-music mute intentionally leaves Dead Cells music available. Original full-screen musical cutscenes and cinematic BGM therefore do not play through this character effect stream.

Before #248 the stream was an SDL2 queue, and the test also passed with the host's SDL3 binary globally preloaded alongside SDL2. The separate constructor crash investigation and poisoned-allocation regression are documented in [NATIVE_INITIALIZATION.md](NATIVE_INITIALIZATION.md).

With SDL2, the same strict test also passed with SDL audio-driver overrides removed: the default desktop output device opened and queued all 288,836 samples, with identical sample peaks and successful pause/disposal checks. This verifies the software path to that output device; acoustic output was not independently recorded.

## SDL3 on both systems (#248)

DCCM 35.13.6 ships SDL3 alone. `linux-x64.zip` has `core/native/linux-x64/libSDL3.so.0` (SDL 3.4.2), and its `sdl.hdll` needs that file; `win-x64.zip` has `core/native/win-x64/SDL3.dll` (SDL 3.4.16), and its `sdl.hdll` imports it. Neither package has an SDL2. The host's .NET side uses `SDL3-CS` on both. On Linux the lab puts only DCCM's native folder on `LD_LIBRARY_PATH`, so the old SDL2 binding found the system's `libSDL2-2.0.so.0` (here `libsdl2-classic` 2.32.10), a second SDL beside the game's; a player without that package would have had silent effects. Windows has no system SDL2.

`NdsAudio` therefore uses SDL3's audio-stream API, and `NativeLibraries` maps `SDL3` to `libSDL3.so.0` and `SDL3.dll`. Both names resolve to the copy the game already loaded: the loader matches the Linux soname and the Windows module name before searching. The game plays its own sound through OpenAL, so the SDL3 audio subsystem is the runtime's alone; `SDL_InitSubSystem` and `SDL_QuitSubSystem` keep their count balanced. The stream keeps the backlog bounds, pause clearing and gain path above. SDL3 picks the device period itself, where the SDL2 code asked for 1,024 frames.

Checked on 2026-10-21:

- `tests/audio` against DCCM's Linux `libSDL3.so.0` (loader trace confirms that file), `SDL_AUDIO_DRIVER=dummy`: passes, 288,836 samples, jump peak 1,571, landing peak 5,597, queue bounds, pause and disposal. With no driver override it passes the same way, and the process opened a PipeWire `Stream/Output/Audio` node ("SDL Application"). Acoustic output was not recorded.
- The same test published for `win-x64` (self-contained .NET 10.0.12) under Wine (Proton Experimental), with the #242 `kssu_bridge.dll` and DCCM's `SDL3.dll` beside it: passes with the dummy driver and with Wine's default device, with the same numbers. The SDL2 build of the same test from `main` fails there with `Unable to load DLL 'SDL2'`. Wine is not Windows 11.
- One isolated Linux live run: see [RUNTIME_LAUNCH.md](RUNTIME_LAUNCH.md#sdl3-audio-248).
- Not checked: Windows 11, and sound on a real Windows device.
