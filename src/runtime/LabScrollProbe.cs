using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.en.mob;
using dc.tool;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of a Scroll of Power claimed beside paired enemies (#80).
/// On a clear floor of a new run it creates four stationary Zombies ahead of
/// Kirby and a real Survival scroll just in front of him, through their own
/// constructors. Kirby walks toward them; once the Zombies are paired with
/// source objects, the scroll is claimed as the player's short press would
/// (ItemDrop.onActivate). Every host frame from the claim on records the host's
/// pause, awake and cinematic state, whether the original machine advanced, and
/// the bridge's proxy flags. The run must reach the end without a fatal error,
/// with the Survival tier raised and the Zombies paired again afterwards.
/// With KIRBY_LAB_SCROLL_INHALE=1 Kirby stands and holds the original inhale
/// instead, and the scroll is claimed once a Zombie is in the suction, so the
/// swallow completes during the host's claim cinematic. The host Zombies it
/// swallowed must then die and Kirby must keep the original mouthful.
/// </summary>
internal static class LabScrollProbe
{
    private static string? run;
    private static uint? start, placedAt, spawnedAt, claimedAt, finishedAt;
    private static uint lastFrame;
    private static int direction = 1;
    private static int tierBefore, pairedAtClaim, pairedAfter, lockFrames, lockSteps;
    private static object? clearSpot;
    private static ItemDrop? scroll;
    private static readonly List<Mob> zombies = [];
    private static readonly bool inhale = Environment.GetEnvironmentVariable("KIRBY_LAB_SCROLL_INHALE") == "1";
    private static bool suctionAtClaim, endedDuringLock;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyInput.TestOverride = Input;
    }

    private static int Paired(KirbyNdsProvider provider) => zombies.Count(z => !z.destroyed && provider.Enemies.Pairs(z));

    /// <summary>Each host frame after the claim, including paused, locked and cinematic frames.</summary>
    internal static void Poll()
    {
        if (run is null || claimedAt is null || finishedAt is not null) return;
        var provider = KirbyMod.Instance?.NativeProvider;
        var nds = provider?.Machine;
        var hero = KirbyMod.Instance?.ActiveSession?.Hero;
        if (provider is null || nds is null || hero is not { destroyed: false } || hero._level is null) return;
        uint frame = nds.State.Frame;
        bool paused = hero._level.game.paused || hero._level.paused, cinematic = hero._level.game.hasCinematic();
        bool locked = !hero.awake || cinematic;
        var flags = Enumerable.Range(1, 31).Select(i => (Slot: i, nds.Proxy((uint)i).Flags)).Where(p => p.Flags != 0)
            .Select(p => new[] { p.Slot, (int)p.Flags }).ToArray();
        if (locked)
        {
            lockFrames++;
            if (frame != lastFrame) lockSteps++;
            // Swallowed (4) or Gone (8): a source object ended while no normal step ran.
            if (flags.Any(p => (p[1] & 12) != 0)) endedDuringLock = true;
        }
        File.AppendAllText(Path.Combine(run, "scroll-trace.jsonl"), JsonSerializer.Serialize(new
        {
            frame, advanced = frame != lastFrame, paused, hero.awake, cinematic, manualLock = hero.controller.manualLock,
            controllerLocked = hero.controller.parent.isLocked, available = KirbyInput.Available(hero), x = (hero.cx + hero.xr) * 24, hero.survivalTier,
            paired = Paired(provider), flags,
        }) + "\n");
        lastFrame = frame;
    }

    private static ushort? Input(KirbySession session)
    {
        var provider = KirbyMod.Instance?.NativeProvider;
        var machine = provider?.Machine;
        if (provider is null || machine is null || run is null || finishedAt is not null) return 0;
        Hero hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (frame - start.Value < 180) return 0;
        if (placedAt is null)
        {
            clearSpot = LabAbilityItemProbe.MoveToClearFloor(hero);
            placedAt = frame;
            return 0;
        }
        if (spawnedAt is null)
        {
            if (frame - placedAt.Value < 60) return 0;
            direction = hero.dir >= 0 ? 1 : -1;
            double y = (hero.cy + hero.yr) * 24;
            for (int i = 0; i < 4; i++)
            {
                var mob = Zombie.Class.create(hero._level, hero.cx + (3 + i) * direction, hero.cy, 1, 1);
                mob.setPosPixel((hero.cx + (3 + i) * direction) * 24 + 12, y);
                if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
                // Stationary, durable fixtures: the check is the pairing, not the fight.
                mob.life = mob.maxLife = 1000;
                mob.lockAiS(60);
                zombies.Add(mob);
            }
            scroll = new ItemDrop(hero._level, hero.cx + 2 * direction, hero.cy,
                InventItem.Class.fromItem("SurvivalUp".AsHaxeString(), null), true, Ref<bool>.In(false));
            scroll.init();
            tierBefore = hero.survivalTier;
            spawnedAt = frame;
            return 0;
        }
        uint t = frame - spawnedAt.Value;
        ushort walk = direction > 0 ? (ushort)16 : (ushort)32;
        if (claimedAt is null)
        {
            // Bit 32: the original suction holds that source object.
            bool suction = Enumerable.Range(1, 31).Any(i => (machine.Proxy((uint)i).Flags & 32) != 0);
            // Claim while Kirby is still walking (or inhaling) and the Zombies are paired.
            if (t >= 40 && (inhale ? suction : Paired(provider) > 0) || t > 240)
            {
                suctionAtClaim = suction;
                pairedAtClaim = Paired(provider);
                lastFrame = frame;
                claimedAt = frame;
                scroll!.onActivate(hero, false);
            }
            if (inhale) return t >= 30 ? (ushort)2 : (ushort)0;
            return t >= 20 ? walk : (ushort)0;
        }
        uint since = frame - claimedAt.Value;
        if (since >= 300)
        {
            finishedAt = frame;
            pairedAfter = Paired(provider);
            int swallowed = zombies.Count(z => z.destroyed || z.life <= 0);
            bool mouthful = (machine.ActionFlags & 2) != 0;
            File.WriteAllText(Path.Combine(run, "scroll.json"), JsonSerializer.Serialize(new
            {
                clearSpot, direction, inhale, suctionAtClaim, endedDuringLock, tierBefore, tierAfter = hero.survivalTier, scrollTaken = scroll!.destroyed,
                pairedAtClaim, pairedAfter, swallowed, mouthful, lockFrames, lockSteps,
                passed = scroll.destroyed && hero.survivalTier > tierBefore && pairedAtClaim > 0 && pairedAfter > 0 &&
                    (!inhale || suctionAtClaim && endedDuringLock && swallowed > 0 && mouthful),
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        // After an inhale claim no button is pressed, so the mouthful is kept, not spat.
        return since < 60 && !inhale ? walk : (ushort)0;
    }
}
