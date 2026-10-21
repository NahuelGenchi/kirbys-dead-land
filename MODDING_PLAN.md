# Mod implementation and acceptance

Host: Steam Linux Dead Cells v35 build23762174, DCCM35.13.6 and .NET10.0.401.
Source: user-owned KSSU USA YKWE revision0. A separate selectable outfit and private
runtime lab preserve the installed game and existing Sonic/Knuckles resources.

The chosen route is now implemented: an embedded original-ROM controller,
translated host terrain and enemy collisions, live original sprites/effects/audio,
and DCCM equipment/damage/save integration.

| Milestone | Evidence |
|---|---|
| ROM extraction and host pack | Decoded archives, exhaustive pixel checks, independent PAK checks |
| Original controller and terrain | Pinned native library and recorded source regression suite |
| Real host vertical slice | Walk/jump, actual enemy inhale, random copy, original attack, retained shield counter |
| Persistence | Fresh process resumes exact original frame/copy/position/shield |
| Original graphics/audio | All standard form OBJ tests, helper/riding tests, source background comparisons, sound queue tests |
| Host lifecycle | Outfit/stat health, actual biome transition, lethal source damage and normal New Game reset passed |
| Host boundaries | Original Ice block (replaced by the host freeze status in #42), generated door, active Concierge resistance/damage and 31-enemy capacity passed |
| Final input | All nine real binding masks, inverted directions and physical-reader movement/jump passed; base-form left-click on the starting sword runs the original inhale with no host attack (#39) |
| Local delivery | Final build staged; fresh-profile launch passed,20s real gameplay captured, test saves archived and play profile empty |

Each launch backs up original saves (a verified archive in the data root), mounts installed
files read-only and redirects file/Cloud saves to the private profile. Test input
is internal to the isolated process. Normal play uses `./play-kirby`; no loader is
installed into Steam's game directory. Nothing is published automatically.

Completion requires the actual playable result and measured boundary behavior.
All-ability sprite comparisons alone do not establish gameplay completeness.
