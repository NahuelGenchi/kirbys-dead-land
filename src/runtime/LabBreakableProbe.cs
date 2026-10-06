using System.Text.Json;
using dc.en.inter;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in copy attack against a genuine generated level door, or with
/// KIRBY_LAB_BREAKABLE_TARGET=gold a real host PileOfGold spawned beside Kirby.
/// </summary>
internal static class LabBreakableProbe
{
    private static readonly bool Gold = Environment.GetEnvironmentVariable("KIRBY_LAB_BREAKABLE_TARGET") == "gold";
    private static string? run;
    private static uint? setupStart, start;
    private static bool fed;
    private static dc.Entity? target;
    private static int initialLife, damage, goldDrops;
    private static uint lastFrame;

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        Hook_Door.onDamage += (orig, door, attack) => { orig(door, attack); Damaged(door, attack); };
        Hook_PileOfGold.onDamage += (orig, pile, attack) => { orig(pile, attack); Damaged(pile, attack); };
        Hook_PileOfGold.dropLoot += (orig, pile, x, y, power) =>
        {
            if (target?.HashlinkPointer == pile.HashlinkPointer) goldDrops++;
            orig(pile, x, y, power);
        };
        KirbyInput.TestOverride = Input;
    }

    private static void Damaged(dc.Entity entity, dc.tool.atk.AttackData attack)
    {
        if (target?.HashlinkPointer != entity.HashlinkPointer) return;
        damage += Math.Max(0, attack.inflictedDmg);
        System.IO.File.AppendAllText(Path.Combine(run!, "breakable-hits.jsonl"), JsonSerializer.Serialize(new
        {
            frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame,
            attack.finalDmg, attack.inflictedDmg, entity.life, broken = (entity as Door)?.broken,
        }) + "\n");
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        if (machine.State.Ability != 18)
        {
            if (machine.State.Ability != 1) { setupStart = null; return 4; }
            setupStart ??= machine.State.Frame;
            if (machine.State.Frame - setupStart.Value < 30) return 2;
            if (!fed) { fed = machine.Feed(18); if (!fed) return 2; }
            return 128;
        }
        if (start is null)
        {
            var hero = session.Hero;
            if (Gold)
            {
                // Constructed and initialized as level markers spawn it.
                var pile = new PileOfGold(hero._level, hero.cx, hero.cy);
                pile.init();
                target = pile;
            }
            else target = Enumerable.Range(0, hero._level.entities.length)
                .Select(i => hero._level.entities.getDyn(i)).OfType<Door>()
                .Where(d => !d.destroyed && d.life > 0 && d._breakable && !d.locked && !d.broken)
                .OrderBy(d => Math.Abs(d.cx - hero.cx) + Math.Abs(d.cy - hero.cy)).FirstOrDefault();
            if (target is null) return 0;
            initialLife = target.life;
            hero.setPosPixel((target.cx + target.xr) * 24 - 28, (target.cy + target.yr) * 24);
            if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
            start = machine.State.Frame;
        }
        uint elapsed = machine.State.Frame - start.Value;
        // Copy acquisition changes the ability before its original transform
        // animation releases control. Let it finish before pressing attack.
        if (elapsed is >= 100 and < 115) return 16;
        if (elapsed is >= 120 and < 250) return 2;
        // The pile takes one life per hit: keep tapping attack until it breaks.
        if (Gold && elapsed is >= 250 and < 1000) return (ushort)((elapsed / 8) % 2 == 0 ? 2 : 0);
        return 0;
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run is null || session is null || machine is null || lastFrame == machine.State.Frame) return;
        lastFrame = machine.State.Frame;
        var proxies = Enumerable.Range(1, 31).Select(slot =>
        {
            var proxy = machine.Proxy((uint)slot);
            return new { slot, proxy.Entity, proxy.Flags, proxy.Hp, x = proxy.XRaw / 65536.0, y = proxy.YRaw / 65536.0 };
        }).Where(p => p.Entity != 0).ToArray();
        System.IO.File.AppendAllText(Path.Combine(run, "breakable-trace.jsonl"), JsonSerializer.Serialize(new
        {
            frame = lastFrame,
            heroX = (session.Hero.cx + session.Hero.xr) * 24,
            heroY = (session.Hero.cy + session.Hero.yr) * 24,
            x = target is null ? 0 : (target.cx + target.xr) * 24,
            y = target is null ? 0 : (target.cy + target.yr) * 24,
            radius = target?.radius, height = target?.hei,
            canBeHit = target?.canBeHitBy(session.Hero),
            proxies,
        }) + "\n");
        System.IO.File.WriteAllText(Path.Combine(run, "breakable.json"), JsonSerializer.Serialize(new
        {
            frame = lastFrame, sourceAbility = machine.State.Ability,
            target = target?.GetType().FullName, targetUid = target?.__uid,
            initialLife, life = target?.life, destroyed = target?.destroyed,
            broken = (target as Door)?.broken, damage, goldDrops,
            passed = target is not null && damage > 0 && ((target as Door)?.broken == true || target.life <= 0 || target.destroyed),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
