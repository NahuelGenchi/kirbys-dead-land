using System.Text.Json;
using dc.en;
using dc.en.mob.boss;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of Conjunctivius' tentacles (#86). A new run starts in the
/// real BeholderPit; once its Beholder exists, Kirby copies Sword and the boss's
/// own createTentacles makes a wave of three real tentacles with their natural
/// AI. Kirby walks to the nearest living tentacle and swings at it, also while
/// it is burrowed. Every host frame records each tentacle's ground state, whether
/// the host lets the hero hit it, and whether it is paired with an original
/// object. Incoming damage to Kirby is cancelled so the wave can be finished.
/// Passes when every tentacle dies to copy hits and none stays paired for more
/// than one host frame while the host refuses the hero's hits.
/// </summary>
internal static class LabTentacleProbe
{
    private const int Wave = 3;
    private static string? run;
    private static uint? start, fedAt, spawnedAt, finishedAt;
    private static bool fed, swallowSent;
    private static Beholder? boss;
    private static readonly List<BeholderTtcl> tentacles = [];
    private static readonly Dictionary<int, bool> wasInGround = [];
    private static readonly Dictionary<int, int> unhittablePairedRun = [];
    private static int surfaced, burrowed, pairedFrames, pairedUnhittableFrames, longestUnhittablePairing, hitsLanded, damage;
    private static int excludedIncomingHits;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        dc.Hook_User.newGame += (orig, user, seed, level, custom, scoring, gameMode) =>
            orig(user, seed, ((HaxeProxyBase)dc.Data.Class.level.resolve("BeholderPit".AsHaxeString(), false)).ToVirtual<LevelInfo>(), custom, scoring, gameMode);
        dc.en.Hook_Hero.applyAttackResult += (orig, attacked, attack) =>
        {
            // Keep Kirby alive through the boss and tentacle attacks; incoming
            // damage is verified by other probes.
            if (KirbyMod.Instance?.ActiveSession?.Hero.__uid == attacked.__uid && attack.finalDmg > 0)
            { excludedIncomingHits++; attack.inflictedDmg = 0; return; }
            orig(attacked, attack);
        };
        Hook_Mob.onDamage += (orig, mob, attack) =>
        {
            orig(mob, attack);
            if (mob is not BeholderTtcl || attack.source?.HashlinkPointer != KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer) return;
            hitsLanded++;
            damage += Math.Max(0, attack.inflictedDmg);
        };
        KirbyInput.TestOverride = Input;
    }

    private static void Event(string action, object value) => File.AppendAllText(
        Path.Combine(run!, "tentacle-events.jsonl"), JsonSerializer.Serialize(new { action, value }) + "\n");

    private static bool Alive(Mob mob) => !mob.destroyed && mob.life > 0;

    /// <summary>Each host frame once the wave exists.</summary>
    internal static void Poll()
    {
        if (run is null || spawnedAt is null || finishedAt is not null) return;
        var provider = KirbyMod.Instance?.NativeProvider;
        var hero = KirbyMod.Instance?.ActiveSession?.Hero;
        if (provider?.Machine is not { } machine || hero is not { destroyed: false }) return;
        var rows = new List<object>();
        foreach (var t in tentacles)
        {
            bool alive = Alive(t);
            bool inGround = alive && t._isInGround, hittable = alive && t.canBeHitBy(hero);
            bool paired = alive && provider.Enemies.Pairs(t);
            if (alive && wasInGround.TryGetValue(t.__uid, out bool before) && before != inGround)
            {
                if (inGround) burrowed++; else surfaced++;
                Event(inGround ? "burrowed" : "surfaced", new { frame = machine.State.Frame, t.__uid, paired, t.life });
            }
            wasInGround[t.__uid] = inGround;
            if (paired) pairedFrames++;
            int streak = paired && !hittable ? unhittablePairedRun.GetValueOrDefault(t.__uid) + 1 : 0;
            unhittablePairedRun[t.__uid] = streak;
            if (streak > 0) pairedUnhittableFrames++;
            longestUnhittablePairing = Math.Max(longestUnhittablePairing, streak);
            rows.Add(new { t.__uid, alive, life = alive ? t.life : 0, inGround, hittable, paired,
                controlled = alive && provider.ControlsEnemy(t), x = (t.cx + t.xr) * 24 });
        }
        File.AppendAllText(Path.Combine(run, "tentacle-trace.jsonl"), JsonSerializer.Serialize(new
        {
            frame = machine.State.Frame, ability = machine.State.Ability, heroX = (hero.cx + hero.xr) * 24, tentacles = rows,
        }) + "\n");
    }

    private static ushort? Input(KirbySession session)
    {
        var provider = KirbyMod.Instance?.NativeProvider;
        var machine = provider?.Machine;
        if (machine is null || run is null || finishedAt is not null) return 0;
        Hero hero = session.Hero;
        uint frame = machine.State.Frame;
        if (!hero.awake || hero._level.game.hasCinematic()) return 0;
        start ??= frame;
        if (boss is null)
        {
            var entities = hero._level.entities;
            for (int i = 0; i < entities.length; i++)
                if (entities.getDyn(i) is Beholder candidate && !candidate.destroyed) { boss = candidate; break; }
            if (boss is null)
            {
                if (frame - start.Value > 600) Finish(frame, "no Beholder in the level");
                return 0;
            }
            Event("found-beholder", new { frame, boss.__uid, boss.life, x = (boss.cx + boss.xr) * 24, y = (boss.cy + boss.yr) * 24 });
        }
        // Copy Sword: original inhale, supplied Sword content, then Down to swallow.
        if (machine.State.Ability != 18)
        {
            if (!fed)
            {
                fed = machine.Feed(18);
                if (fed) fedAt = frame;
                return 2;
            }
            if (frame - fedAt!.Value < 4) return 0;
            if (!swallowSent && frame - fedAt.Value < 10) return 128;
            swallowSent = true;
            if (frame - fedAt.Value > 400) Finish(frame, "Sword was not copied");
            return 0;
        }
        if (spawnedAt is null)
        {
            boss.createTentacles(Wave, false);
            var all = hero._level.entities;
            for (int i = 0; i < all.length; i++)
                if (all.getDyn(i) is BeholderTtcl t && Alive(t)) tentacles.Add(t);
            spawnedAt = frame;
            if (tentacles.Count == 0) { Finish(frame, "createTentacles made no tentacle"); return 0; }
            var first = tentacles[0];
            hero.setPosPixel((first.cx + first.xr) * 24 - 60, (first.cy + first.yr) * 24);
            if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
            Event("spawned-wave", new { frame, count = tentacles.Count,
                uids = tentacles.Select(t => t.__uid).ToArray(), x = (first.cx + first.xr) * 24, y = (first.cy + first.yr) * 24 });
            return 0;
        }
        uint since = frame - spawnedAt.Value;
        var target = tentacles.Where(Alive).OrderBy(t => Math.Abs((t.cx + t.xr - hero.cx - hero.xr) * 24)).FirstOrDefault();
        if (target is null || since >= 5400)
        {
            Finish(frame, target is null ? null : "time limit");
            return 0;
        }
        double dx = (target.cx + target.xr - hero.cx - hero.xr) * 24;
        ushort toward = dx >= 0 ? (ushort)16 : (ushort)32;
        // Approach to sword range, then swing every 16 frames, facing the target.
        if (Math.Abs(dx) > 30) return toward;
        return (since % 16) switch { 0 => toward, < 5 => 2, _ => 0 };
    }

    private static void Finish(uint frame, string? failure)
    {
        finishedAt = frame;
        int killed = tentacles.Count(t => !Alive(t));
        File.WriteAllText(Path.Combine(run!, "tentacle.json"), JsonSerializer.Serialize(new
        {
            failure, frames = spawnedAt is uint s ? frame - s : 0, spawned = tentacles.Count, killed,
            surfaced, burrowed, hitsLanded, damage, pairedFrames, pairedUnhittableFrames, longestUnhittablePairing,
            excludedIncomingHits, bossLife = boss is { destroyed: false } ? boss.life : 0,
            passed = failure is null && tentacles.Count == Wave && killed == Wave && burrowed > 0 && longestUnhittablePairing <= 1,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
