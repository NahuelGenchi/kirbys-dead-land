#!/usr/bin/env python3
"""Build the optional headless oracle in its private lab; no system installs.

Downloads pinned open-source repositories and Cargo dependencies. Applies the
three checked camera-shim signature corrections required by the pinned sources.
Never edits the ROM, Dead Cells, a user save, or an existing unexpected checkout.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
from nds_vendor import GPU_EDITS, check_gpu_patch
from nds_graphics_patch import GRAPHICS_WRAPPER_FILES, GRAPHICS_CORE_FILES, check_graphics_patch
from nds_call_patch import CALL_CORE_FILES, check_call_patch
from nds_step_patch import STEP_CORE_FILES, check_step_patch
from nds_jit_patch import JIT_WRAPPER_FILES, JIT_CORE_FILES, check_jit_patch
from data_root import data_root

ROOT = data_root() / "oracle"
WRAPPER = "51f25edaf3b2df63f91d049eff8c38fb289150eb"
CORE = "82fdbc78483f43b310e920e21acc47787cb43564"
BRIDGE = "src/melon/cpp/Platform.cpp"
REPAIRS = [
    ("void Camera_Start(int num)", "void Camera_Start(int num, void* userdata)"),
    ("void Camera_Stop(int num)", "void Camera_Stop(int num, void* userdata)"),
    ("void Camera_CaptureFrame(int num, u32 *frame, int width, int height, bool yuv)",
     "void Camera_CaptureFrame(int num, u32 *frame, int width, int height, bool yuv, void* userdata)"),
]


def run(*args, cwd=None):
    subprocess.run(list(args), cwd=cwd, check=True)


def git_output(folder, *args):
    return subprocess.check_output(["git", "-C", str(folder), *args], text=True).strip()


def main():
    argparse.ArgumentParser(description=__doc__).parse_args()
    vendor = ROOT / "vendor/melon-rs"
    if not vendor.exists():
        vendor.parent.mkdir(parents=True, exist_ok=True)
        run("git", "clone", "--no-checkout", "--depth", "1",
            "https://github.com/benhall-7/melon-rs.git", str(vendor))
        run("git", "fetch", "--depth", "1", "origin", WRAPPER, cwd=vendor)
        run("git", "checkout", "--detach", WRAPPER, cwd=vendor)
    if git_output(vendor, "rev-parse", "HEAD") != WRAPPER:
        raise SystemExit("Existing oracle vendor checkout has an unexpected revision; left untouched.")
    source = subprocess.check_output(["git", "-C", str(vendor), "show", f"{WRAPPER}:{BRIDGE}"]).decode()
    repaired = source
    for before, after in REPAIRS:
        if repaired.count(before) != 1:
            raise SystemExit("Pinned bridge repair precondition failed; no edits applied.")
        repaired = repaired.replace(before, after)
    changed = git_output(vendor, "diff", "--name-only").splitlines()
    if any(path not in (BRIDGE, "melonDS", *GPU_EDITS, *GRAPHICS_WRAPPER_FILES, *JIT_WRAPPER_FILES) for path in changed):
        raise SystemExit("Unexpected vendor modifications; left untouched.")
    check_gpu_patch(vendor, WRAPPER)
    bridge = vendor / BRIDGE
    if bridge.read_text() not in (source, repaired):
        raise SystemExit("Existing bridge differs from both known variants; left untouched.")
    run("git", "-c", "url.https://github.com/.insteadOf=git@github.com:", "submodule",
        "update", "--init", "--depth", "1", "--recursive", cwd=vendor)
    if git_output(vendor / "melonDS", "rev-parse", "HEAD") != CORE:
        raise SystemExit("Unexpected melonDS submodule revision.")
    if any(path not in (*GRAPHICS_CORE_FILES, *CALL_CORE_FILES, *STEP_CORE_FILES, *JIT_CORE_FILES) for path in git_output(vendor / "melonDS", "diff", "--name-only").splitlines()):
        raise SystemExit("Unexpected melonDS submodule modifications; left untouched.")
    check_graphics_patch(vendor, WRAPPER, CORE, apply=True)
    check_call_patch(vendor, CORE, apply=True)
    check_step_patch(vendor, CORE, apply=True)
    check_jit_patch(vendor, WRAPPER, CORE, apply=True)
    check_gpu_patch(vendor, WRAPPER, apply=True)
    bridge.write_text(repaired)
    crate = ROOT / "runner"
    crate.mkdir(exist_ok=True)
    source_path = Path(__file__).with_name("oracle_runner.rs").resolve()
    manifest = ('[package]\nname="kssu-oracle-runner"\nversion="0.1.0"\nedition="2021"\n'
                '[[bin]]\nname="kssu-oracle-runner"\npath=' + json.dumps(str(source_path)) + '\n'
                '[dependencies]\nmelon-rs={path="../vendor/melon-rs",default-features=false}\n'
                'chrono="=0.4.45"\nserde_json="=1.0.151"\n')
    (crate / "Cargo.toml").write_text(manifest)
    env = dict(os.environ, CARGO_TARGET_DIR=str(vendor / "target"))
    subprocess.run(["cargo", "build", "--release", "--manifest-path", str(crate / "Cargo.toml")],
                   cwd=ROOT, env=env, check=True)
    report = {
        "melon_rs_commit": WRAPPER, "melon_ds_commit": CORE,
        "bridge_repairs": [after for _, after in REPAIRS],
        "bridge_sha256": hashlib.sha256(bridge.read_bytes()).hexdigest(),
        "runner_source_sha256": hashlib.sha256(source_path.read_bytes()).hexdigest(),
        "cargo_lock_sha256": hashlib.sha256((crate / "Cargo.lock").read_bytes()).hexdigest(),
        "binary": str(vendor / "target/release/kssu-oracle-runner"),
    }
    (ROOT / "build-provenance.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
