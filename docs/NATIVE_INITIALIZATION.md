# Native GPU constructor repair

The pinned melonDS core could crash before the first emulated frame. The recovered Linux core dump places the fault in `GPU::SyncAllVRAMCaptures`, called by `GPU::SetRenderer` from the `NDS` constructor. No SDL library was involved in that process.

`GPU::GPU` calls `SetRenderer` before the normal `Reset`. At that point its renderer pointer is null, but `VRAMCaptureBlockFlags[16]` was uninitialized. A stale `IsCapture` bit without `Synced` made the synchronization loop call `Rend->SyncVRAMCapture` through that null pointer. The faulting registers contain a null renderer and flags `0x8AEB`.

The checked patch in `tools/nds_graphics_patch.py` changes the member declaration to `u16 VRAMCaptureBlockFlags[16] {};`. This initializes the empty capture state before any constructor call, without changing running-frame behavior. Both native build paths validate this exact addition against the pinned source revision.

The original binary reliably crashed on its first construction with glibc `MALLOC_PERTURB_=111`, whose allocated bytes become `0x90`. The corrected binary passed 64 real machine construction, first-frame, and disposal cycles across eight poison values. The regression loads the owned ROM and private seed in separate Python workers; it loads no SDL, opens no window or audio device, and does not touch host saves:

```sh
python3 tools/verify_native_init.py
```

Its private JSON report records the tested native-library hash, seed hash, poison values, and successful cycle count. This resolves the independently reproduced constructor fault. Any other crash still requires its own stack trace.

The audio investigation also tested the installed SDL2 queue with the exact bundled SDL3 library globally preloaded. Thirty audio subsystem/device/queue/disposal cycles passed. Neither binary has `SDL_*` dynamic relocations, and loader binding diagnostics resolved the exercised SDL2 entry points to SDL2 itself. Those checks provided no evidence of SDL2/SDL3 symbol interposition as the constructor failure's cause.
