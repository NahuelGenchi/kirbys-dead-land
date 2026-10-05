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
Check(ColorlessDamage.Tier(1, 1, 1) == 1 && ColorlessDamage.Tier(4, 9, 2) == 9 &&
      ColorlessDamage.Tier(7, 3, 5) == 7 && ColorlessDamage.Tier(2, 2, 6) == 6, "Colorless uses the highest scroll tier");
Check(ColorlessDamage.Multiplier(1) == 1, "No scrolls keeps original damage");
Check(Math.Abs(ColorlessDamage.Multiplier(5) - 1.74900625) < 1e-12, "Tier 5 is 1.15^4");
Check(ColorlessDamage.HostDamage(23, 100, 500, 1) == 23 && ColorlessDamage.SourceHp(50, 1, 32760) == 50,
    "Tier 1 keeps exact Wheel damage and HP");
Check(ColorlessDamage.HostDamage(23, 100, 500, ColorlessDamage.Multiplier(5)) == 40, "Tier 5 truncates Wheel 23 × 1.749 to 40");
Check(ColorlessDamage.HostDamage(18, 100, 500, ColorlessDamage.Multiplier(3)) == 23, "Tier 3 truncates Sword 18 × 1.3225 to 23");
Check(ColorlessDamage.SourceHp(100, ColorlessDamage.Multiplier(5), 32760) == 58, "Source HP divides host life, rounded up");
Check(ColorlessDamage.SourceHp(1_000_000, 1, 32760) == 32760, "Source HP stays within the native range");
Check(ColorlessDamage.HostDamage(0, 10, 10, 2) == 0 && ColorlessDamage.HostDamage(-4, 10, 10, 2) == 0,
    "No damage without a source HP drop");
for (int tier = 1; tier <= 40; tier++)
{
    double multiplier = ColorlessDamage.Multiplier(tier);
    for (int life = 1; life <= 400; life++)
    {
        int sourceHp = ColorlessDamage.SourceHp(life, multiplier, 32760);
        for (int hit = 1; hit <= sourceHp + 3; hit++)
        {
            int sourceDamage = Math.Min(hit, sourceHp);
            int dealt = ColorlessDamage.HostDamage(sourceDamage, sourceHp, life, multiplier);
            bool sourceLethal = hit >= sourceHp;
            if (sourceLethal != (dealt >= life) || dealt != (int)(sourceDamage * multiplier))
                throw new Exception($"Colorless damage disagrees for tier {tier}, life {life}, hit {hit}");
        }
    }
}
Check(true, "Source and host lethality agree, and every hit equals host truncation, for tiers 1-40 and lives 1-400");
Check(ColorlessDamage.SourceHp(69, ColorlessDamage.Multiplier(2), 32760) == 60, "Floating-point division cannot add a spare source HP");
foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
{
    try { _ = ColorlessDamage.HostDamage(1, 2, 2, invalid); throw new Exception("Invalid colorless multiplier accepted"); }
    catch (ArgumentOutOfRangeException) { Check(true, "Reject invalid colorless multiplier"); }
}
Console.WriteLine($"PASS: {assertions} user-rule assertions. No claim of NDS physics or in-game verification.");
