using Kirby.Core;

int assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    assertions++;
}

int next = 0;
var rules = new KirbyRules(["fire", "sword", "ice"], n => next++ % n);
Check(rules.IsBaseForm, "Start in base form");
Check(rules.CanEquip(HostWeaponKind.Sword) && rules.CanEquip(HostWeaponKind.Bow) &&
      rules.CanEquip(HostWeaponKind.Shield), "Base equipment whitelist");
Check(!rules.CanEquip(HostWeaponKind.Other), "Reject unsupported host equipment");
Check(rules.EquipShield("shield-42"), "Record base-form shield");
Check(rules.ResolveAttack(HostWeaponKind.Sword) == AttackRoute.HostWeapon, "Base sword");
Check(rules.ResolveAttack(HostWeaponKind.Bow) == AttackRoute.HostWeapon, "Base bow");
Check(rules.Absorb(100) == "fire", "Confirmed enemy gives ability");
Check(!rules.CanEquip(HostWeaponKind.Shield) && !rules.CanEquip(HostWeaponKind.Sword), "No new equipment while copied");
Check(!rules.CanUse(HostWeaponKind.Sword), "No host sword after copy");
Check(!rules.CanUse(HostWeaponKind.Bow), "No host bow after copy");
Check(rules.ResolveAttack(HostWeaponKind.Sword) == AttackRoute.Ability, "Sword button becomes ability");
Check(rules.ResolveAttack(HostWeaponKind.Bow) == AttackRoute.Ability, "Bow button becomes ability");
Check(rules.ResolveAttack(HostWeaponKind.Shield, "shield-42") == AttackRoute.Parry, "Retained shield parries");
Check(rules.ResolveAttack(HostWeaponKind.Shield, "shield-43") == AttackRoute.None, "Different shield rejected");
Check(!rules.EquipShield("shield-43") && rules.RetainedShieldId == "shield-42", "Copy cannot replace retained shield");
Check(rules.Absorb(101) is null, "Cannot inhale while copied");
rules.DropAbility();
Check(rules.IsBaseForm && rules.CanUse(HostWeaponKind.Bow), "Drop restores base weapon actions");
Check(rules.Absorb(100) is null, "Duplicate absorbed enemy cannot reroll");
Check(rules.Absorb(101) == "sword", "Another enemy selects another random index");
rules.DropAbility();
rules.ResetForLevel();
Check(rules.Absorb(100) == "ice", "Entity IDs can be reused after level transition");
Check(rules.RetainedShieldId == "shield-42", "Level transition retains shield");
rules.Reset();
Check(rules.IsBaseForm && rules.RetainedShieldId is null, "New run resets state");

for (int selected = 0; selected < 3; selected++)
{
    int index = selected;
    var selection = new KirbyRules(["fire", "sword", "ice"], _ => index);
    Check(selection.Absorb(1) == new[] { "fire", "sword", "ice" }[index], "Each configured ability reachable");
}

var bad = new KirbyRules(["fire"], _ => 1);
try { bad.Absorb(1); throw new Exception("Invalid RNG accepted"); }
catch (InvalidOperationException) { Check(bad.IsBaseForm, "Failed selection leaves state unchanged"); }
Console.WriteLine($"PASS: {assertions} user-rule assertions. No claim of NDS physics or in-game verification.");
