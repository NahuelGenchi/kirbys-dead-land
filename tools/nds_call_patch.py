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
per instruction. The JIT is disabled in this build.
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

CALL_CORE_FILES = tuple(CORE_EDITS)


def check_call_patch(vendor: Path, core_revision: str, apply=False):
    """Accept exactly pristine or this known patch; apply only after all checks."""
    writes = []
    root = vendor / "melonDS"
    for relative, replacements in CORE_EDITS.items():
        original = subprocess.check_output(["git", "-C", str(root), "show", f"{core_revision}:{relative}"], text=True)
        patched = original
        for before, after in replacements:
            if patched.count(before) != 1:
                raise SystemExit(f"Call hook patch precondition failed: {relative}")
            patched = patched.replace(before, after)
        target = root / relative
        actual = target.read_text()
        if actual not in (original, patched):
            raise SystemExit(f"Unexpected call hook edits: {relative}; left untouched.")
        if actual != patched:
            writes.append((target, patched))
    if apply:
        for target, patched in writes:
            target.write_text(patched)
