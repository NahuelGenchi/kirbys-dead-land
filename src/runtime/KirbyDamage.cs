using dc.en;
using dc.tool.atk;
using Hashlink.Marshaling;
using Hashlink.Proxy.Values;
using HaxeProxy.Runtime;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>Apply exact adapter damage (original, scaled as colorless gear) after the host resolves target validity and blocking.</summary>
internal static class KirbyDamage
{
    private static readonly Dictionary<nint, int> sourceHits = [];
    /// <summary>Copy hits whose damage already includes a crit (#88); the host draws them as crits.</summary>
    private static readonly HashSet<nint> criticalHits = [];
    /// <summary>The enemy a hit by the original CPU helper is landing on while the host resolves it, else 0 (#206).</summary>
    private static nint helperTarget;
    /// <summary>Lab-only observer of each Thorny counter dropped because a helper's hit raised it.</summary>
    internal static Action<AttackData>? TestDropped;
    /// <summary>
    /// A Thorny's spikes raised inside a hit the helper landed on it (#206). The host
    /// applies that hit as Kirby's, so the Thorny answers Kirby, judging the side from
    /// his place; the answer is not Kirby's to take. Its only counter-attack among the
    /// host's enemy hit handlers (applyAttackResult, onDamage, onHit*, onHurt*, onDie).
    /// </summary>
    internal static bool IsHelperCounter(dc.Entity? source) =>
        source is dc.en.mob.SpikedSatyr && HelperDamage.IsCounter(helperTarget, source.HashlinkPointer);
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
    /// <param name="fromHelper">The original CPU helper landed the hit, not Kirby (#206): the target's counter-attack
    /// at its attacker, which the host takes for Kirby, is dropped (see <see cref="IsHelperCounter"/>).</param>
    /// <returns>True when the host resolved a successful hit (not blocked, parried or missed).</returns>
    public static bool Hit(Hero hero, dc.Entity mob, int damage, bool frozenSafe = false, bool critical = false, bool fromHelper = false)
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
        nint outer = helperTarget;
        if (fromHelper) helperTarget = mob.HashlinkPointer;
        try { AttackUtils.Class.hit(attack, mob); }
        finally { sourceHits.Remove(attack.HashlinkPointer); criticalHits.Remove(attack.HashlinkPointer); helperTarget = outer; }
        if (alive && (mob.destroyed || mob.life <= 0)) KirbyDamageLog.Kill(mob);
        return attack.isSuccess();
    }
}
