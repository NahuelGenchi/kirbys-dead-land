# Host internals

What Dead Cells looks like from the inside where the mod meets it: how one host frame runs, the entity loop, level generation and loading, drawing, the two garbage collectors, DCCM's hook layer, and every kind of call the mod makes into the host. Issue #174, read on 2026-10-16 from Dead Cells v35 (`hlboot.dat` SHA-256 `59350cad475a2375b9e39d7cd13fdb6e4ed9a027fd827ae5140f913c1c07ce7e`) and DCCM 35.13.6.

Nothing here changes the mod. The lab runs behind the numbers are in [Performance, Kirby against the Beheaded](PERFORMANCE.md#kirby-against-the-beheaded-174), and the fixes they led to follow it there.

## How it was established

Three methods, named beside each result where it matters.

- **Bytecode**: the game's `hlboot.dat` was disassembled with crashlink into a private index of 37,448 functions, 4,855 classes and 503 native functions, with calls resolved through the class hierarchy.
- **IL**: DCCM's assemblies (ModCore, HashlinkSharp, HaxeProxy and the `GameProxy.dll` it generates) were read as IL. An offline benchmark ran DCCM's delegate plumbing without the game.
- **Run**: lab runs in isolated profiles with `KIRBY_LAB_PERF` ([Measuring](PERFORMANCE.md#measuring)).

The installed game, the Steam directory and the saves were only read. The disassembly, the index, the IL dumps and the notes stay in the private data root under `research/dc-re`; none of it is in this repository.

## The program

Dead Cells is Haxe compiled to HashLink bytecode, which HashLink's JIT compiles when the game starts. DCCM ships its own build of HashLink whose JIT leaves a 32-byte pad at the start of every function, where a detour can be written. When the code loads, DCCM generates `GameProxy.dll`: a .NET proxy class and a `Hook_` class for every Haxe class, engine included (`h2d`, `h3d`, the process library, level generation). No game logic runs in .NET. A proxy only reads and writes HashLink memory and calls HashLink functions.

## One host frame

`Boot.mainLoop` runs Heaps' application loop once a frame: timers, SDL events, `Boot.update`, then `h3d.Engine.render`, which calls `Boot.render` and draws the 2D scene. The game's own frame profiler marks the present. Only vsync limits the frame rate; the lab runs without it. [bytecode]

`Boot.update` runs delayed calls, tweens and the controller, then `Process.updateAll` with a time step of `dt × wanted fps × speed`, then the asset manager. `Process.updateAll` (the game's copy of Deepnight's `dn.Process`) runs `update` on every process tree, then `postUpdate` on every tree, then removes destroyed processes. [bytecode]

DCCM hooks `Boot.update` and raises the mods' frame event (`IOnFrameUpdate`) after the original returns: the frame's game state is final and nothing is drawn yet. The mod runs the original's step there. [IL]

Drawing walks the scene graph (`h2d.Scene.render`, `RenderContext.drawScene`, `Object.drawRec`). Each sprite on screen is one call of `HSprite.draw`: 79 a frame with Kirby moving at the start of the Prisoners' Quarters, about 200 with the Beheaded wading in the Greenhouse. The level's layers are lit (`LightedLayers`), which is why every atlas page comes with a normal-map page (`_n.png`). [bytecode, run]

## The entity loop

`Level.update` is the entity loop. Every frame it queries a quadtree for the visible part of the level (allocating one query rectangle), updates each entity's culling, calls `preUpdate` on every entity and resolves overlaps between them. Physics and AI, `fixedUpdate` (342 overrides), run at a fixed 30 Hz from an accumulator: at 120 fps, on one frame in four. They run only for entities whose `canUpdate` holds. `Entity.canUpdate` is true for an entity marked always-updated, or one that is in the game and not frozen; `Mob.canUpdate` adds any mob with a target. Critters, splatters and lore follow; some cooldown keys are built as strings every frame. [bytecode]

At the start of the Prisoners' Quarters the host calls `Entity.canUpdate` 370–590 times and `Mob.canUpdate` 140–330 times a frame (#123). [run]

`Level.postUpdate` calls every entity's `postUpdate` and then `spriteUpdate`, which moves the sprite to the entity. Every `update` finishes before any `postUpdate` starts, so anything the mod sets while `Level.update` runs and clears when it returns is gone by then. [bytecode]

## Level generation and loading

A level is built on the main thread when it is entered (`Game.loadMainLevel`). [bytecode]

1. `LevelGen.generate` seeds a random generator with the run's seed and asks the biome's structure class for a graph of rooms: main and secondary rooms, triggered, timed and challenge doors, elite rooms, teleports, lore rooms, then the difficulty.
2. It fits a map to the graph (a generic generator, and variants for the Cliff, Dooku's castle and the Tumulus). When that fails it logs an error and starts again with a new random seed.
3. `buildLevelMap` lays out the tiles with the biome's map builder (a generic one and ten variants). `Level.init` builds the lit display layers, the level map and its audio, then attaches mobs, loot, merchants, doors, NPCs and breakable props.
4. Between those steps it calls `Boot.tryRender` (from 17 places), which draws a frame when enough time has passed. That is what keeps the loading screen moving during a load that never leaves the main thread.
5. Once the new level is active, `LevelTransition.loadNewLevel` forces a full HashLink collection. It is the only collection the game forces.

How long a load takes was not measured.

## Two garbage collectors

**HashLink's** holds the game's objects. It collects when allocation asks for it, marking on up to 16 threads on this machine (`HL_GC_THREADS` defaults to the processor count, at most 16). In the lab runs the game allocated 5–17 KB a frame there and ran 0–7 full collections in a run. One of them, in each of four runs, made a frame longer than 20 ms; the others did not show. [run]

**.NET's** holds DCCM's objects and the mod's. In the game it is the workstation collector in its interactive (concurrent) mode, as `GCSettings` reports, although DCCM's shell configuration asks for the server collector. Which setting wins was not traced. [run, IL]

Two links between them. Before every HashLink full collection, DCCM takes its lock and runs a blocking .NET generation-1 collection, so that wrappers still alive in .NET keep their HashLink objects alive. And every call of a hooked function allocates a finalizable `HlCallback` (below). An object with a finalizer cannot die in its first collection, so it and everything it points at are promoted. [IL]

That is what the runs show. With the mod's hooks installed, nearly every .NET collection was a blocking generation-1 collection that promoted 5.5–6.3 MB and paused 6.4–7.4 ms (medians per run). With the mod loaded but idle (`KIRBY_LAB_BARE=1`) they promoted 1–2 MB and paused 1.8–3.5 ms. [run]

## DCCM's hook layer

Adding a handler to a host function (`Hook_X.f += ...`) gives that function a manager, once, for the whole process. The manager writes a detour into the function's pad that leads to a .NET callback. The callback has a direct route back to the original, with the raw arguments and no marshaling; the manager's constructor turns it on, and the first handler, added in the same call, turns it off, so DCCM itself never takes it. [IL]

From then on every call takes the managed path: the arguments are marshaled into an `object[]` and boxed, and the handler chain is rebuilt from scratch: a typed `orig` delegate, a bound delegate, a closure for the original, and a finalizable `HlCallback` for each handler. The outermost handler is then called through reflection. That is about 2 µs and 920 bytes for a call with one handler, whatever the handler does, and 1,840 bytes for a mob when both `Mob.canUpdate` and `Entity.canUpdate` are hooked. [IL, run (#123)]

Removing the last handler only takes it out of the list. The detour stays, so every later call still crosses into .NET for nothing: 0.77 µs and 56 bytes, against 0.38 µs and nothing for a function never hooked, for the rest of the session. [IL, run (#123)]

#178 first turned the direct route back on. The game then crashed, a segmentation fault after the held enemies were let go, in 3 of the 4 `crowd` runs that did it; why was not traced. Its second version undoes the detour instead (`NativeHooks.HookHandle.Disable`), which leaves the function as it was before it was hooked, and writes it again before the next handler (`Enable`). MonoMod frees the trampoline to the original when it undoes a detour and makes a new one when it applies it, so the manager's closure over the original, made once in its constructor, is made again from the new one. [IL, run]

A DCCM "processor" (HarmonyX) replaces the callback's target and also turns the direct route off. DCCM's own hooks are `Boot.mainLoop`, `Boot.update`, `Boot.render`, `HUD.postUpdate` and the Steam sync: 5 hooked calls a frame, 6.6 with the lab's own when the mod is idle. [IL, run]

## The mod's calls into the host

What each kind of call costs, from the IL, with the measured figures where there are any.

| Call | Cost |
| :--- | :--- |
| A hooked host function, one handler | about 2 µs and 920 B a call, plus whatever the handler allocates [run, IL] |
| The same function after its handlers are removed | 0.77 µs and 56 B a call while its detour stays [run] |
| A value field (`map.wid`, `hero.cx`) | a cached field offset and a pointer read, no allocation |
| A reference field | no allocation while the object's wrapper is alive |
| An instance method with plain arguments (`hasTag`, `hasCinematic`) | no allocation |
| A static Haxe function through a proxy's `Class` (`dc.hxd.Key.Class.isDown`) | a new delegate on every call; `object` arguments are boxed |
| `"x".AsHaxeString()` | a new string in the HashLink heap and two .NET objects |
| `haxeString.ToString()` | a new .NET string |

`KIRBY_LAB_PERF_HOOKS=1` counts the calls that take the managed path ([Measuring](PERFORMANCE.md#measuring)). Kirby moving at the start of the Prisoners' Quarters, 108.7 a frame:

| Host function | Calls a frame | Handler |
| :--- | ---: | :--- |
| `HSprite.draw` | 78.9 | hides the Beheaded's sprites drawn from the outfit atlas (#175 removes it) |
| `ControllerAccess.getMovementSticks` | 7.3 | keeps the host from walking the hero (#185 removes it) |
| `Entity.spriteUpdate` | 3.9 | hurtbox projection, never active there (#177 removes it) |
| `HealFlask.isEnabled` | 3.3 | no flask while Kirby plays (#186 removes it) |
| `ItemMetaManager.hasUnlockedItem` | 2.0 | the Collector's rule for ability items (#187 installs it only while the Collector checks an investment) |
| `HeroWeaponsManager.updateWeapon` | 2.0 | the ability's weapon slots |
| `Boot.mainLoop`, `Boot.update`, `Boot.render`, `HUD.postUpdate`, the Steam sync | 1 each | DCCM's own |
| `Main.update`, `Tutorial.tryToStart` | 1, 0.6 | the lab's probes (not in play), and the mod's tutorial rule |
| `Level.update`, `Hero.preUpdate`, `Hero.postUpdate`, `HeroHead.updateHeadFx` | 1 each | the mod's projection, input and hero synchronization, and the hidden spirit flame |
| `Hero.fixedUpdate`, `Hero.updateBodyHeight`, `HiddenTrigger.fixedUpdate` | 0.3 each | the same at 30 Hz |

With the 31 Zombies of the `crowd` plan held by the original: 482.4 a frame, of which `Mob.canUpdate` 266.7, `HSprite.draw` 135.0, `Entity.spriteUpdate` 32.5, `Entity.updatePositionXY` 12.1 and `Mob.fixedUpdate` 9.6; the held-entity hooks (#123) are on while anything is held. The Beheaded with the mod loaded: 103.8 moving and about 235 wading in the Greenhouse (`HSprite.draw` 201.5), because the `HSprite.draw` hook was installed whenever the original runtime was enabled, whatever the outfit.

## Found, ranked

The findings, ranked by what each costs a Kirby run, and the fixes they led to are in [Performance](PERFORMANCE.md#found-inside-dead-cells-ranked-174).

## Not established

- Which runtime configuration makes .NET's collector the workstation one.
- What the 90 ms draw in the Beheaded's movement runs draws. A shader compiled on first use is likely in software drawing; it was not traced.
- How long a level takes to generate and load.
- Whether DCCM's native hooks (`NativeHooks.CreateHook`) would be cheaper than its managed ones.
- Nothing here was checked in windowed play on the GPU.
