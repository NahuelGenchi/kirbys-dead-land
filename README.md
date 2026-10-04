# Kirby Super Star Ultra for Dead Cells — development project

**This is not yet a playable mod.** It contains a working local ROM converter,
verified Dead Cells asset-format conversion, and a compiled Linux gameplay-hook
adapter. Kirby's original controller, animation timing and ability behavior have
not been ported into Dead Cells. The adapter remains inactive without that
implementation. There is no finished outfit or playable installer.

This is a new project built with [Universal Modder](https://github.com/rehan-remade/universal-modder).
The existing Sonic/Knuckles project, installed Dead Cells files and player saves
were not modified.

## Required gameplay

- Preserve Kirby Super Star Ultra movement, physics, animation timing and original
  copy-ability movesets.
- Successful enemy absorption gives a random copy ability. The working
  interpretation preserves inhaling, mouthful and swallowing before the random draw.
- Base Kirby may use Dead Cells swords, bows and shields.
- Copied Kirby uses the ability's attacks and retains the same shield for parrying.

The randomization/equipment rules are implemented and tested separately from the
missing gameplay controller. Ordinary Dead Cells enemy kills do not trigger copying.
No generic host weapon is presented as an original Kirby ability.

## What works locally

- The user's USA ROM is identified and extracted without modification: 1,886
  named NitroFS files and 61 player/helper sprite archives.
- Original 4bpp tiles, RGB555 palettes, OAM component placement and separate
  left/right facing are decoded. There are 491 normal-color base Kirby poses and
  20,224 poses across all archive groups, including alternate colors and helpers.
- Matching body/cap layers produce 630 ability pose compositions in both facings.
  Unknown shared palettes are flagged; incomplete poses are excluded from the
  generated Dead Cells asset pack.
- BATL textures preserve visible source pixels, transparency and ROM pivots.
  They are numbered **poses**, not recovered timed animation clips.
- A C# adapter compiles against [Dead Cells Core Modding v35.13.6](https://github.com/dead-cells-core-modding/core/releases/tag/v35.13.6).
  It hooks weapon use, equipment changes, hero updates and level transitions, with
  a contract for the future original-gameplay provider.
- A headless emulator boots the original ROM and captures real base-form walking
  and jumping. Repeating the same 191-frame sequence produced identical traces,
  screenshots and final RAM. This runs independently of Dead Cells; see the
  [measured results and reproduction commands](docs/ORACLE_RESULTS.md).

All decoded images, ROM code and generated asset archives stay under
`~/.local/share/kirby-dead-cells/`, outside this source repository. Nothing was
published. The staged lab has not been launched in Dead Cells.

## Reproduce development outputs

Python 3.12+ and Pillow 12.1+ are required. Run from this project folder:

```bash
python3 tools/extract_kirby.py \
  '/home/rayzza/Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds' \
  --extract-all
python3 tools/build_assets.py --output "$HOME/.local/share/kirby-dead-cells/assets-final"
python3 tools/verify_assets.py --build "$HOME/.local/share/kirby-dead-cells/assets-final"
python3 scripts/setup_runtime.py --build
python3 scripts/stage_runtime.py \
  --assets "$HOME/.local/share/kirby-dead-cells/assets-final/res.pak"
```

Setup downloads checksum-pinned development dependencies into a private directory.
Staging prepares a separate lab and does not launch or install into the real game.
The generated pack contains decoded pose atlases; loading it does not add a
selectable outfit. The developer resource-loading flag defaults to false.

```bash
python3 -m unittest discover -s tests -p 'test_*.py' -v
python3 tools/oracle_tests.py
"$HOME/.local/share/kirby-dead-cells/dotnet/dotnet" run --project tests/Kirby.Core.Tests.csproj
```

Run all offline checks, including the independent DCCM archive reader, with:

```bash
python3 scripts/check.py --assets "$HOME/.local/share/kirby-dead-cells/assets-final"
```

## Remaining work

The central missing component is the original NDS gameplay controller. No complete
reusable KSSU gameplay decompilation was found. A faithful port requires either
reconstructing and comparing the original routines, or embedding the original
runtime with new bridges for host collision, enemies and rendering. Emulator RAM
inspection tools and the research findings are included for that work.

Original timed animations, inhale/swallow/spit, hovering, all 24 copy abilities,
combat collision/damage, outfit registration, input bindings, audio and save/level
state handling remain unfinished. Neither a successful build nor decoded artwork
establishes gameplay equivalence. No in-game Kirby gameplay was verified.

The chosen loader disables achievement unlocks. Save/cloud isolation of a future
lab launch still needs verification; merely having a separate lab directory does
not prove it. No live-game loader installation or input automation has occurred.

Details: [ROM format](docs/ROM_FORMAT.md), [host integration](docs/RUNTIME_RECON.md),
[gameplay fidelity](docs/GAMEPLAY_FIDELITY.md), [original-ROM observations](docs/ORACLE_RESULTS.md),
and [work journal](MODLOG.md).

## Credits

Kirby Super Star Ultra: Nintendo / HAL Laboratory. Dead Cells: Motion Twin /
Evil Empire. Framework: Dead Cells Core Modding. ROM observation research:
[melon-kssu](https://github.com/benhall-7/melon-kssu). Built with Codex and Universal
Modder. Converter and adapter source are separate from the user's copyrighted
ROM and game assets; generated local packs are not redistributable releases.
