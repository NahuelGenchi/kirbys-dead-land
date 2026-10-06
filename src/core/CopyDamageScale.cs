namespace Kirby.Core;

/// <summary>
/// Deliberate scaling of copy-ability damage dealt to host targets. A multiplier
/// of 1 keeps exact original damage. The paired source enemy receives host life
/// divided by the multiplier, so a source hit is lethal exactly when its scaled
/// host damage is and original death reactions (Ice freeze, star) stay in the
/// same tick as the host kill.
/// </summary>
public static class CopyDamageScale
{
    /// <summary>Health given to the paired source enemy for this multiplier.</summary>
    public static int SourceHp(int hostLife, double multiplier, int maxSourceHp)
    {
        Validate(multiplier);
        return (int)Math.Clamp(Math.Ceiling(hostLife / multiplier), 1, maxSourceHp);
    }

    /// <summary>
    /// Scale one source HP delta for the host. A hit the source enemy survives
    /// never kills the host enemy.
    /// </summary>
    public static int HostDamage(int sourceDamage, int sourceHp, int hostLife, double multiplier)
    {
        Validate(multiplier);
        if (sourceDamage <= 0) return 0;
        double scaled = Math.Round(sourceDamage * multiplier, MidpointRounding.AwayFromZero);
        int damage = (int)Math.Min(scaled, int.MaxValue);
        return sourceDamage < sourceHp ? Math.Min(damage, Math.Max(hostLife - 1, sourceDamage)) : damage;
    }

    private static void Validate(double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier < 1)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "Copy damage multipliers must be finite and at least 1.");
    }
}
