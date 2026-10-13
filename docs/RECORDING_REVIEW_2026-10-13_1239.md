# Recording review 2026-10-13 12:39: Kirby flickers beside a helper

User recording: `Screencast From 2026-10-13 12-39-33.mp4`, 12.9 seconds, 305
frames, about 24 frames per second. The owner reported that Kirby flickers a
lot. The play log of that session (`20261013-123448`) shows steady pacing: host
and original at 60 fps, locked 98–100%, about 3.1 ms an original step. Speed
was not the cause.

## Kirby is drawn a terrain cell away for single frames

Kirby runs and floats with a helper beside him. In six frames of the recording
(5.71, 8.18, 8.69, 8.93, 8.99 and 10.11 s) he is drawn about 70 screen pixels
from his path, sideways or upwards, and is back on it in the next frame. That
distance is one terrain cell, 24 original pixels. The helper jumps with him.

**Cause.** The original waits for VBlank and then runs one logic pass: it
moves Kirby and the camera, then builds its sprites for the next VBlank. An
emulator frame ended 71 scanlines after VBlank. Kirby alone finishes his pass
in that time. With a helper the pass is often longer, so the frame ended with
Kirby and the camera already moved and the sprites not yet built. The mod then
moved its terrain window by a cell under that unfinished pass, and the pass
built Kirby's sprites a cell away. The fifth recording's fault looked the same
but came from the pairing of the sprites with a screen position; here the
sprites themselves were wrong.

A headless replay of the mod's placement reproduces it only with a helper:
over 2,082 frames, 16 to 39 frames ended inside a pass and Kirby left his place
for one frame up to 5 times. A live run with a helper on the old frame boundary
shows him a cell off in 10 of 1,809 captures, with 118 frames ended inside a
pass in a minute.

**Fix.** A source frame now ends as VBlank begins, between two passes, by a
checked addition to the pinned emulator's frame loop
(`tools/nds_step_patch.py`). The emulated machine runs exactly as before; only
the point where the mod looks at it and moves it changes. Two cases that the
new boundary does not cover by itself are closed too. While the original takes
longer than a frame over a load (an ability's or a helper's graphics, two or
three frames), the window and Kirby wait. And the window now takes the
original's drawing copy of the camera along, so Kirby's reported screen position
stays where he is drawn when a pass leaves the scene as it is. The bridge
counts every frame that still ends inside a pass and the `Kirby pacing` log
line reports it each minute.

With the fix the same replay has no frame inside a pass and no jump of even
3 px, and the live run with a helper (`20261013-135009`, about 65 s of runs
and flaps) has Kirby a cell off in none of 1,831 captures; its log counts 5
frames inside a pass in the first minute, the Sword and helper loads. The
record of the change, its evidence and what was not run is in
[the bridge document](NDS_BRIDGE.md#where-a-source-frame-ends-134).

## Scope

The live runs used the isolated lab profile and a private build of the bridge;
the shared bridge and the owner's play runtime were not rebuilt. After the
merge, `python3 tools/nds_build.py` and a new staging are needed before play.
Not checked: the owner's play launcher, a controller, scenes with many host
enemies, an elevator during a load.
