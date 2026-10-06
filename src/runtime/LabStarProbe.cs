using System.Text.Json;
using dc.en;
using dc.en.mob;
using Kirby.Core;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check: Kirby inhales a real Zombie and spits the original star at a
/// second Zombie, once with that target paired to a source object (the original
/// collision hits it) and once kept without one (the host strikes it). A third
/// phase swallows for a copy ability, discards it with SELECT and lets the bouncing
/// original ability star cross a Zombie behind Kirby. Targets get 5000 life so their
/// stagger can be observed. The last two phases move Kirby beside a real level wall
/// and send a spit star, then a discarded star, into it.
/// </summary>
internal static unsafe class LabStarProbe
{
    private enum Kind { Spit, Discard }

    private sealed class Phase(string name, Kind kind, bool unpaired = false, bool wall = false)
    {
        public string Name { get; } = name;
        public Kind Kind { get; } = kind;
        public bool Unpaired { get; } = unpaired;
        public bool Wall { get; } = wall;
        public uint? ReadyAt, ConsumedAt, CopiedAt, FiredAt, HitAt, FinishedAt, StunFirst, StunLast;
        public bool TimedOut, NoWallSite, TargetPairedAtFire;
        public string? CopyAbility;
        public int TargetLifeBefore, TargetLifeAfter, KirbyLifeBefore, KirbyLifeAfter;
        public double TargetXAtHit, TargetXAfter, TargetDxFromKirbyAtFire;
        public bool StunAfterHit, StaggerAffectAfterHit;
        public double? WallFaceX;
        public int WallSide;
        public readonly List<object> Strikes = [];
        public readonly List<(uint Frame, uint Address, string Kind, StarBox Box)> Stars = [];
    }

    private const int TargetLife = 5000;
    private static string? run;
    private static uint? start, phaseStart;
    private static readonly Phase[] phases =
    [
        new("spit-paired", Kind.Spit), new("spit-unpaired", Kind.Spit, unpaired: true), new("ability-star", Kind.Discard),
        new("spit-wall", Kind.Spit, wall: true), new("ability-wall", Kind.Discard, wall: true),
    ];
    private static int current;
    private static Mob? victim, target;
    private static uint lastReportFrame;
    private static (int X, int Floor, int Side, int Distance)? site;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyEnemies.TestUnpaired = entity => current < phases.Length && phases[current].Unpaired &&
            entity.HashlinkPointer == target?.HashlinkPointer;
        KirbyStars.TestObserver = (kind, mob, damage, source) =>
        {
            if (current >= phases.Length || run is null) return;
            var phase = phases[current];
            phase.Strikes.Add(new { frame = Frame(), kind = kind.ToString(), source, damage, onTarget = mob.HashlinkPointer == target?.HashlinkPointer,
                lifeBefore = mob.life, type = mob.GetType().Name });
            if (mob.HashlinkPointer == target?.HashlinkPointer && phase.HitAt is null)
            {
                phase.HitAt = Frame();
                phase.TargetXAtHit = (mob.cx + mob.xr) * 24;
            }
        };
        KirbyStars.TestTrace = (address, kind, box) =>
        {
            if (current >= phases.Length) return;
            var stars = phases[current].Stars;
            uint frame = Frame();
            if (stars.Count < 600 && !stars.Any(s => s.Frame == frame && s.Address == address))
                stars.Add((frame, address, kind.ToString(), box));
        };
        KirbyInput.TestOverride = Input;
    }

    private static uint Frame() => KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame ?? 0;

    private static Mob Spawn(Hero hero, double dx, bool durable = false)
    {
        var mob = Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
        mob.setPosPixel((hero.cx + hero.xr) * 24 + dx, (hero.cy + hero.yr) * 24);
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        // A star kills a fresh Zombie outright; the target survives so its stagger can be seen.
        if (durable) mob.maxLife = mob.life = TargetLife;
        return mob;
    }

    private static void Hold(Mob? mob)
    {
        // Keep the enemies from walking into Kirby or attacking before the star flies.
        if (mob is { destroyed: false, life: > 0 }) { mob.lockAiS(0.2); mob.dx = 0; }
    }

    /// <summary>
    /// A flat stretch of solid floor, open five cells up, that ends in a solid wall
    /// six cells tall: taller than the ability star's 93 px bounce.
    /// </summary>
    private static (int X, int Floor, int Side, int Distance)? FindWall(Hero hero)
    {
        var map = hero._level.map;
        int* grid = (int*)map.collisions.bytes;
        int Cell(int x, int y) => x < 0 || y < 0 || x >= map.wid || y >= map.hei ? 3 : grid[y * map.wid + x];
        bool Solid(int x, int y) => (Cell(x, y) & 3) != 0;
        bool Open(int x, int y) => (Cell(x, y) & (3 | 8 | 16 | 64 | 512 | 1024)) == 0;
        (int, int, int, int)? best = null;
        int bestScore = int.MaxValue;
        for (int floor = hero.cy - 15; floor <= hero.cy + 15; floor++)
        for (int x = hero.cx - 45; x <= hero.cx + 45; x++)
        foreach (int side in new[] { -1, 1 })
        for (int distance = 5; distance <= 7; distance++)
        {
            bool ok = true;
            // Two spare cells behind Kirby too, for the turn and the victim.
            for (int k = -2; k < distance && ok; k++)
            {
                int cx = x + side * k;
                ok = Solid(cx, floor) && Enumerable.Range(1, 5).All(up => Open(cx, floor - up));
            }
            int wall = x + side * distance;
            if (!ok || !Enumerable.Range(1, 6).All(up => Solid(wall, floor - up))) continue;
            int score = Math.Abs(x - hero.cx) + Math.Abs(floor - hero.cy) * 2;
            if (score < bestScore) { bestScore = score; best = (x, floor, side, distance); }
            break;
        }
        return best;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null || run is null) return 0;
        Hero hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (!hero.awake || hero._level.game.hasCinematic() || frame - start.Value < 180) return 0;
        if (current >= phases.Length) return 0;
        var phase = phases[current];
        if (phaseStart is null)
        {
            phaseStart = frame;
            phase.KirbyLifeBefore = hero.life;
            if (phase.Wall)
            {
                site ??= FindWall(hero);
                if (site is not { } s) { phase.NoWallSite = true; Next(frame); return 0; }
                phase.WallSide = s.Side;
                int wall = s.X + s.Side * s.Distance;
                phase.WallFaceX = s.Side > 0 ? wall * 24 : (wall + 1) * 24;
                hero.setPosPixel(s.X * 24 + 12, s.Floor * 24);
                if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
            }
            return 0;
        }
        uint elapsed = frame - phaseStart.Value;
        if (elapsed > 1500) { phase.TimedOut = true; Next(frame); return 0; }
        if (phase.ReadyAt is null)
        {
            // Kirby starts the run facing right. hero.dir does not follow a turn
            // (the bridge's facing byte stayed 0 in run 20261005-221013), so a wall
            // phase uses the side it turned Kirby to.
            int dir = hero.dir < 0 ? -1 : 1;
            if (phase.Wall)
            {
                // Settle, then turn: a spit flies at the wall, a discarded star leaves backwards.
                dir = phase.Kind == Kind.Spit ? phase.WallSide : -phase.WallSide;
                if (elapsed < 40) return 0;
                if (elapsed < 46) return dir > 0 ? (ushort)16 : (ushort)32;
                if (elapsed < 76) return 0;
            }
            phase.ReadyAt = frame;
            victim = Spawn(hero, dir * 36);
            if (phase.Kind == Kind.Spit && !phase.Wall) target = Spawn(hero, dir * 150, durable: true);
            return 0;
        }
        if (phase.FiredAt is null) { Hold(victim); Hold(target); }
        if (phase.ConsumedAt is null)
        {
            if (victim is { destroyed: false, life: > 0 }) return 2;
            phase.ConsumedAt = frame;
            return 0;
        }
        uint since = frame - phase.ConsumedAt.Value;
        if (phase.Kind == Kind.Discard && phase.CopiedAt is null)
        {
            // Swallow with the original DOWN, then wait for the copy to load.
            if (since < 30) return 0;
            if (since < 34) return 128;
            if (session.Rules.AbilityId is null) return 0;
            phase.CopiedAt = frame;
            phase.CopyAbility = session.Rules.AbilityId;
            // The discarded star leaves Kirby backwards.
            if (!phase.Wall) target = Spawn(hero, -(hero.dir < 0 ? -1 : 1) * 60, durable: true);
            return 0;
        }
        if (phase.FiredAt is null)
        {
            uint ready = phase.Kind == Kind.Discard ? frame - phase.CopiedAt!.Value : since;
            if (ready < 60) return 0;
            phase.FiredAt = frame;
            if (target is not null)
            {
                phase.TargetLifeBefore = target.life;
                phase.TargetDxFromKirbyAtFire = (target.cx + target.xr - hero.cx - hero.xr) * 24;
                phase.TargetPairedAtFire = KirbyMod.Instance!.NativeProvider!.Enemies.Pairs(target);
            }
        }
        uint fired = frame - phase.FiredAt.Value;
        // Several frames, because a host frame may advance zero source frames.
        if (fired < 4) return phase.Kind == Kind.Discard ? (ushort)4 : (ushort)2;
        if (target is { destroyed: false, life: > 0 } t && phase.HitAt is not null)
        {
            if (t.hasAffect(StarProjectiles.StunAffect))
            {
                phase.StunAfterHit = true;
                phase.StunFirst ??= frame;
                phase.StunLast = frame;
            }
            phase.StaggerAffectAfterHit |= t.hasAffect(StarProjectiles.StaggerAffect);
            if (frame - phase.HitAt.Value == 12) phase.TargetXAfter = (t.cx + t.xr) * 24;
        }
        if (fired < (phase.Kind == Kind.Discard ? 300u : 150u)) return 0;
        phase.TargetLifeAfter = target is { destroyed: false } alive ? alive.life : 0;
        phase.KirbyLifeAfter = hero.life;
        Next(frame);
        return 0;
    }

    private static void Next(uint frame)
    {
        phases[current].FinishedAt = frame;
        foreach (var mob in new[] { victim, target })
            if (mob is { destroyed: false } && mob.life > 0) mob.destroy();
        victim = target = null;
        current++;
        phaseStart = null;
    }

    /// <summary>Horizontal turns of each star's center: wall or room-edge bounces.</summary>
    private static IEnumerable<object> Turns(Phase phase) => phase.Stars.GroupBy(s => s.Address).SelectMany(group =>
    {
        var path = group.OrderBy(s => s.Frame).ToArray();
        return Enumerable.Range(1, Math.Max(0, path.Length - 2))
            .Where(i => (path[i].Box.CenterX - path[i - 1].Box.CenterX) * (path[i + 1].Box.CenterX - path[i].Box.CenterX) < 0)
            .Select(i => (object)new { path[i].Frame, path[i].Kind, path[i].Box.Left, path[i].Box.Right });
    });

    public static void Poll()
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run is null || machine is null || machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        File.WriteAllText(Path.Combine(run, "stars.json"), JsonSerializer.Serialize(new
        {
            frame = lastReportFrame,
            completedPhases = current,
            site = site is { } s ? new { s.X, s.Floor, s.Side, s.Distance } : null,
            phases = phases.Select(p => new
            {
                p.Name, kind = p.Kind.ToString(), p.Unpaired, p.Wall, p.NoWallSite, p.WallSide, p.WallFaceX,
                p.ReadyAt, p.ConsumedAt, p.CopiedAt, p.CopyAbility, p.FiredAt, p.HitAt, p.FinishedAt, p.TimedOut,
                p.TargetPairedAtFire, p.TargetDxFromKirbyAtFire, p.TargetLifeBefore, p.TargetLifeAfter, p.KirbyLifeBefore, p.KirbyLifeAfter,
                p.StunAfterHit, p.StunFirst, p.StunLast, p.StaggerAffectAfterHit, p.TargetXAtHit, p.TargetXAfter,
                strikes = p.Strikes,
                starFrames = p.Stars.Count,
                firstStar = p.Stars.Count > 0 ? new { p.Stars[0].Frame, p.Stars[0].Kind, p.Stars[0].Box.Left, p.Stars[0].Box.Right, p.Stars[0].Box.Top, p.Stars[0].Box.Bottom } : null,
                lastStar = p.Stars.Count > 0 ? new { p.Stars[^1].Frame, p.Stars[^1].Kind, p.Stars[^1].Box.Left, p.Stars[^1].Box.Right, p.Stars[^1].Box.Top, p.Stars[^1].Box.Bottom } : null,
                turns = Turns(p),
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
