"""Checked changes that build the pinned melonDS with its recompiler (#153).

The bridge ran the interpreter only. This builds the recompiler in and leaves
the choice to the bridge, slice by slice (tools/nds_call_patch.py, second
stage); a machine starts on the interpreter.

- ``build.rs`` of melon-rs: ``ENABLE_JIT`` on for the core, and ``JIT_ENABLED``
  for the C++ shim, which includes the core's headers and must see the same
  object layouts.
- ``src/Args.h``: a machine is created with the recompiler's settings and fast
  memory off. Fast memory maps the emulated address space and resolves the
  rest through a SIGSEGV handler.
- ``src/ARMJIT_Global.cpp``: that handler is not installed. Creating a machine
  installed it process-wide whatever the settings; the bridge runs inside the
  game's process, which has handlers of its own, and never uses fast memory.
"""
import subprocess
from pathlib import Path

WRAPPER_EDITS = {
    "build.rs": [
        ('        .define("ENABLE_JIT", "OFF")\n', '        .define("ENABLE_JIT", "ON")\n'),
        ('        // .define("GDBSTUB_ENABLED", None)\n',
         '        // .define("GDBSTUB_ENABLED", None)\n        .define("JIT_ENABLED", None)\n'),
    ],
}

CORE_EDITS = {
    "src/Args.h": [
        ("    std::optional<JITArgs> JIT = JITArgs();\n",
         "    // Checked change (tools/nds_jit_patch.py): fast memory off; the bridge chooses\n"
         "    // interpreter or recompiler for each slice (kssu_recompile).\n"
         "    std::optional<JITArgs> JIT = JITArgs{32, true, true, false};\n"),
    ],
    "src/ARMJIT_Global.cpp": [
        ("        ARMJIT_Memory::RegisterFaultHandler();\n",
         "        // Checked change (tools/nds_jit_patch.py): no fault handler. Fast memory is\n"
         "        // never used and the host process has its own SIGSEGV handlers.\n"),
        ("        ARMJIT_Memory::UnregisterFaultHandler();\n",
         "        // Checked change (tools/nds_jit_patch.py): none was installed.\n"),
    ],
}

JIT_WRAPPER_FILES = tuple(WRAPPER_EDITS)
JIT_CORE_FILES = tuple(CORE_EDITS)


def check_jit_patch(vendor: Path, wrapper_revision: str, core_revision: str, apply=False):
    """Accept exactly pristine or this known patch; apply only after all checks."""
    writes = []
    for root, revision, edits in ((vendor, wrapper_revision, WRAPPER_EDITS), (vendor / "melonDS", core_revision, CORE_EDITS)):
        for relative, replacements in edits.items():
            original = subprocess.check_output(["git", "-C", str(root), "show", f"{revision}:{relative}"], text=True)
            patched = original
            for before, after in replacements:
                if patched.count(before) != 1:
                    raise SystemExit(f"Recompiler patch precondition failed: {relative}")
                patched = patched.replace(before, after)
            target = root / relative
            actual = target.read_text()
            if actual not in (original, patched):
                raise SystemExit(f"Unexpected recompiler edits: {relative}; left untouched.")
            if actual != patched:
                writes.append((target, patched))
    if apply:
        for target, patched in writes:
            target.write_text(patched)
