#!/usr/bin/env python3
"""Inspect an existing KSSU main-RAM dump; never attach, write memory or drive input.

The candidate memory layout is research evidence, not a verified gameplay spec.
See docs/GAMEPLAY_FIDELITY.md for provenance and limits. No third-party source or
ROM-derived bytes are included in this script.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

ROM_SHA256 = "b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027"
SOURCE_COMMIT = "0af58922f3f940244bc239f186508963950fddb3"
RAM_START, RAM_SIZE = 0x02000000, 0x400000
ENTITY_HEADS, HIT_HEADS = 0x02049DEC, 0x02076F00
POWER_ADDRESS = 0x020BAF7B


class InvalidDump(ValueError):
    pass


class Ram:
    def __init__(self, data: bytes):
        if len(data) not in (RAM_SIZE, 4 * RAM_SIZE):
            raise InvalidDump("expected a raw 4 MiB DS or 16 MiB emulator main-RAM dump")
        self.data = data

    def read(self, address: int, fmt: str):
        offset = address - RAM_START
        size = struct.calcsize("<" + fmt)
        if offset < 0 or offset + size > RAM_SIZE:
            raise InvalidDump(f"pointer outside DS main RAM: {address:#010x}")
        return struct.unpack_from("<" + fmt, self.data, offset)

    def value(self, address: int, fmt: str):
        return self.read(address, fmt)[0]

    def nodes(self, table: int, slots: int, node_size: int):
        """Reject cycles/invalid pointers instead of producing plausible partial data."""
        for slot in range(slots):
            pointer = self.value(table + slot * 4, "I")
            seen = set()
            while pointer:
                if pointer % 4:
                    raise InvalidDump(f"unaligned node pointer {pointer:#010x}")
                if pointer in seen:
                    raise InvalidDump(f"cycle in slot {slot} at {pointer:#010x}")
                if len(seen) >= 256:
                    raise InvalidDump(f"implausible chain length in slot {slot}")
                self.read(pointer, f"{node_size}s")
                seen.add(pointer)
                yield slot, pointer
                pointer = self.value(pointer, "I")


def inspect_ram(data: bytes, entity_address: int | None = None) -> dict:
    ram = Ram(data)
    entities = []
    for slot, address in ram.nodes(ENTITY_HEADS, 7, 0x8C):
        x, y, vx, vy = ram.read(address + 0x80, "iihh")
        entities.append({
            "address": address,
            "slot": slot,
            "kind_raw": ram.value(address + 0x16, "B"),
            "parent_address": ram.value(address + 4, "I"),
            "x_raw": x, "y_raw": y, "vx_raw": vx, "vy_raw": vy,
            "x_candidate_pixels": x / 65536,
            "y_candidate_pixels": y / 65536,
        })
    hits = []
    for slot, address in ram.nodes(HIT_HEADS, 32, 0x23A):
        definition = ram.value(address + 0x14, "I")
        # Preserve raw words instead of treating disputed damage/frame fields as fact.
        definition_words = list(ram.read(definition, "14H")) if definition else None
        hits.append({
            "address": address,
            "slot": slot,
            "owner_address": ram.value(address + 0x38, "I"),
            "bounds_raw": list(ram.read(address + 0x24, "4h")),
            "definition_address": definition,
            "definition_words_raw": definition_words,
            "hp_raw": ram.value(address + 0x238, "h"),
        })
    result = {
        "schema": "kssu-ram-observation/v1",
        "layout_status": "candidate-layout-with-base-motion-observations-see-ORACLE_RESULTS.md",
        "layout_source": "https://github.com/benhall-7/melon-kssu/tree/" + SOURCE_COMMIT,
        "ram_sha256": hashlib.sha256(data).hexdigest(),
        "power_raw": ram.value(POWER_ADDRESS, "B"),
        "camera_raw": {
            "x": ram.value(0x0204222E, "h"),
            "y": ram.value(0x0204222A, "h"),
        },
        "entities": entities,
        "hits": hits,
    }
    if entity_address is not None:
        matches = [e for e in entities if e["address"] == entity_address]
        if len(matches) != 1:
            raise InvalidDump("selected entity must occur exactly once in the live entity lists")
        entity = matches[0]
        # A caller must identify Kirby independently; slot/kind are not sufficient.
        result["selected_entity"] = {
            **entity,
            "identity_status": "caller-selected-not-automatically-identified-as-Kirby",
            "hits": [hit for hit in hits if hit["owner_address"] == entity_address],
        }
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ram", type=Path, help="existing raw main-RAM dump, not a savestate")
    parser.add_argument("--rom", type=Path, required=True, help="verify exact local source-ROM SHA256")
    parser.add_argument("--entity", type=lambda v: int(v, 0), help="independently identified Kirby entity address")
    parser.add_argument("--frame", type=int, help="frame index from capture system")
    parser.add_argument("--out", type=Path, help="JSON output; defaults to stdout")
    args = parser.parse_args(argv)
    try:
        if hashlib.sha256(args.rom.read_bytes()).hexdigest() != ROM_SHA256:
            raise InvalidDump("unsupported ROM fingerprint; do not apply unverified addresses to another revision")
        if args.frame is not None and args.frame < 0:
            raise InvalidDump("frame cannot be negative")
        result = inspect_ram(args.ram.read_bytes(), args.entity)
        result["source_rom_sha256"] = ROM_SHA256
        result["frame"] = args.frame
        output = json.dumps(result, indent=2) + "\n"
        if args.out:
            with args.out.open("x", encoding="utf-8") as stream:
                stream.write(output)
        else:
            sys.stdout.write(output)
        return 0
    except (OSError, ValueError, struct.error) as exc:
        print(f"oracle_snapshot: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
