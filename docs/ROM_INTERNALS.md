# ROM internals

What the Kirby Super Star Ultra program looks like from the inside: where its code sits in memory, how one original frame is spent, and the tables that drive Kirby, his copy abilities, hits, enemies and sound. Issue #146, read on 2026-10-13 from the USA revision 0 ROM (SHA-256 `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`).

Nothing here changes the mod. It records facts the bridge and the runtime can rest on, the measurements behind them, and the fix candidates they suggest.

## How it was established

Two methods, named beside each result.

- **Static**: the ARM9 program and its overlays were disassembled with capstone into a private function index (5,996 functions in the code loaded during play) with literal and call cross-references.
- **Run**: a private copy of the bridge under `repros/re-debug` in the private data root, with switches added to melonDS for this pass only: an executed-instruction histogram, read and write watchpoints, a breakpoint that records the registers, a dump of the stack memory `kssu_read` cannot reach, timers around each emulator stage, the emulated ARM9 clock, the 2D renderer and the recompiler. Every run starts from the `ready2` Green Greens state with `kssu_main_screen_only` on.

All runs are headless. **None is a live game check**, and no claim here says anything works in play. The shared bridge, its evidence, the installed game and the saves were not touched. The disassembly, the index and the instrumented emulator stay in the private data root.

## The program in memory

The cartridge image is 134,217,728 bytes and uses 76.0 MB. Video is 46.4 MB of that (51 files under `movie`); the level data is one 10.65 MB file; all of Kirby's sprite archives are 2.73 MB; the sound archive is 1.28 MB. The ARM9 program is 291,320 bytes at `0x02000000` and is not compressed; the ARM7 program is 159,528 bytes.

The rest of the code is in 157 overlays, loaded on demand. During play in Green Greens these are present (static, checked against the RAM of the ready state):

| Overlay | Address | Bytes | Role |
| ---: | :--- | ---: | :--- |
| 23 | `0x02071240` | 23,744 | hit and collision system; its 33,408 bytes of zeroed data follow at `0x02076F00`, where the hit lists start |
| 24 | `0x0207F180` | 131,520 | scene: level file, spawner, HUD |
| 25 | `0x020A0080` | 110,240 | player core and the `rom/pd/*.b` sprite loader; the player record `0x020BAF68` is in its data |
| 26 | `0x020BC180` | 592,896 | Kirby's actions and all 24 copy abilities; 2,212 functions, the largest overlay |
| 27 | `0x0214CD80` | 143,552 | enemy, stage-object and mid-boss file tables |
| 28 | `0x021700A0` | 163,424 | code shared by enemies and objects |
| 29 | `0x02197F20` | 19,680 | the Waddle Dee family |

Overlays 1 to 3 are large and share `0x020B8880`, so only one is ever present; none is loaded during play.

### Enemy slots

Each ordinary enemy species is its own small overlay, and those overlays are linked at five fixed addresses (static, from the overlay table):

| Slot | Address | Overlays |
| ---: | :--- | :--- |
| 0 | `0x021A2B00` | 61 to 74 |
| 1 | `0x02197F20` | 29 to 39 |
| 2 | `0x021A4CE0` | 75 to 86 |
| 3 | `0x0219F2A0` | 51 to 60 |
| 4 | `0x0219CC00` | 40 to 50 |

Two overlays of one slot occupy the same memory, so a room of the original can hold at most one species from each slot. The mod's proxies are Waddle Dees, slot 1. A second real species could only come from another slot; Blade Knight, Chilly or Burning Leo could never be loaded beside Waddle Dee.

## One original frame

Run, idle Kirby, 60 frames.

The logic pass is the chain `0x02000BB8` (main) → `0x0201452C` → `0x020141CC` → `0x02001258` → `0x0201FEE0` → the task runner `0x0200E970` → `PLAYER_UPDATE 0x020B2714`. `PLAYER_UPDATE` is a straight run of about twenty calls on the actor and its record. The sprite list is built by `0x020100A0`, twice a frame. A second thread runs `0x0201B0C8` → `0x0201CC94` → `0x0201E2DC`, which calls `0x0201E89C` 229 times a frame.

A DS frame gives the ARM9 560,190 system cycles. Idle, the original is busy for 34.7% of them and halted for 65.1%, about 150,000 instructions in main RAM a frame, almost all ARM (2 Thumb instructions a frame). 474 distinct functions run. By region: the ARM9 program 73.7%, overlay 24 17.9%, overlay 25 3.9%, overlay 23 2.4%, overlay 27 1.6%, and Kirby's own overlay 26 only 0.3%. The single hottest functions are system ones: `0x0201CD2C` 8.7%, `0x0201E2DC` 7.8%, the sprite-list builder 6.7%.

### How full the frame gets

Run, Sword Kirby pressing B, 360 frames a row. "Unfinished" counts frames that ended before the logic pass did, the lag frame of the original and the same count as the bridge's `kssu_unfinished_passes`.

| Proxies | Helper | Mean busy | 95th percentile | Peak | Unfinished |
| ---: | :--- | ---: | ---: | ---: | ---: |
| 0 | no | 35.5% | 42.9% | 52.0% | 0 |
| 0 | yes | 40.3% | 47.6% | 57.1% | 0 |
| 8 | no | 41.5% | 48.8% | 90.8% | 0 |
| 8 | yes | 47.7% | 61.3% | 94.8% | 0 |
| 16 | yes | 54.0% | 67.2% | 84.1% | 0 |
| 24 | yes | 60.6% | 88.0% | 100% | 5 |
| 31 | no | 56.6% | 70.5% | 100% | 3 |
| 31 | yes | 63.8% | 83.8% | 100% | 2 |

With the emulated ARM9 clock doubled (run, same inputs):

| Proxies | Helper | Mean busy | 95th percentile | Peak | Unfinished |
| ---: | :--- | ---: | ---: | ---: | ---: |
| 16 | yes | 32.1% | 39.4% | 48.4% | 0 |
| 31 | yes | 38.9% | 53.5% | 82.4% | 0 |

Kirby's 600-frame trace of position and pose is byte-identical at both clocks with 0 and with 8 proxies, where the original never lags. It differs only in the 31-proxy case, where the normal clock drops frames and the doubled one does not.

## Kirby's action machine

Static and run. Offsets are from the player record `R = 0x020BAF68`.

| Field | Meaning |
| :--- | :--- |
| `R+0x14` | player field 4: 2 while Kirby holds a mouthful, 1 when he holds nothing (#158) |
| `R+0x18` | action id, 0x01 to 0x2C |
| `R+0x19` | action group, from the table `0x020BAAC4[id]` |
| `R+0x1C` | pending action, 0 for none |
| `R+0x7FC` | action table, `0x02131688` for every ability |
| `R+0x800` | the current ability's move table, 0 for Normal |
| `R+0x804` | current action descriptor |
| `R+0x808` | current move descriptor |
| `R+0x818` | first word of the ability descriptor |

The 16-bit action code the docs already use (`0x020BAF80`, for example `0x142C`) is group then id. `0x020B1D78` changes action: it calls the old action's exit, points `R+0x804` at `table + id × 24`, and calls the new action's enter. Every action shares the same five handlers; only its first word differs. That word is a selector, which returns a **move**: another 24-byte descriptor, stored at `R+0x808`. So an action is a state, and a move is what Kirby does inside it. `(R+0x808 − R+0x800) / 24` is the index of the running copy move.

A mouthful is not an action of its own: it keeps the groups of standing, walking and jumping. The stand's selector `0x0210EA60` returns `0x021314C0 + n × 24` with `n` picked from player field 4 (`R+0x14`, read through `0x020A8214`): 3 when field 4 is 2, the mouthful stand `0x02131508`, and 1 when it is 1, the plain stand `0x021314D8` (6 with Plasma, 5 when field 5 is 4). Run (bridge case `move_mouthful`): field 4 is 2 from the stand after a catch, through the mouthful walk `0x02131148`, jump `0x021311C0`, fall `0x02131238` and landing `0x021310E8`, until a spit's star leaves or a swallowed copy has loaded, and 1 again after; the mouth count at `0x020BB211` is cleared by the spit but stays set after a swallow until the next inhale. The give-ability routine `0x020AF020` calls `0x020AEF70` after `SetAbility`, which sets field 4 back to 1 and asks for a stand or a fall. With Sword, `R+0x800` was `0x0212F7EC` and a swing index 3; in base form it was 0.

Actions named by driving the input and reading the fields (run):

| Code | Action | Moves seen |
| :--- | :--- | :--- |
| `0406` | stand | plain; mouthful |
| `0507` | landing | plain; mouthful |
| `0509` | wall bump | |
| `060A` | skid | |
| `060B` | walk | plain; mouthful; wading |
| `060C` | dash | |
| `070D` | jump | plain; mouthful |
| `070E` | fall | plain; mouthful |
| `070F` | float | |
| `0815` | swim | three |
| `0916` | climb | |
| `0A17` | guard | |
| `0B18` | slide | |
| `0E1B` | ability change | drop with Select; make a helper with X; swallow and transform (about 79 frames) |
| `0F1D` | hurt | |
| `1021` | death | |
| `142C` | ability use | Normal: inhale, spit, exhale, water gun; a copy ability: its own table |

Ids 01 to 05, 08, 10, 11, 13, 14, 1C, 1E, 1F, 20 and 22 to 2B were not reached and are not named.

Animations are lists of 32-bit commands in overlay 26 data. Commands `0x18` and `0x1C` show a pose (high 16 bits) for a number of frames (second byte); `0x0040FF01` ends or loops. Other commands (`0x02`, `0x03`, `0x08`, `0x31`, `0x35`, `0x38`, `0x42`, `0x44`, `0x48`) are not decoded. These are the timed sequences `ROM_FORMAT.md` lists as not recovered.

## Copy abilities

Static and run. `SetAbility 0x0211B734` calls the old ability's exit from `0x0214A038` (16 bytes an ability: enter, exit), then reads a descriptor pointer from `0x02149C98`. A descriptor is 16 bytes: a word copied to `R+0x818`, the move table copied to `R+0x800`, and the animation set. A second pointer table, `0x02149C28`, is used when the byte at `0x0205B6A5` is 2; it differs from the first only for Jet.

Move tables, in ability order from Cutter (2) to Sleep (25): `0x02129DE8`, `0x0212A20C`, `0x0212A818`, `0x0212AD20`, `0x0212B5B4`, `0x0212BC64`, `0x0212C2DC`, `0x0212C8E0`, `0x0212D004`, `0x0212D694`, `0x0212DD70`, `0x0212DF58`, `0x0212E12C`, `0x0212E8AC`, `0x0212EF6C`, `0x0212F2E8`, `0x0212F7EC`, `0x0212FFEC`, `0x02130610`, `0x021307B0`, `0x0213089C`, `0x02130970`, `0x02130A64`, `0x02130B44`.

### What each ability does

Run. Each ability was set with `SetAbility` and given ten inputs (B, held B, forward, up or down with B, dash then B, and four in the air), with no target present. A row reports the copy moves that ran, the damage numbers of the hits Kirby's own hit lists held, the largest hit box, the fastest Kirby moved, and the hit-list slots used. It is a sweep, not a full move list: grabs and hits that need a victim do not appear.

| Id | Ability | Moves seen | Damage | Largest box | Fastest px/frame | Slots |
| ---: | :--- | ---: | :--- | :--- | ---: | :--- |
| 2 | Cutter | 4 | 12–22 | 45×33 | 5.6 | 7, 13 |
| 3 | Mirror | 3 | 12–22 | 29×33 | 2.9 | 3, 7, 13 |
| 4 | Beam | 3 | 6–18 | 21×29 | 3.5 | 7, 13 |
| 5 | Fighter | 4 | 5–22 | 13×33 | 4.8 | 7, 13 |
| 6 | Suplex | 4 | 16 | 35×21 | 6.9 | 7 |
| 7 | Yo-Yo | 2 | 10–18 | 27×34 | 3.5 | 7, 13 |
| 8 | Wheel | 2 | 14–23 | 31×39 | 3.5 | 7 |
| 9 | Bomb | 2 | 16 | 21×21 | 3.8 | 13 |
| 10 | Ice | 3 | 8–20 | 71×70 | 3.5 | 7, 13 |
| 11 | Fire | 3 | 6–20 | 33×29 | 4.0 | 7, 13 |
| 12 | Plasma | 1 | 4–8 | 23×11 | 3.5 | 7, 13 |
| 13 | Ninja | 5 | 6–25 | 49×42 | 6.0 | 7, 13 |
| 14 | Stone | 1 | 20–50 | 33×23 | 6.0 | 7, 13 |
| 15 | Wing | 3 | 4–26 | 37×29 | 8.0 | 7, 13 |
| 16 | Jet | 3 | 10–14 | 29×45 | 4.0 | 7, 13 |
| 17 | Copy | 1 | 10 | 15×11 | 1.9 | 7 |
| 18 | Sword | 4 | 12–24 | 29×39 | 5.7 | 7, 13 |
| 19 | Hammer | 4 | 4–80 | 29×25 | 2.7 | 7, 13 |
| 20 | Parasol | 4 | 4–28 | 39×23 | 4.1 | 7, 13 |
| 21 | Paint | 1 | 70 | 513×513 | 0 | 2 |
| 22 | Mike | 1 | 40 | 513×513 | 0 | 2 |
| 23 | Crash | 1 | 160 | 513×513 | 0 | 2 |
| 24 | Cook | 1 | 70 | 513×513 | 0 | 1 |
| 25 | Sleep | 1 | none | | 0 | |

Slot 7 holds hits attached to Kirby's body, slot 13 projectiles and elements, slot 2 the screen-wide attacks, slot 1 Cook, slot 3 Mirror's reflector. The 80 is Hammer's up + B flip; the next largest ordinary hit is Stone's 50 from the air.

## Hits and health

Static and run. A hit group is 0x240 bytes with four 0x8C nodes. `0x02071B38` builds one from a record this document calls the **vitals**:

| Offset | Size | Meaning |
| ---: | ---: | :--- |
| 0 | s8 | class, 0 to 59, to group `+0x238` |
| 1 | s8 | copy ability given when swallowed, to `+0x239` |
| 2 | s16 | health, to `+0x23A`, `+0x23C` and `+0x23E` |
| 16 | s16 | body hit: damage, negative |
| 32 | 4 × s16 | body hit box: x, y, width, height |

`0x02071DF8(group, field, value)` sets one attribute later: field 0 the class, 1 the copy ability, 6 and 7 the health words. A breakpoint on `0x02071ADC` while a proxy spawned gave Waddle Dee's vitals at `0x0219C8C0` (class 0, no copy, health 16) and Parasol Waddle Dee's at `0x0219C8F0` (copy 20, health 16).

Groups come from one pool of 48, which `0x02071C00` sets up at each stage load: header `0x02076F00` (used list `+0x80`, free list `+0x84`, count `+0x8C`), groups from `0x02077428` every `0x244` bytes, each with a first word that links it into one of the two lists. `0x020714DC` takes one and `0x02071508` gives it back; `0x020742C8(descriptor, callback, parent)` creates a collision actor with one. Only the setup reads the count or where the groups are. An enemy holds one for its body. Each hit makes the recoil routine `0x02156ACC` (overlay 27) create a companion that holds one more for about 7 frames, and then write the enemy's three health words into the companion's group without checking that it got one. A second pool of 96 (count `+0x8E`, records from `0x0207E0E8`, free list `+0x88`, taken by `0x02071384`) is checked by its callers. Thirty-one proxies and one swing that hit 24 of them ran the first pool out (#151; docs/NDS_BRIDGE.md, The collision pool).

## Objects, spawning and the enemy catalogue

Static, with the spawner located by a read watchpoint on a proxy's spawn record.

**Type table** `0x0209E10C`, 16 bytes a type, types 0x00 to 0xBF:

| Offset | Size | Meaning |
| ---: | ---: | :--- |
| 0 | s16 | class, an index into `0x0203E0B4` (8 bytes: flag, priority, init function) |
| 2 | s16 | overlay number, −1 when the code is always loaded |
| 4 | s16 | kind: 0 enemy or object, 1 mid-boss, 2 boss |
| 6 | s16 | slot |
| 8 | u32 | graphics file code |
| 12 | u32 | palette file code |

**File codes** are `(table << 12) | index`, index from 1, resolved by `0x02003820` through the table pointers at `0x0203D210`. Table 5 is `rom/ene05`, 7 `rom/mboss07`, 15 `rom/y15`. This is how each type below got its file name.

**Spawn records** are 52 bytes: `+0` state, `+1` type, `+2` and `+3` parameters, `+4` category (1 to 11), `+10` and `+12` position. The spawner `0x020941E0` in overlay 24 reads a record and calls the creator `0x02093BC4`. Green Greens' own records hold types 01, 02, 06, 0A and 1A: Waddle Dee, Waddle Doo, Cappy, Bronto Burt and Twizzy, which is the stage's cast. The bridge's proxy records are type 01, category 2.

**Catalogue.** Static: each enemy overlay was scanned for the descriptor that points at its vitals. Names are read from the file names; health and copy ability are the stored numbers, not observed in a fight. "Touch" is the body hit's damage against Kirby's full health of 76.

| Species | File | Types | Overlay | Slot | Health | Copy | Touch |
| :--- | :--- | :--- | ---: | ---: | :--- | :--- | :--- |
| Bomber | `bomber` | 05 | 61 | 0 | 16 | Crash | 7 |
| Cappy | `cappy` | 06 | 62 | 0 | 12 | none | 7 |
| Kabu | `kabu` | 17 | 63 | 0 | 16 | none | 7 |
| Laser Ball | `laserball` | 2F | 64 | 0 | 8 | Beam | 6 |
| Moto Shotzo | `motoshotzo` | 2E | 65 | 0 | 26 | none | 8 |
| Coconut | `coconut` | 37 | 66 | 0 | 1 | none | 7 |
| (file `shooting`) | `shooting` | 21 38 3B | 67 | 0 | not found | not found | not found |
| Waddle Dee, Waddle Doo | `wdeedoo` | 01 02 31 32 34 35 | 29 | 1 | 16, 18 | none, Beam | 7, 8 |
| Blade Knight | `bladeknight` | 20 | 30 | 1 | 24 | Sword | 8 |
| Walky | `walky` | 2B | 31 | 1 | 16 | Mike | 7 |
| Chilly | `chilly` | 29 | 32 | 1 | 20 | Ice | 8 |
| Burning Leo | `burninleo` | 0C | 33 | 1 | 16 | Fire | 10 |
| water enemies | `waters`, `waters2` | 1C 1D 1E | 34 | 1 | 8, 16, 16 | none | 7, 7, 10 |
| Plasma Wisp | `plasma` | 27 | 35 | 1 | 10 | Plasma | 10 |
| Bio Spark | `biospark` | 09 | 36 | 1 | 8 | Ninja | 6 |
| Capsule J | `capsulej` | 0B | 37 | 1 | 18 | Jet | 9 |
| (file `magical`) | `magical` | 2D | 38 | 1 | 18 | none | 7 |
| Sword Knight | `swordknight` | 3F | 39 | 1 | 48 | Sword | 8 |
| Shotzo | `shotzo` | 15 | 75 | 2 | 1 | none | 8 |
| Grizzo | `grizzo` | 14 | 76 | 2 | 55 | none | 11 |
| Gim | `gim` | 0E | 77 | 2 | 16 | Yo-Yo | 8 |
| Knuckle Joe | `knuckle` | 12 | 78 | 2 | 30 | Fighter | 8 |
| Jungle Bomb | `junglebomb` | 10 | 79 | 2 | 16 | Bomb | 8 |
| Birdon | `birdon` | 26 | 80 | 2 | 16 | Wing | 8 |
| Tac | `tac` | 2C | 81 | 2 | 8 | Copy | 6 |
| Sir Kibble | `sirkibble` | 03 | 51 | 3 | 20 | Cutter | 10 |
| Twizzy | `twitoo` | 1A 1B | 52 | 3 | 6 | none | 6, 7 |
| Gator | `gator` | 08 | 53 | 3 | 16 | none | 8 |
| Scarfy | `scarfy` | 18 | 54 | 3 | 16 | Crash | 7 |
| Poppy Bros. Jr. | `poppyjr` | 13 39 3A | 55 | 3 | not found | not found | not found |
| Gip | `gip` | 30 | 56 | 3 | 8 | none | 7 |
| Bandana Waddle Dee | `bandanawdee` | 07 | 60 | 3 | 250 | none | 7 |
| Noddy | `noddy` | 04 | 40 | 4 | 12 | Sleep | 8 |
| Bronto Burt | `brontoburt` | 0A | 41 | 4 | 8 | none | 7 |
| Rocky | `rocky` | 28 | 42 | 4 | 42 | Stone | 10 |
| Broom Hatter | `broom` | 16 | 43 | 4 | 16 | none | 7 |
| Simirror | `simirror` | 2A | 44 | 4 | 20 | Mirror | 7 |
| Wheelie | `wheelie` | 0F | 45 | 4 | 14 | Wheel | 6 |
| Squishy | `squishy` | 1F | 46 | 4 | 16 | none | 6 |
| Lovely | `lovely` | 11 | 47 | 4 | not found | not found | not found |
| Flotzo | `flotzo` | 3D | 50 | 4 | 32 | none | 6 |

Blade Knight and Sword Knight are overlays of the same size, 9,024 bytes, with their records at the same addresses; 2,761 of those bytes differ, so they are close relatives and not one program with other numbers. Types 22 to 25 are the Meta-Knights (Axe, Trident, Javelin, Mace) in overlay 117 with health 50 to 80. Types 3C and 41 to 4F are the seven mid-bosses in overlay 87 (files `ironmom`, `jukid`, `poppysr`, `cook`, `frosty`, `bonkers`, `bugzzy`), and types 50 to 6A are 27 boss entries in overlay 91; their health records were not found by this scan. Types 80 to 97 are common objects in overlay 28, and 78 to 7F, 99 and BA are stage objects (truck, cannon, catapult, spring, switch, save floor).

## Random numbers and sound

Static. `0x0200B8A4(n)` is the game's random routine, with 86 callers. It keeps a 12-bit seed at `0x0204219C`, steps it as `seed = (seed × 61 + 1401) mod 4096`, and returns `seed × n / 4096`. A flag at `0x020421A4` freezes the seed. There are 4,096 states and the sequence visits all of them before it repeats.

The sound archive `sdx_plus.sdat` keeps its symbol names: 566 sequences, of which 114 are music (`SEQ_…`) and 452 are effects (`SE_…`), 115 banks and 32 players.

## What the emulator costs on the host

Run, i7-14700KF, power profile `performance`, driven from Python, mean of `kssu_step`. "Crowd" is 31 proxies and a helper.

| Build | Idle | Crowd |
| :--- | ---: | ---: |
| Interpreter (what the bridge uses) | 2.41 ms | 3.71 ms |
| Recompiler, fast memory off | 1.31 ms | 1.71 ms |
| Recompiler, engine A scanlines not drawn | 0.93 ms | 1.24 ms |
| Recompiler, not drawn, ARM9 clock doubled | | 1.31 ms |
| Interpreter, ARM9 clock doubled | | 3.84 ms |

Inside one interpreted frame the host time is 56–69% ARM9, 11–16% ARM7, 10–14.5% drawing the 2D screen and 2.6–3.5% sound mixing.

A 600-frame trace of Kirby's position and pose is byte-identical between the interpreter and the recompiler, idle, with 8 proxies and with the crowd. `PERFORMANCE.md` records why the recompiler was left out of #123: "it changes the emulated timing that the recorded bridge evidence rests on". These traces show no difference; they cover three scenes, not the bridge suite.

## Fix candidates

Unless a row says otherwise, none is implemented. Each needs its own issue, the bridge suite, and a live check before any claim.

| Candidate | Evidence here | What it needs |
| :--- | :--- | :--- |
| Run the original on the recompiler | 46% less step time idle, 54% less in the crowd, identical traces | The call hook lives in the interpreter loop, so fall back to the interpreter while `kssu_hook_state` is armed; discard compiled blocks after the bridge patches code in RAM; pass explicit settings (a recompiler build defaults to fast memory, which installs a fault handler inside the host process); define `JIT_ENABLED` for the C++ shim as well, or the object layouts disagree and `kssu_open` crashes; the 84 bridge cases |
| Double the emulated ARM9 clock | Unfinished passes go from 2–5 in 360 frames to 0 with a crowd; identical traces where the original does not lag; 4 to 6% more step time | Timings that count frames across a slow pass (the limited-ability revert, loads) measured again; a decision recorded in `GAMEPLAY_FIDELITY.md`, since the original does lag here |
| Draw engine A only when it is read | 0.4 ms a step | The layer capture is used only by screen-wide attacks; skip scanline drawing and `kssu_read_layers` outside them |
| Read the move, not only the action | `R+0x808` names the move | Mouthful, wading and each copy attack can be told apart without inferring them from poses; `0E1B` and `1021` are ability change and death. The mouthful is done (#158: `kssu_move`, player field 4); wading and copy attacks are not |
| Cheaper per-step bookkeeping | See `PERFORMANCE.md` | Block reads for OAM and palette; no per-step allocations in `kssu_world`; fewer `kssu_state` calls |
| Real species in free slots | Enemy slots above | A host enemy mapped to a copy ability could be a real species from slots 0, 2, 3 or 4 instead of a patched Waddle Dee. Not tried |

## Not established

- Apart from player field 4 during a mouthful (#158), nothing in this document was checked in play.
- The enemy health and copy numbers are stored values read statically; only Waddle Dee's and Parasol Waddle Dee's were seen being applied.
- The second number in each enemy descriptor (20 for Waddle Dee, 120 for Sword Knight) is probably a score value; not confirmed.
- Mid-boss and boss health, the `shooting`, `poppyjr` and `lovely` records, the action ids listed above, the undecoded animation commands, and tile essence codes beyond the six already known.
- The recompiler and clock results were not run against `tools/nds_tests.py`, the renderer or background verifiers, or the game.
