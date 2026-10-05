#!/usr/bin/env python3
"""Run the headless DS oracle inside its private lab, with source fingerprints.

Build tools/oracle_runner.rs against the pinned melon-rs dependency first.
Only emulated DS input is sent. No desktop keyboard/mouse or user save is used.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

from PIL import Image
from oracle_snapshot import ROM_SHA256

ROOT = Path.home() / ".local/share/kirby-dead-cells/oracle"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rom", type=Path, required=True)
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--state", type=Path)
    parser.add_argument("--runner", type=Path, default=ROOT / "vendor/melon-rs/target/release/kssu-oracle-runner")
    args = parser.parse_args()
    root, output = ROOT.resolve(), args.output.expanduser().resolve()
    if root not in output.parents or output.exists():
        parser.error("output must be a new directory inside the private oracle lab")
    if args.state and root not in args.state.expanduser().resolve().parents:
        parser.error("initial state must come from the isolated oracle lab")
    rom, plan, runner = (p.expanduser().resolve() for p in (args.rom, args.plan, args.runner))
    if digest(rom) != ROM_SHA256:
        parser.error("source ROM SHA256 differs from the inspected YKWE revision")
    provenance = {"rom_sha256": ROM_SHA256, "plan_sha256": digest(plan), "runner_sha256": digest(runner)}
    command = [str(runner), str(rom), str(plan), str(output)]
    if args.state:
        state = args.state.expanduser().resolve()
        provenance["initial_state_sha256"] = digest(state)
        command.append(str(state))
    subprocess.run(command, cwd=root, check=True)
    for image in output.glob("*.ppm"):
        with Image.open(image) as source:
            source.save(image.with_suffix(".png"))
    provenance["trace_sha256"] = digest(output / "trace.jsonl")
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    print(json.dumps({"output": str(output), **provenance}, indent=2))


if __name__ == "__main__":
    main()
