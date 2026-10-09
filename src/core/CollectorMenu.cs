namespace Kirby.Core;

/// <summary>
/// Collector entries that do nothing in a Kirby run, so the Collector does not
/// list them (#99). Every host weapon other than a shield, and every skill,
/// becomes a random ability item (#55), so their blueprints and the upgrades
/// that only choose which weapon the run starts with change nothing. Unlock
/// gates still move: a hidden entry the player has found counts as unlocked.
/// </summary>
public static class CollectorMenu
{
    /// <summary>
    /// Host item groups (the item sheet's separators in <c>data.cdb</c>, host build
    /// 23762174) whose entries are weapons other than shields, or skills. Group 6
    /// (Shield) stays.
    /// </summary>
    public static IReadOnlyDictionary<int, string> HiddenGroups { get; } = new Dictionary<int, string>
    {
        [0] = "DeployedTrap",
        [1] = "Grenade",
        [3] = "Power",
        [4] = "Melee",
        [5] = "Ranged",
    };

    /// <summary>Upgrades outside those groups that cannot work for Kirby: host item ID → why.</summary>
    public static IReadOnlyDictionary<string, string> HiddenUpgrades { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RandomBow"] = "Random Starter Bow only picks which bow the run starts with; that bow becomes a random ability item either way.",
        ["RandomCC"] = "Random Melee Weapon only picks which melee weapon the run starts with; that weapon becomes a random ability item either way.",
        ["BackpackUnlock"] = "The backpack holds a weapon; Kirby carries none.",
        // Kirby has no health flask (#101); F holds his limited-use ability.
        ["Flask1"] = "Health Flask I unlocks the health flask; Kirby has no flask.",
        ["Flask2"] = "Health Flask II adds a flask charge; Kirby has no flask.",
        ["Flask3"] = "Health Flask III adds a flask charge; Kirby has no flask.",
        ["Flask4"] = "Health Flask IV adds a flask charge; Kirby has no flask.",
    };

    /// <summary>
    /// Whether the Collector hides this entry in a Kirby run: a weapon or skill
    /// blueprint, an upgrade above, or a mutation the mutation menu hides (#90).
    /// </summary>
    public static bool Hides(string? itemId, int group) =>
        !string.IsNullOrEmpty(itemId) &&
        (HiddenGroups.ContainsKey(group) || HiddenUpgrades.ContainsKey(itemId) || MutationMenu.IsHidden(itemId));

    /// <summary>
    /// The unlock count that gates the Collector's upgrades ("unlock N more items").
    /// The host counts every found entry that is unlocked and has a cell cost; this
    /// adds the found, still locked, hidden entries with a cell cost, as if unlocked.
    /// </summary>
    public static int CountUnlocked(int hostCount, IEnumerable<(string Id, int Group, int CellCost, bool Unlocked)> found) =>
        hostCount + found.Count(entry => !entry.Unlocked && entry.CellCost > 0 && Hides(entry.Id, entry.Group));

    /// <summary>
    /// Whether an entry counts as owned when the Collector checks a prerequisite
    /// (<c>canInvestOnItem</c>): every investment first needs Health Flask I, and
    /// some name another entry as their required item. A hidden entry cannot be
    /// bought, so it counts as owned (#101); otherwise the host's own answer stands.
    /// </summary>
    public static bool MeetsPrerequisite(string? itemId, int group, bool hostUnlocked) => hostUnlocked || Hides(itemId, group);
}
