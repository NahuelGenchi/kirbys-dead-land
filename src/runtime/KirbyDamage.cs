using dc.en;
using dc.tool.atk;

namespace Kirby.Runtime;

/// <summary>Apply exact adapter damage (original, scaled as colorless gear) after the host resolves target validity and blocking.</summary>
internal static class KirbyDamage
{
    private static readonly Dictionary<nint, int> sourceHits = [];
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
    public static void Hit(Hero hero, dc.Entity mob, int damage)
    {
        var attack = AttackUtils.Class.createFromHero(hero, damage, null);
        attack.useHeroScaling = false;
        sourceHits.Add(attack.HashlinkPointer, damage);
        try { AttackUtils.Class.hit(attack, mob); }
        finally { sourceHits.Remove(attack.HashlinkPointer); }
    }
}
