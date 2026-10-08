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

    /// <summary>The next copy hit that lands is a sure crit (used up by <see cref="Spend"/>).</summary>
    public bool Guaranteed { get; private set; }

    /// <summary>Current crit chance: base plus the live timed bonuses, capped at 1.</summary>
    public double Chance => Math.Min(1, baseChance + Sum(chance));

    /// <summary>Damage factor of a crit now: base plus the live timed bonuses.</summary>
    public double DamageMultiplier => baseMultiplier + Sum(damage);

    /// <summary>Add crit chance (for example 0.1 for +10%) that lasts <paramref name="seconds"/>.</summary>
    public void AddChance(double amount, double seconds) => Add(chance, amount, seconds);

    /// <summary>Add crit damage (for example 0.5 for +50% on top of the crit) that lasts <paramref name="seconds"/>.</summary>
    public void AddDamage(double amount, double seconds) => Add(damage, amount, seconds);

    /// <summary>Make the next copy hit that lands a crit.</summary>
    public void Guarantee() => Guaranteed = true;

    /// <summary>Whether a hit on this tick is a crit. <paramref name="random"/> returns a value in [0, 1).</summary>
    public bool Roll(Func<double> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return Guaranteed || Chance > 0 && random() < Chance;
    }

    /// <summary>A sure crit was spent by a hit; ordinary chance is unchanged.</summary>
    public void Spend() => Guaranteed = false;

    /// <summary>Let <paramref name="seconds"/> pass; expired bonuses end.</summary>
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        Age(chance, seconds);
        Age(damage, seconds);
    }

    /// <summary>The multiplier a crit applies on top of the ordinary copy multiplier, or 1 when the tick is not a crit.</summary>
    public double Factor(bool critical) => critical ? DamageMultiplier : 1;

    public void Reset()
    {
        chance.Clear();
        damage.Clear();
        Guaranteed = false;
    }

    private static void Add(List<Timed> list, double amount, double seconds)
    {
        if (!double.IsFinite(amount) || amount <= 0 || !double.IsFinite(seconds) || seconds <= 0) return;
        list.Add(new Timed(amount, seconds));
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

    private readonly record struct Timed(double Amount, double Seconds);
}
