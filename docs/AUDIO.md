# Original sound effects

The native bridge retains stereo PCM produced by the original ROM's sound driver. `kssu_read_audio` drains signed 16-bit interleaved samples; capacity and returned count are sample counts, not stereo frame counts. The export never separates a stereo pair. The queue is bounded at 65,536 samples and reset when a source state is loaded.

melonDS already resamples its hardware output to 48,000 Hz. The bridge sets output skew to 1.0 because the host steps the original 59.826098 Hz frame clock. Treating the export as 32,768 Hz would slow and pitch-shift the effects. The measured 180-frame sequence returned 288,836 interleaved samples, consistent with 48 kHz stereo at the original frame rate.

`NdsAudio` opens an independent SDL2 queued playback device using that format. It drains the native queue after source stepping, applies the host effect gain, bounds device backlog, clears pending effects on pause and disposes its device when the outfit deactivates. Its SDL audio subsystem reference is balanced independently of the host's reference. Device failure is exposed through `DeviceError` and does not prevent original gameplay stepping. Physical playback still depends on the selected audio device.

Source stage music is muted through the original driver, rather than filtering the mixed PCM. The supplied ROM's SDAT symbol table names sequence player 0 `BGM_Player`; the jump player is separately named `PLAYER_JUMP`. Reverse inspection of the original ARM9 routines identifies the NitroSystem player table at `0x0205EBC4`, with 0x24-byte records and a volume byte at +0x20. Therefore writing zero at `0x0205EBE4` mutes the source music player while preserving the effect players. The table layout agrees with the independently reconstructed [NitroSystem player implementation](https://github.com/pret/pokediamond/blob/master/arm9/asm/NNS_SND_player.s). The driver applies this volume during its normal update. No ROM file is modified.

The hidden collision carrier and host-enemy proxies use Waddle Dee's original movement maintenance. Its walking animation also emits sequence295 `SE_ARUKI` on `PLAYER_TR1` every31 source frames, even when that internal object is parked outside the scene. This was identified from live sound-driver records, then isolated with a separate no-carrier build. The production bridge validates and suppresses only six original walking-script sound calls in emulated RAM; the remaining animation, AI, physics, collision maintenance, Kirby sounds and other effects keep running. Private RAM probes and the production build both recovered exactly silent idle/settled intervals while retaining the original jump and landing peaks. The strict audio assertions were retained.

Verification uses an isolated flat source map so original stage enemies do not contribute their own effects. With the music player muted, source idle frames 10–59 and settled frames 120–179 contain exactly zero PCM. The original jump and landing remain audible in the source data, with measured sample peaks 1,571 and 5,597. With music enabled, the same idle interval has RMS 1,427.68. A C# test also validates the ABI, stereo sample rate, SDL queue, gain path, pause clearing and disposal using SDL's dummy output driver:

```sh
dotnet build tests/audio/Kirby.Audio.Tests.csproj -c Release
SDL_AUDIODRIVER=dummy dotnet tests/audio/bin/Release/net10.0/Kirby.Audio.Tests.dll \
  ~/.local/share/kirby-dead-cells/oracle/vendor/melon-rs/target/release/libkssu_bridge.so \
  "$HOME/Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds" \
  ~/.local/share/kirby-dead-cells/oracle/ready2/000.state
```

Dummy-device verification establishes the playback code and queue behavior, not physical speaker output. The source-music mute intentionally leaves Dead Cells music available. Original full-screen musical cutscenes and cinematic BGM therefore do not play through this character effect stream.

The production audio test also passed with the host's actual SDL3 binary globally preloaded alongside SDL2. The separate constructor crash investigation and poisoned-allocation regression are documented in [NATIVE_INITIALIZATION.md](NATIVE_INITIALIZATION.md).

The same strict test also passed with SDL audio-driver overrides removed: the default desktop output device opened and queued all 288,836 samples, with identical sample peaks and successful pause/disposal checks. This verifies the software path to that output device; acoustic output was not independently recorded.
