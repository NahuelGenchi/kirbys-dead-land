using System.Text.Json;
using dc.en;
using dc.en.mob;
using Kirby.Core;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of a screen-wide copy attack (Crash by default; Paint or
/// Mike through <c>KIRBY_LAB_TEST_ABILITY</c>). The ability is prepared through
/// original inhale, supplied content and Down. Real Zombies with locked AI then
/// stand near Kirby (paired, inside the original's window), near him but kept
/// without a source object, beyond the original's window, beyond the pairing
/// window, and far off screen. Original B fires the attack; <c>screen.json</c>
/// records, per Zombie, where it stood, who struck it and whether it died.
/// </summary>
internal static class LabScreenProbe
{
    private sealed class Target(string name, double dx, bool unpaired = false)
    {
        public string Name { get; } = name;
        public double Dx { get; } = dx;
        public bool Unpaired { get; } = unpaired;
        public Mob? Mob;
        public int LifeBefore;
        public double[]? Position;
        public bool? PairedAtFire, SourceReachesAtFire, InViewAtFire;
        public readonly List<object> HostStrikes = [];
        public uint? DiedAt;
    }

    private static string run = "";
    private static byte ability = 23;
    private static uint? start, setupStart, readyAt, spawnedAt, firedAt;
    private static uint lastTick, lastReportFrame;
    private static bool setupFed;
    private static double[]? view, sourceWindow;
    private static readonly Target[] targets =
    [
        new("near-paired", 40), new("near-unpaired", 110, unpaired: true), new("beyond-source-window", 230),
        new("beyond-pairing", 360), new("behind-beyond-source-window", -230), new("off-screen", 1400),
    ];

    private static void Stage(string phase) =>
        System.IO.File.AppendAllText(Path.Combine(run, "screen-events.txt"), $"{lastTick}: {phase}\n");

    public static void Install(string output)
    {
        run = output;
        if (byte.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_TEST_ABILITY"), out byte requested)) ability = requested;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyEnemies.TestUnpaired = entity => targets.Any(t => t.Unpaired && t.Mob?.HashlinkPointer == entity.HashlinkPointer);
        KirbyScreenAttacks.TestObserver = (mob, sourceDamage, hostDamage) =>
        {
            var target = targets.FirstOrDefault(t => t.Mob?.HashlinkPointer == mob.HashlinkPointer);
            target?.HostStrikes.Add(new { frame = lastTick, sourceDamage, hostDamage, lifeBefore = mob.life });
            Stage($"Host screen strike on {target?.Name ?? "other"}: {sourceDamage} source -> {hostDamage} host, life {mob.life}");
        };
        // Every other strike on a Zombie, and the host damage it took, for unexpected deaths.
        KirbyEnemies.TestObserver = (mob, sourceDamage, hostDamage) =>
            Stage($"Source hit on {Name(mob)}: {sourceDamage} source -> {hostDamage} host");
        KirbyStars.TestObserver = (kind, mob, hostDamage, _) => Stage($"{kind} star hit on {Name(mob)}: {hostDamage} host");
        Hook_Mob.onDamage += (orig, mob, attack) =>
        {
            orig(mob, attack);
            if (targets.Any(t => t.Mob?.HashlinkPointer == mob.HashlinkPointer))
                Stage($"onDamage({Name(mob)}): {attack.inflictedDmg} from {attack.source?.GetType().FullName ?? "none"}, life {mob.life}, " +
                    $"at ({(mob.cx + mob.xr) * 24:0.0},{(mob.cy + mob.yr) * 24:0.0}), dmgType {attack.dmgType}");
        };
        KirbyInput.TestOverride = Input;
    }

    private static string Name(dc.Entity entity) =>
        targets.FirstOrDefault(t => t.Mob?.HashlinkPointer == entity.HashlinkPointer)?.Name ?? "other";

    private static Mob Spawn(Hero hero, double dx)
    {
        var mob = Zombie.Class.create(hero._level, hero.cx, hero.cy, 1, 1);
        mob.setPosPixel((hero.cx + hero.xr) * 24 + dx, (hero.cy + hero.yr) * 24);
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        mob.lockAiS(30);
        return mob;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        uint tick = lastTick = machine.State.Frame;
        start ??= tick;
        if (tick - start.Value < 300) return 0;
        var hero = session.Hero;
        if (readyAt is null)
        {
            if (machine.State.Ability != 1 && machine.State.Ability != ability) { setupStart = null; return 4; }
            setupStart ??= tick;
            if (machine.State.Ability == ability) { readyAt = tick; Stage($"Ability {ability} ready"); return 0; }
            if (tick - setupStart.Value < 30) return 2;
            if (!setupFed) { setupFed = machine.Feed(ability); if (!setupFed) return 2; }
            return 128;
        }
        if (spawnedAt is null)
        {
            if (tick - readyAt.Value < 60) return 0; // Original copy animation.
            foreach (var target in targets)
            {
                target.Mob = Spawn(hero, target.Dx);
                target.LifeBefore = target.Mob.life;
                Stage($"{target.Name}: Zombie life {target.LifeBefore} at dx {target.Dx}");
            }
            spawnedAt = tick;
            return 0;
        }
        if (tick - spawnedAt.Value < 30) return 0; // Pairing settles.
        if (firedAt is null)
        {
            firedAt = tick;
            Snapshot(hero, machine);
        }
        return tick - firedAt.Value < 40 ? (ushort)2 : (ushort)0;
    }

    /// <summary>Where each Zombie stands relative to both screens as B goes down.</summary>
    private static void Snapshot(Hero hero, NdsRuntime machine)
    {
        var state = machine.State;
        var window = ScreenAttacks.SourceWindow(state.CameraX, state.CameraY);
        sourceWindow = [window.Left, window.Top, window.Right, window.Bottom];
        if (hero._level.viewport is { } viewport)
            view = [viewport.realX - viewport.wid / 2.0, viewport.realY - viewport.hei / 2.0,
                viewport.realX + viewport.wid / 2.0, viewport.realY + viewport.hei / 2.0];
        var enemies = KirbyMod.Instance!.NativeProvider!.Enemies;
        foreach (var target in targets)
        {
            if (target.Mob is not { } mob) continue;
            double x = (mob.cx + mob.xr) * 24, y = (mob.cy + mob.yr) * 24;
            target.Position = [x, y];
            var source = enemies.SourcePosition(machine, mob);
            target.PairedAtFire = source is not null;
            target.SourceReachesAtFire = source is { } at && ScreenAttacks.SourceReaches(at.X, at.Y, state.CameraX, state.CameraY);
            var body = KirbyStars.HostBody(mob);
            target.InViewAtFire = view is not null && body.Right >= view[0] && body.Left <= view[2] && body.Bottom >= view[1] && body.Top <= view[3];
            Stage($"{target.Name} at fire: ({x:0.0},{y:0.0}) paired={target.PairedAtFire} sourceReaches={target.SourceReachesAtFire} inView={target.InViewAtFire}");
        }
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run.Length == 0 || session is null || machine is null || machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        foreach (var target in targets)
            if (target.Mob is { } mob && target.DiedAt is null && (mob.destroyed || mob.life <= 0))
            {
                target.DiedAt = lastReportFrame;
                Stage($"{target.Name} died at ({(mob.cx + mob.xr) * 24:0.0},{(mob.cy + mob.yr) * 24:0.0})");
            }
        System.IO.File.WriteAllText(Path.Combine(run, "screen.json"), JsonSerializer.Serialize(new
        {
            frame = lastReportFrame,
            ability,
            sourceAbility = machine.State.Ability,
            readyAt, spawnedAt, firedAt,
            hero = new[] { (session.Hero.cx + session.Hero.xr) * 24, (session.Hero.cy + session.Hero.yr) * 24 },
            view, sourceWindow,
            targets = targets.Select(t => new
            {
                t.Name, t.Dx, t.Unpaired, t.Position, t.LifeBefore,
                lifeNow = t.Mob?.life, t.PairedAtFire, t.SourceReachesAtFire, t.InViewAtFire,
                t.HostStrikes, t.DiedAt,
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
