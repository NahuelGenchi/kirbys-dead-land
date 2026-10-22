> Historical reconnaissance from the initial foundation. The playable original-ROM
> runtime, outfit/CDB integration and live verification were implemented afterward.
> See [RUNTIME_LAUNCH.md](RUNTIME_LAUNCH.md) and [GAMEPLAY_FIDELITY.md](GAMEPLAY_FIDELITY.md)
> for the current behavior. Statements below about missing runtime features describe that earlier stage.

# Runtime reconnaissance and adapter status

Inspected on 2026-10-03. The new mod is **not yet a playable Kirby port**. The existing Sonic project changes assets only. This request needs a controller, collision and combat implementation driven by the original Nintendo DS game, plus host integration. No verified original-gameplay provider is available yet; the compiled adapter deliberately stays dormant instead of substituting Dead Cells movement or weapons for Kirby's moves.

## Installed host

- Owned Steam Linux installation: `~/.local/share/Steam/steamapps/common/Dead Cells`.
- Steam app ID: 588650; installed build ID: **23762174**.
- Native executable: ELF64 x86-64 PIE, unstripped. GNU build ID: `bfc9155c89db6bc9148ceac463e3f7ea0203dca9`.
- Executable SHA-256: `d4e2a6e26851f0cefe46975e999d6e5d43edea64bf05fb3d0461c16f145bf5e6`.
- Loose HashLink bytecode `hlboot.dat`, SHA-256: `59350cad475a2375b9e39d7cd13fdb6e4ed9a027fd827ae5140f913c1c07ce7e`.
- Existing `res.pak` contains the user's Sonic mod. It was not changed by this work.
- `libhl.so` exports dynamic field access and object-runtime APIs, but not `hl_module_init` or `hl_code_read`. A simplistic LD_PRELOAD replacement for those functions is therefore not a ready route on this installation.

The previous project's crashlink 0.0.9 environment and function-name index were used read-only. Game-derived bytecode/decompilation remains outside this source project.

## Selected framework and evidence

[Dead Cells Core Modding](https://github.com/dead-cells-core-modding/core) has a native Linux x64 release and a C# mod API. The latest checked release was [v35.13.6](https://github.com/dead-cells-core-modding/core/releases/tag/v35.13.6), released 2026-09-21, source commit `31f597bc462e6767f28a62e14ed36d65721e6d46`. Source `Directory.Build.props` targets .NET 10. Its CI builds/tests both Linux and Windows, against Steam and GOG HashLink inputs; this is stronger evidence than assuming Windows-only support from the README's examples.

Downloaded official Linux release: `linux-x64.zip`, 28,416,176 bytes, SHA-256 `c57b1fca88e688d228fe5e0aa2b3b4789117f7ff9b092af5c9fd5ada1d3c14a2`.

The real `core/mdk/ref/GameProxy.dll` was inspected using Mono.Cecil, and the runtime compiled against it. **This verifies API signatures, not successful execution against this installed game's bytecode.** Upstream Linux support does not prove this particular mod or host combination works in a running game.

The NuGet dependency `DeadCellsCoreModding.MDK` version 1.0.1 is an MSBuild bridge. It requires the installed release MDK through `DCCM_MDK_ROOT`; installing only that NuGet package is insufficient. The upstream MDK shell installer edits shell configuration and global NuGet sources, so this project uses command-local environment variables instead.

Compiler: official .NET SDK **10.0.401**, installed under `~/.local/share/kirby-dead-cells/dotnet`. SDK archive SHA-512 is pinned in `scripts/setup_runtime.py`, from [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json). Since #184 the build also restores `Microsoft.NETCore.App.Crossgen2.linux-x64` from NuGet at the runtime version the SDK carries (10.0.12, about 38 MB) and compiles the packed `Kirby.Core` and `Kirby.Runtime` ahead of time with it (`src/runtime/Kirby.Runtime.csproj`; [the first Kirby frame](PERFORMANCE.md#the-first-kirby-frame-184)).

## Concrete host API

The following signatures are present in the released GameProxy reference assembly and are used by the compiled adapter:

| Purpose | Exact API |
|---|---|
| Entry point | `KirbyMod : ModBase`, constructor `KirbyMod(ModInfo info)`, `Initialize()` |
| Current player | `ModCore.Modules.Game.Instance.HeroInstance` (`dc.en.Hero`) |
| Character selection | `hero._level.game.user.heroSkin` |
| Equipment use | `dc.tool.Hook_Weapon.tryToUse`, callback `bool(orig_tryToUse orig, Weapon weapon)` |
| Equipment change | `dc.tool.Hook_Inventory.equip`, callback `bool(orig_equip orig, Inventory inventory, InventItem item)` |
| Fixed controller update | `dc.en.Hook_Hero.fixedUpdate`, callback `void(orig_fixedUpdate orig, Hero hero)` |
| Frame callback | `IOnHeroUpdate.OnHeroUpdate(double dt)` |
| Level transition | `dc.tool.hero.Hook_HeroWeaponsManager.onLevelChanged` |
| Hero disposal | `IOnHeroDispose.OnHeroDispose()` |
| Equipped slots | `hero.inventory.nbWeapons`, `hero.inventory.getEquippedWeaponOn(int)` |
| Physical shield identity | `InventItem.permanentId`; type via `item.hasTag("Shield")` |
| Database item identity | `InventItem.getItemKind()` |
| Enemy instance identity | `dc.en.Mob.__uid`, plus its level identity |
| Enemy lifecycle | `Mob.destroyed`, `Mob.life`, `Mob.kill()`, `Mob.destroy()`; these are host operations, not an inhale implementation |
| Shared input plumbing | `hero.controller`, `ControllerAccess.onActPressed`, `HeroWeaponsManager.hasButtonDownForWeapon` / `hasButtonPressedForWeapon` |
| Host motion state | `Entity.dx`, `dy`, `gravity`, `frict`, `cx`, `cy`, `xr`, `yr`, `dir`; `Hero.runSpd`, `accelFactor` |

Installed bytecode name/index corroboration includes `en.Hero.fixedUpdate` 2155, `en.Hero.preUpdate` 2137, `tool.Weapon.tryToUse` 1870, `en.Hero.onMobDeath` 1953, `en.Mob.onDie` 1061, and `Entity.fixedUpdate` 950. Those indices describe this host build and are not hard-coded into the adapter.

Movement values are exposed but were **not changed to guessed Kirby constants**. The DS gameplay code is not portable by extracting sprite files; original fixed-point integration, action transitions, movement modes, timing and collision behavior still require tracing and validation.

## Implemented adapter behavior

`src/runtime/KirbyMod.cs` registers real host hooks and delegates to the independently tested user rules in `src/core` only after a gameplay provider is explicitly registered and the selected outfit is `KirbySuperStarUltra`.

- Base form may use explicitly classified swords, bows and shields.
- Weapon groups are not a safe classification: Dead Cells' ranged group includes guns, magic and thrown weapons; `isBow` can include non-bows. `config.json` therefore uses item IDs inspected in the installed database. Crossbows, daggers, spears and guns are excluded. The list may need additional semantic review before final gameplay acceptance.
- Evolution blocks host non-shield attacks and invokes the registered ability handler.
- The retained shield is identified by its physical `permanentId`, not display name or database kind. A second copy of the same shield type cannot replace it after evolution.
- Inventory restrictions affect weapon and active-item equip calls, not passive progression items, outfits or consumables.
- Ordinary enemy death never grants an evolution.
- `KirbySession.CompleteAbsorption` requires a live enemy from the same level, successful consumption callback, and a dead/destroyed enemy before awarding a random implemented ability. It is intended to be called after an original inhale/swallow sequence; no such sequence is supplied yet.
- Random choices come only from `IKirbyGameplayProvider.ImplementedAbilityIds`; an empty or duplicated provider list is rejected. There are no fake ability handlers.
- The level callback clears consumed enemy IDs while retaining the current session's ability and shield. This only works while the same Hero instance survives; replacement/deserialization of Hero destroys the session. Cross-level persistence is not established.

The adapter **does not yet** provide the original controller, inhale, floating, swimming, slide, spit stars, copy-ability action graphs, source-accurate attack damage/hitboxes, sprite choreography, sound playback, HUD or controller bindings. It does not currently persist evolution or retained-shield state across save/reload, new Hero instances, run restart or death. Cross-level state needs explicit host lifecycle verification. No actual enemy-consumption lifecycle implementation is supplied: `CompleteAbsorption` is a registration/integration contract that checks the provider callback's result, not a working inhale/swallow mechanic. `Weapon.tryToUse` handles native weapon-use attempts; an eventual provider must also route direct ability input independently of native weapon availability/ammo/readiness. A complete input port must test empty equipment, locked input, pause, two weapons, held inputs, death and room changes. These are substantive unfinished work, not optional polish.

## Assets without modifying the global archive

DCCM's official `sample/SampleSimple` builds a local `res.pak` using:

```xml
<GenerateSinglePakFile>true</GenerateSinglePakFile>
<PackAssets Include="assets/**/*" RootInPak="kirby" />
```

The bootstrap can load an externally built `res.pak` beside its `modinfo.json` with `FsPak.Instance.FileSystem.loadPak(...)` during `IOnAfterLoadingAssets`. It does not reload or patch the host CDB: the current pack contains decoded pose atlases and metadata only. When an actual outfit database patch is authored, the explicit merge API is `CDBManager.Instance.LoadJsonData(CDBManager.Instance.GetAlteredCDB())`.

Use differential CDB entries rather than a complete replacement database to preserve Sonic and other local modifications. The official converter is:

```text
DCCMTool cdb diff -i edited.cdb -t original.cdb -o diff.pak
```

The resulting archive contains records below `data.cdb_/<sheet>/<row>.json`; `__separator_group_Name` identifies the row group (for an item outfit, `Skin`). A full outfit needs the matching skin database entry and complete model assets. Decoded standalone pose atlases do not satisfy that requirement.

`LoadDecodedAssetAtlases` defaults to **false**. Setting it true loads local atlas resources for developer inspection without a gameplay provider. It does not register an outfit, map animations, or replace the selected character. No cosmetic substitute or playable preview is enabled. No atlas-loading game session was launched.

## Reproduce the verified build and staging

```bash
python3 scripts/setup_runtime.py --build
python3 scripts/stage_runtime.py
```

Paths:

- Source project: `~/Games/mods/kirby-super-star-ultra-dead-cells`.
- Build output: `src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra/`.
- SDK and dependencies: `~/.local/share/kirby-dead-cells/`.
- Isolated development lab: `~/.local/share/kirby-dead-cells/lab`.
- Staged adapter: `~/.local/share/kirby-dead-cells/lab/coremod/mods/KirbySuperStarUltra`.

The setup script pins release URLs and archive hashes, uses local environment variables, and never installs into the game. The staging script copies the runtime into the lab, excludes the optional Goldberg emulator, explicitly sets `EnableGoldberg=false`, and links game files as inputs. It creates no links to real saves. **Neither script starts the game.** The lab's `save/` is only the loader's default mod-data location; isolation of all original-game Steam/cloud writes has not been proven and must be checked before the first game launch. Do not assume the directory alone proves save isolation.

## Windows (#244)

The adapter's source is the same on Linux and Windows. What differs is the names of the native libraries and the system the packed DLLs are compiled for.

- **Library names.** `src/runtime/NativeLibraries.cs` registers one import resolver for the assembly when it loads. P/Invokes use three short names. `kssu_bridge` is the bridge that `NdsRuntime.Open` loaded from the mod's folder: `libkssu_bridge.so` or `kssu_bridge.dll`, the files `tools/nds_build.py` builds for each system. `SDL3` is `libSDL3.so.0` or `SDL3.dll` (SDL2 until #248), and `GL` is `libGL.so.1` or `opengl32.dll`, the game's own copies. Before this change the resolver was set inside `NdsRuntime.Open`, and SDL and OpenGL were named by their Linux files.
- **Performance probe.** `KIRBY_LAB_PERF` reads `/proc`, HashLink's `libhl.so` and Linux thread IDs. On other systems it only writes `perf.json` with an `unsupported` note.
- **ReadyToRun.** `-p:ModReadyToRunOS=windows` compiles the packed `Kirby.Core` and `Kirby.Runtime` for Windows on Linux. It references the Windows framework of the same runtime version (`Microsoft.NETCore.App.Runtime.win-x64` 10.0.12, restored from NuGet only for that build). The default is the build machine's system, so the Linux build compiles as before. Images go to `obj/Release/net10.0/r2r/<system>/`.

Since #246 the runtime also builds with the Windows SDK: on a Windows build machine the build runs `crossgen2.exe`. That build passed under Wine ([Windows scripts](RUNTIME_LAUNCH.md#windows-246)).

Checked on 2026-10-21. This is Linux and Wine evidence; nothing has run on Windows 11 yet.

- `python3 scripts/check.py` passes on Linux (78 Python tests, including `tests/test_windows_runtime.py`, plus every C# build).
- `tests/nds` against the Linux bridge, the ROM and `oracle/ready2/000.state` passes: ABI sizes, seed, walking, facing, RAM bounds, 3 live hits.
- The same `tests/nds` source, published for `win-x64` (self-contained .NET 10.0.12), passes under Wine (Proton Experimental) against the #242 `kssu_bridge.dll` with the same result. Under Wine it needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, because Wine's ICU lacks a function .NET calls, and Wine's own `mscoree`, which loads .NET images.
- The packed DLLs are Linux x64 images (PE machine `0xFD1D`) by default and Windows x64 images (`0x8664`) with `-p:ModReadyToRunOS=windows`. The Linux compiler arguments are the same as before apart from the output folder. Whether the Windows runtime uses the precompiled code was not measured.
- **Not checked:** the OpenGL file name against the Windows game, which was not inspected. Also unchecked: DCCM on Windows, staging, the launcher, anything on Windows 11, and a live game check.

### SDL3 audio (#248)

DCCM 35.13.6 ships SDL3 alone on both systems, so Kirby's sound effects now use SDL3 and the `SDL3` name. Read-only inspection of the pinned archives:

| Package (SHA-256) | SDL file | SDL version | `sdl.hdll` links | SDL2 |
| :--- | :--- | :--- | :--- | :--- |
| `linux-x64.zip` (`c57b1fca…14a2`) | `core/native/linux-x64/libSDL3.so.0` | 3.4.2 | `libSDL3.so.0` | none |
| `win-x64.zip` (`9ceb86e7…31ba`) | `core/native/win-x64/SDL3.dll` | 3.4.16 | `SDL3.dll` | none |

Both packages also carry `core/host/SDL3-CS.dll`. `SDL3.dll` exports every function `NdsAudio` binds. The installed Linux game folder still has its own `libSDL2-2.0.so.0`, but DCCM's game process does not load it: the lab's `LD_LIBRARY_PATH` holds only DCCM's native folder, and in the live run below the game mapped DCCM's `libSDL3.so.0` and `sdl.hdll` and no SDL2. Before #248 the SDL2 binding could only find a system SDL2 on Linux, and nothing on Windows.

Checked on 2026-10-21 (details in [AUDIO.md](AUDIO.md#sdl3-on-both-systems-248)):

- `python3 scripts/check.py` passes (102 Python tests, 6 new in `tests/test_sdl_audio.py`).
- `tests/audio` passes on DCCM's Linux `libSDL3.so.0`, and, built for `win-x64`, under Wine with DCCM's `SDL3.dll`. The SDL2 build of the same test fails under Wine with `Unable to load DLL 'SDL2'`, the silent-audio case this fixes.
- One isolated Linux live run with Kirby active: [SDL3 audio (#248)](RUNTIME_LAUNCH.md#sdl3-audio-248).
- **Not checked:** Windows 11, and whether the Windows game's process loads DCCM's `SDL3.dll` from `core/native/win-x64` (expected, since its `sdl.hdll` imports it).

The upstream loader intentionally disables achievement unlocks (`Game` and `SteamPlatformModule` hooks). Do not promise that achievements remain enabled. This differs from the existing Sonic archive-only approach. The upstream runtime also intercepts platform functions; no ownership-check changes were authored here, and no Steam emulator was enabled or launched. The separately documented Nintendo DS oracle uses FreeBIOS in a standalone process.

## Validation completed

- Verified official archive SHA-256 and native Linux package contents.
- Ran SDK 10.0.401 on the local Linux host.
- Inspected exact runtime reference types and delegate signatures with Mono.Cecil.
- Compiled `Kirby.Runtime.csproj` in Release: **0 warnings, 0 errors**.
- Checked Python setup/staging syntax and executed staging successfully.
- Ran the core rule test runner: **26 user-rule assertions passed**.
- No real-game bootstrap, sprite display, player controls, absorption, copied attack or parry was observed. No gameplay verification or completion claim is justified yet.

Primary upstream references: [source](https://github.com/dead-cells-core-modding/core), [Linux release](https://github.com/dead-cells-core-modding/core/releases/tag/v35.13.6), [official samples](https://github.com/dead-cells-core-modding/core/tree/main/sample), [framework documentation](https://dead-cells-core-modding.github.io/docs/docs/).

## Independent PAK validation and upstream unpack limitation

`tests/pak/Kirby.Pak.Verify.csproj` uses **the released `GameRes.Core.dll` 35.13.6**, rather than the Python builder's own parser. It opens the archive read-only through `GameRes.Core.Pak.PakFile`, reads each payload synchronously without memory mapping or extraction, checks the exact set of archive paths, verifies every payload SHA-256 against `build-manifest.json`, checks each stored Adler-32 with the released reader's checksum implementation, and verifies the whole-archive SHA-256. Nothing is written out by the verifier.

Run after regenerating an asset pack:

```bash
DCCM_MDK_ROOT=$HOME/.local/share/kirby-dead-cells/research/dccm-35.13.6/core/mdk \
  ~/.local/share/kirby-dead-cells/dotnet/dotnet run \
  --project tests/pak/Kirby.Pak.Verify.csproj -c Release -- \
  ~/.local/share/kirby-dead-cells/assets-final/build-manifest.json
```

An optional second positional argument selects a PAK other than `res.pak` alongside the manifest. Nonzero exit indicates missing/unexpected/duplicate entries, payload or archive hash mismatch, header checksum mismatch, or parser errors.

Final verification after sprite-bound and rebuild fixes passed for archive `1bc2d0b700e85167e3d905854f6c0c320b933622cb3ce0f47ce5e9df8ddd39bf`: **331 assets, 24,126,968 payload bytes, zero empty payloads**. Reader DLL SHA-256: `ecc72b7a0f2d22c79e6de2c9f379827e189c6fa6a076916d57a5f4a1e5aba0e4`. Negative cases for wrong archive digest, wrong payload digest, unexpected entries and missing entries each returned failure as intended.

The official DCCMTool `pak unpack` command returned exit 0 during an earlier export with six empty output files. Source inspection explains why exit status is insufficient: [`UnpackPakCommand.Execute`](https://github.com/dead-cells-core-modding/core/blob/31f597bc462e6767f28a62e14ed36d65721e6d46/mdk/DCCMTool/Commands/Pak/UnpackPakCommand.cs) calls `File.WriteAllBytesAsync` in both export branches without awaiting or joining those tasks, then returns 0. The process can exit with file writes unfinished. Direct reader verification above avoids that export race. A successful unpack exit alone is **not proof of a complete export**, and PAK integrity is still not proof of in-game compatibility or faithful NDS mechanics.
