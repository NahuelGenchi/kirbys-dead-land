namespace Kirby.Core;

/// <summary>
/// Deliberate copy-ability damage scaling by the active Boss Cell count. The
/// host hero multiplier never applies to copy attacks; only this curve does.
/// Zero Boss Cells keeps exact original damage, and counts beyond the table use
/// its last entry.
/// </summary>
public sealed class CopyDamageCurve
{
    public static IReadOnlyList<double> Default { get; } = [1.0, 1.25, 1.6, 2.0, 2.5, 3.0];

    private readonly double[] multipliers;

    public CopyDamageCurve(IEnumerable<double>? multipliers = null)
    {
        this.multipliers = (multipliers ?? Default).ToArray();
        if (this.multipliers.Length == 0 || this.multipliers[0] != 1 ||
            this.multipliers.Any(m => !double.IsFinite(m)) ||
            this.multipliers.Zip(this.multipliers.Skip(1)).Any(pair => pair.Second < pair.First))
            throw new ArgumentException("Copy damage multipliers must start at 1, be finite and never decrease.",
                nameof(multipliers));
    }

    public double Multiplier(int bossCells) => multipliers[Math.Clamp(bossCells, 0, multipliers.Length - 1)];

    /// <summary>
    /// Health given to the paired source enemy, so a source hit is lethal
    /// exactly when its scaled host damage is.
    /// </summary>
    public int SourceHp(int hostLife, int bossCells, int maxSourceHp) =>
        (int)Math.Clamp(Math.Ceiling(hostLife / Multiplier(bossCells)), 1, maxSourceHp);

    /// <summary>
    /// Scale one source HP delta for the host. A hit the source enemy survives
    /// never kills the host enemy, so original death reactions (Ice freeze, star)
    /// stay in the same tick as the host kill.
    /// </summary>
    public int HostDamage(int sourceDamage, int sourceHp, int hostLife, int bossCells)
    {
        if (sourceDamage <= 0) return 0;
        double scaled = Math.Round(sourceDamage * Multiplier(bossCells), MidpointRounding.AwayFromZero);
        int damage = (int)Math.Min(scaled, int.MaxValue);
        return sourceDamage < sourceHp ? Math.Min(damage, Math.Max(hostLife - 1, sourceDamage)) : damage;
    }
}
