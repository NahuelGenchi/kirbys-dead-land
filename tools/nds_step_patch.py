"""Checked addition to the pinned melonDS frame loop: end a step as VBlank begins.

``NDS::RunFrame`` runs from scanline 0 to the end of scanline 262. The original
game waits for VBlank (scanline 192), then runs one logic pass: it reads the
buttons, moves every actor and the camera, and builds the sprite records and
the shadow OAM that its next VBlank shows. A call that ends 71 scanlines after
VBlank therefore ends inside that pass whenever the pass is longer, which it is
with a helper or many objects. The bridge then read a half-updated machine and
moved the terrain window under it (#134): Kirby was drawn a terrain cell off
for one frame.

With ``kssu_vblank_step`` set, a call ends as scanline 192 begins, before the
VBlank interrupt is taken, and the next call continues that frame. The game is
then between two passes at every call boundary unless one pass takes longer
than a whole frame. A call resumes when it finds the machine at that point
(VCount 192 in an unfinished frame), so a savestate taken there continues
correctly under either setting. A state saved at the end of a frame has 192
scanlines left before its VBlank, in which the original only waits: with the
switch at 2 a call runs just those, and does nothing when the machine is there
already. The bridge makes that call once after it loads a state, so every step
is one whole frame from VBlank to VBlank and runs one logic pass, as before.
The emulated machine executes exactly the same; only where a call returns
changes. While the switch is zero a call ends with its frame as before.

A second stage (#150) lets the bridge choose the ARM9's clock. The DS runs it at
67 MHz (``ARM9ClockShift`` 1); the core already knows 134 MHz (shift 2), which it
uses for a DSi. With ``kssu_arm9_shift`` set, ``RunFrame`` brings the machine to
that shift before it runs: the ARM9's timestamps are rescaled, upward without
loss, and the bus timings derived again. A savestate does not record the shift
its ARM9 timestamps count in, so ``kssu_arm9_state`` carries it: the core writes
it when it saves and takes it, when set, as it loads. With both left at zero the
machine runs and loads exactly as before.
"""
import subprocess
from pathlib import Path

SWITCH = '''// Checked addition (tools/nds_step_patch.py): where a RunFrame call ends.
extern "C" {
// Set by the bridge. 1: a call ends as VBlank begins instead of at the end of the
// frame. 2: the same, and a call that finds the machine there already does nothing.
melonDS::u32 kssu_vblank_step = 0;
// Set by GPU::StartScanline as scanline 192 begins.
melonDS::u32 kssu_vblank_reached = 0;
}

'''

CLOCK = '''// Checked addition (tools/nds_step_patch.py, #150): the ARM9's clock.
extern "C" {
// Set by the bridge: the shift RunFrame brings the ARM9 clock to. 1 is the DS's
// 67 MHz, 2 twice that; 0 leaves the machine as it is.
melonDS::u32 kssu_arm9_shift = 0;
// The shift a savestate's ARM9 timestamps count in, which the state does not say.
// Written when a state is saved; set by the bridge before one is loaded.
melonDS::u32 kssu_arm9_state = 0;
}

'''

# Second stage (#150), applied on top of the edits below.
CORE_EDITS_2 = {
    "src/NDS.cpp": [
        ("melonDS::u32 kssu_vblank_reached = 0;\n}\n\n", "melonDS::u32 kssu_vblank_reached = 0;\n}\n\n" + CLOCK),
        ("    file->VarArray(MainRAM, MainRAMMaxSize);\n",
         "    // Checked addition (tools/nds_step_patch.py): the timings below are derived\n"
         "    // from the clock the state's ARM9 timestamps count in.\n"
         "    if (file->Saving) kssu_arm9_state = ARM9ClockShift;\n"
         "    else if (kssu_arm9_state) ARM9ClockShift = kssu_arm9_state;\n\n"
         "    file->VarArray(MainRAM, MainRAMMaxSize);\n"),
        ("    Current = this;\n\n    // Checked addition (tools/nds_step_patch.py): a call that finds the machine\n",
         "    Current = this;\n\n"
         "    // Checked addition (tools/nds_step_patch.py): the clock the bridge asked for,\n"
         "    // changed as DSi::Set_SCFG_Clock9 changes it but keeping the fraction when it rises.\n"
         "    if (kssu_arm9_shift && kssu_arm9_shift != ARM9ClockShift)\n"
         "    {\n"
         "        if (kssu_arm9_shift > ARM9ClockShift)\n"
         "        {\n"
         "            ARM9Timestamp <<= kssu_arm9_shift - ARM9ClockShift;\n"
         "            ARM9Target <<= kssu_arm9_shift - ARM9ClockShift;\n"
         "        }\n"
         "        else\n"
         "        {\n"
         "            ARM9Timestamp >>= ARM9ClockShift - kssu_arm9_shift;\n"
         "            ARM9Target >>= ARM9ClockShift - kssu_arm9_shift;\n"
         "        }\n"
         "        ARM9ClockShift = kssu_arm9_shift;\n"
         "        ARM9.UpdateRegionTimings(0x00000, 0x100000);\n"
         "    }\n\n"
         "    // Checked addition (tools/nds_step_patch.py): a call that finds the machine\n"),
    ],
}

CORE_EDITS = {
    "src/NDS.cpp": [
        ('#include "ARMJIT_Memory.h"\n\nnamespace melonDS\n{', '#include "ARMJIT_Memory.h"\n\n' + SWITCH + 'namespace melonDS\n{'),
        ("    Current = this;\n\n    FrameStartTimestamp = SysTimestamp;\n\n    GPU.TotalScanlines = 0;\n\n    LagFrameFlag = true;\n",
         "    Current = this;\n\n"
         "    // Checked addition (tools/nds_step_patch.py): a call that finds the machine\n"
         "    // where the last one stopped, as VBlank began, continues that frame.\n"
         "    const bool vblankStep = kssu_vblank_step != 0;\n"
         "    bool resumeFrame = GPU.VCount == 192 && GPU.TotalScanlines == 0 && !(CPUStop & CPUStop_Sleep);\n"
         "    kssu_vblank_reached = 0;\n\n"
         "    // 2 only brings a machine loaded at the end of a frame to that point.\n"
         "    if (kssu_vblank_step == 2 && (resumeFrame || !Running || (CPUStop & CPUStop_Sleep)))\n"
         "        return 263;\n\n"
         "    if (!resumeFrame)\n    {\n        FrameStartTimestamp = SysTimestamp;\n\n        GPU.TotalScanlines = 0;\n    }\n\n"
         "    LagFrameFlag = true;\n"),
        ("            if (!(CPUStop & CPUStop_Wakeup))\n            {\n                GPU.StartFrame();\n            }\n"
         "            CPUStop &= ~CPUStop_Wakeup;\n\n            while (Running && GPU.TotalScanlines==0)\n",
         "            if (!(CPUStop & CPUStop_Wakeup) && !resumeFrame)\n            {\n                GPU.StartFrame();\n            }\n"
         "            resumeFrame = false;\n"
         "            CPUStop &= ~CPUStop_Wakeup;\n\n"
         "            while (Running && GPU.TotalScanlines==0 && !(vblankStep && kssu_vblank_reached))\n"),
        ("        if (GPU.TotalScanlines == 0)\n            continue;\n",
         "        if (vblankStep && kssu_vblank_reached)\n            break;\n\n"
         "        if (GPU.TotalScanlines == 0)\n            continue;\n\n"
         "        if (vblankStep && !(CPUStop & CPUStop_Sleep))\n        {\n"
         "            // the frame ended inside this call: run on to the next frame's VBlank\n"
         "            SPU.BufferAudio();\n            FrameStartTimestamp = SysTimestamp;\n            GPU.TotalScanlines = 0;\n"
         "            continue;\n        }\n"),
    ],
    "src/GPU.cpp": [
        ('#include "GPU_Soft.h"\n\nnamespace melonDS\n{',
         '#include "GPU_Soft.h"\n\n// Checked addition (tools/nds_step_patch.py): defined in NDS.cpp.\n'
         'extern "C" { extern melonDS::u32 kssu_vblank_reached; }\n\nnamespace melonDS\n{'),
        ("        if (CaptureEnable)\n        {\n            CaptureCnt &= ~(1<<31);\n            CaptureEnable = false;\n        }\n    }\n"
         "    else if (VCount == 262)\n",
         "        if (CaptureEnable)\n        {\n            CaptureCnt &= ~(1<<31);\n            CaptureEnable = false;\n        }\n\n"
         "        kssu_vblank_reached = 1;\n    }\n"
         "    else if (VCount == 262)\n"),
    ],
}

STEP_CORE_FILES = tuple(CORE_EDITS)


def check_step_patch(vendor: Path, core_revision: str, apply=False):
    """Accept exactly pristine or a known stage of this patch; apply only after all checks."""
    writes = []
    root = vendor / "melonDS"
    for relative, replacements in CORE_EDITS.items():
        original = subprocess.check_output(["git", "-C", str(root), "show", f"{core_revision}:{relative}"], text=True)
        first = original
        for before, after in replacements:
            if first.count(before) != 1:
                raise SystemExit(f"Step patch precondition failed: {relative}")
            first = first.replace(before, after)
        patched = first
        for before, after in CORE_EDITS_2.get(relative, []):
            if patched.count(before) != 1:
                raise SystemExit(f"Step patch second-stage precondition failed: {relative}")
            patched = patched.replace(before, after)
        target = root / relative
        actual = target.read_text()
        if actual not in (original, first, patched):
            raise SystemExit(f"Unexpected step patch edits: {relative}; left untouched.")
        if actual != patched:
            writes.append((target, patched))
    if apply:
        for target, patched in writes:
            target.write_text(patched)
