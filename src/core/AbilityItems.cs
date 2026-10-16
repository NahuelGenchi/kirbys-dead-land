namespace Kirby.Core;

/// <summary>What a host weapon slot currently holds, as far as the ability item cares.</summary>
public enum SlotContent { Empty, Ability, Shield, Other }

/// <summary>
/// Kirby ability items: one host item per original copy ability. They replace
/// the host weapons and skills Kirby cannot use, are taken only by the original
/// inhale and swallow, and sit in a weapon slot while Kirby holds that copy.
/// </summary>
public static class AbilityItems
{
    /// <summary>Item IDs are this prefix and the ability ID, for example KirbyAbilityFire.</summary>
    public const string Prefix = "KirbyAbility";

    public static string ItemId(string abilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityId);
        return Prefix + abilityId;
    }

    /// <summary>The ability an item ID stands for, or null for any other item.</summary>
    public static string? AbilityOf(string? itemId) =>
        itemId is not null && itemId.Length > Prefix.Length && itemId.StartsWith(Prefix, StringComparison.Ordinal)
            ? itemId[Prefix.Length..] : null;

    /// <summary>The name shown to the player; the original spells Yo-Yo with a hyphen.</summary>
    public static string DisplayName(string abilityId) => abilityId == "YoYo" ? "Yo-Yo" : abilityId;

    /// <summary>
    /// A new floor item is replaced when it is a host weapon other than a shield,
    /// or a skill (host item kind Active). Amulets, food, blueprints and every
    /// other kind stay, and an ability item is never replaced again.
    /// </summary>
    public static bool Replaces(bool weapon, bool skill, bool shield, bool abilityItem) =>
        !abilityItem && (skill || weapon && !shield);

    /// <summary>
    /// In a Kirby run the forge lists only ability items and shields (#58), on
    /// top of the host's own test. The starting Rusty Sword, which stays in its
    /// slot until the first copy, and any other host weapon or skill never show.
    /// </summary>
    public static bool ForgeLists(bool abilityItem, bool shield) => abilityItem || shield;

    /// <summary>
    /// Dead Cells' own weapon damage bonus (InventItem.getDamageBonus, adjusted
    /// level from getAdjustedItemLevel and getStatBonusCount for non-amulets),
    /// as a fraction: copy damage is multiplied by 1 + this. The adjusted level
    /// is the item level plus 2 per "+" (QualityUp), or plus 6 when Legendary.
    /// The runtime asks the host itself; this copy documents and tests the rule.
    /// </summary>
    public static double HostDamageBonus(int itemLevel, int qualityUps, bool legendary)
    {
        int adjusted = itemLevel + (legendary ? 6 : 2 * Math.Max(0, qualityUps));
        int statBonuses = adjusted <= 1 ? 0 : adjusted / 3 + (adjusted - 1) / 3;
        return statBonuses * 0.25 + 0.75 * (adjusted - 1) / 18.0;
    }

    /// <summary>The item card's main info line: the copy-damage bonus, or plain "Copy ability".</summary>
    public static string InfoLine(double damageBonus)
    {
        int percent = (int)Math.Round(damageBonus * 100, MidpointRounding.AwayFromZero);
        return percent > 0 ? $"+{percent}% copy damage" : "Copy ability";
    }

    /// <summary>
    /// The weapon slot the copy's item goes to: the slot that already holds an
    /// ability item, else a slot holding another unusable weapon (it is removed),
    /// else an empty slot, else a shield other than the retained one. Returns -1
    /// only when every slot holds the retained shield.
    /// </summary>
    public static int Slot(IReadOnlyList<SlotContent> slots, int retainedShieldSlot = -1)
    {
        ArgumentNullException.ThrowIfNull(slots);
        foreach (var wanted in new[] { SlotContent.Ability, SlotContent.Other, SlotContent.Empty })
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] == wanted) return i;
        for (int i = 0; i < slots.Count; i++)
            if (slots[i] == SlotContent.Shield && i != retainedShieldSlot) return i;
        return -1;
    }

    /// <summary>
    /// Kirby holds one shield (#139). The slots whose shield leaves: every
    /// shield slot except the retained one, or except the first when the
    /// retained shield is in none of them.
    /// </summary>
    public static IReadOnlyList<int> SpareShields(IReadOnlyList<SlotContent> slots, int retainedShieldSlot = -1)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var shields = Enumerable.Range(0, slots.Count).Where(i => slots[i] == SlotContent.Shield).ToList();
        if (shields.Count < 2) return [];
        shields.Remove(shields.Contains(retainedShieldSlot) ? retainedShieldSlot : shields[0]);
        return shields;
    }
}

/// <summary>
/// Ability items in Kirby's mouth. The original swallow decides which copy he
/// gets (a mouthful of several objects runs the original roulette); the first
/// held item of that ability becomes his, and every other one goes back to the
/// floor. A mouthful spat out or lost without a copy returns every item.
/// </summary>
public sealed class MouthfulItems<T> where T : class
{
    private readonly List<(T Item, string Ability)> held = [];
    private bool seenFull;

    public int Count => held.Count;

    /// <summary>The original mouth held these items and is now empty without a copy: they were spat out.</summary>
    public bool Emptied { get; private set; }

    /// <summary>
    /// Follow the original mouth after each source step. The original swallow
    /// leaves its mouth count set until the next inhale, so the mouth emptying
    /// in base form after it was seen full means a spit (or a mouthful lost).
    /// </summary>
    public void Observe(bool mouthFull, bool baseForm)
    {
        if (held.Count == 0) { seenFull = Emptied = false; return; }
        if (mouthFull) seenFull = true;
        else if (seenFull && baseForm) Emptied = true;
    }

    public IEnumerable<T> Items => held.Select(entry => entry.Item);

    public void Add(T item, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityId);
        if (held.Any(entry => ReferenceEquals(entry.Item, item))) return;
        held.Add((item, abilityId));
    }

    /// <summary>Settle the mouthful once the original swallow or spit has happened.</summary>
    public (T? Taken, IReadOnlyList<T> Returned) Resolve(string? copiedAbility)
    {
        int index = copiedAbility is null ? -1
            : held.FindIndex(entry => StringComparer.Ordinal.Equals(entry.Ability, copiedAbility));
        T? taken = index >= 0 ? held[index].Item : null;
        var returned = held.Where((_, i) => i != index).Select(entry => entry.Item).ToArray();
        Clear();
        return (taken, returned);
    }

    public void Clear()
    {
        held.Clear();
        seenFull = Emptied = false;
    }
}
