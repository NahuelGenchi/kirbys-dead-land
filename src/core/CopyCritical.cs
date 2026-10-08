namespace Kirby.Core;

/// <summary>
/// Kirby's critical hits (#88). This is a mod addition: the original game has no
/// crits and Dead Cells gives ability items the NoCritical tag. Kirby starts with
/// <see cref="BaseChance"/> (0 by default); timed bonuses from mutations add to it.
/// A crit multiplies one copy hit by <see cref="DamageMultiplier"/>.
///
/// The roll is made before the source step, per paired enemy, and folded into the
/// multiplier that pairs source HP with host life. A hit on a rolled tick is
/// therefore lethal in the source exactly when it is lethal in the host, like any
/// other copy hit. Rolling every tick is the same as rolling once per hit, because
/// the roll does not depend on whether a hit lands.
/// </summary>
public sealed class CopyCritical
{
    public const double DefaultDamageMultiplier = 1.5;
    private readonly List<Timed> chance = [];
    private readonly List<Timed> damage = [];
    private double guaranteeLeft;
    private double ownedChance;
    private double baseChance;
    private double baseMultiplier = DefaultDamageMultiplier;

    /// <summary>Crit chance without any timed bonus, from 0 to 1.</summary>
    public double BaseChance
    {
        get => baseChance;
        set => baseChance = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
    }

    /// <summary>Damage factor of a crit before timed damage bonuses; at least 1.</summary>
    public double BaseDamageMultiplier
    {
        get => baseMultiplier;
        set => baseMultiplier = double.IsFinite(value) ? Math.Max(1, value) : DefaultDamageMultiplier;
    }

    /// <summary>Crit chance a mutation gives while it is owned, not timed (#89).</summary>
    public double OwnedChance
    {
        get => ownedChance;
        set => ownedChance = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
    }

    /// <summary>The next copy hit that lands is a sure crit (used up by <see cref="Spend"/>).</summary>
    public bool Guaranteed => guaranteeLeft > 0;

    /// <summary>Current crit chance: base, owned and live timed bonuses, capped at 1.</summary>
    public double Chance => Math.Min(1, baseChance + ownedChance + Sum(chance));

    /// <summary>Damage factor of a crit now: base plus the live timed bonuses.</summary>
    public double DamageMultiplier => baseMultiplier + Sum(damage);

    /// <summary>
    /// Add crit chance (for example 0.1 for +10%) that lasts <paramref name="seconds"/>.
    /// With a <paramref name="key"/> at most <paramref name="maxStacks"/> bonuses of that
    /// key live at once; one more replaces the one with the least time left.
    /// </summary>
    public void AddChance(double amount, double seconds, string? key = null, int maxStacks = int.MaxValue) =>
        Add(chance, amount, seconds, key, maxStacks);

    /// <summary>Add crit damage (for example 0.5 for +50% on top of the crit) that lasts <paramref name="seconds"/>.</summary>
    public void AddDamage(double amount, double seconds, string? key = null, int maxStacks = int.MaxValue) =>
        Add(damage, amount, seconds, key, maxStacks);

    /// <summary>Make the next copy hit that lands a crit, if it lands within <paramref name="seconds"/>.</summary>
    public void Guarantee(double seconds = double.PositiveInfinity)
    {
        if (double.IsNaN(seconds) || seconds <= 0) return;
        guaranteeLeft = Math.Max(guaranteeLeft, seconds);
    }

    /// <summary>
    /// Whether a hit on this tick is a crit. <paramref name="random"/> returns a value in [0, 1).
    /// <paramref name="extraChance"/> is a chance only this target gets (Heart of Ice).
    /// </summary>
    public bool Roll(Func<double> random, double extraChance = 0)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (Guaranteed) return true;
        double total = Math.Min(1, Chance + Math.Max(0, double.IsFinite(extraChance) ? extraChance : 0));
        return total > 0 && random() < total;
    }

    /// <summary>A sure crit was spent by a hit; ordinary chance is unchanged.</summary>
    public void Spend() => guaranteeLeft = 0;

    /// <summary>Let <paramref name="seconds"/> pass; expired bonuses end.</summary>
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        Age(chance, seconds);
        Age(damage, seconds);
        if (guaranteeLeft > 0) guaranteeLeft = Math.Max(0, guaranteeLeft - seconds);
    }

    /// <summary>The multiplier a crit applies on top of the ordinary copy multiplier, or 1 when the tick is not a crit.</summary>
    public double Factor(bool critical) => critical ? DamageMultiplier : 1;

    public void Reset()
    {
        chance.Clear();
        damage.Clear();
        guaranteeLeft = 0;
        ownedChance = 0;
    }

    private static void Add(List<Timed> list, double amount, double seconds, string? key, int maxStacks)
    {
        if (!double.IsFinite(amount) || amount <= 0 || double.IsNaN(seconds) || seconds <= 0 || maxStacks < 1) return;
        if (key is not null)
        {
            int count = 0, oldest = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Key != key) continue;
                count++;
                if (oldest < 0 || list[i].Seconds < list[oldest].Seconds) oldest = i;
            }
            if (count >= maxStacks) list.RemoveAt(oldest);
        }
        list.Add(new Timed(amount, seconds, key));
    }

    private static double Sum(List<Timed> list)
    {
        double sum = 0;
        foreach (var bonus in list) sum += bonus.Amount;
        return sum;
    }

    private static void Age(List<Timed> list, double seconds)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            double left = list[i].Seconds - seconds;
            if (left <= 0) list.RemoveAt(i);
            else list[i] = list[i] with { Seconds = left };
        }
    }

    private readonly record struct Timed(double Amount, double Seconds, string? Key);
}
