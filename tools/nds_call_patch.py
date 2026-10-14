"""Checked addition to the pinned melonDS ARM9 interpreter: run one original routine.

The F slot (#102) switches Kirby's copy through the ROM's own ability routine
(0x0211B734) instead of the inhale and swallow. The bridge cannot execute ROM
code by itself, so this hook lets it arm one call: the next time ARM9 code is
about to execute ``kssu_hook_at`` (the player's per-frame update entry), the
interpreter saves r0-r14, calls ``kssu_hook_fn`` with four arguments and a
return address of ``kssu_hook_at``, then restores the registers and continues
the interrupted update when that routine returns. The routine may block the
game thread across frames while it loads from the cartridge; the state stays
``2`` until it returns, and the bridge carries the hook state in its savestate
trailer. While ``kssu_hook_state`` is zero the interpreter only tests it once
per instruction.

The second stage (#153) is for a build with the recompiler
(tools/nds_jit_patch.py). Such a machine always runs the recompiler's frame
loop, and each ARM9 or ARM7 slice of it runs on the interpreter unless the
bridge has set ``kssu_recompile``. While a call is armed the ARM9 stays on the
interpreter, where the hook is. The recompiler keeps no prefetched
instructions, so the first interpreter slice after a compiled one refills
them. ``kssu_recompile_flush`` discards the compiled blocks before the next
ARM9 slice; the bridge sets it after it changes code in RAM directly. With
``kssu_recompile`` zero the machine runs the same interpreter loop as before.
"""
import subprocess
from pathlib import Path

HOOK = '''// Checked addition (tools/nds_call_patch.py): run one original ARM routine for the bridge.
extern "C" {
melonDS::u32 kssu_hook_at = 0, kssu_hook_fn = 0, kssu_hook_state = 0;
melonDS::u32 kssu_hook_args[4] = {0, 0, 0, 0};
melonDS::u32 kssu_hook_saved[15] = {0};
}
// True when the pipeline was redirected; the caller skips this instruction.
static inline bool KssuHook(melonDS::ARMv5* cpu, melonDS::u32 pc)
{
    if (pc != kssu_hook_at || (cpu->CPSR & 0x20)) return false;
    if (kssu_hook_state == 1)
    {
        for (int i = 0; i < 15; i++) kssu_hook_saved[i] = cpu->R[i];
        for (int i = 0; i < 4; i++) cpu->R[i] = kssu_hook_args[i];
        cpu->R[14] = kssu_hook_at;
        kssu_hook_state = 2;
        cpu->JumpTo(kssu_hook_fn);
        return true;
    }
    if (kssu_hook_state == 2)
    {
        for (int i = 0; i < 15; i++) cpu->R[i] = kssu_hook_saved[i];
        kssu_hook_state = 3;
        cpu->JumpTo(kssu_hook_at);
        return true;
    }
    return false;
}

'''

CORE_EDITS = {
    "src/ARM.cpp": [
        ('#include "ARMJIT_Memory.h"\n\nnamespace melonDS\n{', '#include "ARMJIT_Memory.h"\n\n' + HOOK + 'namespace melonDS\n{'),
        ("                // prefetch\n                R[15] += 4;\n                CurInstr = NextInstr[0];\n"
         "                NextInstr[0] = NextInstr[1];\n                NextInstr[1] = CodeRead32(R[15], false);",
         "                if (kssu_hook_state && KssuHook(this, R[15] - 4)) continue;\n\n"
         "                // prefetch\n                R[15] += 4;\n                CurInstr = NextInstr[0];\n"
         "                NextInstr[0] = NextInstr[1];\n                NextInstr[1] = CodeRead32(R[15], false);"),
    ],
}

RECOMPILE = '''// Checked addition (tools/nds_call_patch.py, #153): who runs each slice of a recompiler build.
extern "C" {
melonDS::u32 kssu_recompile = 0;        // set by the bridge: nonzero runs the recompiler
melonDS::u32 kssu_recompile_flush = 0;  // set by the bridge after it changes code in RAM
}
#ifdef JIT_ENABLED
static bool kssu_compiled[2] = {false, false};
// True when this slice runs on the interpreter: the recompiler is not asked
// for, or a call is armed for the ARM9, whose hook is in the interpreter loop.
static inline bool KssuInterpret(melonDS::ARM* cpu, int num)
{
    if (kssu_recompile && !(num == 0 && kssu_hook_state))
    {
        if (num == 0 && kssu_recompile_flush) { cpu->NDS.JIT.ResetBlockCache(); kssu_recompile_flush = 0; }
        kssu_compiled[num] = true;
        return false;
    }
    // The recompiler keeps no prefetched instructions; the interpreter runs from them.
    if (kssu_compiled[num]) { cpu->FillPipeline(); kssu_compiled[num] = false; }
    return true;
}
#endif

'''

GATE = '''#ifdef JIT_ENABLED
    // Checked addition (tools/nds_call_patch.py, #153): the bridge chooses who runs this slice.
    if constexpr (mode == CPUExecuteMode::JIT)
        if (KssuInterpret(this, %d)) return Execute<CPUExecuteMode::Interpreter>();
#endif

'''

CORE_EDITS_2 = {
    "src/ARM.cpp": [
        (HOOK + 'namespace melonDS\n{', HOOK + RECOMPILE + 'namespace melonDS\n{'),
        ("void ARMv5::Execute()\n{\n", "void ARMv5::Execute()\n{\n" + GATE % 0),
        ("void ARMv4::Execute()\n{\n", "void ARMv4::Execute()\n{\n" + GATE % 1),
    ],
}

CALL_CORE_FILES = tuple(CORE_EDITS)


def check_call_patch(vendor: Path, core_revision: str, apply=False):
    """Accept exactly pristine or a known stage of this patch; apply only after all checks."""
    writes = []
    root = vendor / "melonDS"
    for relative, replacements in CORE_EDITS.items():
        original = subprocess.check_output(["git", "-C", str(root), "show", f"{core_revision}:{relative}"], text=True)
        patched = original
        for before, after in replacements:
            if patched.count(before) != 1:
                raise SystemExit(f"Call hook patch precondition failed: {relative}")
            patched = patched.replace(before, after)
        first = patched
        for before, after in CORE_EDITS_2.get(relative, []):
            if patched.count(before) != 1:
                raise SystemExit(f"Call hook patch second-stage precondition failed: {relative}")
            patched = patched.replace(before, after)
        target = root / relative
        actual = target.read_text()
        if actual not in (original, first, patched):
            raise SystemExit(f"Unexpected call hook edits: {relative}; left untouched.")
        if actual != patched:
            writes.append((target, patched))
    if apply:
        for target, patched in writes:
            target.write_text(patched)
