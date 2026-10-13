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
    """Accept exactly pristine or this known patch; apply only after all checks."""
    writes = []
    root = vendor / "melonDS"
    for relative, replacements in CORE_EDITS.items():
        original = subprocess.check_output(["git", "-C", str(root), "show", f"{core_revision}:{relative}"], text=True)
        patched = original
        for before, after in replacements:
            if patched.count(before) != 1:
                raise SystemExit(f"Step patch precondition failed: {relative}")
            patched = patched.replace(before, after)
        target = root / relative
        actual = target.read_text()
        if actual not in (original, patched):
            raise SystemExit(f"Unexpected step patch edits: {relative}; left untouched.")
        if actual != patched:
            writes.append((target, patched))
    if apply:
        for target, patched in writes:
            target.write_text(patched)
