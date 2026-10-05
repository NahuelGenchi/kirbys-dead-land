# Kirby Super Star Ultra ROM asset extraction

The converter uses only the locally supplied USA Nintendo DS ROM (game code `YKWE`). It does not download sprites. All generated files belong outside this mod project, by default at `~/.local/share/kirby-dead-cells/rom`.

```sh
python3 tools/extract_kirby.py '/path/to/Kirby Super Star Ultra (USA).nds' --extract-all
```

Requires Python 3 and Pillow. `--archive bkir --archive aswd` restricts decoding for quick iteration; without this option all 61 `rom/pd/*.b` archives are decoded. `--extract-all` also writes every NitroFS file, ARM9/ARM7 executables and unnamed FAT files to the private output directory. No ROM, extracted PNG or decompiled game code should be distributed with the mod.

## Verified extraction

The supplied ROM SHA-256 is `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`. It has 1,886 named NitroFS files and 157 unnamed FAT files. The decoder exported 20,224 group-indexed graphic poses, including alternate colors. Every pose has independently decoded left and right OAM attributes. This is not a count of unique animations.

Normal pink Kirby is `decoded/bkir/frames/0000.png` through `0490.png`: 491 poses with no unresolved palette parts. `decoded/bkir/contact.png` shows labeled poses. `decoded/bkir/manifest.json` contains original composition metadata and pivots.

Representative complete body/cap compositions include all 39 Fire, 44 Ice, 35 Jet, and 15 Bomb attack poses. Sword has 19 complete compositions out of 42; the others omit fixed-palette effect parts and are explicitly flagged. There are 2,300 unresolved fixed-palette part occurrences across all groups, including alternate colors and helpers.

These results were checked by opening the decoded contact sheets, including base Kirby, Sword body and cap layers, Fire layers and Sword compositions. Pixels and palettes come from the ROM. Palette-bank interpretation beyond local banks 0 and 1 is not verified and is not guessed.

## Output interface

Each `decoded/<archive>/manifest.json` contains:

- `groups`: indexed palette/pose groups. The first group remains compatible with `frames/NNNN.png`; other groups use `groups/N/frames/NNNN.png`.
- `groups[N].frames`: each entry contains `index`, `file`, `origin: [x,y]`, `pivot: [-x,-y]`, `source_pointer`, OAM `parts`, `unresolved_parts` and the ROM graphics-block pointers.
- `left`: the corresponding left-facing frame metadata and PNG. Do not simply mirror the right-facing PNG: the ROM has distinct left OAM coordinates for many poses.
- `atlas`: a rectangle in the group's `atlas.png`, with the corresponding `atlas_pivot`. Atlas cells are sized per group (128 or 256 pixels) to retain outlying weapon/effect parts; no pixel scaling is performed.
- `composed_frames`: present for attack archives with matching counts in groups 0 and 2. These combine body/effect and cap/weapon at their original OAM origins. Their transparent canvases use the union of the original layer bounds, with explicit `origin`, `pivot`, `size`, `left_file`, `left_origin`, `left_pivot` and `unresolved_parts`. Only entries with an empty `unresolved_parts` list have every local part rendered. Composition order is visually checked for Sword and Fire; other ability layers still need emulator comparison.

A PNG's `origin` is its top-left relative to the original sprite's OAM origin. This origin is **not necessarily the character's feet**. Preserve these offsets when layering or converting to host-engine atlases.

Palettes are RGB555, converted to 8-bit channels by bit replication. Palette index zero is transparent. `palettes.png` shows every local palette. Sprite `contact.png` uses a dark opaque diagnostic background; runtime `frames` and `atlas` PNGs have transparency.

## Archive roles

`bkir.b` contains common Kirby body poses. `c*.b` archives contain cap/weapon overlays, not complete characters. The `a*.b` archives contain ability-specific body/effect poses and, in many cases, matching cap/weapon groups. Groups 0/1 and 2/3 are normal/alternate-color pairs. `h*.b` archives contain helpers. Some `a` archives have empty cap groups or unequal counts; the converter deliberately does not automatically pair those. `shot.b` uses an auxiliary effect-table format that is preserved raw and not yet decoded into poses.

Useful inferred names, based on filenames and visual inspection:

| Archive | Ability |
| --- | --- |
| abea | Beam |
| abom | Bomb |
| acoo | Cook |
| acpy | Copy |
| acra | Crash |
| acut | Cutter |
| afig | Fighter |
| afir | Fire |
| aham | Hammer |
| aice | Ice |
| ajet | Jet |
| amik | Mike |
| amir | Mirror |
| anin | Ninja |
| apai | Paint |
| apar | Parasol |
| apls | Plasma |
| aslp | Sleep |
| asto | Stone |
| asup | Suplex |
| aswd | Sword |
| awhe | Wheel |
| awng | Wing |
| ayoy | Yo-Yo |

## Binary findings

NitroFS FNT/FAT addresses come from NDS header offsets `0x40` through `0x4C`. Directory records have 8-byte entries, and file allocation entries are 8-byte start/end offsets. The converter checks bounds, directory IDs and path components before writing files.

The custom `.b` header stores group count/table at `0x00/0x04`, auxiliary group count/table at `0x08/0x0C`, palette start/count at `0x10/0x14`, graphics start/256-byte-block count at `0x18/0x1C`, OAM start/count at `0x20/0x24`, and unique-pose-record start/count at `0x28/0x2C`.

A normal group record is four little-endian words: indexed-pose count, flags, indexed-pose-pointer table and local palette base. Each pose descriptor is 12 bytes: OAM pointer, graphics-pointer-table pointer, part count, 256-byte graphics-block count and two local palette indices. Each OAM part is four halfwords: vertical/shape attributes, right-facing horizontal attributes, left-facing horizontal attributes and graphics-block/palette-bank attributes.

Graphics use low-nibble-first 4bpp, 8 by 8 tiled Nintendo DS layout. OAM shape/size selects the rectangle dimensions. Local banks 0 and 1 select the pose's two palette indices. Higher palette-bank values are engine-shared resources. The decoder preserves their descriptors and omits them from colored output until that mapping is established.

## Fidelity limits and next reverse-engineering targets

The archive tables hold poses and palette variants. They do **not** provide named timed animation sequences, movement physics, input-state transitions, damage rules or complete ability mechanics. Extraction does not port those systems. The following base pose selections are visual interpretations for inspection only, with no recovered original duration: side standing 85; walking 41–45; rising jump 56–58; aerial poses 66–72; expanding inhale mouth 81–84; crouch/squash 89–91. The corresponding original game states and frame timing require execution traces or code analysis before a one-to-one gameplay claim is possible.

`rom/info02/gki*` and `ga*` inspected files are raw tiled UI graphics, not animation-command streams. ARM9 overlay 25 (`raw/system/file_0025.bin`, 110,240 bytes) references `rom/pd/aswd.b` and the other sprite archives; it is a candidate for sprite/animation-loader analysis. Overlay 24 handles scene/data resources. These are retained in their original binary form outside the project. The shared `rom/stay00/col_stay` palette is a candidate for fixed effect banks but has not been mapped to them.
