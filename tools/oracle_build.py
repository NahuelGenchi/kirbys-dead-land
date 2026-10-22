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
import shutil
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
PROJECT = Path(__file__).resolve().parents[1]
# Each build target (#242): its Rust target (None: this Linux machine's own), the
# bridge's and the runner's file names there, and its provenance file suffix.
# Windows builds are cross-built here with MinGW-w64.
TARGETS = {
    "linux": (None, "libkssu_bridge.so", "kssu-oracle-runner", ""),
    "windows": ("x86_64-pc-windows-gnu", "kssu_bridge.dll", "kssu-oracle-runner.exe", "-windows"),
}
# The target whose programs this machine runs.
HOST = "windows" if os.name == "nt" else "linux"


def release_dir(vendor, target=HOST):
    triple = TARGETS[target][0]
    return vendor / "target" / triple / "release" if triple else vendor / "target/release"


def bridge_binary(vendor, target=HOST):
    return release_dir(vendor, target) / TARGETS[target][1]


def runner_binary(vendor, target=HOST):
    return release_dir(vendor, target) / TARGETS[target][2]


def provenance_file(name, target):
    """bridge-provenance.json or build-provenance.json, with the target's suffix."""
    return ROOT / f"{name}-provenance{TARGETS[target][3]}.json"


def windows_environment(env):
    """MinGW-w64 settings for the Windows cross-build. The POSIX thread model's
    compilers, since melonDS uses std::thread, which the win32 model's GCC 13 lacks.
    GCC's and C++'s runtimes linked in, so the DLL and EXE need only Windows' own
    libraries. No data sections: with them, MinGW's GCC writes melonDS's 128 MiB
    zero-filled JIT buffer into the file instead of leaving it uninitialized."""
    triple = TARGETS["windows"][0]
    key = triple.replace("-", "_")
    gcc, gxx = "x86_64-w64-mingw32-gcc-posix", "x86_64-w64-mingw32-g++-posix"
    for tool in (gcc, gxx):
        if not shutil.which(tool):
            raise SystemExit(f"{tool} is missing; install MinGW-w64 with POSIX threads (Ubuntu: g++-mingw-w64-x86-64-posix).")
    library = subprocess.run(["rustc", "--print", "target-libdir", "--target", triple], capture_output=True, text=True).stdout.strip()
    if not library or not any(Path(library).glob("libstd-*.rlib")):
        raise SystemExit(f"Rust's {triple} target is missing; run rustup target add {triple}.")
    # cc reads CFLAGS_<target> instead of CFLAGS, so a build's own flags are kept.
    flags = {name: " ".join([env.get(name, ""), "-fno-data-sections"]).strip() for name in ("CFLAGS", "CXXFLAGS")}
    return {
        f"CC_{key}": gcc, f"CXX_{key}": gxx, f"AR_{key}": "x86_64-w64-mingw32-gcc-ar-posix",
        f"CFLAGS_{key}": flags["CFLAGS"], f"CXXFLAGS_{key}": flags["CXXFLAGS"],
        f"CXXSTDLIB_{key}": "static:-bundle=stdc++",
        f"CMAKE_TOOLCHAIN_FILE_{key}": env.get(f"CMAKE_TOOLCHAIN_FILE_{key}", str(PROJECT / "packaging/melonds-windows.cmake")),
        f"CARGO_TARGET_{key.upper()}_LINKER": gcc,
        "RUSTFLAGS": " ".join([env.get("RUSTFLAGS", ""), "-C link-arg=-static"]).strip(),
    }


def windows_report(binary):
    """What a Windows build's provenance adds: its target, the Windows-only
    callback file both programs include, and the program's own hash."""
    stubs = PROJECT / "src/nds/melon_windows.rs"
    return {"target": TARGETS["windows"][0],
            "windows_source_sha256": hashlib.sha256(stubs.read_bytes()).hexdigest(),
            "binary_sha256": hashlib.sha256(binary.read_bytes()).hexdigest()}


def cargo_build(manifest, vendor, target, cwd=None):
    """Builds a crate against the vendor checkout, for one of TARGETS."""
    command = ["cargo", "build", "--release", "--manifest-path", str(manifest)]
    env = dict(os.environ, CARGO_TARGET_DIR=str(vendor / "target"))
    if target == "windows":
        command += ["--target", TARGETS[target][0]]
        env.update(windows_environment(env))
    subprocess.run(command, cwd=cwd, env=env, check=True)


def run(*args, cwd=None):
    subprocess.run(list(args), cwd=cwd, check=True)


def runner_manifest(source, melon_rs="../vendor/melon-rs"):
    """The oracle runner's Cargo.toml; tools/emulator_notices.py reads its crates."""
    return ('[package]\nname="kssu-oracle-runner"\nversion="0.1.0"\nedition="2021"\n'
            '[[bin]]\nname="kssu-oracle-runner"\npath=' + json.dumps(str(source)) + '\n'
            '[dependencies]\nmelon-rs={path=' + json.dumps(str(melon_rs)) + ',default-features=false}\n'
            'chrono="=0.4.45"\nserde_json="=1.0.151"\n')


def bridge_manifest(source, melon_rs="../vendor/melon-rs"):
    """The bridge's Cargo.toml (tools/nds_build.py); tools/emulator_notices.py reads its crates."""
    return ('[package]\nname="kssu-bridge"\nversion="0.1.0"\nedition="2021"\n[lib]\ncrate-type=["cdylib"]\n'
            'path=' + json.dumps(str(source)) + '\n[dependencies]\n'
            'melon-rs={path=' + json.dumps(str(melon_rs)) + ',default-features=false}\nchrono="=0.4.45"\nsha2="=0.10.9"\n')


def git_output(folder, *args):
    return subprocess.check_output(["git", "-C", str(folder), *args], text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", choices=TARGETS, default="linux",
                        help="Build the runner for Linux, or cross-build kssu-oracle-runner.exe for Windows (#242).")
    args = parser.parse_args()
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
    (crate / "Cargo.toml").write_text(runner_manifest(source_path))
    cargo_build(crate / "Cargo.toml", vendor, args.target, cwd=ROOT)
    report = {
        "melon_rs_commit": WRAPPER, "melon_ds_commit": CORE,
        "bridge_repairs": [after for _, after in REPAIRS],
        "bridge_sha256": hashlib.sha256(bridge.read_bytes()).hexdigest(),
        "runner_source_sha256": hashlib.sha256(source_path.read_bytes()).hexdigest(),
        "cargo_lock_sha256": hashlib.sha256((crate / "Cargo.lock").read_bytes()).hexdigest(),
        "binary": str(runner_binary(vendor, args.target)),
    }
    if args.target == "windows":
        report.update(windows_report(runner_binary(vendor, args.target)))
    provenance_file("build", args.target).write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
