using System.Buffers.Binary;
using dc.en;
using HaxeProxy.Runtime;
using Kirby.Core;
using Entity = dc.Entity;

namespace Kirby.Runtime;

/// <summary>
/// Kirby's spat and discarded stars as host projectiles. The ROM spawns, moves and
/// removes each star through the injected host terrain (spit stars burst on walls,
/// the ability star bounces); after every source frame this reads the stars'
/// original hit boxes and strikes host enemies with them.
/// </summary>
internal sealed unsafe class KirbyStars
{
    private const uint RamStart = 0x02000000, RamEnd = 0x02400000;
    // The native hit list holds every active original collision record.
    private readonly NdsHit[] records = new NdsHit[512];
    private readonly Dictionary<uint, uint> updates = [];
    private readonly Dictionary<uint, Star> stars = [];
    // Union of each other original object's records: a source enemy's body.
    private readonly Dictionary<uint, StarBox> bodies = [];
    private readonly Dictionary<uint, double> lastX = [];
    private readonly StarStrikes strikes = new();
    /// <summary>Lab-only observer of (star kind, target, host damage, struck through its source enemy).</summary>
    internal static Action<StarKind, Entity, int, bool>? TestObserver;
    /// <summary>Lab-only observer of each star's host box (pixels) after every source frame.</summary>
    internal static Action<uint, StarKind, StarBox>? TestTrace;

    private readonly record struct Star(uint Address, StarKind Kind, StarBox Box, int Damage, int Travel);

    public void Clear()
    {
        stars.Clear(); bodies.Clear(); lastX.Clear(); strikes.Clear();
    }

    /// <summary>Read the stars and source bodies of the source frame just stepped.</summary>
    public void Scan(NdsRuntime nds)
    {
        stars.Clear(); bodies.Clear(); updates.Clear();
        int count = nds.ReadHits(records);
        Span<byte> word = stackalloc byte[4];
        for (int i = 0; i < count; i++)
        {
            ref NdsHit hit = ref records[i];
            uint owner = hit.Owner;
            if (owner < RamStart || owner >= RamEnd - 0x34) continue;
            if (!updates.TryGetValue(owner, out uint update))
            {
                nds.Read(owner + 0x30, word);
                updates[owner] = update = BinaryPrimitives.ReadUInt32LittleEndian(word);
            }
            var box = new StarBox(hit.Bounds[0], hit.Bounds[1], hit.Bounds[2], hit.Bounds[3]);
            var kind = StarProjectiles.Identify(update);
            if (kind == StarKind.None)
            {
                bodies[owner] = bodies.TryGetValue(owner, out var body) ? body.Union(box) : box;
                continue;
            }
            // Signed original damage word; a spit star carries 52 and 48 records.
            int damage = -(short)hit.Words[2];
            stars[owner] = stars.TryGetValue(owner, out var star)
                ? star with { Box = star.Box.Union(box), Damage = Math.Max(star.Damage, damage) }
                : new Star(owner, kind, box, damage, 0);
        }
        foreach (var star in stars.Values.ToArray())
        {
            double x = star.Box.CenterX;
            int travel = lastX.TryGetValue(star.Address, out double previous) ? Math.Sign(x - previous) : 0;
            stars[star.Address] = star with { Travel = travel };
            lastX[star.Address] = x;
        }
        foreach (uint gone in lastX.Keys.Where(address => !stars.ContainsKey(address)).ToArray()) lastX.Remove(gone);
        strikes.Retain(stars.Keys);
    }

    /// <summary>
    /// A paired enemy just lost source HP. If a spit star's original attack box
    /// touches its source body, that was the star: return its kind and the push
    /// direction for the stagger, once per star and enemy. A lethal hit removes the
    /// source body's records in the same frame, so the host-sized box the source
    /// object was given also counts.
    /// </summary>
    public StarKind SourceStrike(uint sourceEntity, Entity target, int ox, int oy, out int direction)
    {
        direction = 0;
        if (stars.Count == 0) return StarKind.None;
        var body = HostBody(target).Offset(-ox, -oy);
        if (bodies.TryGetValue(sourceEntity, out var source)) body = body.Union(source);
        foreach (var star in stars.Values)
        {
            if (StarProjectiles.Route(star.Kind, paired: true) != StarRoute.Source || !star.Box.Overlaps(body)) continue;
            direction = StarProjectiles.BumpDirection(star.Box.CenterX, body.CenterX, star.Travel);
            return strikes.TryStrike(star.Address, target.HashlinkPointer) ? star.Kind : StarKind.None;
        }
        return StarKind.None;
    }

    /// <summary>
    /// Strike the host enemies the source cannot: those with no source object, and
    /// every enemy the ability star crosses. Damage is the star's source damage,
    /// scaled like any other copy hit on that enemy.
    /// </summary>
    public void StrikeHost(Hero hero, KirbyEnemies enemies, int ox, int oy, Func<Entity, double> scale)
    {
        if (stars.Count == 0) return;
        var entities = hero._level.entities;
        foreach (var star in stars.Values)
        {
            var box = star.Box.Offset(ox, oy);
            TestTrace?.Invoke(star.Address, star.Kind, box);
            for (int j = 0; j < entities.length; j++)
            {
                if (entities.getDyn(j) is not Mob mob || mob.destroyed || mob.life <= 0 || !mob.canBeHitBy(hero)) continue;
                if (StarProjectiles.Route(star.Kind, enemies.Pairs(mob)) != StarRoute.Host || enemies.Controls(mob)) continue;
                if (!box.Overlaps(HostBody(mob)) || !strikes.TryStrike(star.Address, mob.HashlinkPointer)) continue;
                int sourceDamage = StarProjectiles.SourceDamage(star.Kind, star.Damage);
                if (sourceDamage <= 0) continue;
                // The same conversion a paired enemy's HP drop would get.
                double multiplier = enemies.HostMultiplier(scale, mob);
                int life = mob.life;
                int damage = Math.Max(1, ColorlessDamage.HostDamage(sourceDamage,
                    ColorlessDamage.SourceHp(life, multiplier, KirbyEnemies.MaxSourceHp), life, multiplier));
                TestObserver?.Invoke(star.Kind, mob, damage, false);
                Stagger(mob, StarProjectiles.BumpDirection(box.CenterX, (mob.cx + mob.xr) * 24, star.Travel),
                    () => KirbyDamage.Hit(hero, mob, damage));
            }
        }
    }

    /// <summary>Host hurt box in pixels: the size and foot alignment the source object is given.</summary>
    private static StarBox HostBody(Entity mob)
    {
        double x = (mob.cx + mob.xr) * 24, foot = (mob.cy + mob.yr) * 24;
        return new((int)Math.Floor(x - mob.radius), (int)Math.Floor(foot - mob.hei), (int)Math.Ceiling(x + mob.radius), (int)Math.Ceiling(foot));
    }

    /// <summary>
    /// The host dive landing's treatment of each enemy it hits: affect 16 and a
    /// horizontal bump away, the hit, then Stun with the host's diminishing returns.
    /// Host resistances (bosses, stun immunity) apply inside setAffectS and bump.
    /// </summary>
    internal static void Stagger(Mob mob, int direction, Func<bool> hit)
    {
        mob.setAffectS(StarProjectiles.StaggerAffect, StarProjectiles.StaggerSeconds, Ref<double>.Null, null);
        mob.bump(direction * StarProjectiles.BumpSpeed(Random.Shared.NextDouble()), 0, null);
        if (!hit() || mob.destroyed || mob.life <= 0) return;
        mob.setAffectS(StarProjectiles.StunAffect, StarProjectiles.StunSeconds * mob.getDiminishingFactor(9, 3, 6, null),
            Ref<double>.Null, null);
    }
}
