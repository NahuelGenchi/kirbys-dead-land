# Third-party notices

[Back to README](README.md) · [License](LICENSE)

Kirby's Dead Land is licensed under the GNU General Public License, version 3 or
(at your option) any later version (`GPL-3.0-or-later`); the full text is in
[`LICENSE`](LICENSE). This repository and its release archives contain only the
project's own source, scripts and documentation. No dependency below is bundled:
setup downloads or builds each one on the player's machine, at the pinned version.

## Not included, and never redistributed

- **Kirby Super Star Ultra** (Nintendo / HAL Laboratory). Players supply their own
  USA revision 0 ROM. No ROM, ROM extract, sprite, sound or save state is included.
- **Dead Cells** (Motion Twin / Evil Empire). Players need their own Steam install.
  No game file, bytecode, asset pack or save is included.

Names are used only to describe compatibility. This project is not affiliated
with or endorsed by Nintendo, HAL Laboratory, Motion Twin or Evil Empire.

## Built or downloaded by setup

| Component | Version | License | Source | Used by |
| :--- | :--- | :--- | :--- | :--- |
| melonDS | `82fdbc78483f43b310e920e21acc47787cb43564` | GPL-3.0-or-later | <https://github.com/melonDS-emu/melonDS> | Embedded emulator in the NDS bridge |
| melon-rs | `51f25edaf3b2df63f91d049eff8c38fb289150eb` | GPL-3.0 | <https://github.com/benhall-7/melon-rs> | Rust wrapper around melonDS |
| Dead Cells Core Modding | 35.13.6 (`linux-x64.zip`) | MIT | <https://github.com/dead-cells-core-modding/core> | Mod loader and runtime host |
| DeadCellsCoreModding.MDK | 1.0.1 | MIT | <https://github.com/dead-cells-core-modding/core> | Build-time mod SDK (NuGet) |
| .NET SDK | 10.0.401 | MIT | <https://github.com/dotnet/sdk> | Builds the runtime |
| chrono (Rust crate) | 0.4.45 | MIT OR Apache-2.0 | <https://github.com/chronotope/chrono> | NDS bridge |
| sha2 (Rust crate) | 0.10.9 | MIT OR Apache-2.0 | <https://github.com/RustCrypto/hashes> | NDS bridge |
| serde_json (Rust crate) | 1.0.151 | MIT OR Apache-2.0 | <https://github.com/serde-rs/json> | Oracle runner |
| libjpeg-turbo (`libturbojpeg0`) | Ubuntu 1:2.1.5-4ubuntu4 | BSD-3-Clause and IJG | <https://libjpeg-turbo.org> | Preloaded for DCCM's `fmt.hdll` |
| NumPy (Python) | >=1.26 | BSD-3-Clause | <https://github.com/numpy/numpy> | Asset tools |
| Pillow (Python) | >=12.1,<13 | MIT-CMU | <https://github.com/python-pillow/Pillow> | Asset tools |

Revisions are pinned in `tools/oracle_build.py`, `tools/nds_build.py`,
`scripts/setup_runtime.py`, `scripts/launch_runtime.py` and `requirements.txt`, and
setup checks each download's hash. Cargo resolves the crates' own dependencies at
build time; no `Cargo.lock` is pinned.

## Modified GPL source

The bridge builds melon-rs and melonDS with this project's patches, which are
part of this repository's source: `src/nds/melon-rs-gpu.patch` and the patch
stages in `tools/nds_*_patch.py`, applied by `tools/oracle_build.py` and
`tools/nds_build.py`. The bridge (`src/nds/lib.rs`) links that code, so the bridge
and anything distributed with it are covered by the GPL.

Release archives contain no binaries. Anyone who distributes a built bridge
(`libkssu_bridge.so`) must also offer its complete corresponding source: this
project's source at the same commit, plus melon-rs and melonDS at the revisions
above.

## Research references

ROM memory addresses and structure layouts were cross-checked against
[melon-kssu](https://github.com/benhall-7/melon-kssu) at
`0af58922f3f940244bc239f186508963950fddb3`, which declares no license. Only
facts (addresses and field offsets) are cited; no melon-kssu code is included.
