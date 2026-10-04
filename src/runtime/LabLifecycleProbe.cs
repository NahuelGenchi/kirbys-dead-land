using System.Text.Json;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>Opt-in private-lab checks of host outfit, transition and death lifecycles.</summary>
internal static class LabLifecycleProbe
{
    private static string? run;
    private static int phase, ticks;
    private static uint sourceFrame;
    private static byte sourceAbility;
    private static string? shield;
    private static nint firstLevel;
    private static bool installed;
    private static int expectedHealth;

    internal static void Install()
    {
        if (installed || Environment.GetEnvironmentVariable("KIRBY_LAB_LIFECYCLE") != "1" ||
            Environment.GetEnvironmentVariable("KIRBY_LAB_PLAY") == "1") return;
        run = Environment.GetEnvironmentVariable("KIRBY_LAB_RUN_ROOT");
        if (run is null || !Directory.Exists(run)) return;
        installed = true;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyInput.TestOverride = _ => 0;
        dc.Hook_Main.update += (orig, main) => { orig(main); Poll(); };
    }

    private static void Record(string name, object value) => System.IO.File.AppendAllText(
        Path.Combine(run!, "lifecycle.jsonl"), JsonSerializer.Serialize(new { name, phase, value }) + "\n");

    private static void Poll()
    {
        var mod = KirbyMod.Instance;
        var hero = ModCore.Modules.Game.Instance.HeroInstance;
        var machine = mod?.NativeProvider?.Machine;
        if (machine is null) return;
        ticks++;
        if (phase == 5 && ticks > 120)
        {
            Record("death-observed", new { life = hero?.life, destroyed = hero?.destroyed,
                sourceHealth = machine.Health, sourceFrame = machine.State.Frame });
            phase = 9;
        }
        if (hero is null || hero.destroyed) return;
        if (phase == 0 && hero.awake && !hero._level.game.hasCinematic() && ticks > 180)
        {
            sourceFrame = machine.State.Frame; sourceAbility = machine.State.Ability;
            shield = mod!.ActiveSession?.Rules.RetainedShieldId;
            firstLevel = hero._level.HashlinkPointer;
            Record("before-outfit-change", new { sourceFrame, sourceAbility, shield, hero.hei, hero.radius, hero.life, hero.maxLife });
            hero.applySkin(null!);
            phase = 1; ticks = 0;
        }
        else if (phase == 1 && ticks > 120)
        {
            Record("normal-outfit", new { inactive = mod!.ActiveSession is null,
                sourceFrozen = machine.State.Frame <= sourceFrame + 1,
                outfit = hero._level.game.user.heroSkin?.ToString(), hero.hei, hero.radius, hero.life, hero.maxLife });
            // Health lost while wearing another outfit must not be undone by
            // reactivating the paused source machine.
            hero.life = Math.Max(1, hero.life - 25);
            expectedHealth = Math.Clamp((int)Math.Round(hero.life / (double)hero.maxLife * machine.MaxHealth), 1, machine.MaxHealth);
            hero.applySkin("KirbySuperStarUltra".AsHaxeString());
            phase = 2; ticks = 0;
        }
        else if (phase == 2 && ticks > 180 && mod!.ActiveSession is not null)
        {
            Record("kirby-restored", new { progressed = machine.State.Frame > sourceFrame,
                sameAbility = sourceAbility == machine.State.Ability,
                sameShield = shield == mod.ActiveSession.Rules.RetainedShieldId,
                healthImported = hero.life == expectedHealth && machine.Health == expectedHealth,
                hero.hei, hero.radius, hero.life, hero.maxLife });
            hero.updateMaxLife();
            Record("host-maxlife-recomputed", new { hero.life, hero.maxLife, expectedHealth });
            dc.en.inter.Exit? exit = null;
            var exits = new List<object>();
            for (int i = 0; i < hero._level.entities.length; i++)
                if (hero._level.entities.getDyn(i) is dc.en.inter.Exit candidate)
                {
                    exits.Add(new { destination = candidate.destLevel?.ToString(), candidate.locked });
                    if (!candidate.locked && !string.IsNullOrEmpty(candidate.destLevel?.ToString())) exit ??= candidate;
                }
            Record("level-exits", exits);
            if (exit is null)
            {
                Record("transition-unavailable", new { reason = "No unlocked exit in this generated level." });
                phase = 9; return;
            }
            hero.setPosPixel((exit.cx + exit.xr) * 24, (exit.cy + exit.yr) * 24);
            if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
            Record("transition-start", new { destination = exit.destLevel?.ToString(), sourceFrame = machine.State.Frame });
            exit.onActivate(hero, false);
            phase = 3; ticks = 0;
        }
        else if (phase == 3 && hero._level.HashlinkPointer != firstLevel && hero.awake &&
            !hero._level.game.hasCinematic() && mod!.ActiveSession is not null)
        {
            Record("transition-complete", new { sameAbility = sourceAbility == machine.State.Ability,
                sameShield = shield == mod.ActiveSession.Rules.RetainedShieldId,
                hero.hei, hero.radius, hero.life, hero.maxLife, sourceFrame = machine.State.Frame });
            phase = 4; ticks = 0;
        }
        else if (phase == 4 && ticks > 180)
        {
            hero.life = 3;
            var attacker = dc.en.mob.Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
            attacker.lockAiS(10);
            var attack = dc.tool.atk.AttackUtils.Class.createFromMob(attacker, 10, null);
            dc.tool.atk.AttackUtils.Class.hit(attack, hero);
            Record("lethal-host-attack", new { hero.life, sourceHealth = machine.Health });
            phase = 5; ticks = 0;
        }
    }
}
