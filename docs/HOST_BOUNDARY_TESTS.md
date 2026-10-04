# Live boss and crowd checks

`LabBossCrowdProbe` is an opt-in acceptance fixture in the isolated launcher. It does not run during normal play. Each report includes source frames, actual host entity IDs, the adapter's UID-to-slot bindings, source capture flags, and host life. The fixture uses original ROM inputs and actual Dead Cells entities.

## Concierge

Private run `20261004-022453` started a normal new run in the game's real `Bridge` boss arena. The arena created and activated its own Concierge (`Behemoth`). Its AI remained active: it moved and selected six original host animation groups, including its attack and attack preparation.

Kirby performed 96 observed original inhale frames. The boss was never source-controlled or captured. The fixture then provided Sword copy content through the source ingestion interface and pressed the original swallow and attack inputs. Boss life fell from 26,378 to 26,346 while the boss stayed alive and ready. The adapter held exactly one binding for that boss. The visible framebuffer showed the original Sword Kirby and host boss in the arena.

The fixture teleports Kirby into attack range and rejects incoming host damage to keep this finite outgoing-damage/capture check alive; it excluded four hits. Therefore this check does **not** establish player survival, incoming boss damage, complete boss phases, or resistance to every capture move. Those are distinct from the tested real boss AI, inhale resistance, binding, and original Sword damage path. Native tests separately cover the source resistance mask for inhale, Suplex, and Cook.

The run completed its 55-second limit without a fatal log, and the isolated launcher's real-save comparison passed.

```sh
python3 scripts/launch_runtime.py --seconds 55 --start-game \
  --outfit KirbySuperStarUltra --input-plan boss
```

## Thirty-one host enemies

Private run `20261004-022833` spawned 31 actual host Zombies on the starting platform. All 31 had simultaneous active source proxy objects and distinct matching host UID bindings; all stayed alive. Kirby then moved into range and used original Sword. Host damage callbacks recorded 21 positive hits on 21 different targets, each for 18 damage, totaling 378 damage. No incoming damage was excluded in this crowd fixture.

The enemies' AI is temporarily locked and each has 1,000 HP, allowing routing and slot capacity to be measured without simultaneous host attacks or deaths obscuring the count. This establishes capacity and real original-to-host damage delivery, not behavior of 31 independently attacking enemies or support beyond the 31-slot limit. An earlier wide-row setup crossed generated room geometry and is not counted as the successful capacity check. The successful run completed its 45-second limit without a fatal log, with real saves unchanged.

```sh
python3 scripts/launch_runtime.py --seconds 45 --start-game \
  --outfit KirbySuperStarUltra --input-plan crowd
```
