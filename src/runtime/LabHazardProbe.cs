using System.Reflection;
using System.Text.Json;
using dc.en;
using dc.en.mob;
using Entity = dc.Entity;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check: real Grenader and Archer enemies use their natural AI to fire
/// at Kirby while the original inhale is held. Each consumed projectile is then
/// spat back toward its shooter or swallowed with the original DOWN action.
/// Archer arrows fly at the Beheaded's chest height, above the original inhale box
/// on flat ground, so the arrow phase jumps before inhaling.
/// </summary>
internal static class LabHazardProbe
{
    private sealed class Phase(string shooter, string action, bool jump)
    {
        public string Shooter { get; } = shooter;
        public string Action { get; } = action;
        public bool Jump { get; } = jump;
        public string? ProjectileType { get; set; }
        public uint? ConsumedAt, ActedAt, FinishedAt;
        public bool TimedOut, MouthfulAtConsumption, MouthfulAfter;
        public int KirbyLifeBeforeCapture, KirbyLifeAtConsumption, KirbyLifeAfter;
        public double ShooterDxAtAction, ShooterDyAtAction;
        public int HeroDirAtAction, ShooterLifeBeforeAction, ShooterLifeAfter;
        public int OutgoingHits, OutgoingDamage, OutgoingHitsOnShooter;
        public byte SourceAbilityAfter;
        public string? CopyAbilityAfter;
    }

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo EnemiesField = typeof(KirbyNdsProvider).GetField("enemies", Private)!;
    private static readonly FieldInfo MobsField = typeof(KirbyEnemies).GetField("mobs", Private)!;
    private static string? run;
    private static uint? start, phaseStart;
    private static readonly Phase[] phases =
        [new("Grenader", "spit", false), new("Grenader", "swallow", false), new("Archer", "spit", true)];
    private static int current;
    private static Mob? shooter;
    private static readonly Dictionary<nint, (Entity Projectile, bool Held)> tracked = [];
    private static readonly Dictionary<nint, (string Type, bool Paired, double MinDx, double HeightAtMinDx)> approaches = [];
    private static int hostileProjectilesSeen, ownProjectilesPaired;
    private static uint lastReportFrame;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        Hook_Mob.onDamage += (orig, mob, attack) =>
        {
            orig(mob, attack);
            if (current >= phases.Length || phases[current].ActedAt is null ||
                attack.source?.HashlinkPointer != KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer) return;
            var phase = phases[current];
            phase.OutgoingHits++;
            phase.OutgoingDamage += Math.Max(0, attack.inflictedDmg);
            if (mob.HashlinkPointer == shooter?.HashlinkPointer) phase.OutgoingHitsOnShooter++;
        };
        KirbyInput.TestOverride = Input;
    }

    private static Mob Spawn(Hero hero, string kind)
    {
        // Beyond the original inhale box (60px ahead), inside the 300px pairing range.
        int dir = hero.dir < 0 ? -1 : 1;
        Mob mob = kind == "Archer"
            ? Archer.Class.create(hero._level, hero.cx + dir * 5, hero.cy, 1, 1)
            : Grenader.Class.create(hero._level, hero.cx + dir * 5, hero.cy, 1, 1);
        mob.setPosPixel((hero.cx + hero.xr) * 24 + dir * 120, (hero.cy + hero.yr) * 24);
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        return mob;
    }

    private static void Track(Hero hero, NdsRuntime machine, Phase phase, uint frame)
    {
        var provider = KirbyMod.Instance!.NativeProvider!;
        var bound = (Entity?[])MobsField.GetValue(EnemiesField.GetValue(provider))!;
        double hx = (hero.cx + hero.xr) * 24, hy = (hero.cy + hero.yr) * 24;
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
        {
            if (entities.getDyn(j) is not Entity e || e is not (Bullet or Grenade) || e.destroyed) continue;
            bool held = provider.ControlsEnemy(e);
            if (!e.isOpponent(hero)) { if (held) ownProjectilesPaired++; continue; }
            if (!tracked.TryGetValue(e.HashlinkPointer, out var entry)) { hostileProjectilesSeen++; entry = (e, false); }
            tracked[e.HashlinkPointer] = (e, entry.Held || held);
            bool paired = bound.Any(m => m?.HashlinkPointer == e.HashlinkPointer);
            double dx = Math.Abs((e.cx + e.xr) * 24 - hx), height = hy - (e.cy + e.yr) * 24;
            var a = approaches.TryGetValue(e.HashlinkPointer, out var old) ? old : (e.GetType().FullName ?? "?", false, double.MaxValue, 0);
            approaches[e.HashlinkPointer] = (a.Item1, a.Item2 || paired, Math.Min(a.Item3, dx), dx < a.Item3 ? height : a.Item4);
        }
        foreach (var (key, (projectile, held)) in tracked.ToArray())
        {
            if (!projectile.destroyed) continue;
            tracked.Remove(key);
            if (!held || phase.ConsumedAt is not null) continue;
            phase.ConsumedAt = frame;
            phase.ProjectileType = projectile.GetType().FullName;
            phase.MouthfulAtConsumption = (machine.ActionFlags & 2) != 0;
            phase.KirbyLifeAtConsumption = hero.life;
        }
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null || run is null) return 0;
        Hero hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (!hero.awake || hero._level.game.hasCinematic() || frame - start.Value < 180) return 0;
        if (current >= phases.Length)
        {
            if (shooter is { destroyed: false } done && done.life > 0) done.destroy();
            return 0;
        }
        var phase = phases[current];
        if (shooter is null || shooter.destroyed || shooter.life <= 0 || shooter.GetType().Name != phase.Shooter)
        {
            if (shooter is { destroyed: false } old && old.life > 0) old.destroy();
            shooter = Spawn(hero, phase.Shooter);
            return 0;
        }
        if (phaseStart is null) { phaseStart = frame; phase.KirbyLifeBeforeCapture = hero.life; }
        Track(hero, machine, phase, frame);
        if (phase.ConsumedAt is not uint consumed)
        {
            if (frame - phaseStart.Value > 2400) { phase.TimedOut = true; Next(frame); return 0; }
            if (!phase.Jump) return 2; // Hold the original inhale while the shooter fires.
            // Jump, then inhale on the way down: the original box spans 0..34px
            // above Kirby's foot, so it reaches arrow height only in the air.
            return ((frame - phaseStart.Value) % 70) switch { < 3 => 1, < 8 => 0, < 60 => 2, _ => 0 };
        }
        if (phase.ActedAt is null)
        {
            if (frame - consumed < 30) return 0;
            phase.ActedAt = frame;
            phase.ShooterLifeBeforeAction = shooter.life;
            phase.ShooterDxAtAction = (shooter.cx + shooter.xr - hero.cx - hero.xr) * 24;
            phase.ShooterDyAtAction = (shooter.cy + shooter.yr - hero.cy - hero.yr) * 24;
            phase.HeroDirAtAction = hero.dir;
        }
        uint acted = frame - phase.ActedAt.Value;
        // Several frames, because a host frame may advance zero source frames.
        if (acted < 4) return phase.Action == "spit" ? (ushort)2 : (ushort)128;
        if (acted < 150) return 0;
        phase.ShooterLifeAfter = shooter.destroyed ? 0 : shooter.life;
        phase.SourceAbilityAfter = machine.State.Ability;
        phase.CopyAbilityAfter = session.Rules.AbilityId;
        phase.MouthfulAfter = (machine.ActionFlags & 2) != 0;
        phase.KirbyLifeAfter = hero.life;
        Next(frame);
        return 0;
    }

    private static void Next(uint frame)
    {
        phases[current].FinishedAt = frame;
        current++;
        phaseStart = null;
        tracked.Clear();
    }

    public static void Poll()
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run is null || machine is null || machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        File.WriteAllText(Path.Combine(run, "hazard.json"), JsonSerializer.Serialize(new
        {
            frame = lastReportFrame,
            completedPhases = current,
            hostileProjectilesSeen,
            ownProjectilesPaired,
            phases = phases.Select(p => new
            {
                p.Shooter, p.Action, p.Jump, p.ProjectileType, p.ConsumedAt, p.ActedAt, p.FinishedAt, p.TimedOut,
                p.MouthfulAtConsumption, p.MouthfulAfter, p.KirbyLifeBeforeCapture, p.KirbyLifeAtConsumption, p.KirbyLifeAfter,
                p.ShooterDxAtAction, p.ShooterDyAtAction, p.HeroDirAtAction, p.ShooterLifeBeforeAction, p.ShooterLifeAfter,
                p.OutgoingHits, p.OutgoingDamage, p.OutgoingHitsOnShooter, p.SourceAbilityAfter, p.CopyAbilityAfter,
            }),
            approaches = approaches.Values.Select(a => new { type = a.Type, paired = a.Paired, minDx = a.MinDx, heightAboveFootAtMinDx = a.HeightAtMinDx }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
