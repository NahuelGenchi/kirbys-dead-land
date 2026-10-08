using dc.en;
using dc.tool.atk;
using Hashlink.Marshaling;
using Hashlink.Proxy.Values;
using HaxeProxy.Runtime;

namespace Kirby.Runtime;

/// <summary>Apply exact adapter damage (original, scaled as colorless gear) after the host resolves target validity and blocking.</summary>
internal static class KirbyDamage
{
    private static readonly Dictionary<nint, int> sourceHits = [];
    /// <summary>Copy hits whose damage already includes a crit (#88); the host draws them as crits.</summary>
    private static readonly HashSet<nint> criticalHits = [];
    public static void Install()
    {
        dc.Hook_Entity.applyAttackResult += (orig, entity, attack) =>
        {
            if (sourceHits.TryGetValue(attack.HashlinkPointer, out int damage) && attack.isSuccess())
            {
                attack.finalDmg = damage;
                attack.rawFinalDmg = damage;
            }
            orig(entity, attack);
        };
        // The host resets hitResult to Normal when a hit starts, then lets the target
        // adjust it (blocks, parries); a hit that stays Normal becomes Critical here,
        // which gives the host's crit number and sound. The damage is already set.
        Hook_AttackTargetImpl.updateHitResult += (orig, target, hitResult, attack) =>
        {
            var result = orig(target, hitResult, attack);
            return criticalHits.Contains(attack.HashlinkPointer) && result is dc.HitResult.Normal ? Critical : result;
        };
        Hook_Mob.applyAttackResult += (orig, mob, attack) =>
        {
            if (sourceHits.TryGetValue(attack.HashlinkPointer, out int damage) && attack.isSuccess())
            {
                attack.finalDmg = damage;
                attack.rawFinalDmg = damage;
            }
            orig(mob, attack);
        };
    }
    private static dc.HitResult? criticalResult;
    /// <summary>
    /// The host's own Critical result: enum values are compared by identity, so a
    /// newly allocated Critical would not count (see <see cref="Frost"/>).
    /// </summary>
    private static dc.HitResult Critical => criticalResult ??= FindCritical();
    private static dc.HitResult FindCritical()
    {
        var type = HaxeProxyUtils.GetHashlinkType(typeof(dc.HitResult));
        int index = (int)dc.HitResult.Indexes.Critical;
        foreach (var global in HashlinkMarshal.Module.Globals)
            if (ReferenceEquals(global.Type, type) && global.Value is HashlinkEnum value && value.Index == index)
                return value.AsHaxe<dc.HitResult>();
        throw new InvalidOperationException("The host Critical hit result was not found.");
    }
    private static dc.DamageType? frost;
    /// <summary>
    /// The host's own Frost damage type. Haxe compares parameterless enum values
    /// by identity (MobAttackTarget.preHitResultUpdate checks against the global
    /// singleton), so a newly allocated Frost value would not count as Frost.
    /// </summary>
    internal static dc.DamageType Frost => frost ??= FindFrost();
    private static dc.DamageType FindFrost()
    {
        var type = HaxeProxyUtils.GetHashlinkType(typeof(dc.DamageType));
        int index = (int)dc.DamageType.Indexes.Frost;
        foreach (var global in HashlinkMarshal.Module.Globals)
            if (ReferenceEquals(global.Type, type) && global.Value is HashlinkEnum value && value.Index == index)
                return value.AsHaxe<dc.DamageType>();
        throw new InvalidOperationException("The host Frost damage type was not found.");
    }
    /// <param name="frozenSafe">Deal the hit as host Frost damage, which leaves a host freeze in place
    /// (Kirby's Ice); any other hit large enough shatters it, as host attacks do.</param>
    /// <returns>True when the host resolved a successful hit (not blocked, parried or missed).</returns>
    public static bool Hit(Hero hero, dc.Entity mob, int damage, bool frozenSafe = false, bool critical = false)
    {
        var attack = AttackUtils.Class.createFromHero(hero, damage, null);
        attack.useHeroScaling = false;
        // The copy's ability item is the attack's source item, so its affixes
        // (burn, bleed, poison, oil, freeze, on-kill effects…) apply as for a
        // weapon's hit (AttackUtils.createFromHeroItem) (#57).
        if (KirbyAbilityItems.CopyItem(hero) is { } item)
        {
            attack.useItemAffixes(item);
            attack.sourceItem = item;
        }
        if (frozenSafe) attack.dmgType = Frost;
        bool alive = !mob.destroyed && mob.life > 0;
        sourceHits.Add(attack.HashlinkPointer, damage);
        if (critical) criticalHits.Add(attack.HashlinkPointer);
        try { AttackUtils.Class.hit(attack, mob); }
        finally { sourceHits.Remove(attack.HashlinkPointer); criticalHits.Remove(attack.HashlinkPointer); }
        if (alive && (mob.destroyed || mob.life <= 0)) KirbyDamageLog.Kill(mob);
        return attack.isSuccess();
    }
}
