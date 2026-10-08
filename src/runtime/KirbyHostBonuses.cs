using dc.en;
using dc.tool.atk;

namespace Kirby.Runtime;

/// <summary>
/// The host's own damage mutations for Kirby's copy attacks (#91). Dead Cells
/// treats Kirby's copy hit as an ordinary melee hit, so its on-hit effects run, but
/// the damage the mod applies replaces the host's number, which drops every
/// mutation multiplier. <c>HeroAttackSource.computeExtraBonusMul</c> (the perks and
/// aspects product) reads only the hero's inventory and status effects (Tranquility,
/// Combo, Vengeance, Tainted Flask, Extended Healing, Gastronomy, Point Blank, Demon
/// Strength), so asking it for a throwaway attack gives the factor the host would
/// have applied. The factor goes into the source-HP pairing like a crit, so
/// lethality stays in agreement.
///
/// Not asked: <c>computeBonusMul</c>, which also consumes one-shot status effects
/// (the Counterattack bonus) whether or not a hit lands.
/// </summary>
internal sealed class KirbyHostBonuses
{
    /// <summary>Host attack tag 14, which Point Blank needs; the hero must also be within 4 cells of the target.</summary>
    private const int CloseRangeTag = 14;
    private readonly Hero hero;
    private readonly AttackData attack;

    private KirbyHostBonuses(Hero hero, AttackData attack)
    {
        this.hero = hero;
        this.attack = attack;
    }

    /// <summary>One throwaway attack for a whole source tick.</summary>
    public static KirbyHostBonuses Begin(Hero hero)
    {
        var attack = AttackUtils.Class.createFromHero(hero, 1, null);
        // A copy attack counts as the close-range kind for Point Blank; the host
        // adds its bonus only when the hero is within 4 cells of the target.
        attack.addTag(CloseRangeTag);
        return new KirbyHostBonuses(hero, attack);
    }

    /// <summary>The host's mutation damage factor against <paramref name="mob"/>; 1 when no mutation applies.</summary>
    public double Factor(dc.Entity mob)
    {
        double factor = hero.attackSource.computeExtraBonusMul(attack, mob);
        return double.IsFinite(factor) && factor > 0 ? factor : 1;
    }
}
