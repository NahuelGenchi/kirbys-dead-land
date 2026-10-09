namespace Kirby.Core;

/// <summary>
/// Mutations that cannot work in a Kirby run, so the mutation menu does not offer
/// them (#90). Dead Cells weapons never appear in a Kirby run: every weapon is an
/// ability item, so mutations that need a weapon type, ammo, planted arrows, the
/// backpack weapon or switching weapons have nothing to act on, and skill-use
/// triggers never fire because skills become ability items too. Mutations of
/// "ranged" attacks go as well: every copy attack counts as melee in a Kirby run.
/// Kirby has no health flask (#101), so the mutations that change or refill it go too.
/// </summary>
public static class MutationMenu
{
    /// <summary>Host item ID → why it cannot work for Kirby.</summary>
    public static IReadOnlyDictionary<string, string> Hidden { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["P_DmgPlantedArrow"] = "Barbed Tips needs arrows stuck in enemies; Kirby fires none.",
        ["P_AmmoOnHit"] = "Ripper needs arrows stuck in enemies; Kirby fires none.",
        ["P_Backpack_Melee"] = "Porcupack uses the weapon in the backpack; Kirby carries none.",
        ["P_Backpack_Ranged"] = "Acrobatipack uses the ranged weapon in the backpack; Kirby carries none.",
        ["P_Backpack_Shield"] = "Armadillopack uses the shield in the backpack; Kirby's shield is not carried there.",
        ["P_AttackSpeed_Combo"] = "Kill Rhythm needs two different weapons to switch between; both buttons fire the same copy ability.",
        ["P_DmgSkillRanged"] = "Ranger's Gear follows a skill use; skills are ability items for Kirby.",
        ["P_DmgSkl"] = "Scheme follows a skill use; skills are ability items for Kirby.",
        ["P_Ammo"] = "Ammo multiplies weapon ammo; Kirby has none (its Kirby version waits for #68).",
        ["P_DeployedDmg"] = "Support needs a deployed skill; Kirby deploys none (his helper's position is not readable).",
        ["P_DeathBomb"] = "Parting Gift follows kills by ranged attacks; Kirby has no ranged weapons.",
        ["P_ShareDamage"] = "Networking marks enemies hit by ranged attacks; Kirby has no ranged weapons.",
        ["P_Hot"] = "Extended Healing changes how the health flask heals; Kirby has no flask.",
        ["P_QuickHeal"] = "Emergency Triage changes how the health flask heals; Kirby has no flask.",
        ["P_CorruptedHealing"] = "Tainted Flask follows health flask use and refills its charges; Kirby has no flask.",
        ["P_CursedFlask"] = "Cursed Flask changes the health flask's charges; Kirby has no flask.",
    };

    public static bool IsHidden(string? itemId) => itemId is not null && Hidden.ContainsKey(itemId);
}
