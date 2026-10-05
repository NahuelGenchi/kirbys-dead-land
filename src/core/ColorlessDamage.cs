namespace Kirby.Core;

/// <summary>
/// Copy-ability damage treated as Dead Cells colorless gear. The host gives a
/// colorless item its owner's highest scroll tier (Entity.getRelevantTierFor)
/// and scales damage with Const.scaleHeroValueToTier, damage × 1.15^(tier − 1),
/// truncated by AttackUtils.computeFinalDmg. Each tier is 1 + that stat's scroll
/// count (Hero.computeTiers), so no scrolls keeps exact original damage.
/// </summary>
public static class ColorlessDamage
{
    public static int Tier(int brutality, int tactics, int survival) => Math.Max(brutality, Math.Max(tactics, survival));

    /// <summary>Platform-independent copy of Const.scaleHeroValueToTier(1, tier).</summary>
    public static double Multiplier(int tier) => Math.Pow(1.15, tier - 1);

    /// <summary>
    /// Health given to the paired source enemy: the smallest source damage whose
    /// truncated host damage is lethal, so a source hit is lethal exactly when
    /// its scaled host damage is. Division alone can be off by one in floating
    /// point (69 / 1.15 is slightly above 60, yet 60 × 1.15 kills 69).
    /// </summary>
    public static int SourceHp(int hostLife, double multiplier, int maxSourceHp)
    {
        Validate(multiplier);
        long hp = Math.Max(1, (long)Math.Ceiling(hostLife / multiplier));
        while (hp > 1 && Scale((int)Math.Min(hp - 1, int.MaxValue), multiplier) >= hostLife) hp--;
        while (Scale((int)Math.Min(hp, int.MaxValue), multiplier) < hostLife) hp++;
        return (int)Math.Clamp(hp, 1, maxSourceHp);
    }

    /// <summary>
    /// Scale one source HP drop like the host scales colorless damage. A hit the
    /// source enemy survives never kills the host enemy, and a lethal source hit
    /// always does, so original death reactions (Ice freeze, star) stay in the
    /// same tick as the host kill despite floating-point rounding.
    /// </summary>
    public static int HostDamage(int sourceDamage, int sourceHp, int hostLife, double multiplier)
    {
        Validate(multiplier);
        if (sourceDamage <= 0) return 0;
        int damage = Scale(sourceDamage, multiplier);
        return sourceDamage < sourceHp ? Math.Min(damage, hostLife - 1) : Math.Max(damage, hostLife);
    }

    /// <summary>Std.int(damage × multiplier), as AttackUtils.computeFinalDmg does.</summary>
    private static int Scale(int damage, double multiplier) => (int)Math.Min(damage * multiplier, int.MaxValue);

    private static void Validate(double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "A colorless multiplier must be finite and positive.");
    }
}
