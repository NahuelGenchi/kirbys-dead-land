using System.Text.Json;
using System.Reflection;
using dc.en;
using dc.en.mob;
using dc.en.mob.boss;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace Kirby.Runtime;

/// <summary>Opt-in private fixtures for a real boss and a full host-enemy neighborhood.</summary>
internal static class LabBossCrowdProbe
{
    private static string? run, mode;
    private static uint? start, ready, fedAt;
    private static uint lastPoll = uint.MaxValue;
    private static bool fed, spawned, damageHookInstalled;
    private static readonly List<Mob> targets = [];
    private static readonly Dictionary<int, int> initialHp = [];
    private static readonly HashSet<string> bossAnimations = [];
    private static Behemoth? boss;
    private static int maximumProxies, maximumBoundFixtureTargets, swordFormFrames, inhaleFrames, minimumBossHp = int.MaxValue;
    private static int excludedIncomingHits;
    private static int outgoingHitCount, outgoingDamage;
    private static readonly HashSet<int> damagedTargets = [];
    private static bool captureObserved, sourceControlledBoss, bossMoved;
    private static double bossStartX;
    private static int setupAttempts;

    internal static void Install(string output, string plan)
    {
        if (plan is not ("boss" or "crowd")) throw new ArgumentException("Unknown boss/crowd fixture");
        run = output; mode = plan;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        Hook_Mob.onDamage += (orig, mob, attack) =>
        {
            orig(mob, attack);
            if (!targets.Any(target => target.__uid == mob.__uid) ||
                attack.source?.HashlinkPointer != KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer) return;
            outgoingHitCount++;
            outgoingDamage += Math.Max(0, attack.inflictedDmg);
            if (attack.inflictedDmg > 0) damagedTargets.Add(mob.__uid);
            Event("original-attack-hit-host", new
            {
                frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame,
                uid = mob.__uid, attack.rawFinalDmg, attack.finalDmg, attack.inflictedDmg, remainingLife = mob.life,
            });
        };
        if (plan == "boss")
        {
            dc.Hook_User.newGame += (orig, user, seed, level, custom, scoring, gameMode) =>
                orig(user, seed, ((HaxeProxyBase)dc.Data.Class.level.resolve("Bridge".AsHaxeString(), false)).ToVirtual<LevelInfo>(), custom, scoring, gameMode);
        }
        KirbyInput.TestOverride = Input;
    }

    private static void Event(string action, object value) => File.AppendAllText(
        Path.Combine(run!, "boss-crowd-events.jsonl"), JsonSerializer.Serialize(new { action, value }) + "\n");

    private static void Position(Hero hero, double x, double footY)
    {
        hero.setPosPixel(x, footY);
        if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null || run is null) return 0;
        Hero hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (!hero.awake || hero._level.game.hasCinematic()) return 0;
        if (mode == "boss") return BossInput(session, machine, frame);
        uint elapsed = frame - start.Value;
        if (elapsed >= 60 && !fed)
        {
            fed = machine.Feed(18);
            if (fed) Event("sword-content-fed", new { frame });
        }
        if (elapsed >= 200 && !spawned && machine.State.Ability == 18)
        {
            spawned = true; ready = frame;
            double x = (hero.cx + hero.xr) * 24, y = (hero.cy + hero.yr) * 24;
            for (int i = 0; i < 31; i++)
            {
                var mob = Zombie.Class.create(hero._level, hero.cx + 3, hero.cy, 1, 1);
                // Keep every durability target on the verified start platform;
                // a wide row can cross room walls or pits in generated levels.
                mob.setPosPixel(x + 55 + (i % 8), y);
                if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
                // A stationary durability fixture tests routing/capacity without
                // conflating 31 simultaneously running host attack behaviors.
                mob.life = mob.maxLife = 1000;
                mob.lockAiS(60);
                targets.Add(mob); initialHp[mob.__uid] = mob.life;
            }
            Event("spawned-real-host-crowd", new { count = targets.Count, frame });
        }
        if (ready is uint at)
            return (frame - at) switch
            {
                >= 120 and < 136 => 16,
                >= 136 and < 300 => 2,
                _ => 0,
            };
        return elapsed switch { >= 30 and < 80 => 2, >= 80 and < 84 => 128, _ => 0 };
    }

    private static ushort BossInput(KirbySession session, NdsRuntime machine, uint frame)
    {
        if (!damageHookInstalled)
        {
            damageHookInstalled = true;
            // Install after normal mod initialization, around its damage hook.
            dc.en.Hook_Hero.applyAttackResult += (orig, attacked, attack) =>
            {
                // Keep the routing/capture fixture alive through real boss AI.
                // Incoming damage and survival are verified by separate probes.
                if (KirbyMod.Instance?.ActiveSession?.Hero.__uid == attacked.__uid && attack.finalDmg > 0)
                { excludedIncomingHits++; attack.inflictedDmg = 0; return; }
                orig(attacked, attack);
            };
        }
        var hero = session.Hero;
        if (boss is null)
        {
            var entities = hero._level.entities;
            for (int i = 0; i < entities.length; i++)
                if (entities.getDyn(i) is Behemoth candidate && !candidate.destroyed)
                {
                    boss = candidate; targets.Add(candidate); initialHp[candidate.__uid] = candidate.life;
                    bossStartX = (candidate.cx + candidate.xr) * 24;
                    Event("found-arena-boss", new { candidate.__uid, candidate.life, candidate.maxLife,
                        ready = candidate.isReady(), x = bossStartX });
                    break;
                }
            if (boss is null) { setupAttempts++; return 16; }
        }
        if (!ready.HasValue && !boss.isReady())
        {
            // Walk into the real arena so its original entry/cinematic sets the
            // boss ready. Its AI is never locked or replaced by this fixture.
            setupAttempts++;
            return 16;
        }
        ready ??= frame;
        uint elapsed = frame - ready.Value;
        if (boss.destroyed || boss.life <= 0 || elapsed >= 600) return 0;
        // Keep the fixture in attack range while the boss continues its normal
        // movement. Healing keeps the finite routing probe alive; it is not an
        // incoming-damage or player-survival assertion.
        hero.life = machine.MaxHealth;
        machine.SetHealth((ushort)machine.MaxHealth, (ushort)machine.MaxHealth);
        double bossX = (boss.cx + boss.xr) * 24;
        Position(hero, bossX - boss.radius - 24, (boss.cy + boss.yr) * 24);
        hero.dir = 1;
        if (elapsed >= 100 && !fed)
        {
            fed = machine.Feed(18);
            if (fed) { fedAt = frame; Event("sword-content-fed", new { frame }); }
        }
        if (!fed) return 2;
        return (frame - fedAt!.Value) switch
        {
            < 2 => 0,
            < 8 => 128,
            >= 160 and < 360 => 2,
            _ => 0,
        };
    }

    internal static void Poll()
    {
        var provider = KirbyMod.Instance?.NativeProvider;
        var machine = provider?.Machine;
        var session = KirbyMod.Instance?.ActiveSession;
        if (run is null || machine is null || session is null || machine.State.Frame == lastPoll) return;
        lastPoll = machine.State.Frame;
        int active = 0;
        var source = new List<object>();
        for (uint i = 1; i <= 31; i++)
        {
            var proxy = machine.Proxy(i);
            if ((proxy.Flags & 1) != 0) active++;
            if (mode == "boss") captureObserved |= (proxy.Flags & (4 | 32)) != 0;
            if (proxy.Flags != 0) source.Add(new { slot = i, proxy.Flags, proxy.Hp, proxy.XRaw, proxy.YRaw });
        }
        maximumProxies = Math.Max(maximumProxies, active);
        // Read the adapter's own mapping as well as the native count: nearby
        // breakables must not be mistaken for one of the 31 fixture enemies.
        var bindings = ((KirbyEnemies)typeof(KirbyNdsProvider).GetField("enemies",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(provider)!).Capture();
        int boundFixtureTargets = bindings.Count(binding => targets.Any(target => target.__uid == binding.Uid));
        maximumBoundFixtureTargets = Math.Max(maximumBoundFixtureTargets, boundFixtureTargets);
        if ((machine.ActionFlags & 1) != 0) inhaleFrames++;
        if (machine.State.Ability == 18) swordFormFrames++;
        if (boss is { destroyed: false })
        {
            bossMoved |= Math.Abs((boss.cx + boss.xr) * 24 - bossStartX) > 1;
            sourceControlledBoss |= provider!.ControlsEnemy(boss);
            minimumBossHp = Math.Min(minimumBossHp, boss.life);
            bossAnimations.Add(boss.spr?.groupName?.ToString() ?? "");
        }
        var targetRows = targets.Select(mob => new
        {
            uid = mob.__uid, mob.destroyed, life = mob.destroyed ? 0 : mob.life,
            initialLife = initialHp[mob.__uid],
            sourceControlled = !mob.destroyed && provider!.ControlsEnemy(mob),
        }).ToArray();
        File.WriteAllText(Path.Combine(run, "boss-crowd.json"), JsonSerializer.Serialize(new
        {
            mode, frame = lastPoll, elapsed = ready is uint at ? lastPoll - at : 0,
            setupAttempts, spawned, fed, originalAbility = machine.State.Ability,
            maximumProxies, active, boundFixtureTargets, maximumBoundFixtureTargets, inhaleFrames, swordFormFrames,
            excludedIncomingHits, outgoingHitCount, outgoingDamage, damagedTargets = damagedTargets.Count,
            bossReady = boss is { destroyed: false } && boss.isReady(), bossMoved,
            sourceControlledBoss, captureObserved, minimumBossHp,
            bossAnimations = bossAnimations.Order().ToArray(), targets = targetRows, source,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
