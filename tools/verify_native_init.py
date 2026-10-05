#!/usr/bin/env python3
"""Stress real native machine construction with glibc allocation poisoning.

The pinned core's original GPU constructor read uninitialized capture flags
before installing its renderer. MALLOC_PERTURB_=111 reproduced the null call on
the first construction. This test uses no SDL, desktop, or host save files.
"""
import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path.home() / ".local/share/kirby-dead-cells"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", type=Path, default=ROOT / "oracle/vendor/melon-rs/target/release/libkssu_bridge.so")
    parser.add_argument("--rom", type=Path, default=Path.home() / "Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds")
    parser.add_argument("--state", type=Path, default=ROOT / "oracle/ready2/000.state")
    parser.add_argument("--cycles", type=int, default=8)
    parser.add_argument("--report", type=Path, default=ROOT / "research/native-init-verification.json")
    parser.add_argument("--worker", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if not 1 <= args.cycles <= 100:
        parser.error("cycles must be between 1 and 100")
    if args.worker:
        lib = C.CDLL(str(args.library.resolve()))
        lib.kssu_open.argtypes = [C.c_char_p, C.c_char_p]
        lib.kssu_open.restype = C.c_void_p
        lib.kssu_close.argtypes = [C.c_void_p]
        lib.kssu_step.argtypes = [C.c_void_p, C.c_uint16, C.c_uint32]
        for _ in range(args.cycles):
            handle = lib.kssu_open(os.fsencode(args.rom.resolve()), os.fsencode(args.state.resolve()))
            if not handle:
                raise RuntimeError("Native machine failed to open")
            try:
                if lib.kssu_step(handle, 0, 1) != 0:
                    raise RuntimeError("Native machine failed its first frame")
            finally:
                lib.kssu_close(handle)
        print(f"PASS {args.cycles} construction/first-frame/disposal cycles", flush=True)
        return

    started = time.monotonic()
    results = []
    for poison in (111, 127, 63, 1, 42, 85, 128, 170):
        command = [sys.executable, str(Path(__file__).resolve()), "--worker", "--cycles", str(args.cycles),
                   "--library", str(args.library.resolve()), "--rom", str(args.rom.resolve()), "--state", str(args.state.resolve())]
        process = subprocess.run(command, env=dict(os.environ, MALLOC_PERTURB_=str(poison)),
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=120)
        if process.returncode != 0:
            raise RuntimeError(f"MALLOC_PERTURB_={poison} failed with {process.returncode}:\n"
                               + process.stdout[-2000:] + process.stderr[-2000:])
        results.append({"malloc_perturb": poison, "cycles": args.cycles})
    report = {"library_sha256": hashlib.sha256(args.library.read_bytes()).hexdigest(),
              "state_sha256": hashlib.sha256(args.state.read_bytes()).hexdigest(),
              "successful_cycles": args.cycles * len(results), "cases": results,
              "elapsed_seconds": round(time.monotonic() - started, 3), "sdl_loaded": False}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(f"PASS {report['successful_cycles']} native construction/first-frame/disposal cycles across 8 allocation poison values; {args.report}")


if __name__ == "__main__":
    main()
