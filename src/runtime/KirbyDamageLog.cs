using System.Globalization;
using System.Text;
using dc.en;
using dc.tool.atk;

namespace Kirby.Runtime;

/// <summary>
/// One log line for every way Kirby loses health and every enemy his copy hits
/// kill, so an unexplained hit in normal play names its source (#78). Kirby's
/// damage is rare, so this stays on outside the lab.
/// </summary>
internal static unsafe class KirbyDamageLog
{
    private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private static (string Type, int Uid, long Ms, double X, double Y)? lastKill;
    private static readonly NdsHit[] hits = new NdsHit[512];

    private static void Write(Func<string> text)
    {
        // Diagnostics must never interrupt the hit they describe.
        try { KirbyNdsProvider.Log?.Invoke("Kirby damage: " + text()); }
        catch (Exception error) { KirbyNdsProvider.Log?.Invoke("Kirby damage: log failed: " + error.Message); }
    }

    private static string Describe(dc.Entity? entity, Hero hero)
    {
        if (entity is null) return "none";
        double x = (entity.cx + entity.xr) * 24 - (hero.cx + hero.xr) * 24;
        double y = (entity.cy + entity.yr) * 24 - (hero.cy + hero.yr) * 24;
        return string.Create(CultureInfo.InvariantCulture,
            $"{entity.GetType().Name}#{entity.__uid} life={entity.life} destroyed={entity.destroyed} at=({x:0},{y:0})");
    }

    private static string SinceKill(Hero hero)
    {
        if (lastKill is not { } kill) return "no kill yet";
        double x = kill.X - (hero.cx + hero.xr) * 24, y = kill.Y - (hero.cy + hero.yr) * 24;
        return string.Create(CultureInfo.InvariantCulture,
            $"{clock.ElapsedMilliseconds - kill.Ms} ms after killing {kill.Type}#{kill.Uid} at=({x:0},{y:0})");
    }

    /// <summary>The managed hooks the host call came through (which host path delivered it).</summary>
    private static string Path()
    {
        var frames = new System.Diagnostics.StackTrace(0, false).GetFrames();
        var names = new List<string>();
        foreach (var frame in frames)
        {
            if (frame.GetMethod() is not { DeclaringType: { } type } method) continue;
            string owner = type.FullName ?? type.Name;
            if (!owner.StartsWith("Kirby.", StringComparison.Ordinal) && !owner.StartsWith("dc.", StringComparison.Ordinal)) continue;
            if (owner.StartsWith("Kirby.Runtime.KirbyDamageLog", StringComparison.Ordinal)) continue;
            names.Add(owner.Replace("Kirby.Runtime.", "") + "." + method.Name);
            if (names.Count == 14) break;
        }
        return string.Join(" < ", names);
    }

    internal static void Kill(dc.Entity mob)
    {
        lastKill = (mob.GetType().Name, mob.__uid, clock.ElapsedMilliseconds, (mob.cx + mob.xr) * 24, (mob.cy + mob.yr) * 24);
        Write(() => string.Create(CultureInfo.InvariantCulture,
            $"kill {mob.GetType().Name}#{mob.__uid} t={clock.ElapsedMilliseconds}"));
    }

    /// <summary>A host attack accepted for Kirby; it enters the next original tick as a hurt.</summary>
    internal static void HostAttack(Hero hero, AttackData attack, int sourceDamage)
    {
        Write(() => string.Create(CultureInfo.InvariantCulture,
            $"host hit t={clock.ElapsedMilliseconds} dmg={attack.finalDmg} source={sourceDamage} hit={attack.hitResult?.GetType().Name} " +
            $"dmgType={attack.dmgType?.GetType().Name} from {Describe(attack.source, hero)} carrier={Describe(attack.carrier, hero)} " +
            $"weapon={attack.sourceWeapon?.GetType().Name ?? "none"} " +
            $"hurtbox={System.Text.Json.JsonSerializer.Serialize(KirbyHurtbox.Diagnostics)}; {SinceKill(hero)}; via {Path()}"));
    }

    /// <summary>The original lowered Kirby's health. Names the original hit boxes touching him.</summary>
    internal static void SourceHit(Hero hero, NdsRuntime nds, int before, int after, dc.Entity? queued) =>
        Write(() => DescribeSourceHit(hero, nds, before, after, queued));

    private static string DescribeSourceHit(Hero hero, NdsRuntime nds, int before, int after, dc.Entity? queued)
    {
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"source hit t={clock.ElapsedMilliseconds} {before}->{after} queued={(queued is null ? "none" : Describe(queued, hero))}; {SinceKill(hero)}; touching:"));
        uint player = nds.State.Player;
        int count = nds.ReadHits(hits);
        (int L, int T, int R, int B)? body = null;
        for (int i = 0; i < count; i++)
            if (hits[i].Slot == 14 && hits[i].Owner == player)
            {
                var b = Bounds(ref hits[i]);
                body = body is { } u ? (Math.Min(u.L, b.L), Math.Min(u.T, b.T), Math.Max(u.R, b.R), Math.Max(u.B, b.B)) : b;
            }
        var owners = new Dictionary<uint, uint>();
        for (uint slot = 1; slot <= 31; slot++)
            if (nds.Proxy(slot) is { Entity: not 0 } proxy) owners[proxy.Entity] = slot;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].Owner == player || body is not { } k) continue;
            var b = Bounds(ref hits[i]);
            if (b.R < k.L - 8 || b.L > k.R + 8 || b.B < k.T - 8 || b.T > k.B + 8) continue;
            string owner = owners.TryGetValue(hits[i].Owner, out uint s) ? "proxy" + s : "0x" + hits[i].Owner.ToString("x8");
            text.Append(CultureInfo.InvariantCulture, $" [{owner} slot{hits[i].Slot} {b.L},{b.T},{b.R},{b.B}]");
        }
        if (body is { } kb) text.Append(CultureInfo.InvariantCulture, $" kirby={kb.L},{kb.T},{kb.R},{kb.B}");
        return text.ToString();
    }

    /// <summary>Host health fell outside both paths above (synced into the original as is).</summary>
    internal static void HostLife(Hero hero, int source, int host) =>
        Write(() => string.Create(CultureInfo.InvariantCulture,
            $"host life t={clock.ElapsedMilliseconds} {source}->{host} without an original hit; {SinceKill(hero)}"));

    private static (int L, int T, int R, int B) Bounds(ref NdsHit hit) =>
        (hit.Bounds[0], hit.Bounds[1], hit.Bounds[2], hit.Bounds[3]);
}
