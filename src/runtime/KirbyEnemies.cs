using Entity = dc.Entity;
using dc.en;
using dc.en.inter;
using dc.tool.atk;
using HaxeProxy.Runtime;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Pairs live host enemies and hostile projectiles with original enemy objects
/// for source collision and capture logic.
/// </summary>
public sealed class KirbyEnemies
{
    internal const int MaxSourceHp = 32760;
    private const int Capacity = 31;
    private readonly Entity?[] mobs = new Entity?[Capacity + 1];
    private readonly int[] assignedHp = new int[Capacity + 1];
    private readonly bool[] detached = new bool[Capacity + 1];
    private readonly bool[] controlled = new bool[Capacity + 1];
    private readonly byte[] abilities = new byte[Capacity + 1];
    /// <summary>Where each paired floor ability item lay before the original suction moved it.</summary>
    private readonly (int Cx, int Cy)[] itemOrigins = new (int, int)[Capacity + 1];
    /// <summary>Swallowing an Elite always yields original Sword.</summary>
    private static readonly byte EliteAbility = checked((byte)(Array.IndexOf(KirbyNdsProvider.Abilities, "Sword") + 2));
    // Sampled when source HP is assigned so the matching delta scales identically.
    private double damageMultiplier = 1;
    private EnemyBinding[]? restoring;
    private readonly KirbyStars stars = new();
    /// <summary>Floor ability items the original inhale put in Kirby's mouth this source tick.</summary>
    internal readonly List<KirbyAbilityItems.Held> Swallowed = [];
    /// <summary>Highest scroll tier sampled for the current source tick.</summary>
    internal int ColorlessTier { get; private set; } = 1;
    /// <summary>The host's colorless damage multiplier for <see cref="ColorlessTier"/>.</summary>
    internal double ColorlessMultiplier { get; private set; } = 1;
    /// <summary>Lab-only observer of (target, source HP drop, host damage) for each copy hit.</summary>
    internal static Action<Entity, int, int>? TestObserver;
    /// <summary>Lab-only: host entities kept without a source object, as if out of range.</summary>
    internal static Func<Entity, bool>? TestUnpaired;
    /// <summary>
    /// Host life per source damage point: the original-unit scale, times the Elite
    /// empowerment, times the colorless scroll multiplier, all sampled in Prepare.
    /// </summary>
    internal double HostMultiplier(Func<Entity, double> scale, Entity mob) =>
        scale(mob) * damageMultiplier * ColorlessMultiplier;
    /// <summary>The host enemy has a live source object (not a detached corpse).</summary>
    internal bool Pairs(Entity mob)
    {
        for (int i = 1; i <= Capacity; i++)
            if (mobs[i]?.HashlinkPointer == mob.HashlinkPointer) return true;
        return false;
    }
    internal EnemyBinding[] Capture() => Enumerable.Range(1, Capacity)
        .Where(i => detached[i] || mobs[i] is { destroyed: false }).Select(i => detached[i]
            ? new EnemyBinding { Slot = i, Detached = true }
            : new EnemyBinding { Slot = i, Uid = mobs[i]!.__uid, Type = Kind(mobs[i]!), Ability = abilities[i],
                X = (mobs[i]!.cx + mobs[i]!.xr) * 24, Y = (mobs[i]!.cy + mobs[i]!.yr) * 24 }).ToArray();
    internal void Restore(EnemyBinding[] bindings)
    {
        Array.Clear(mobs); Array.Clear(controlled); Array.Clear(abilities); Array.Clear(detached); Array.Clear(assignedHp); restoring = bindings;
    }
    public bool Controls(Entity mob) => Enumerable.Range(1, Capacity).Any(i => controlled[i] && mobs[i]?.HashlinkPointer == mob.HashlinkPointer);
    public void Clear(NdsRuntime nds)
    {
        for (uint i = 1; i <= Capacity; i++) Release(nds, i);
        stars.Clear();
        Swallowed.Clear();
    }
    private void Release(NdsRuntime nds, uint i)
    {
        nds.SetProxy(i, 0, 0, 1, false);
        mobs[i] = null; controlled[i] = false; abilities[i] = 0; assignedHp[i] = 0; detached[i] = false;
    }
    /// <param name="scale">Host life per original damage point for a target.</param>
    /// <param name="bodyContact">The original touch damage between Kirby and enemies: off by default
    /// (Dead Cells has none) and while Kirby stomps, as the host dive lands on enemies; his own
    /// attacks, inhale and slide still hit them.</param>
    public void Prepare(KirbySession session, NdsRuntime nds, int ox, double oy, Func<Entity, double> scale, bool bodyContact = true)
    {
        var hero = session.Hero;
        if (restoring is not null)
        {
            foreach (var binding in restoring)
            {
                if (binding.Slot < 1 || binding.Slot > Capacity) throw new InvalidDataException("Invalid saved source enemy slot.");
                if (binding.Detached) { detached[binding.Slot] = true; continue; }
                for (int j = 0; j < hero._level.entities.length; j++)
                {
                    if (hero._level.entities.getDyn(j) is not Entity candidate || !Live(candidate) ||
                        Kind(candidate) != binding.Type || mobs.Any(m => m?.HashlinkPointer == candidate.HashlinkPointer)) continue;
                    double x = (candidate.cx + candidate.xr) * 24, y = (candidate.cy + candidate.yr) * 24;
                    // Hxbit may regenerate __uid when loading. The host's saved
                    // type and exact world position identify the restored object.
                    if (Math.Abs(x - binding.X) > 0.01 || Math.Abs(y - binding.Y) > 0.01) continue;
                    mobs[binding.Slot] = candidate;
                    itemOrigins[binding.Slot] = (candidate.cx, candidate.cy);
                    abilities[binding.Slot] = KirbyAbilityItems.IsAbilityDrop(candidate) ? KirbyAbilityItems.CopyByte(candidate) :
                        candidate is not Mob ? (byte)0 :
                        binding.Ability is >= 2 and <= 25 ? binding.Ability : RollAbility(session);
                    controlled[binding.Slot] = (candidate is Mob and not dc.en.mob.Boss || IsHazard(candidate)) &&
                        nds.Proxy((uint)binding.Slot).Controlled;
                    break;
                }
                if (mobs[binding.Slot] is null) Release(nds, (uint)binding.Slot);
            }
            restoring = null;
        }
        // Copy attacks scale like colorless gear: the highest scroll tier,
        // passed through the host's own Const.scaleHeroValueToTier.
        ColorlessTier = ColorlessDamage.Tier(hero.brutalityTier, hero.tacticTier, hero.survivalTier);
        ColorlessMultiplier = dc.Const.Class.scaleHeroValueToTier(1.0, ColorlessTier);
        damageMultiplier = session.Rules.DamageMultiplier;
        double hx = (hero.cx + hero.xr) * 24, hy = (hero.cy + hero.yr) * 24;
        for (uint i = 1; i <= Capacity; i++)
        {
            if (detached[i])
            {
                var remnant = nds.Proxy(i);
                if (remnant.Gone || remnant.Flags == 0) Release(nds, i);
                continue;
            }
            var mob = mobs[i];
            if (mob is null) continue;
            if (!Live(mob) || mob._level.HashlinkPointer != hero._level.HashlinkPointer)
            { Release(nds, i); continue; }
            // Only base Kirby can inhale an ability item; copy attacks never meet it.
            if (KirbyAbilityItems.IsAbilityDrop(mob) && !session.Rules.IsBaseForm && !controlled[i])
            { Release(nds, i); continue; }
            var distance = Distance(mob, hx, hy);
            if (!controlled[i] && (distance.X > 320 || distance.Y > 120)) Release(nds, i);
        }
        List<(Entity Mob, double Distance)> candidates = [];
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
        {
            if (entities.getDyn(j) is not Entity mob || !Live(mob)) continue;
            // A floor ability item is never hit; it is only there for base Kirby to inhale.
            if (KirbyAbilityItems.IsAbilityDrop(mob) ? !session.Rules.IsBaseForm :
                IsHazard(mob) ? !MouthfulHazards.CanPair(mob.isOpponent(hero), live: true, mob is Bullet { disabled: true })
                : !Eligible(mob) || !mob.canBeHitBy(hero)) continue;
            if (mobs.Any(m => m?.HashlinkPointer == mob.HashlinkPointer) || TestUnpaired?.Invoke(mob) == true) continue;
            var (x, y) = Distance(mob, hx, hy);
            if (x <= 300 && y <= 96) candidates.Add((mob, x * x + y * y));
        }
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var candidate in candidates)
        {
            int slot = Enumerable.Range(1, Capacity).FirstOrDefault(i => mobs[i] is null && !detached[i], -1);
            if (slot < 0) break;
            mobs[slot] = candidate.Mob;
            abilities[slot] = candidate.Mob is Mob ? RollAbility(session) : KirbyAbilityItems.CopyByte(candidate.Mob);
            itemOrigins[slot] = (candidate.Mob.cx, candidate.Mob.cy);
        }
        for (uint i = 1; i <= Capacity; i++)
        {
            if (mobs[i] is not { } mob) continue;
            // Original frames can also run without a normal step (a host cinematic
            // walk, such as claiming a scroll), so its source object may already be
            // swallowed or gone. Complete acknowledges that ending after this step;
            // the bridge refuses to rebind the slot before then.
            if (nds.Proxy(i) is { Swallowed: true } or { Gone: true }) continue;
            // Checked every tick: a host mob can also become an Elite after binding.
            if (mob is Mob { elite: true }) abilities[i] = EliteAbility;
            int x = checked((int)Math.Round(((mob.cx + mob.xr) * 24 - ox) * 65536));
            int y = checked((int)Math.Round(((mob.cy + mob.yr) * 24 - 17 - oy) * 65536));
            // A projectile's source object has one HP: any original attack that
            // strikes it is lethal and removes the host projectile. Enemies hold
            // the host life in original damage units, so both games agree on
            // which hit is lethal (death reactions); the Elite empowerment and
            // the colorless scroll multiplier apply on top.
            // A floor ability item takes no damage: its proxy keeps full HP.
            bool item = KirbyAbilityItems.IsAbilityDrop(mob);
            assignedHp[i] = IsHazard(mob) ? 1 : item ? MaxSourceHp :
                ColorlessDamage.SourceHp(mob.life, HostMultiplier(scale, mob), MaxSourceHp);
            nds.SetProxy(i, x, y, assignedHp[i]);
            // Projectiles stay inhalable; their zero copy byte leaves the original
            // copyless mouthful, which spits a star and swallows into nothing.
            // A floor ability item carries its ability's copy byte.
            nds.SetProxyOptions(i, abilities[i], !item && (mob is not Mob && !IsHazard(mob) || mob is dc.en.mob.Boss));
            nds.SetProxyContact(i, mob is Mob && bodyContact);
            nds.SetProxySize(i, (ushort)Math.Clamp(Math.Ceiling(Math.Max(mob.radius * 2, item ? 16 : 1)), 1, 1024),
                (ushort)Math.Clamp(Math.Ceiling(Math.Max(mob.hei, item ? 16 : 1)), 1, 1024));
        }
    }
    public string? Complete(KirbySession session, NdsRuntime nds, int ox, double oy, Func<Entity, double> scale)
    {
        string? copied = null;
        // Kirby's ability in the source step just run: Ice hits use the host freeze.
        byte sourceAbility = nds.State.Ability;
        stars.Scan(nds);
        for (uint i = 1; i <= Capacity; i++)
        {
            if (mobs[i] is not { } mob) continue;
            NdsProxy proxy = nds.Proxy(i);
            if (proxy.Pending) continue;
            if (IsHazard(mob)) { CompleteHazard(nds, i, mob, proxy, ox, oy); continue; }
            if (mob is ItemDrop drop) { copied = CompleteItem(session, nds, i, drop, proxy, ox, oy) ?? copied; continue; }
            // Cook can also change the source mouthful byte. Classify its
            // destruction before ordinary inhale so it cannot award a copy.
            if (proxy.Gone && (proxy.Flags & 128) != 0)
            {
                if (!mob.destroyed && mob.life > 0)
                {
                    if (mob is Mob cooked) AddCookFood(session, cooked);
                    mob.kill();
                }
                Release(nds, i); continue;
            }
            if (proxy.Swallowed && mob is Mob absorbed)
            {
                string abilityId = KirbyNdsProvider.Abilities[abilities[i] - 2];
                string? ability = session.CompleteAbsorption(absorbed, abilityId, () => { mob.kill(); return mob.life <= 0 || mob.destroyed; });
                if (ability is not null)
                {
                    copied = ability;
                }
                Release(nds, i);
                continue;
            }
            // Native HP follows the host's actual remaining life: an original
            // lethal hit removes the remaining host life, and a surviving
            // original enemy leaves the host enemy alive.
            int damage = assignedHp[i] - proxy.Hp;
            if (damage > 0 && !mob.destroyed && mob.life > 0)
            {
                int hostDamage = Math.Max(1, ColorlessDamage.HostDamage(damage, assignedHp[i], mob.life, HostMultiplier(scale, mob)));
                TestObserver?.Invoke(mob, damage, hostDamage);
                // A spit star's original hit (and burst) on its source object also staggers the host enemy.
                if (mob is Mob struck && stars.SourceStrike(proxy.Entity, mob, ox, oy, out int direction) is var star and not StarKind.None)
                {
                    KirbyStars.TestObserver?.Invoke(star, mob, hostDamage, true);
                    KirbyStars.Stagger(struck, direction, () => KirbyDamage.Hit(session.Hero, mob, hostDamage));
                }
                // Only a hit the host resolved (not blocked or parried) freezes.
                else if (KirbyDamage.Hit(session.Hero, mob, hostDamage, frozenSafe: IceFreeze.Applies(sourceAbility)) &&
                    mob is Mob chilled && IceFreeze.Freezes(sourceAbility, damage, !mob.destroyed && mob.life > 0,
                    mob.hasAffect(IceFreeze.FreezeAffect))) Freeze(chilled);
            }
            if (mob.destroyed || mob.life <= 0)
            {
                if (!proxy.Gone && mob is Mob and not dc.en.mob.Boss && !IceFreeze.Applies(sourceAbility))
                {
                    // Loot/death belong to the host; the original defeated
                    // object's star or other reaction keeps running. Reserve
                    // the slot until its native destructor completes. An Ice
                    // kill is retired at once instead: no original ice block.
                    nds.DetachProxy(i);
                    mobs[i] = null; controlled[i] = false; detached[i] = true;
                }
                else Release(nds, i);
                continue;
            }
            if (proxy.Gone) { Release(nds, i); continue; }
            bool sourceControls = proxy.Controlled && mob is Mob and not dc.en.mob.Boss;
            if (sourceControls)
            {
                var enemy = (Mob)mob;
                if (!controlled[i]) enemy.interruptSkills();
                enemy.lockAiS(0.1);
                enemy.dx = enemy.dy = 0;
                enemy.setPosPixel(proxy.XRaw / 65536.0 + ox, proxy.YRaw / 65536.0 + oy + 17);
                enemy.fallStartY = (enemy.cy + enemy.yr) * 24;
                if (enemy.yr == 0) { enemy.cy--; enemy.yr = 1; }
                enemy.spriteUpdate();
            }
            controlled[i] = sourceControls;
        }
        stars.StrikeHost(session.Hero, this, ox, oy, scale);
        return copied;
    }
    /// <summary>
    /// The host Frost Blast's freeze (tool.weap.Freeze.onExecute): affect 23 for
    /// 3 s times the host's diminishing factor. Host resistances apply inside
    /// setAffectS, and the host thaw adds its own slow and stun.
    /// </summary>
    private static void Freeze(Mob mob)
    {
        double factor = mob.getDiminishingFactor(IceFreeze.DiminishingKey, IceFreeze.DiminishingFullPowerUses,
            IceFreeze.DiminishingMaxUses, null);
        if (factor > 0) mob.setAffectS(IceFreeze.FreezeAffect, IceFreeze.FreezeSeconds * factor, Ref<double>.Null, null);
    }
    private void CompleteHazard(NdsRuntime nds, uint i, Entity projectile, NdsProxy proxy, int ox, double oy)
    {
        var action = MouthfulHazards.Resolve(proxy.Swallowed, proxy.Gone && (proxy.Flags & 128) != 0, proxy.Gone,
            assignedHp[i] - proxy.Hp, proxy.Controlled);
        if (action != HazardAction.Remove && projectile.destroyed) action = HazardAction.Release;
        switch (action)
        {
            case HazardAction.Remove:
                // No copy, loot or explosion: the original mouthful now holds it.
                if (!projectile.destroyed) projectile.destroy();
                Release(nds, i);
                break;
            case HazardAction.Release:
                Release(nds, i);
                break;
            case HazardAction.Hold:
                // KirbyEnemyHooks suspends its host update, so it can neither hit
                // Kirby nor detonate while the original suction carries it.
                projectile.setPosPixel(proxy.XRaw / 65536.0 + ox, proxy.YRaw / 65536.0 + oy + 17);
                projectile.spriteUpdate();
                controlled[i] = true;
                break;
            default:
                controlled[i] = false;
                break;
        }
    }
    /// <summary>
    /// A floor ability item follows the original suction, and the original
    /// inhale takes it off the floor into Kirby's mouth. It is never damaged;
    /// any other end of its proxy (for example Cook) just releases the slot.
    /// </summary>
    private string? CompleteItem(KirbySession session, NdsRuntime nds, uint i, ItemDrop drop, NdsProxy proxy, int ox, double oy)
    {
        if (drop.destroyed || !KirbyAbilityItems.IsAbilityItem(drop.item)) { Release(nds, i); return null; }
        if (proxy.Swallowed)
        {
            string? copied = abilities[i] is >= 2 and <= 25 &&
                session.CompleteItemAbsorption(drop, KirbyNdsProvider.Abilities[abilities[i] - 2]) is { } ability ? ability : null;
            if (copied is not null)
            {
                Swallowed.Add(new KirbyAbilityItems.Held(drop.item, itemOrigins[i].Cx, itemOrigins[i].Cy, drop._level.HashlinkPointer));
                drop.destroy();
            }
            Release(nds, i);
            return copied;
        }
        if (proxy.Gone) { Release(nds, i); return null; }
        // It follows only the original suction. A hit (a spit star, a slide)
        // gives its proxy an original recoil, which must not move the item.
        bool inhaled = proxy.Controlled && proxy.Inhaling;
        controlled[i] = inhaled;
        // Where it lies until the original suction takes hold of it.
        if (!inhaled) itemOrigins[i] = (drop.cx, drop.cy);
        if (inhaled)
        {
            drop.dx = drop.dy = 0;
            drop.setPosPixel(proxy.XRaw / 65536.0 + ox, proxy.YRaw / 65536.0 + oy + 17);
            drop.spriteUpdate();
        }
        return null;
    }
    private static bool IsHazard(Entity entity) => entity is Bullet or Grenade;
    private static bool Live(Entity entity) => !entity.destroyed && (entity.life > 0 || IsHazard(entity) || entity is ItemDrop);
    /// <summary>
    /// Cook's requested host reward: one host food item joins the cooked
    /// enemy's own loot, so the host death path drops it at that enemy.
    /// </summary>
    private static void AddCookFood(KirbySession session, Mob mob)
    {
        if (!session.Rules.ClaimCookFood(mob.__uid, mob is dc.en.mob.Boss, mob.elite)) return;
        var food = dc.tool.InventItem.Class.fromItem(dc.level.LootGen.Class.getSmallHealingKind(), 1);
        mob.addToLoot(new dc.LootType.InvItem(food), Ref<bool>.In(false));
    }
    // Gold deposits (PileOfGold, GoldNugget) have host life and drop gold in onDamage, like breakable props.
    private static bool Eligible(Entity entity) =>
        entity is Mob or Door or BreakableProp or ExplosiveBarrel or PileOfGold or GoldNugget || entity.isBreakableByHero();
    private static (double X, double Y) Distance(Entity entity, double heroX, double heroY) =>
        (Math.Max(0, Math.Abs((entity.cx + entity.xr) * 24 - heroX) - entity.radius),
         Math.Max(0, Math.Abs((entity.cy + entity.yr) * 24 - entity.hei / 2 - (heroY - 14.5)) - (entity.hei + 29) / 2));
    private static string Kind(Entity entity) => entity is Mob mob ? mob.type.ToString() : "entity:" + entity.GetType().FullName;
    private static byte RollAbility(KirbySession session) => checked((byte)(Array.IndexOf(KirbyNdsProvider.Abilities,
        session.Rules.SelectCopyAbility()) + 2));
}

internal sealed class EnemyBinding
{
    public int Slot { get; set; }
    public int Uid { get; set; }
    public string Type { get; set; } = "";
    public byte Ability { get; set; }
    public bool Detached { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
