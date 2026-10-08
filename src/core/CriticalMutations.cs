namespace Kirby.Core;

[Flags]
public enum KirbyMutation
{
    None = 0,
    KillerInstinct = 1,
    BlindFaith = 2,
    HeartOfIce = 4,
    HuntersInstinct = 8,
    MasterOfArmsInstinct = 16,
}

/// <summary>
/// Numbers of the Kirby versions of the five skill-cooldown mutations (#89). Skill
/// cooldowns do nothing in a Kirby run, so each mutation keeps its Dead Cells trigger
/// and gives timed crit chance or damage instead. These are the owner's to tune.
/// </summary>
public sealed class MutationTuning
{
    /// <summary>Killer Instinct: crit chance per copy-attack kill, how long it lasts, and the stack cap.</summary>
    public double KillerChance { get; set; } = 0.05;
    public double KillerSeconds { get; set; } = 6;
    public int KillerStacks { get; set; } = 5;
    /// <summary>Blind Faith: how long a parry keeps the next copy attack a sure crit.</summary>
    public double BlindFaithSeconds { get; set; } = 8;
    /// <summary>Heart of Ice: extra crit chance against slowed, stunned, frozen or rooted enemies up close.</summary>
    public double HeartOfIceChance { get; set; } = 0.25;
    /// <summary>Hunter's Instinct: crit chance after copying an ability, and how long it lasts.</summary>
    public double HuntersChance { get; set; } = 0.20;
    public double HuntersSeconds { get; set; } = 10;
    /// <summary>Master of Arms Instinct: crit chance while owned, and the crit damage each crit adds, its time and stack cap.</summary>
    public double MasterChance { get; set; } = 0.10;
    public double MasterDamage { get; set; } = 0.25;
    public double MasterSeconds { get; set; } = 5;
    public int MasterStacks { get; set; } = 3;
}

/// <summary>
/// What the owned cooldown mutations do in a Kirby run: they react to Kirby's
/// events and feed <see cref="CopyCritical"/>. The runtime reports each event once.
/// </summary>
public sealed class CriticalMutations(CopyCritical critical)
{
    public MutationTuning Tuning { get; set; } = new();
    public KirbyMutation Owned { get; private set; }

    /// <summary>Follow the mutations the hero holds now. Only Master of Arms Instinct gives chance by itself.</summary>
    public void SetOwned(KirbyMutation owned)
    {
        Owned = owned;
        critical.OwnedChance = Owned.HasFlag(KirbyMutation.MasterOfArmsInstinct) ? Tuning.MasterChance : 0;
    }

    /// <summary>A copy attack killed an enemy (Killer Instinct).</summary>
    public void CopyKill()
    {
        if (Owned.HasFlag(KirbyMutation.KillerInstinct))
            critical.AddChance(Tuning.KillerChance, Tuning.KillerSeconds, nameof(KirbyMutation.KillerInstinct), Tuning.KillerStacks);
    }

    /// <summary>The shield parried successfully (Blind Faith).</summary>
    public void Parry()
    {
        if (Owned.HasFlag(KirbyMutation.BlindFaith)) critical.Guarantee(Tuning.BlindFaithSeconds);
    }

    /// <summary>Kirby copied an ability (Hunter's Instinct).</summary>
    public void Copied()
    {
        if (Owned.HasFlag(KirbyMutation.HuntersInstinct))
            critical.AddChance(Tuning.HuntersChance, Tuning.HuntersSeconds, nameof(KirbyMutation.HuntersInstinct), 1);
    }

    /// <summary>A copy hit landed as a crit (Master of Arms Instinct).</summary>
    public void CriticalLanded()
    {
        if (Owned.HasFlag(KirbyMutation.MasterOfArmsInstinct))
            critical.AddDamage(Tuning.MasterDamage, Tuning.MasterSeconds, nameof(KirbyMutation.MasterOfArmsInstinct), Tuning.MasterStacks);
    }

    /// <summary>The extra crit chance one target gives (Heart of Ice), when it is slowed, stunned, frozen or rooted and close.</summary>
    public double TargetChance(bool qualifies) =>
        qualifies && Owned.HasFlag(KirbyMutation.HeartOfIce) ? Tuning.HeartOfIceChance : 0;
}

/// <summary>Mod-written mutation texts for a Kirby run, with the live tuning numbers in them.</summary>
public static class MutationDescriptions
{
    /// <summary>Host item IDs of the five cooldown mutations.</summary>
    public const string KillerInstinct = "P_CDR_Kill", HuntersInstinct = "P_CDR_Distance", BlindFaith = "P_CDR_Parry",
        HeartOfIce = "P_CDR_locked", MasterOfArms = "P_CDR_Crit";

    public static KirbyMutation Mutation(string? itemId) => itemId switch
    {
        KillerInstinct => KirbyMutation.KillerInstinct,
        BlindFaith => KirbyMutation.BlindFaith,
        HeartOfIce => KirbyMutation.HeartOfIce,
        HuntersInstinct => KirbyMutation.HuntersInstinct,
        MasterOfArms => KirbyMutation.MasterOfArmsInstinct,
        _ => KirbyMutation.None,
    };

    public static string? Describe(string? itemId, MutationTuning t) => Mutation(itemId) switch
    {
        KirbyMutation.KillerInstinct =>
            $"Copy-attack kills give +{Percent(t.KillerChance)}% critical chance for {Seconds(t.KillerSeconds)} sec. Stacks up to {t.KillerStacks} times.",
        KirbyMutation.BlindFaith =>
            $"A successful parry makes your next copy attack within {Seconds(t.BlindFaithSeconds)} sec a critical hit.",
        KirbyMutation.HeartOfIce =>
            $"+{Percent(t.HeartOfIceChance)}% critical chance against slowed, stunned, frozen or rooted enemies at close range.",
        KirbyMutation.HuntersInstinct =>
            $"Copying an ability gives +{Percent(t.HuntersChance)}% critical chance for {Seconds(t.HuntersSeconds)} sec.",
        KirbyMutation.MasterOfArmsInstinct =>
            $"+{Percent(t.MasterChance)}% critical chance. Critical hits give +{Percent(t.MasterDamage)}% critical damage for {Seconds(t.MasterSeconds)} sec. Stacks up to {t.MasterStacks} times.",
        _ => null,
    };

    private static string Percent(double fraction) => Math.Round(fraction * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string Seconds(double seconds) => seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The host's mutation texts say "melee" and "ranged" attacks. In a Kirby run every
/// copy attack counts as melee (no host weapons appear), so the words change and
/// the host's own numbers stay (#91).
/// </summary>
public static class CopyAttackWording
{
    /// <summary>The text with its melee and ranged wording turned into copy-attack wording, or null if nothing changed.</summary>
    public static string? Rewrite(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string result = System.Text.RegularExpressions.Regex.Replace(text, "(?i)close-ranged ranged attacks?", m =>
            char.IsUpper(m.Value[0]) ? "Copy attacks at close range" : "copy attacks at close range");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?i)\bmelee kills?\b", m =>
            (char.IsUpper(m.Value[0]) ? "Copy-attack kill" : "copy-attack kill") + (m.Value.EndsWith('s') || m.Value.EndsWith('S') ? "s" : ""));
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?i)\bmelee attacks?\b", m =>
            (char.IsUpper(m.Value[0]) ? "Copy attack" : "copy attack") + (m.Value.EndsWith('s') || m.Value.EndsWith('S') ? "s" : ""));
        return result == text ? null : result;
    }
}
