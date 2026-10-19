using System.Text.Json;
using dc.en;
using dc.en.mob;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of the Thorny's spikes against the helper's hits (#206). In the
/// run's first room a real Thorny stands with its back to Kirby and is hit through
/// <see cref="KirbyDamage.Hit"/>, the path every copy hit takes: first as Kirby's own
/// hit, which the Thorny answers with its spikes (Kirby loses health), then marked as
/// the helper's, whose answer must be dropped before it reaches him, then as Kirby's
/// again, which shows the helper mark does not outlive its hit. Each trial has its own
/// Thorny with its AI held and waits for Kirby's hurt state to end. The helper's own
/// attribution from the original's records (#128) is not exercised: no helper exists.
/// Passes when the Kirby hits are answered and cost Kirby health, and the helper hit
/// is answered, dropped once, and costs him none.
/// </summary>
internal static class LabThornyProbe
{
    private sealed class Trial(string name, bool fromHelper)
    {
        public string Name { get; } = name;
        public bool FromHelper { get; } = fromHelper;
        public Mob? Thorny;
        public uint SpawnedAt, HitAt;
        public int Side, DirAtHit, LifeBefore, LifeAfter, ThornyLifeBefore, ThornyLifeAfter, DroppedBefore, Dropped, OthersRemoved;
        public bool HitLanded, Hittable;
        public bool Done;
    }

    private static readonly Trial[] trials = [new("kirby", false), new("helper", true), new("kirby-again", false)];
    private const double Distance = 64;
    private static string? run;
    private static uint? start;
    private static int current, dropped;
    private static bool finished;
    private static string? failure;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyDamage.TestDropped = _ => dropped++;
        KirbyInput.TestOverride = _ => 0;
    }

    private static bool Alive(Mob mob) => !mob.destroyed && mob.life > 0;

    /// <summary>Each host frame; the hits are made here, outside the hero's own update.</summary>
    internal static void Poll()
    {
        if (run is null || finished) return;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (KirbyMod.Instance?.ActiveSession?.Hero is not { destroyed: false } hero || machine is null) return;
        if (!hero.awake || hero._level.game.hasCinematic()) return;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (frame - start.Value < 180) return;
        if (current >= trials.Length) { Finish(null); return; }
        var trial = trials[current];
        if (trial.Thorny is null) { Spawn(hero, trial, frame); return; }
        var thorny = trial.Thorny;
        if (trial.HitAt == 0)
        {
            if (frame - trial.SpawnedAt < 20) return;
            trial.Hittable = Alive(thorny) && thorny.canBeHitBy(hero);
            if (!trial.Hittable)
            {
                if (frame - trial.SpawnedAt > 300) Finish($"the {trial.Name} Thorny could not be hit");
                return;
            }
            // The Thorny faces away from Kirby: a hit from behind. Set again here, in the
            // frame of the hit, in case its sprite update turned it.
            thorny.dir = trial.Side;
            trial.DirAtHit = thorny.dir;
            trial.LifeBefore = hero.life;
            trial.ThornyLifeBefore = thorny.life;
            trial.DroppedBefore = dropped;
            trial.HitAt = frame;
            trial.HitLanded = KirbyDamage.Hit(hero, thorny, 3, fromHelper: trial.FromHelper);
            trial.ThornyLifeAfter = Alive(thorny) ? thorny.life : 0;
            return;
        }
        // Kirby's hurt reaches the host a source frame after the hit; read it after a while.
        if (!trial.Done && frame - trial.HitAt >= 90)
        {
            trial.LifeAfter = hero.life;
            trial.Dropped = dropped - trial.DroppedBefore;
            trial.Done = true;
            if (!thorny.destroyed) thorny.destroy();
        }
        // Past Kirby's hurt invulnerability, so the next trial is not shielded by it.
        if (trial.Done && frame - trial.HitAt >= 270) current++;
        else if (frame - trial.SpawnedAt > 900) Finish($"the {trial.Name} trial did not finish");
    }

    private static void Spawn(Hero hero, Trial trial, uint frame)
    {
        // Other hostile enemies near Kirby could hurt him and blur the reading.
        var entities = hero._level.entities;
        double heroX = (hero.cx + hero.xr) * 24;
        for (int i = entities.length - 1; i >= 0; i--)
            if (entities.getDyn(i) is Mob other && !other.destroyed && other.isOpponent(hero) &&
                Math.Abs((other.cx + other.xr) * 24 - heroX) < 600)
            { other.destroy(); trial.OthersRemoved++; }
        trial.Side = hero.dir < 0 ? -1 : 1;
        var thorny = SpikedSatyr.Class.create(hero._level, hero.cx + trial.Side * 3, hero.cy, 1, 1);
        thorny.setPosPixel(heroX + trial.Side * Distance, (hero.cy + hero.yr) * 24);
        if (thorny.yr == 0) { thorny.cy--; thorny.yr = 1; }
        // Its AI would turn it and could attack Kirby itself; only its answer to a hit is under test.
        thorny.lockAiS(60);
        trial.Thorny = thorny;
        trial.SpawnedAt = frame;
    }

    private static void Finish(string? reason)
    {
        finished = true;
        failure = reason;
        bool Kirby(Trial t) => t.Done && t.HitLanded && t.Dropped == 0 && t.LifeAfter < t.LifeBefore;
        bool Helper(Trial t) => t.Done && t.HitLanded && t.Dropped == 1 && t.LifeAfter >= t.LifeBefore;
        File.WriteAllText(Path.Combine(run!, "thorny.json"), JsonSerializer.Serialize(new
        {
            failure,
            trials = trials.Select(t => new
            {
                t.Name, t.FromHelper, t.Side, t.DirAtHit, t.Hittable, t.HitLanded, t.OthersRemoved,
                t.LifeBefore, t.LifeAfter, t.ThornyLifeBefore, t.ThornyLifeAfter, countersDropped = t.Dropped, t.Done,
            }),
            passed = failure is null && Kirby(trials[0]) && Helper(trials[1]) && Kirby(trials[2]),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
