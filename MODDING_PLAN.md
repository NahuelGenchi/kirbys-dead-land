# Modding plan and acceptance gates

Host: native Linux Steam Dead Cells v35, build 23762174, app 588650. HashLink
gameplay bytecode; no anti-cheat detected. Existing Sonic asset modification must
remain available. User source: KSSU USA YKWE rev0 ROM, SHA256 in `docs/GAMEPLAY_FIDELITY.md`.

Chosen route: local ROM conversion plus a DCCM C# gameplay plugin. Asset-only
modding cannot provide inhaling, random copy abilities or original physics.
DCCM v35.13.6 and .NET SDK 10.0.401 are pinned and available privately. The actual
original-gameplay provider remains the blocking implementation work.

1. Decode the source assets and preserve original origins/facing. **Done for
   supported palette parts; animation timing and shared palettes remain partial.**
2. Verify pack formats and compile real host hooks. **Done, offline.**
3. Recover an original-game oracle and establish the controller/collision/combat
   bridge. **In progress; no original controller integration exists.**
4. Prove one vertical slice in the real host: Kirby idle, walk, jump and inhale
   one enemy, random copy, original ability attack, retained shield parry.
   **Not reached.**
5. Port and compare every original state and ability, then validate death, room
   transitions, save/reload and input variants. **Not reached.**
6. Package a new, selectable mod while preserving other outfits. Record live
   evidence and retain a reversible installation. **Not reached.**

Before any Dead Cells launch, use Universal Modder to snapshot actual host saves
and configuration, prove that the lab cannot write to Steam/cloud progression,
and get any required permission for live-game loader installation or desktop
input control. No current work needs either action: all builds and asset tests
run outside the installed game. No earlier mod's backup is restored.

Completion means observed original Kirby gameplay in Dead Cells with only the
requested changes, not successful compilation, sprite extraction or a costume.
