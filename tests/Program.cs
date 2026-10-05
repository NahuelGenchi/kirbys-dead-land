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
var flat = new CopyDamageCurve();
Check(flat.Multiplier(0) == 1 && flat.Multiplier(-1) == 1, "Zero Boss Cells keeps original damage");
Check(flat.Multiplier(5) == 3 && flat.Multiplier(9) == 3, "Boss Cells beyond the table use its last entry");
Check(Enumerable.Range(0, 5).All(bc => flat.Multiplier(bc + 1) > flat.Multiplier(bc)), "Default curve rises every Boss Cell");
Check(flat.HostDamage(23, 50, 50, 0) == 23 && flat.SourceHp(50, 0, 32760) == 50, "BC0 keeps exact Wheel damage and HP");
Check(flat.HostDamage(23, 100, 500, 4) == 58, "BC4 scales Wheel 23 by 2.5");
Check(flat.SourceHp(10, 5, 32760) == 4 && flat.SourceHp(1, 5, 32760) == 1, "Source HP divides host life, rounded up");
Check(flat.SourceHp(1_000_000, 1, 32760) == 32760, "Source HP stays within the native range");
Check(flat.HostDamage(0, 10, 10, 3) == 0 && flat.HostDamage(-4, 10, 10, 3) == 0, "No damage without a source HP drop");
var lethalCurve = new CopyDamageCurve([1, 1.6]);
for (int life = 1; life <= 400; life++)
{
    int sourceHp = lethalCurve.SourceHp(life, 1, 32760);
    for (int hit = 1; hit <= sourceHp + 3; hit++)
    {
        int dealt = lethalCurve.HostDamage(Math.Min(hit, sourceHp), sourceHp, life, 1);
        if ((hit >= sourceHp) != (dealt >= life) || dealt < Math.Min(hit, sourceHp))
            throw new Exception($"Source and host lethality disagree for life {life}, hit {hit}");
    }
}
Check(true, "Source and host lethality agree for every hit against lives 1-400");
foreach (double[] invalid in new[] { Array.Empty<double>(), [1.5], [1, 0.9], [1, double.NaN], [1, double.PositiveInfinity] })
{
    try { _ = new CopyDamageCurve(invalid); throw new Exception("Invalid copy damage curve accepted"); }
    catch (ArgumentException) { Check(true, "Reject invalid copy damage curve"); }
}
Console.WriteLine($"PASS: {assertions} user-rule assertions. No claim of NDS physics or in-game verification.");
