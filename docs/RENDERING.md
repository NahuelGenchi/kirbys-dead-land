# Original ROM rendering and separate outfit

`KirbySpriteRenderer` draws the running original machine's OBJ output. It reads ARM9 OAM, OBJ VRAM, palette RAM and DISPCNT after each source step. The source game selects poses, cap layers, projectiles, flips and frame timing; the host does not choose a replacement animation. `NdsObjRenderer` is the pure decoder used both in game and in the source framebuffer tests.

The OBJ layer uses palette banks 0–7. Source enemies observed in the reference room use bank 8; Mike's flashed target uses bank 15. The decoder preserves numerical OBJ priority and OAM order, 4-bit tile layout, horizontal/vertical flips and affine matrices. Pixels are uploaded to a nearest-filtered host texture at original scale. The source player center is positioned 14.5 pixels above the host foot point. Both games use 24-pixel terrain cells.

The original seed room's exit-door decoration also uses bank 7. Its yellow stars were exposed by the source camera when host rebasing placed Kirby near source X394. They are fixed around the original doorway at `(396,132)`, not attached to Kirby: changing source Y moved Kirby above or below the same stars. An unmodified source scene at that X showed the original sparkling doorway. The original loader registers these decorations in a separate 16-entry table at `0x0209FBEC`; its draw loop skips entries whose type is zero. `World` now clears only those decoration type fields when supplying host terrain in the verified source room. Kirby/helper/effect objects and their palettes are untouched.

The production repair was independently checked with original Sword at the rebased position. All 16 door types were zero, the unwanted doorway stars disappeared, and 60 subsequent source frames matched the C# OBJ renderer across 46,624 pixels with zero mismatches or unsupported objects. Twelve genuine bank-7 Sword effect occurrences remained, alongside the body and cap. This is a scoped removal of source-room scenery, not a generic filter for star graphics.

Actual host run `20261004-025047`, frame `004656`, was visually inspected after the repair: base Kirby stands aligned with the Dead Cells floor, with no doorway stars, host head/scarf or equipment ghost. The run reported no fatal error and unchanged real saves. Its native library SHA256 was `f94997d82877bcdce29ed8d8cb3741545f08044ad813fa61f59f9406c6314b2c`.

The native hero sprite's own draw is suppressed only while the Kirby renderer is active. Its attached host objects remain available. A separate `kirbySuperStarUltraWeapons` atlas supplies equipment that the host normally bakes into its body frames. The equipment layer follows the host animation/frame, facing, color shader and alpha. It is visible in base form and during host block/parry animations in copy forms. The gameplay adapter separately controls which weapons can actually be used. Both independent layers use the hero's actual `Layers` layer ID, found from exclusive layer end indices; a child index cannot be passed to `Layers.addChildAt`. GPU reads and pixel uploads happen once per original source frame while host positions continue updating at the host render rate.

The user's play recording exposed equipment problems that source-pixel comparison cannot detect. The equipment sprite now copies the host's `None` blend mode instead of using `HSprite`'s default `Alpha`, so it participates in the correct actor lighting pass. It owns a `NormalMap` shader pointing to its own packed atlas page, while sharing the host's live palette/glow/status shaders. The full ordered shader identity sequence is compared before reusing the list: host shader insertion/removal can mutate a link without changing the head pointer. A real wooden-shield pickup and right-click guard visibly rendered the shield in live run `20261004-033400`, frame `003363`.

The equipment atlas is reacquired through the host asset manager while used, and its sprite is recreated if the manager replaces that library. A biome transition resets the manager's `used` flags and explicitly destroys unused libraries; `preventAutoDispose` only prevents GPU eviction and does not protect against that destruction. Holding the old library without reacquiring it caused a reproducible missing-backing-texture error during the transition.

`tools/build_outfit.py` builds `KirbySuperStarUltra` as a new item/skin pair under `data.cdb_`, using DCCM's differential CDB merge. The model has no native head or scarf. Its fallback atlas covers every original host animation name using the authentic ROM's neutral bkir pose 184, permitting normal outfit initialization and previews. Those fallback frames do not supply gameplay animation or source timing.

The builder reads the current game PAK without changing it. Existing outfit rows are not replaced. It preserves all existing `heroSkins` entries and all icon pixels outside one previously unused 24-pixel cell. A source PAK that already contains Kirby is rejected. Equipment pixels retain their original host palette/normal data after removing native body color indices 478–499. The original palette is preserved and Kirby's colors occupy unused slots.

Equipment is retargeted to `(8, -11)` relative to Kirby's foot. `Prop2` is the off hand in the starting sword's `atkBackStabberB` and `atkSaberA` animations, with no corresponding weapon-hand track available. Using it universally placed the blade far away from Kirby. Main-weapon placement now uses the original opaque body/equipment contact centroid when present. Bows and block/parry retain their verified `Prop2` tracks: tall bows also touch the lower body, making a contact centroid inappropriate. Missing-contact frames retain the existing fallback. This host equipment attachment remains a visual approximation. The correction changes only atlas origins; all 10,234 source pixel rectangles, textures, normals and palettes remain identical. All 860 bow-named and 204 shield atlas rows also retain their previous origins.

Build privately:

```sh
python3 tools/build_outfit.py
```

The default output is `~/.local/share/kirby-dead-cells/outfit-build/res.pak`. Stage it as the runtime's `outfit.pak`; load it before applying the altered CDB. No extracted pixels or game data belong in this repository. Rebuilds use a sibling staging directory and only replace an unchanged output owned by this builder. Foreign files and directories cause refusal.

Verify the exact runtime decoder against source scanout:

```sh
python3 tools/verify_renderer.py
```

The verifier drives the original inhale and DOWN swallow routines to prepare all 24 copy abilities, then runs 120 source frames holding the original attack button for each. It also checks base idle and left-facing attack sequences for Sword, Cutter, Beam and Wing. It compiles `tests/render`, which links the same decoder used in the host. Source framebuffer scanout trails OAM commits by one frame, so the comparison uses the following source framebuffer. It compares opaque decoded pixels after reducing both images to the original five-bit color channels; the source framebuffer uses `5-bit << 3`, while host RGBA expands each channel to eight bits. Private reports, source states and raw verification images are saved under `~/.local/share/kirby-dead-cells/render-tests`.

The complete standing-attack matrix, idle and four left-facing cases passed: 29 sequences, 3,068,161 compared pixels, zero decoded-pixel mismatches and zero unsupported objects. The sequences included 9,441 flipped OBJ occurrences and no affine objects. A separate original Sword helper creation/idle sequence also passed 135,609 pixels: the helper uses palette bank 2, and its CP indicator is preserved. This establishes the tested pixel decoding, not exhaustive rendering ownership or move coverage. In particular:

- Palette filtering is inferred from the observed source allocation, rather than a proven per-object ownership link. Enemy hit graphics sharing a player palette can still require separate identification.
- The comparator checks pixels included by the decoder; it does not prove no relevant source object was excluded.
- OBJ windows, 8-bit OBJ textures and bitmap OBJ formats are not rendered by this layer. A separate validated background-effect path handles the tested uniform Crash/Paint/Mike effects below.
- Affine decoding is implemented, but an observed affine source sequence is needed to establish that path against the original scanout.
- Semitransparent OBJ modes are exposed in `UnsupportedObjects` and currently drawn opaque. The verified standing-attack cases did not use those OBJ modes. Scanline hardware blends and nonuniform background transforms are captured but explicitly unsupported by the current background decoder.
- Projectiles outside the source viewport are culled by the source game. Host viewport differences do not expand that source visibility envelope.

These limitations must not be described as complete visual fidelity. Host load, input, terrain, combat and copy-transition verification are separate from these renderer tests.

An original Wheelie mounted state was also checked for 120 source frames: 98,648 included pixels, zero mismatches and zero unsupported objects. The combined riding animation uses palette banks 0, 2 and 3, all preserved. The main Kirby object's hidden pose value `65535` does not suppress live OAM rendering.

An original frozen-enemy Ice cube fixture was checked for 120 source frames: 153,612 included pixels, zero mismatches and zero unsupported objects. The generic cyan ice cube uses palette bank 6 (two OBJ parts), which the existing renderer includes. The proxy enemy's ordinary bank-8 body disappears before that cube phase; including all enemy banks would wrongly display the proxy body in the host. Preserving the original frozen object's lifetime after the host enemy dies is a separate native/host lifecycle responsibility.

`tools/nds_graphics_patch.py` adds a checked, read-only capture to the pinned software renderer. It records engine A's actual top and underlying RGB6/layer pixels, window mask, blend registers, final composite, and standard palette at each source scanline. The resulting original pixel planes are exposed by `kssu_read_layers`; they do not change source rendering or memory. This avoids inferring effects from end-of-frame register snapshots. `BLDY`, for example, is write-only through the normal bus interface.

The captured Crash growing circle is an ordinary OBJ effect already handled above. The later flash changes the extended background palettes: the decoder accepts it only when every visible captured background pixel agrees with the changed backdrop color. Paint and Mike instead produce uniform integer RGB5 palette transforms. The decoder checks all 256 entries against the seed palette before accepting the scale and RGB offsets; it never schedules a flash from an ability ID or substitutes an invented shape.

```sh
python3 tools/verify_background.py
```

The native-linked test runs 300 original frames each of Crash, Paint, Mike and Cook. Across those 1,200 frames, the captured composite matched the independent original framebuffer in all 176,947,200 RGB channel comparisons, including exact emulator RGB6-to-eight-bit expansion. No frame was unsupported. Crash supplied 61 all-white background frames. Paint supplied 231 affected frames with scale `12/16` and offsets `(0,0,0)`, `(7,0,0)`, `(7,7,0)` or `(0,0,7)`; Mike supplied 20 with scale `10/16` and offset `(11,5,0)`. Cook supplied no background transform. Those counts describe the tested original attack sequences, not hard-coded host timings.

The same 300-frame standing-attack test was also run for all 24 abilities: 7,200 source frames, 1,061,683,200 RGB channel comparisons, zero differences and zero unsupported frames. Only the three abilities above changed the captured background during those specific sequences. Pass `--all-abilities` to repeat that broader matrix; this still does not cover every directional, charged, aerial or helper move.

The inferred palette law was independently checked on actual extended-palette background pixels: 47,747 Paint darkening pixels and 45,228 Paint red-tint pixels matched exactly. Mike's source camera shakes; after aligning a captured frame by its measured `(2, -1)` pixel displacement, all 40,848 compared background pixels matched its inferred law. The host camera currently retains its own camera behavior rather than reproducing those source screen shakes.

`NdsBackgroundEffects` uses these source coefficients and timing for a viewport-sized layer at the host's `DP_ROOM_MAIN_BACK`, behind actors and HUD. The host scenery uses continuous alpha blending rather than the DS palette's integer quantization, and foreground scenery retains the host's existing draw order. This is an adaptation of the verified original background transform to the host scene, not pixel-identical reproduction of the DS room. Other spatial background effects, hardware blending and unusual palette changes remain unsupported and are counted rather than replaced with a guessed effect.

Live visual run `20261004-013424` passed for 65 seconds with no fatal errors and unchanged original save files. The opt-in `visual` lab plan obtains Crash, Paint and Mike via real inhale/feed/DOWN operations and captures active effects every 100 ms. Inspected host frames `011031`, `011454` and `011802` respectively show the white Crash background, Paint yellow tint and Mike warm tint, with the original OBJ effects visible and Kirby/HUD outside the background layer. The earlier transition run `20261004-013050` passed the actual exit to Prisoners' Courtyard after equipment atlas reacquisition was fixed; its later forced-death diagnostic null reference was a separate lab issue, subsequently guarded.
