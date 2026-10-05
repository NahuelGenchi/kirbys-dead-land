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
Check(rules.IsBaseForm, "A mouthful does not transform before the original swallow");
rules.SetSourceAbility("fire");
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
rules.SetSourceAbility("sword");
rules.DropAbility();
rules.ResetForLevel();
Check(rules.Absorb(100) == "ice", "Entity IDs can be reused after level transition");
Check(rules.RetainedShieldId == "shield-42", "Level transition retains shield");
rules.Reset();
Check(rules.IsBaseForm && rules.RetainedShieldId is null, "New run resets state");
rules.Restore("ice", "physical-shield-7");
Check(rules.AbilityId == "ice" && rules.ResolveAttack(HostWeaponKind.Shield, "physical-shield-7") == AttackRoute.Parry,
    "Restored copy retains the exact shield");
try { rules.Restore("unknown", null); throw new Exception("Invalid save ability accepted"); }
catch (ArgumentException) { Check(rules.AbilityId == "ice", "Invalid save leaves current copy intact"); }
rules.Restore(null, null);
Check(rules.IsBaseForm && rules.RetainedShieldId is null, "Empty save restores base form");

for (int selected = 0; selected < 3; selected++)
{
    int index = selected;
    var selection = new KirbyRules(["fire", "sword", "ice"], _ => index);
    Check(selection.Absorb(1) == new[] { "fire", "sword", "ice" }[index], "Each configured ability reachable");
}

var bad = new KirbyRules(["fire"], _ => 1);
try { bad.Absorb(1); throw new Exception("Invalid RNG accepted"); }
catch (InvalidOperationException) { Check(bad.IsBaseForm, "Failed selection leaves state unchanged"); }
var mouthful = new KirbyRules(["fire", "ice"], _ => 0);
Check(mouthful.Absorb(1) == "fire" && mouthful.Absorb(2) == "fire", "Two enemies can enter the same mouthful");
Check(mouthful.Absorb(1) is null && mouthful.IsBaseForm, "Repeated capture neither rerolls nor transforms");
mouthful.SetSourceAbility("ice");
Check(mouthful.Absorb(3) is null, "Original transformation closes the inhale window");

var elite = new KirbyRules(["fire", "sword"], _ => 0);
Check(elite.ConfirmAbsorption(1, "sword", elite: true) == "sword" && elite.PendingEmpowerment == "sword" &&
      !elite.AbilityEmpowered && elite.DamageMultiplier == 1, "Elite in mouth is pending, not yet empowered");
elite.SetSourceAbility("sword");
Check(elite.AbilityEmpowered && elite.PendingEmpowerment is null &&
      elite.DamageMultiplier == KirbyRules.EmpoweredDamageMultiplier, "Elite swallow empowers its Sword");
elite.SetSourceAbility("sword");
Check(elite.AbilityEmpowered, "Repeated source report keeps the empowerment");
elite.DropAbility();
Check(!elite.AbilityEmpowered && elite.DamageMultiplier == 1, "Losing the copy ends the empowerment");
elite.SetSourceAbility("sword");
Check(!elite.AbilityEmpowered, "A re-caught discarded star is an ordinary Sword");
elite.DropAbility();
Check(elite.ConfirmAbsorption(2, "sword", elite: true) == "sword" && elite.ConfirmAbsorption(3, "fire") == "fire",
    "Elite and ordinary enemy share a mouthful");
elite.SetSourceAbility("fire");
Check(!elite.AbilityEmpowered && elite.PendingEmpowerment is null, "Mouthful that yields another ability is not empowered");
elite.DropAbility();
elite.ConfirmAbsorption(4, "sword", elite: true);
elite.DropAbility();
Check(elite.PendingEmpowerment is null, "Spitting the Elite out cancels the pending empowerment");
elite.SetSourceAbility("sword");
Check(!elite.AbilityEmpowered, "No stale pending empowerment after a spit");
elite.DropAbility();
Check(elite.ConfirmAbsorption(5, "fire") == "fire" && elite.PendingEmpowerment is null, "Ordinary enemy never empowers");
elite.SetSourceAbility("fire");
Check(!elite.AbilityEmpowered, "Ordinary copy has normal damage");
elite.Restore("sword", "shield-1", empowered: true);
Check(elite.AbilityEmpowered && elite.AbilityId == "sword", "Save restores an empowered copy");
try { elite.Restore(null, null, empowered: true); throw new Exception("Empowered base form accepted"); }
catch (ArgumentException) { Check(elite.AbilityEmpowered && elite.AbilityId == "sword", "Invalid empowerment save leaves state intact"); }
try { elite.Restore("fire", null, pendingEmpowerment: "sword"); throw new Exception("Pending empowerment while copied accepted"); }
catch (ArgumentException) { Check(elite.AbilityId == "sword", "Pending empowerment requires base form"); }
elite.Restore(null, null, pendingEmpowerment: "sword");
elite.SetSourceAbility("sword");
Check(elite.AbilityEmpowered, "Restored mouthful still empowers on swallow");
elite.Reset();
Check(!elite.AbilityEmpowered && elite.PendingEmpowerment is null, "New run resets the empowerment");
elite.Restore("sword", null);
Check(!elite.AbilityEmpowered, "Older saves without the flag restore an ordinary copy");

Check(CopyDamageScale.SourceHp(500, 1, 32760) == 500 && CopyDamageScale.HostDamage(18, 500, 500, 1) == 18,
    "Multiplier 1 keeps exact original damage");
Check(CopyDamageScale.SourceHp(5, 2, 32760) == 3 && CopyDamageScale.HostDamage(18, 250, 500, 2) == 36,
    "Doubled damage halves source health");
Check(CopyDamageScale.SourceHp(100000, 2, 32760) == 32760, "Source health respects the native cap");
Check(CopyDamageScale.HostDamage(0, 10, 20, 2) == 0, "No source damage, no host damage");
foreach (double m in new[] { 1.0, 1.6, 2.0, 2.0 * 3.0 })
for (int hostLife = 1; hostLife <= 400; hostLife++)
{
    int sourceHp = CopyDamageScale.SourceHp(hostLife, m, 32760);
    for (int hit = 1; hit <= sourceHp + 2; hit++)
    {
        bool sourceDies = hit >= sourceHp;
        bool hostDies = CopyDamageScale.HostDamage(hit, sourceHp, hostLife, m) >= hostLife;
        if (sourceDies != hostDies) throw new Exception($"Lethality differs at x{m}, life {hostLife}, hit {hit}");
    }
}
Check(true, "Source and host lethality agree for host lives 1-400 at x1, x1.6, x2 and x6");
foreach (double invalid in new[] { 0.5, double.NaN, double.PositiveInfinity })
{
    try { CopyDamageScale.SourceHp(10, invalid, 32760); throw new Exception($"Invalid multiplier {invalid} accepted"); }
    catch (ArgumentOutOfRangeException) { Check(true, "Invalid multiplier rejected"); }
}
Console.WriteLine($"PASS: {assertions} user-rule assertions. No claim of NDS physics or in-game verification.");
