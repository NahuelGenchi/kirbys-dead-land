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
Check(rules.CanEquip(HostWeaponKind.Shield), "Base form takes a shield");
Check(!rules.CanEquip(HostWeaponKind.Sword) && !rules.CanEquip(HostWeaponKind.Bow) &&
      !rules.CanEquip(HostWeaponKind.Other), "Base form takes no other host weapon");
rules.EquipShield("shield-42");
Check(rules.RetainedShieldId == "shield-42", "Record base-form shield");
Check(!rules.CanUse(HostWeaponKind.Sword) && !rules.CanUse(HostWeaponKind.Bow), "No host sword or bow in base form");
Check(rules.ResolveAttack(HostWeaponKind.Sword) == AttackRoute.Ability, "Base sword button inhales");
Check(rules.ResolveAttack(HostWeaponKind.Bow) == AttackRoute.Ability, "Base bow button inhales");
Check(rules.ResolveAttack(HostWeaponKind.None) == AttackRoute.Ability, "Empty base weapon slot inhales");
Check(rules.ResolveAttack(HostWeaponKind.Shield, "shield-42") == AttackRoute.Parry, "Base shield parries");
Check(rules.Absorb(100) == "fire", "Confirmed enemy gives ability");
Check(rules.IsBaseForm, "A mouthful does not transform before the original swallow");
rules.SetSourceAbility("fire");
Check(!rules.CanEquip(HostWeaponKind.Sword) && !rules.CanEquip(HostWeaponKind.Bow) &&
      !rules.CanEquip(HostWeaponKind.Other), "No new weapons while copied");
Check(rules.CanEquip(HostWeaponKind.Shield), "A shield can still be taken while copied");
Check(!rules.CanUse(HostWeaponKind.Sword), "No host sword after copy");
Check(!rules.CanUse(HostWeaponKind.Bow), "No host bow after copy");
Check(rules.ResolveAttack(HostWeaponKind.Sword) == AttackRoute.Ability, "Sword button becomes ability");
Check(rules.ResolveAttack(HostWeaponKind.Bow) == AttackRoute.Ability, "Bow button becomes ability");
Check(rules.ResolveAttack(HostWeaponKind.Shield, "shield-42") == AttackRoute.Parry, "Retained shield parries");
Check(rules.ResolveAttack(HostWeaponKind.Shield, "shield-43") == AttackRoute.None, "Different shield rejected");
rules.FollowEquippedShields(["shield-42"]);
Check(rules.RetainedShieldId == "shield-42", "Equipped retained shield is kept");
Check(rules.Absorb(101) is null, "Cannot inhale while copied");
rules.DropAbility();
Check(rules.IsBaseForm && !rules.CanUse(HostWeaponKind.Bow) &&
      rules.ResolveAttack(HostWeaponKind.Sword) == AttackRoute.Ability, "Drop restores base inhale, not host weapons");
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
var swap = new KirbyRules(["sword"], _ => 0);
swap.FollowEquippedShields(["wooden"]);
Check(swap.RetainedShieldId == "wooden", "Base form follows the equipped shield");
swap.SetSourceAbility("sword");
swap.EquipShield("cudgel");
swap.FollowEquippedShields(["cudgel"]);
Check(swap.RetainedShieldId == "cudgel" && swap.ResolveAttack(HostWeaponKind.Shield, "cudgel") == AttackRoute.Parry &&
      swap.ResolveAttack(HostWeaponKind.Shield, "wooden") == AttackRoute.None, "Copied Take swap: the new shield parries");
Check(swap.AbilityId == "sword" && swap.ResolveAttack(HostWeaponKind.Sword) == AttackRoute.Ability, "A shield swap keeps the copy");
swap.EquipShield("second");
swap.FollowEquippedShields(["cudgel", "second"]);
Check(swap.RetainedShieldId == "second", "The most recently taken of two equipped shields is retained");
swap.FollowEquippedShields(["cudgel"]);
Check(swap.RetainedShieldId == "cudgel", "A retained shield that left falls back to an equipped shield");
swap.FollowEquippedShields([]);
Check(swap.RetainedShieldId is null && swap.ResolveAttack(HostWeaponKind.Shield, "cudgel") == AttackRoute.None,
    "No equipped shield, no retained shield");
swap.DropAbility();
Check(swap.CanUse(HostWeaponKind.Shield, "anything"), "Base form uses any equipped shield");

var mouthful = new KirbyRules(["fire", "ice"], _ => 0);
Check(mouthful.Absorb(1) == "fire" && mouthful.Absorb(2) == "fire", "Two enemies can enter the same mouthful");
Check(mouthful.Absorb(1) is null && mouthful.IsBaseForm, "Repeated capture neither rerolls nor transforms");
mouthful.SetSourceAbility("ice");
Check(mouthful.Absorb(3) is null, "Original transformation closes the inhale window");

Check(MouthfulHazards.CanPair(hostile: true, live: true, disabled: false), "Live enemy projectiles can be inhaled");
Check(!MouthfulHazards.CanPair(hostile: false, live: true, disabled: false), "Kirby's own arrows are never paired");
Check(!MouthfulHazards.CanPair(true, live: false, disabled: false) && !MouthfulHazards.CanPair(true, true, disabled: true),
    "Destroyed or disabled projectiles are not paired");
Check(MouthfulHazards.Resolve(swallowed: true, cooked: false, gone: false, sourceDamage: 0, controlled: true) == HazardAction.Remove,
    "Original mouthful consumes the projectile");
Check(MouthfulHazards.Resolve(false, cooked: true, gone: true, 0, false) == HazardAction.Remove, "Cook capture consumes the projectile");
Check(MouthfulHazards.Resolve(false, false, false, sourceDamage: 1, controlled: false) == HazardAction.Remove,
    "An original attack striking the projectile destroys it");
Check(MouthfulHazards.Resolve(false, false, false, 0, controlled: true) == HazardAction.Hold, "Suction holds the host projectile");
Check(MouthfulHazards.Resolve(false, false, false, 0, false) == HazardAction.Follow, "Free projectiles keep host physics");
Check(MouthfulHazards.Resolve(false, false, gone: true, 0, false) == HazardAction.Release, "A vanished source object only frees its slot");

Check(IceFreeze.FreezeAffect == 23 && IceFreeze.FreezeSeconds == 3 && (IceFreeze.DiminishingKey, IceFreeze.DiminishingFullPowerUses,
      IceFreeze.DiminishingMaxUses) == (3, 1, 8), "Ice uses the host Frost Blast freeze and its diminishing returns");
Check(IceFreeze.Freezes(sourceAbility: 10, sourceDamage: 4, hostAlive: true, frozen: false), "An Ice hit freezes a living host enemy");
Check(!IceFreeze.Freezes(10, 4, hostAlive: true, frozen: true), "A frozen enemy is not refrozen by the continuing breath");
Check(!IceFreeze.Freezes(10, 4, hostAlive: false, frozen: false), "A killed enemy is not frozen");
Check(!IceFreeze.Freezes(10, sourceDamage: 0, true, false), "No Ice damage, no freeze");
Check(!IceFreeze.Freezes(sourceAbility: 11, 4, true, false) && !IceFreeze.Freezes(1, 4, true, false), "Other forms never freeze");
Check(IceFreeze.Applies(10) && !IceFreeze.Applies(18) && !IceFreeze.Applies(1),
      "Only Ice deals host Frost damage and skips the original remnant (no ice block)");

// Bridge measurement: Kirby at (400, 153.5), camera (272, 12), Crash's record [144,656]×[-103,409].
var crashBox = new StarBox(144, -103, 656, 409);
Check(ScreenAttacks.IsScreenWide(2, 160, crashBox, 272, 12) && ScreenAttacks.IsScreenWide(2, 70, crashBox, 272, 12),
    "Crash's and Paint's records cover the source window");
Check(!ScreenAttacks.IsScreenWide(1, 70, crashBox, 272, 12), "Cook's screen grab (slot 1) is not a damaging wave");
Check(!ScreenAttacks.IsScreenWide(2, 0, crashBox, 272, 12) && !ScreenAttacks.IsScreenWide(2, 52, new StarBox(137, 140, 157, 160), 272, 12),
    "Harmless or ordinary attack records are not screen-wide");
Check(ScreenAttacks.SourceReaches(560, 151.5, 272, 12) && !ScreenAttacks.SourceReaches(564, 151.5, 272, 12) &&
      ScreenAttacks.SourceReaches(240, 151.5, 272, 12) && !ScreenAttacks.SourceReaches(236, 151.5, 272, 12),
    "The original hits within 32 px of either side of its screen, as measured");
Check(ScreenAttacks.SourceReaches(440, -17.7, 272, 12) && !ScreenAttacks.SourceReaches(440, -21.7, 272, 12) &&
      ScreenAttacks.SourceReaches(440, 234.3, 272, 12) && !ScreenAttacks.SourceReaches(440, 238.3, 272, 12),
    "The original hits within 32 px above and below its screen, as measured");
Check(ScreenAttacks.HostStrikes(paired: false, sourceReaches: false, sourceHit: false),
    "An enemy with no source object is struck by the host");
Check(ScreenAttacks.HostStrikes(paired: true, sourceReaches: false, sourceHit: false),
    "A paired enemy beyond the original's window is struck by the host");
Check(!ScreenAttacks.HostStrikes(true, sourceReaches: true, sourceHit: false) && !ScreenAttacks.HostStrikes(true, false, sourceHit: true),
    "The original collision decides inside its window, and a source hit is never repeated");

Check(StarProjectiles.Identify(0x020BD718) == StarKind.Spit && StarProjectiles.Identify(0x020BDD08) == StarKind.BigSpit &&
      StarProjectiles.Identify(0x02166A54) == StarKind.Ability, "The three original star routines are recognized");
Check(StarProjectiles.Identify(0x020C3700) == StarKind.None && StarProjectiles.Identify(0x020B2714) == StarKind.None,
    "Copy-ability attacks and Kirby's body are not stars");
Check(StarProjectiles.SourceDamage(StarKind.Spit, 52) == 52 && StarProjectiles.SourceDamage(StarKind.BigSpit, 60) == 60,
    "Spit stars strike with their own original damage word");
Check(StarProjectiles.SourceDamage(StarKind.Ability, 0) == 52, "The discarded star strikes like a single spit star");
Check(StarProjectiles.SourceDamage(StarKind.None, 52) == 0 && StarProjectiles.SourceDamage(StarKind.Spit, -3) == 0,
    "Non-stars and invalid damage words strike for nothing");
Check(StarProjectiles.Route(StarKind.Spit, paired: true) == StarRoute.Source &&
      StarProjectiles.Route(StarKind.BigSpit, paired: true) == StarRoute.Source,
    "A paired enemy takes spit-star damage once, through its source object");
Check(StarProjectiles.Route(StarKind.Spit, paired: false) == StarRoute.Host &&
      StarProjectiles.Route(StarKind.BigSpit, paired: false) == StarRoute.Host,
    "The host strikes enemies that have no source object");
Check(StarProjectiles.Route(StarKind.Ability, paired: true) == StarRoute.Host &&
      StarProjectiles.Route(StarKind.Ability, paired: false) == StarRoute.Host,
    "The ability star, harmless in the ROM, is always struck by the host");
// Original records are inclusive: the spit star [137,157] hit the proxy body [157,181] in the bridge.
Check(new StarBox(137, 140, 157, 166).Overlaps(new StarBox(157, 134, 181, 168)), "Touching edges overlap");
Check(!new StarBox(137, 140, 156, 166).Overlaps(new StarBox(157, 134, 181, 168)) &&
      !new StarBox(137, 100, 157, 133).Overlaps(new StarBox(157, 134, 181, 168)), "Separated boxes do not");
Check(new StarBox(45, 143, 65, 163).Union(new StarBox(45, 140, 65, 166)) == new StarBox(45, 140, 65, 166) &&
      new StarBox(45, 140, 65, 166).Offset(-24, 48) == new StarBox(21, 188, 41, 214), "Box union and window offset");
Check(StarProjectiles.BumpDirection(100, 120, -1) == 1 && StarProjectiles.BumpDirection(100, 80, 1) == -1,
    "Stagger pushes the enemy away from the star");
Check(StarProjectiles.BumpDirection(100, 100, -1) == -1 && StarProjectiles.BumpDirection(100, 100, 0) == 1,
    "A centered hit follows the star's travel");
Check(StarProjectiles.BumpSpeed(0) == 0.2 && Math.Abs(StarProjectiles.BumpSpeed(1) - 0.3) < 1e-12 &&
      StarProjectiles.BumpSpeed(7) == StarProjectiles.BumpSpeed(1), "Bump speed is the dive's 0.2 to 0.3");
{
    var strikes = new StarStrikes();
    Check(strikes.TryStrike(0x0204A1D0, 1) && !strikes.TryStrike(0x0204A1D0, 1), "A star strikes each enemy once");
    Check(strikes.TryStrike(0x0204A1D0, 2) && strikes.TryStrike(0x0204A308, 1), "Other enemies and other stars still strike");
    strikes.Retain([0x0204A308]);
    Check(strikes.TryStrike(0x0204A1D0, 1), "A star that left the scene frees its address for a new star");
    Check(!strikes.TryStrike(0x0204A308, 1), "A star still present keeps its strikes");
}
var cook = new KirbyRules(["cook"], _ => 0);
cook.SetSourceAbility("cook");
Check(cook.ClaimCookFood(10, boss: false, elite: false), "Cooked ordinary enemy drops food");
Check(!cook.ClaimCookFood(10, boss: false, elite: false), "One cooked enemy cannot drop food twice");
Check(cook.ClaimCookFood(11, boss: false, elite: false), "Each cooked enemy drops its own food");
Check(!cook.ClaimCookFood(12, boss: true, elite: false), "Cooked boss drops no food");
Check(!cook.ClaimCookFood(13, boss: false, elite: true), "Cooked elite drops no food");
Check(!cook.ClaimCookFood(14, boss: true, elite: true), "Elite boss drops no food");
Check(cook.ClaimCookFood(13, boss: false, elite: false), "Excluded elite does not reserve its ID");
Check(cook.AbilityId == "cook" && cook.IsBaseForm == false, "Food claims do not change the copy form");
cook.ResetForLevel();
Check(cook.ClaimCookFood(10, boss: false, elite: false), "Entity IDs can drop food again after level transition");
cook.Restore("cook", null);
Check(cook.ClaimCookFood(10, boss: false, elite: false), "Restored slot clears food claims");
cook.Reset();
Check(cook.ClaimCookFood(10, boss: false, elite: false), "New run clears food claims");
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
foreach (double m in new[] { 3.5, 2.625, 1.75, 2.625 * 1.7, 2.625 * 2 * Math.Pow(1.15, 3), 1.75 * 3.2 * 2 * Math.Pow(1.15, 8) })
for (int hostLife = 1; hostLife <= 400; hostLife++)
{
    int sourceHp = ColorlessDamage.SourceHp(hostLife, m, 32760);
    for (int hit = 1; hit <= sourceHp + 2; hit++)
        if ((hit >= sourceHp) != (ColorlessDamage.HostDamage(hit, sourceHp, hostLife, m) >= hostLife))
            throw new Exception($"Combined lethality differs at x{m}, life {hostLife}, hit {hit}");
}
Check(true, "Lethality agrees with unit scale, item bonus and colorless tier combined");

// Kirby ×0.75 and the helper ×0.5 of the earlier 3.5 (#128).
int HitsToKill(int life, int hit, double kirbyScale, double hitScale)
{
    int hits = 0;
    while (life > 0)
    {
        // Source HP is re-derived from the host life each tick, at Kirby's scale.
        int sourceHp = ColorlessDamage.SourceHp(life, kirbyScale, 32760);
        life -= Math.Max(1, ColorlessDamage.HostDamage(hit, sourceHp, life, hitScale));
        hits++;
    }
    return hits;
}
Check(HitsToKill(120, 18, 3.5, 3.5) == 2, "Before #128 a Zombie took two Sword hits");
Check(HitsToKill(120, 18, 2.625, 2.625) == 3, "Kirby's Sword needs three hits on a Zombie at 2.625");
Check(HitsToKill(120, 18, 2.625, 1.75) == 4, "The helper's Sword needs four hits on a Zombie at 1.75");
Check(Math.Abs(HelperDamage.Ratio(2.625, 1.75) - 2.0 / 3) < 1e-12, "Helper ratio is 1.75 / 2.625");
Check(HelperDamage.Ratio(2.625, 0) == 1 && HelperDamage.Ratio(2.625, -1) == 1 && HelperDamage.Ratio(0, 1.75) == 1 &&
      HelperDamage.Ratio(double.NaN, 1.75) == 1 && HelperDamage.Ratio(2.625, double.PositiveInfinity) == 1,
    "An unset or invalid helper scale gives the helper Kirby's strength");
Check(HelperDamage.IsHelperHit(true, false) && !HelperDamage.IsHelperHit(true, true) &&
      !HelperDamage.IsHelperHit(false, true) && !HelperDamage.IsHelperHit(false, false),
    "A hit is the helper's only when no record of Kirby's touches the enemy");
Check(ColorlessDamage.HostDamage(18, 46, 120, 2.625) == 47 && ColorlessDamage.HostDamage(18, 46, 120, 1.75) == 31,
    "One Sword hit deals 47 for Kirby and 31 for the helper against 120 life");
Check(ColorlessDamage.HostDamage(18, 11, 27, 1.75) == 31, "A source-lethal helper hit still kills, as the original defeat is shared");

var elite = new KirbyRules(["fire", "sword"], _ => 0);
Check(elite.ConfirmAbsorption(1, "sword", elite: true) == "sword" && elite.PendingEmpowerment == "sword" &&
      !elite.AbilityEmpowered, "Elite in mouth is pending, not yet empowered");
elite.SetSourceAbility("sword");
Check(elite.AbilityEmpowered && elite.PendingEmpowerment is null, "Elite swallow empowers its Sword");
elite.SetSourceAbility("sword");
Check(elite.AbilityEmpowered, "Repeated source report keeps the empowerment");
elite.DropAbility();
Check(!elite.AbilityEmpowered, "Losing the copy ends the empowerment");
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

int Run(FramePacer pacer, double hz, int frames, List<int>? record = null)
{
    int total = 0;
    for (int i = 0; i < frames; i++) { int n = pacer.Steps(1 / hz); record?.Add(n); total += n; }
    return total;
}
var vsync = new FramePacer();
var perFrame = new List<int>();
Run(vsync, 59.96, 600, perFrame);
Check(vsync.Locked && perFrame.All(n => n == 1), "A 59.96 Hz display presents one original frame per host frame");
Check(new FramePacer().Steps(1 / 61.0) == 1 && new FramePacer().Steps(1 / 58.5) == 1, "Nearby refresh rates lock");
Check(vsync.Steps(2 / 59.96) == 2 && vsync.Steps(1 / 59.96) == 1 && vsync.Locked, "A host hitch catches up, then the lock resumes");
var fast = new FramePacer();
var fastSteps = new List<int>();
int fastTotal = Run(fast, 144, 1440, fastSteps);
Check(!fast.Locked && Math.Abs(fastTotal - FramePacer.DsRefreshRate * 10) <= 1 && fastSteps.All(n => n is 0 or 1),
    "A 144 Hz display keeps original real-time speed");
var slow = new FramePacer();
Check(Run(slow, 30, 300) is >= 597 and <= 600, "A 30 Hz host runs two original frames per host frame");
var paused = new FramePacer();
Check(paused.Steps(double.NaN) == 0 && paused.Steps(5) <= 15, "Invalid and long host deltas are bounded");

// Moving frames are airborne and still frames walk, unless a test gives the action.
ushort[] Feed(DashAssist assist, params (ushort Mask, bool Enabled, int Vx, int Vy)[] frames) =>
    frames.Select(f => assist.Filter(f.Mask, f.Enabled, f.Vx, f.Vy, f.Vy != 0 ? KirbyActions.Fall : KirbyActions.Walk)).ToArray();
var dash = new DashAssist();
var started = Feed(dash, (16, true, 0, 0), (16, true, 50, 0), (16, true, 100, 0), (16, true, 150, 0));
Check(started.SequenceEqual(new ushort[] { 16, 0, 16, 16 }), "A new press becomes the original press-release-press double tap");
var landing = Feed(dash, (17, true, 352, -900), (16, true, 368, -400), (16, true, 368, 300), (16, true, 352, 0), (16, true, 352, 0), (16, true, 352, 0), (16, true, 400, 0));
Check(landing.SequenceEqual(new ushort[] { 17, 16, 16, 16, 16, 0, 16 }), "Landing while walking re-taps once after two still frames");
var dashLanding = new DashAssist();
Feed(dashLanding, (16, true, 0, 0), (16, true, 0, 0), (16, true, 528, 0));
var kept = Feed(dashLanding, (16, true, 528, -600), (16, true, 528, 200), (16, true, 528, 0), (16, true, 528, 0), (16, true, 528, 0));
Check(kept.All(m => m == 16), "A dash jump lands dashing without an extra release");
var apex = new DashAssist();
Feed(apex, (16, true, 0, 0), (16, true, 0, 0));
var air = Feed(apex, (16, true, 368, -300), (16, true, 368, 0), (16, true, 368, 200), (16, true, 368, 400));
Check(air.All(m => m == 16), "A single zero-velocity apex frame is not a landing");
var exact = new DashAssist();
Check(Feed(exact, (144, true, 0, 0), (144, true, 0, 0)).All(m => m == 144) &&
      Feed(exact, (528, true, 0, 0), (528, true, 0, 0)).All(m => m == 528), "Down and guard combinations keep exact input");
var walk = new DashAssist();
Check(Feed(walk, (16, false, 0, 0), (16, false, 50, 0), (16, false, 100, 0)).All(m => m == 16), "A light analog tilt walks");
var turn = new DashAssist();
var turned = Feed(turn, (16, true, 0, 0), (16, true, 0, 0), (16, true, 528, 0), (32, true, 528, 0), (32, true, 300, 0), (32, true, -100, 0));
Check(turned.SequenceEqual(new ushort[] { 16, 0, 16, 32, 0, 32 }), "Turning around double-taps the new direction");
// Ninja's wall cling (0x712) stands still in the air; the original lets go when the direction is released.
const ushort WallCling = 0x0712;
var cling = new DashAssist();
Feed(cling, (16, true, 0, 0), (16, true, 0, 0), (17, true, 368, -600), (16, true, 368, 400));
var clung = Enumerable.Range(0, 30).Select(_ => cling.Filter(16, true, 0, 0, WallCling)).ToArray();
var dropped = new[] { cling.Filter(16, true, 0, 300, KirbyActions.Fall) }.Concat(
    new[] { KirbyActions.Landing, KirbyActions.Landing, KirbyActions.Landing, KirbyActions.Walk }.Select(a => cling.Filter(16, true, 352, 0, a))).ToArray();
Check(clung.All(m => m == 16) && dropped.SequenceEqual(new ushort[] { 16, 16, 16, 0, 16 }),
    "A held wall cling keeps its direction; the landing after it still re-taps");

byte[] Palette(params ushort[] banks)
{
    var bytes = new byte[512];
    for (int bank = 0; bank < banks.Length; bank++)
        for (int i = 0; i < 16; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bank * 32 + i * 2), (ushort)(banks[bank] + i));
    return bytes;
}
ushort Entry(byte[] bytes, int bank, int i) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bank * 32 + i * 2));
var soft = new PaletteFlashSoftener(2, 0.35);
ushort normal = 0x7B5F, flash = 0x3DEF;
for (int i = 0; i < 10; i++) soft.Apply(Palette(normal, normal));
var shown = new List<ushort>();
for (int frame = 0; frame < 60; frame++)
{
    bool on = frame % 6 < 3;
    var bytes = Palette(on ? flash : normal, normal);
    soft.Apply(bytes);
    shown.Add(Entry(bytes, 0, 1));
}
ushort tinted = PaletteFlashSoftener.Blend((ushort)(normal + 1), (ushort)(flash + 1), 0.35);
Check(shown.All(v => v == normal + 1 || v == tinted) && shown.Contains(tinted) && !shown.Contains((ushort)(flash + 1)),
    "A 3-on/3-off flash is shown as a tint and never reaches the flash palette");
var lasting = new List<ushort>();
for (int frame = 0; frame < 8; frame++) { var bytes = Palette(flash, normal); soft.Apply(bytes); lasting.Add(Entry(bytes, 0, 1)); }
Check(lasting.Take(3).All(v => v == tinted) && lasting.Skip(3).All(v => v == flash + 1), "A lasting palette change is adopted after four frames");
var guard = new List<ushort>();
for (int frame = 0; frame < 40; frame++) { bool on = frame % 4 < 2; var bytes = Palette(on ? normal : flash, normal); soft.Apply(bytes); guard.Add(Entry(bytes, 0, 1)); }
Check(guard.All(v => v == flash + 1 || v == PaletteFlashSoftener.Blend((ushort)(flash + 1), (ushort)(normal + 1), 0.35)), "A 2-on/2-off alternation against the new stable palette is tinted");
var untouched = Palette(normal, flash, 0x1234); var copy = (byte[])untouched.Clone();
new PaletteFlashSoftener(2, 1).Apply(untouched);
Check(untouched.SequenceEqual(copy), "Strength 1 leaves the palette unchanged");
var off = new PaletteFlashSoftener(1, 0);
off.Apply(Palette(normal)); var hidden = Palette(flash); off.Apply(hidden);
Check(Entry(hidden, 0, 5) == normal + 5, "Strength 0 shows the stable palette");
Check(PaletteFlashSoftener.Blend(0, 0x7FFF, 0.5) == (16 | 16 << 5 | 16 << 10), "Blend works per RGB555 channel");

var gate = new RollZoneGate();
Check(!gate.Update(sliding: false, inside: false), "Roll zones are solid while walking outside them");
Check(!gate.Update(sliding: false, inside: true), "Walking into a roll zone does not open it");
Check(gate.Update(sliding: true, inside: false), "A slide opens roll zones before reaching them");
Check(gate.Update(sliding: true, inside: true), "Sliding into a roll zone keeps it open");
Check(gate.Update(sliding: false, inside: true), "A slide that ends inside a roll zone leaves it open");
Check(!gate.Update(sliding: false, inside: false), "Leaving the roll zone closes it again");
Check(!gate.Update(sliding: false, inside: true), "Re-entering without a slide stays closed");
gate.Update(true, true); gate.Reset();
Check(!gate.Open && !gate.Update(false, true), "Reset forgets an entered roll zone");

var hostLanding = new HostLanding();
Check(!hostLanding.Update(KirbyActions.Standing, 0) && !hostLanding.Update(KirbyActions.Walk, 0), "Standing and walking are not landings");
Check(!hostLanding.Update(KirbyActions.Jump, -2682) && !hostLanding.Update(KirbyActions.Jump, -744), "A rising jump has not landed");
Check(!hostLanding.Update(KirbyActions.Fall, 44) && hostLanding.Update(KirbyActions.Landing, 0), "Kirby landing from a fall raises the host landing once");
Check(!hostLanding.Update(KirbyActions.Standing, 0), "The landing is not repeated while he stands");
Check(!hostLanding.Update(KirbyActions.Jump, -744) && !hostLanding.Update(KirbyActions.Fall, 44) &&
      !hostLanding.Update(KirbyActions.Ability, 0) && hostLanding.Update(KirbyActions.Standing, 0), "A fall interrupted by an ability still lands");
Check(!hostLanding.Update(KirbyActions.Walk, 0) && !hostLanding.Update(KirbyActions.Ability, 0) && !hostLanding.Update(KirbyActions.Standing, 0),
    "A ground ability is not a landing");
Check(!hostLanding.Update(KirbyActions.Jump, -744) && !hostLanding.Update(KirbyActions.Climb, 0) && !hostLanding.Update(KirbyActions.Standing, 0),
    "Stepping off a ladder is not a landing");
Check(!hostLanding.Update(KirbyActions.Fall, 44) && !hostLanding.Update(KirbyActions.Climb, 0) && !hostLanding.Update(KirbyActions.Standing, 0),
    "Grabbing a ladder mid-fall forgets the fall");
Check(!hostLanding.Update(KirbyActions.Fall, 44) && hostLanding.Update(KirbyActions.Slide, 0), "Landing into a slide counts");
hostLanding.Update(KirbyActions.Fall, 44); hostLanding.Reset();
Check(!hostLanding.Update(KirbyActions.Standing, 0), "Reset forgets a fall in progress");
var slideHold = new SlideHold();
byte readSteps = 8, readPhase = 11;
(byte, byte) Counters() => (readSteps, readPhase);
const ushort Slide = KirbyActions.Slide;
Check(slideHold.Begin(Slide, 704, false, false, Counters) is null && !slideHold.Holding, "A slide on open floor is left to the original");
Check(slideHold.Begin(KirbyActions.Walk, 704, true, false, Counters) is null && !slideHold.Holding, "Walking in a roll zone is not held");
Check(slideHold.Begin(Slide, 704, true, false, Counters) is (8, 11) && slideHold.Holding, "A slide entering a tunnel keeps the counters it had");
readSteps = 7; readPhase = 3;
Check(slideHold.Begin(Slide, 704, true, false, Counters) is (8, 11), "The held counters stay as they were on entering");
Check(slideHold.Begin(Slide, 704, false, false, Counters) is (SlideHold.LastFastStep, 0) && !slideHold.Holding,
    "Leaving the tunnel in the full-speed part goes on to the slowdown");
Check(slideHold.Begin(Slide, 704, false, false, Counters) is null, "Only the frame the slide is let go changes the counters");
readSteps = 4; readPhase = 9;
Check(slideHold.Begin(Slide, 640, true, false, Counters) is (4, 9) && slideHold.Holding, "A slide entering a tunnel in its slowdown is held too");
Check(slideHold.Begin(Slide, 640, false, false, Counters) is null, "Leaving the tunnel in the slowdown resumes the original's own counts");
readSteps = 8; readPhase = 11;
slideHold.Begin(Slide, 704, true, false, Counters); slideHold.Finish(moved: false);
Check(slideHold.Begin(Slide, 704, true, false, Counters) is null && !slideHold.Holding, "A slide that stops advancing is against a wall and is let go");
Check(slideHold.Begin(Slide, 704, true, false, Counters) is null, "A let-go slide is not taken up again inside the same tunnel");
Check(slideHold.Begin(KirbyActions.Standing, 0, true, false, Counters) is null &&
      slideHold.Begin(Slide, 704, true, false, Counters) is (8, 11), "A new slide in the tunnel is held again");
Check(slideHold.Begin(Slide, 704, true, true, Counters) is null && !slideHold.Holding, "A hit during a held slide goes to the original");
Check(slideHold.Begin(Slide, 0, true, false, Counters) is null, "The stopped frames of a slide's end are not held");
slideHold.Begin(KirbyActions.Standing, 0, false, false, Counters); readSteps = 8;
slideHold.Begin(Slide, 704, true, false, Counters); slideHold.Finish(moved: true);
Check(slideHold.Holding, "A held slide that advances stays held");
slideHold.Reset();
Check(!slideHold.Holding && slideHold.Begin(Slide, 704, false, false, Counters) is null, "Reset forgets a held slide");

// Elevator at centre column 10, four cells wide, cell row 10; its solid row is 11 (top 264).
Check(ElevatorRide.Over(10, 264, 10, 4, 10, 1, 0, false) is { Lift: 0, Raise: 0, Carrying: false }, "An elevator at rest needs no lift and carries nothing");
Check(ElevatorRide.Over(10, 264, 10, 4, 10, 1, 0.5, false) is { Lift: 0, Carrying: true }, "An elevator that arrived this frame still carried Kirby");
Check(default(ElevatorRide.Ride) is { Carrying: false }, "Away from elevators nothing carries Kirby");
var riding = ElevatorRide.Over(10, 261.6, 10, 4, 10, 0.9, 2.4, false);
Check(riding is { } r1 && Math.Abs(r1.Lift + 2.4) < 1e-9 && r1.Raise == 0 && Math.Abs(261.6 - r1.Lift - 264) < 1e-9,
    "Kirby carried to a moving surface stands on the lifted solid row");
Check(ElevatorRide.Over(10, 264, 10, 4, 10, 0.9, 2.4, false) is { } r2 && Math.Abs(r2.Raise - 2.4) < 1e-9,
    "Kirby left on the solid row is raised back onto the surface");
Check(ElevatorRide.Over(10, 256, 10, 4, 9, 0.5, 30, false) is { Raise: 28, Lift: -12, Carrying: true },
    "A rising elevator that passed Kirby's feet since the last frame catches him");
Check(ElevatorRide.Over(10, 256, 10, 4, 9, 0.5, 0, false) is null && ElevatorRide.Over(10, 256, 10, 4, 9, 0.5, -30, false) is null, "A surface that did not rise past Kirby does not catch him");
Check(ElevatorRide.Over(10, 200, 10, 4, 10, 0.5, 0, false) is { Raise: 0, Lift: -12 }, "A jump above a moving elevator keeps its floor lifted");
Check(ElevatorRide.Over(10, 252 - ElevatorRide.Reach - 1, 10, 4, 10, 0.5, 0, false) is null, "High above an elevator the window is not lifted");
Check(ElevatorRide.Over(8, 252, 10, 4, 10, 0.5, 0, false) is not null && ElevatorRide.Over(12, 252, 10, 4, 10, 0.5, 0, false) is null &&
      ElevatorRide.Over(7, 252, 10, 4, 10, 0.5, 0, false) is null, "Across, Kirby rides where the host carries an entity");
Check(ElevatorRide.Over(10, 241, 10, 4, 10, 0, 0, false) is { Lift: -24 }, "A surface at the top of its cell is a whole cell above the solid row");
// Descending 10 px since the last frame, from 252 to 262: Kirby left at 252 is carried down to it.
Check(ElevatorRide.Over(10, 252, 10, 4, 10, 262.0 / 24 - 10, -10, false) is { } carriedDown && Math.Abs(carriedDown.Raise + 10) < 1e-9 && carriedDown.Carrying,
    "Kirby standing on a descending elevator is carried down with it");
Check(ElevatorRide.Over(10, 252, 10, 4, 10, 262.0 / 24 - 10, -10, true) is { Raise: 0 },
    "A jump from a descending elevator is not pulled back down");
Check(ElevatorRide.Over(10, 247, 10, 4, 10, 262.0 / 24 - 10, -10, false) is { Raise: 0 },
    "Feet more than 2 px above the previous surface are not carried down");
Check(ElevatorRide.Over(10, 252, 10, 4, 10, 240.0 / 24 - 10, 12, false) is { } carriedUp && Math.Abs(carriedUp.Raise - 12) < 1e-9,
    "Kirby standing on a rising elevator is carried up with it");
// A stop set into level floor: the elevator overshot its rest (row 106 top, 2544) by 4.92 px into the solid row 106.
double overshot = 2544 + 4.92;
Check(ElevatorRide.Effective(106, 4.92 / 24, sunk: true) == 2544 && ElevatorRide.Effective(106, 4.92 / 24, sunk: false) == overshot,
    "An elevator sunk into a floor offers the floor's top");
Check(ElevatorRide.Over(10, 2544, 10, 4, 106, 4.92 / 24, 0, false, sunk: true) is { Lift: 0, Raise: 0 },
    "Kirby stays on the floor's top while the elevator overshoots into it");
Check(ElevatorRide.Over(10, overshot, 10, 4, 106, 4.92 / 24, 0, false, sunk: true) is { Lift: 0 } intoFloor && Math.Abs(intoFloor.Raise - 4.92) < 1e-9,
    "Kirby the host carried into the floor is put back on its top");
Check(ElevatorRide.Over(10, overshot, 10, 4, 106, 4.92 / 24, 0, false) is { } followed && Math.Abs(followed.Lift - (4.92 - 24)) < 1e-9,
    "Without a floor under the overshoot, Kirby follows the elevator");

var doorWalk = new CinematicWalk();
Check(doorWalk.Update(100, 1 / 60.0) == 0, "A cinematic starts with Kirby standing");
Check(doorWalk.Update(101.5, 1 / 60.0) == 1, "The host walking the hero right holds right");
Check(doorWalk.Update(101.5, 1 / 60.0) == 1, "A rendered frame between host updates keeps walking");
for (int frame = 0; frame < 12; frame++) doorWalk.Update(101.5, 1 / 60.0);
Check(doorWalk.Direction == 0, "Kirby stops once the host stops moving him");
Check(doorWalk.Update(99, 1 / 60.0) == -1, "The host walking the hero left holds left");
Check(doorWalk.Update(400, 1 / 60.0) == 0, "A host placement is not a walk");
doorWalk.Update(401, 1 / 60.0); doorWalk.Reset();
Check(doorWalk.Direction == 0 && doorWalk.Update(500, 1 / 60.0) == 0, "Reset forgets the previous position");

Check(KirbyActions.CanStomp(KirbyActions.Jump) && KirbyActions.CanStomp(KirbyActions.Fall) && KirbyActions.CanStomp(KirbyActions.Float),
    "Stomps start from rising, falling and floating");
Check(!KirbyActions.CanStomp(KirbyActions.Standing) && !KirbyActions.CanStomp(KirbyActions.Slide) && !KirbyActions.CanStomp(KirbyActions.Ability),
    "No stomp on the ground, while sliding or during an exhale or inhale");
Check(KirbyActions.CanSlide(KirbyActions.Walk) && !KirbyActions.CanSlide(KirbyActions.Fall), "Slides start on the ground only");
var slideKey = new SlideKey();
Check(slideKey.Filter(16, true, KirbyActions.Dash, 16) == 145 && slideKey.Filter(16, true, KirbyActions.Slide, 145) == 16 &&
    slideKey.Filter(16, true, KirbyActions.Slide, 16) == 16, "The slide key sends Down + jump once per press, keeping the held direction");
slideKey.Reset();
Check(slideKey.Filter(0, true, KirbyActions.Fall, 0) == 0 && slideKey.Filter(0, true, KirbyActions.Standing, 0) == 0 &&
    slideKey.Filter(0, false, KirbyActions.Standing, 0) == 0 && slideKey.Filter(0, true, KirbyActions.Standing, 0) == 129,
    "A slide key press in the air does nothing and is not kept for landing");
slideKey.Reset();
Check(slideKey.Filter(1, true, KirbyActions.Standing, 1) == 128 && slideKey.Filter(1, true, KirbyActions.Standing, 128) == 129,
    "With jump already held, the slide key releases it for a frame so the jump is a new press");
slideKey.Reset();
Check(slideKey.Filter(64, true, KirbyActions.Standing, 0) == 129, "The slide key's Down replaces a held Up");
Check(PadStick.Directions(0.9, -0.3, 0.2, 0.2, true) == 16 && PadStick.Directions(0.9, -0.3, 0.2, 0.2, false) == 144,
    "A sideways stick that drifts down only runs; read axis by axis it also held Down");
Check(PadStick.Directions(0.7, -0.7, 0.2, 0.2, true) == 144 && PadStick.Directions(-0.7, 0.7, 0.2, 0.2, true) == 96,
    "A diagonal stick sends both directions");
Check(PadStick.Directions(0.2, -0.9, 0.1, 0.1, true) == 128 && PadStick.Directions(-0.2, 0.9, 0.1, 0.1, true) == 64,
    "A vertical stick that drifts sideways does not turn or walk");
Check(PadStick.Directions(0, -0.4, 0.2, 0.2, true) == 0 && PadStick.Directions(0, -0.6, 0.2, 0.2, true) == 128 &&
      PadStick.Directions(0.15, 0, 0.2, 0.2, true) == 0 && PadStick.Directions(-0.3, 0, 0.2, 0.2, true) == 32,
    "Vertical needs a clear tilt; horizontal keeps the host dead zone, so a light tilt still walks");
var discardHold = new HeldButton(0.3);
Check(!discardHold.Update(true, 10) && !discardHold.Update(true, 10.2) && discardHold.Update(true, 10.3) && discardHold.Update(true, 11),
    "A held button acts once it has been held long enough");
Check(!discardHold.Update(false, 11.1) && !discardHold.Update(true, 11.2) && !discardHold.Update(false, 11.3) && !discardHold.Update(true, 11.6),
    "A released button starts its hold again, so taps never add up");
Check(HostStrikes.Slide(KirbyActions.Slide, -704) && !HostStrikes.Slide(KirbyActions.Slide, 384) && !HostStrikes.Slide(KirbyActions.Dash, 704),
    "Only the slide's fast part strikes like a roll");
Check(HostStrikes.Grab(HostStrikes.Suplex, SpeedMatch.Carry, -1766) && !HostStrikes.Grab(HostStrikes.Suplex, SpeedMatch.Carry, 300) &&
      !HostStrikes.Grab(HostStrikes.Suplex, KirbyActions.Dash, 528) && !HostStrikes.Grab(18, SpeedMatch.Carry, 1766),
    "Only Suplex's fast grab lunge bursts doors");
var plantLaunch = new BumpLaunch();
Check(!plantLaunch.Active && plantLaunch.Ascent(100) == 0, "No ascent without a bump");
var plantSteps = BumpLaunch.Steps(1.5 * 24).ToArray();
// Lab run 20261013-202552: the Beheaded's steps off a closed plant, in cells.
double[] beheadedSteps = [1.43500, 1.28650, 1.14925, 1.02234, 0.90494, 0.79629, 0.69570, 0.60252, 0.51618,
    0.43613, 0.36190, 0.29303, 0.22910, 0.16975, 0.11461, 0.06338, 0.01576];
Check(plantSteps.Length == beheadedSteps.Length && plantSteps.Zip(beheadedSteps).All(p => Math.Abs(p.First / 24 - p.Second) < 1e-5),
    "The launch rises by the Beheaded's recorded steps off a plant");
Check(Math.Abs(plantSteps.Sum() - 242.217) < 0.01, "The plant throws Kirby the Beheaded's 242 px");
plantLaunch.Start(1.5 * 24);
double launched = 0; int launchFrames = 0; double firstAscent = plantLaunch.Ascent(double.PositiveInfinity);
launched += firstAscent; launchFrames++;
while (plantLaunch.Active && launchFrames < 600) { launched += plantLaunch.Ascent(double.PositiveInfinity); launchFrames++; }
Check(Math.Abs(firstAscent - 1.435 * 12) < 1e-9, "Each fixed step's rise is spread over two source frames");
Check(launchFrames == 34 && Math.Abs(launched - plantSteps.Sum()) < 1e-9, "The launch lasts the Beheaded's 17 steps and ends at the top");
plantLaunch.Start(1.5 * 24);
Check(Math.Abs(plantLaunch.Ascent(5) - (5 - BumpLaunch.CeilingMargin)) < 1e-9 && !plantLaunch.Active && plantLaunch.Ascent(100) == 0,
    "A ceiling stops the launch a margin short and ends it");
plantLaunch.Start(1.5 * 24); plantLaunch.Ascent(100); plantLaunch.Start(1.5 * 24);
Check(Math.Abs(plantLaunch.Ascent(100) - firstAscent) < 1e-9, "A second bump starts again from rest");
plantLaunch.Start(1.5 * 24); plantLaunch.Reset();
Check(!plantLaunch.Active, "A reset ends the launch");
plantLaunch.Start(0.05 * 24);
Check(!plantLaunch.Active, "A bump weaker than gravity does not launch");
Check(BumpLaunch.AirJumpReach(213, 36, 1, 213, 35, 0.5) && BumpLaunch.AirJumpReach(213, 36, 1, 215, 35, 0.01) &&
      BumpLaunch.AirJumpReach(213, 36, 1, 211, 36, 0.2), "An air jump up to two cells aside and 48 px above a plant is in its reach");
Check(!BumpLaunch.AirJumpReach(213, 36, 1, 216, 35, 0.5) && !BumpLaunch.AirJumpReach(213, 36, 1, 213, 34, 0.9) &&
      !BumpLaunch.AirJumpReach(213, 36, 1, 213, 37, 0.5), "Farther aside, higher, or below the plant is out of reach");
// Lab run 20261013-203833: a closed plant (radius 24, height 24, feet 3324) first touched the falling
// Beheaded (radius 16.8, height 54) 34 px aside between feet 3315.3 and 3326.5; run 202552, centred, between 846.4 and 856.6 (plant feet 876).
Check(!BumpLaunch.Touches(0, 3324, 24, 24, 34, 3315.3, BumpLaunch.BeheadedRadius, BumpLaunch.BeheadedHeight) && BumpLaunch.Touches(0, 3324, 24, 24, 34, 3326.5, 16.8, 54) &&
      !BumpLaunch.Touches(0, 876, 24, 24, 0, 846.4, 16.8, 54) && BumpLaunch.Touches(0, 876, 24, 24, 0, 856.6, 16.8, 54),
    "A closed plant touches the Beheaded's body where the lab recorded it");
Check(BumpLaunch.Touches(0, 876, 24, 24, 30, 864, 16.8, 54) && !BumpLaunch.Touches(0, 876, 24, 24, 31, 864, 16.8, 54),
    "Standing on a closed plant, its touch reaches 30 px to each side");
Check(Math.Abs(BumpLaunch.Steps(0.5 * 24).Sum() - 34.4) < 0.05, "The bite's bump is the Beheaded's 34 px hop");
var stompRun = new StompController();
Check(!stompRun.TryStart(KirbyActions.Standing, 480), "A grounded Kirby cannot stomp");
Check(stompRun.TryStart(KirbyActions.Fall, 400) && stompRun.Active, "A falling Kirby stomps");
Check(!stompRun.TryStart(KirbyActions.Fall, 400), "A stomp cannot restart while active");
Check(stompRun.Mask(1 | 128, KirbyActions.Fall) == 0, "Original buttons are released during a falling stomp");
Check(stompRun.Descent(100, KirbyActions.Fall) == StompController.ExtraDescent, "Full extra descent in open air");
Check(stompRun.Descent(8, KirbyActions.Fall) == 3.5 && stompRun.Descent(4, KirbyActions.Fall) == 0, "Descent leaves the original a full fall step to make the floor contact");
Check(Math.Abs(3.5 + StompController.ExtraDescent - 15) < 1e-9, "The stomp falls 15 px a frame, the Beheaded's dive");
Check(stompRun.Advance(landed: false, interrupted: false) == StompController.Outcome.None && stompRun.Active, "Still diving");
Check(Math.Abs(stompRun.Height(472) - 3) < 1e-9, "Fall height is reported in host cells");
Check(stompRun.Advance(landed: true, interrupted: false) == StompController.Outcome.Landed && !stompRun.Active, "Landing ends the stomp");
Check(stompRun.Descent(100, KirbyActions.Fall) == 0 && stompRun.Mask(1, KirbyActions.Fall) == 1, "An idle stomp leaves descent and buttons alone");
var floatStomp = new StompController();
Check(floatStomp.TryStart(KirbyActions.Float, 300), "A floating Kirby stomps");
Check(floatStomp.Mask(1, KirbyActions.Float) == StompController.ButtonB && floatStomp.Mask(1, KirbyActions.Ability) == 0, "A floating Kirby exhales once, then falls");
Check(floatStomp.Advance(false, interrupted: true) == StompController.Outcome.Cancelled && !floatStomp.Active, "A hit cancels the stomp");
var chainStomp = new StompController();
Check(KirbyActions.CanStomp(KirbyActions.Climb) && chainStomp.TryStart(KirbyActions.Climb, 300), "Kirby stomps from a ladder or chain");
Check(chainStomp.Mask(128 | 1, KirbyActions.Climb) == StompController.ButtonA && chainStomp.Mask(128 | 1, KirbyActions.Jump) == 0,
    "The original A kicks him off once, then buttons are released");
var vineTopStomp = new StompController();
vineTopStomp.TryStart(KirbyActions.Climb, 300);
// Climbing down from a ladder top, the original ignores A for its first frames (#74).
Check(vineTopStomp.Mask(128 | 1, KirbyActions.Climb) == StompController.ButtonA && vineTopStomp.Mask(128 | 1, KirbyActions.Climb) == 0 &&
      vineTopStomp.Mask(128 | 1, KirbyActions.Climb) == StompController.ButtonA && vineTopStomp.Mask(128 | 1, KirbyActions.Fall) == 0,
    "While the original still climbs, A is pressed every other frame until it lets go");
Check(vineTopStomp.Descent(100, KirbyActions.Climb) == 0 && vineTopStomp.Descent(100, KirbyActions.Fall) == StompController.ExtraDescent,
    "No host descent carries a Kirby who still climbs");
var fallStomp = new StompController();
fallStomp.TryStart(KirbyActions.Fall, 300);
Check(fallStomp.Mask(1, KirbyActions.Fall) == 0, "A falling Kirby needs no releasing press");
var stuck = new StompController();
stuck.TryStart(KirbyActions.Jump, 0);
var outcome = StompController.Outcome.None;
for (int frame = 0; frame <= StompController.MaxFrames && outcome == StompController.Outcome.None; frame++) outcome = stuck.Advance(false, false);
Check(outcome == StompController.Outcome.Cancelled && stuck.Frames == 0, "A stomp that never lands is cancelled");

{
// Movement matched to the Beheaded. Terrain: floor at row 30 (y 720), walls and
// slopes placed per test; Kirby's original motion per frame is given as input.
ushort MFlat(int cx, int cy) => cy >= 30 ? SpeedMatch.Solid : SpeedMatch.Free;
const double RunPx = SpeedMatch.RunSpeed;
bool MNear(double a, double b, double e = 1e-6) => Math.Abs(a - b) < e;
var mDash = SpeedMatch.Move(KirbyActions.Dash, 1, 528, 300, 720, 302.0625, 720, MFlat);
Check(MNear(mDash.X, 300 + RunPx) && mDash.Feet == 720, "A dash runs at the Beheaded's 236.5 px/s");
var mWalk = SpeedMatch.Move(KirbyActions.Walk, 1, 352, 300, 720, 301.375, 720, MFlat);
Check(MNear(mWalk.X - 300, 1.375 * SpeedMatch.GroundScale), "A light tilt walks at the same share of that speed");
Check(SpeedMatch.Move(KirbyActions.Walk, 0, 512, 300, 720, 302, 720, MFlat).X == 302 &&
      SpeedMatch.Move(KirbyActions.Skid, -1, 496, 300, 720, 301.9, 720, MFlat).X == 301.9, "Skids and unasked motion keep their original speed");
var mAir = SpeedMatch.Move(KirbyActions.Jump, 1, 528, 300, 700, 302.0625, 698, MFlat);
Check(MNear(mAir.X, 300 + RunPx) && mAir.Feet == 698, "A running jump keeps the run speed; the rise is the original's");
var mDrift = SpeedMatch.Move(KirbyActions.Fall, 1, 368, 300, 600, 301.4375, 603.5, MFlat);
Check(MNear(mDrift.X, 300 + RunPx) && MNear(mDrift.Feet, 600 + 3.5 * SpeedMatch.FallScale), "Air control at run speed; falls at the Beheaded's 403 px/s");
var mGlide = SpeedMatch.Move(KirbyActions.Float, -1, -272, 300, 600, 298.9375, 601.125, MFlat);
Check(MNear(mGlide.X, 300 - RunPx) && MNear(mGlide.Feet, 600 + 1.125 * SpeedMatch.FlightScale), "Floating crosses at run speed and sinks at the same tempo");
var mSlideFast = SpeedMatch.Move(KirbyActions.Slide, 0, 704, 300, 720, 302.75, 720, MFlat);
var mSlideSlow = SpeedMatch.Move(KirbyActions.Slide, 0, 384, 300, 720, 301.5, 720, MFlat);
Check(MNear(mSlideFast.X - 300, 2.75 * 2.05) && MNear(mSlideSlow.X - 300, 1.5 * 1.5), "Slides move like the Beheaded's roll");
Check(MNear(16 * 2.75 * SpeedMatch.SlideFastScale + 27 * SpeedMatch.SlideSlowScale, 130.7, 0.1), "A whole slide covers the roll's 131 px");
ushort MLadder(int cx, int cy) => cy >= 30 ? SpeedMatch.Solid : cx == 12 ? SpeedMatch.Ladder : SpeedMatch.Free;
var mUp = SpeedMatch.Move(KirbyActions.Climb, 0, 0, 300, 600, 300, 600 - 592 / 256.0, MLadder);
var mDown = SpeedMatch.Move(KirbyActions.Climb, 0, 0, 300, 600, 300, 600 + 704 / 256.0, MLadder);
Check(MNear((600 - mUp.Feet) * 60, 592 / 256.0 * 60 * SpeedMatch.GroundScale) && MNear((mDown.Feet - 600) * 60, 704 / 256.0 * 60 * SpeedMatch.GroundScale) && mUp.X == 300,
    "Ladders climb as much faster than the original as the run");
// The added climb stops a full step short of a ladder's ends, its one-way top and ceilings.
ushort MTopped(int cx, int cy) => cy >= 30 ? SpeedMatch.Solid : cx != 12 || cy < 20 ? SpeedMatch.Free : cy == 20 ? SpeedMatch.OneWay : SpeedMatch.Ladder;
Check(SpeedMatch.Move(KirbyActions.Climb, 0, 0, 300, 487, 300, 487 - 592 / 256.0, MTopped).Feet == 484 && SpeedMatch.Move(KirbyActions.Climb, 0, 0, 300, 483, 300, 483 - 592 / 256.0, MTopped).Feet == 483 - 592 / 256.0,
    "Climbing up stops a step short of the one-way top; the original climbs onto it");
Check(SpeedMatch.Move(KirbyActions.Climb, 0, 0, 300, 712, 300, 712 + 704 / 256.0, MTopped).Feet == 720 - SpeedMatch.LandingMargin &&
    MNear(SpeedMatch.Move(KirbyActions.Climb, 0, 0, 300, 480, 300, 480 + 704 / 256.0, MTopped).Feet, 480 + 704 / 256.0 * SpeedMatch.GroundScale),
    "Climbing down from the one-way top speeds up and stops a step above the floor");
ushort MCapped(int cx, int cy) => cy >= 30 ? SpeedMatch.Solid : cx == 12 && cy >= 20 ? SpeedMatch.Ladder : cy == 19 ? SpeedMatch.Solid : SpeedMatch.Free;
Check(SpeedMatch.Move(KirbyActions.Climb, 0, 0, 300, 502, 300, 502 - 592 / 256.0, MCapped).Feet == 480 + SpeedMatch.Height + SpeedMatch.Margin,
    "Climbing up stops short of a solid block above the ladder");
Check(SpeedMatch.Move(0x0B1A, 1, 512, 300, 700, 302, 690, MFlat) == (302, 690), "Other actions (hits, abilities, swimming) keep their original motion");
// Contacts stay with the original collision.
ushort MWall(int cx, int cy) => cy >= 30 || cx == 18 ? SpeedMatch.Solid : SpeedMatch.Free;
var mWall = SpeedMatch.Move(KirbyActions.Dash, 1, 528, 420, 720, 422.0625, 720, MWall);
Check(MNear(mWall.X, 432 - SpeedMatch.HalfWidth - SpeedMatch.Margin), "Added running stops a pixel short of a wall");
Check(SpeedMatch.Move(KirbyActions.Dash, 1, 528, 421.4, 720, 423.5, 720, MWall).X == 423.5, "Touching a wall adds nothing");
var mLanding = SpeedMatch.Move(KirbyActions.Fall, 0, 0, 300, 710, 300, 713.5, MFlat);
Check(MNear(mLanding.Feet, 720 - SpeedMatch.LandingMargin), "Added falling leaves the original a full fall step to land");
ushort MCeiling(int cx, int cy) => cy >= 30 || cy == 25 ? SpeedMatch.Solid : SpeedMatch.Free;
var mHead = SpeedMatch.Move(KirbyActions.Float, 0, 0, 300, 646, 300, 644, MCeiling);
Check(MNear(mHead.Feet, 624 + SpeedMatch.Height + SpeedMatch.Margin), "Added rising stops a pixel below a ceiling");
ushort MPlatform(int cx, int cy) => cy >= 30 ? SpeedMatch.Solid : cy == 28 ? SpeedMatch.OneWay : SpeedMatch.Free;
Check(SpeedMatch.Move(KirbyActions.Float, 0, 0, 300, 690, 300, 688, MPlatform).Feet < 688 &&
      MNear(SpeedMatch.Move(KirbyActions.Fall, 0, 0, 300, 664, 300, 667.5, MPlatform).Feet, 672 - SpeedMatch.LandingMargin),
    "One-way platforms let Kirby up through them and catch him from above");
Check(SpeedMatch.Move(KirbyActions.Fall, 0, 0, 300, 600, 300, 603.5, MLadder).Feet > 603.5, "Ladders never stop a fall");
ushort MPool(int cx, int cy) => cy >= 30 ? SpeedMatch.Solid : cy >= 27 ? SpeedMatch.Water : SpeedMatch.Free;
Check(MNear(SpeedMatch.Move(KirbyActions.Fall, 0, 0, 300, 640, 300, 643.5, MPool).Feet, 648 - SpeedMatch.LandingMargin), "Kirby enters water at the original speed");
// Host water (#144): the surface is in pixels; row 39 spans 936 to 960.
Check(!SpeedMatch.Submerged(0, 39), "A cell without host water is not original water");
Check(!SpeedMatch.Submerged(948, 39) && !SpeedMatch.Submerged(942, 39), "Kirby walks through water 12 or 18 px deep (a partly filled cell)");
Check(SpeedMatch.Submerged(936, 39) && SpeedMatch.Submerged(918, 39) && !SpeedMatch.Submerged(918, 38),
    "Kirby swims in the cells a pool fills, not in its partly filled top row");
// Slopes: the original follows them from Kirby's centre at whole pixel columns.
ushort MRamp(int cx, int cy) => cy >= 30 || (cx >= 17 && cy >= 29) ? SpeedMatch.Solid : (cx, cy) == (16, 29) ? SpeedMatch.SlopeUpRight : (cx, cy) == (17, 28) ? SpeedMatch.SlopeUpRight : SpeedMatch.Free;
Check(SpeedMatch.Surface(MRamp, 389.625, 714, 3) == 714 && SpeedMatch.Surface(MRamp, 402, 701, 3) == 701 &&
      SpeedMatch.Surface(MRamp, 414.375, 689, 3) == 689, "Slope surface matches the original's samples");
var mClimb = SpeedMatch.Move(KirbyActions.Dash, 1, 528, 398, 706, 400.0625, 704, MRamp);
Check(MNear(mClimb.X, 398 + RunPx) && mClimb.Feet == SpeedMatch.Surface(MRamp, mClimb.X, 704, 3), "Running up a slope stays on its surface");
var mDescent = SpeedMatch.Move(KirbyActions.Dash, -1, -528, 412, 691, 409.9375, 693, MRamp);
Check(MNear(mDescent.X, 412 - RunPx) && mDescent.Feet == SpeedMatch.Surface(MRamp, mDescent.X, 693, 3), "Running down a slope stays on its surface");
// Velocities set before an original frame.
Check(SpeedMatch.Brakes(KirbyActions.Dash, 0, 528) && SpeedMatch.Brakes(KirbyActions.Walk, -1, 400) && SpeedMatch.Brakes(KirbyActions.Fall, 0, 368),
    "Releasing or reversing the direction stops Kirby, as it stops the Beheaded");
Check(!SpeedMatch.Brakes(KirbyActions.Dash, 1, 528) && !SpeedMatch.Brakes(KirbyActions.Slide, 0, 704) && !SpeedMatch.Brakes(0x0B1A, 0, 300),
    "Held directions, slides and recoils keep their velocity");
Check(!SpeedMatch.Brakes(KirbyActions.Ability, 0, 2048) && SpeedMatch.Move(KirbyActions.Ability, 1, 2048, 300, 700, 308, 698, MFlat) == (308, 698),
    "Ability moves slower than the dash, not started from one, keep their original velocity and motion");
{
    var actions = new SpeedMatch.ActionFilter();
    actions.Next(KirbyActions.Float);
    bool exhale = actions.Next(KirbyActions.Ability) == KirbyActions.Float && actions.Next(KirbyActions.Ability) == KirbyActions.Float;
    bool after = actions.Next(KirbyActions.Standing) == KirbyActions.Standing && actions.Next(KirbyActions.Ability) == KirbyActions.Ability;
    Check(exhale && after, "The exhale ending a float moves like floating; an attack from the ground keeps the ability action");
    actions.Reset();
    actions.Next(KirbyActions.Dash, -528);
    bool attack = actions.Next(KirbyActions.Ability, 256) == SpeedMatch.Carry && actions.Direction(1) == -1 &&
                  actions.Next(KirbyActions.Ability, 0) == SpeedMatch.Carry && actions.Direction(0) == -1;
    bool ends = actions.Next(KirbyActions.Standing) == KirbyActions.Standing && actions.Direction(1) == 1 &&
                actions.Next(KirbyActions.Ability) == KirbyActions.Ability && actions.Direction(0) == 0;
    Check(attack && ends, "An ability used straight from a dash carries Kirby in the dash's direction until it ends");
    actions.Reset();
    bool belowDash = actions.Next(KirbyActions.Ability, 256) == KirbyActions.Ability && actions.Next(KirbyActions.Ability, -528) == KirbyActions.Ability;
    bool roll = actions.Next(KirbyActions.Ability, -896) == SpeedMatch.Carry && actions.Direction(1) == -1 &&
                actions.Next(KirbyActions.Ability, 100) == SpeedMatch.Carry && actions.Direction(0) == -1 &&
                actions.Next(KirbyActions.Ability, 896) == SpeedMatch.Carry && actions.Direction(-1) == 1;
    Check(belowDash && roll, "An ability move faster than the dash (Wheel's roll) carries Kirby, turning with him");
}
{
    // #166: with the running move the exhale and the wade are their own moves.
    var actions = new SpeedMatch.ActionFilter();
    actions.Next(KirbyActions.Float, 0, 0x02131298);
    bool exhale = actions.Next(KirbyActions.Ability, 0, KirbyMoves.Exhale) == KirbyActions.Float;
    actions.Next(KirbyActions.Float, 0, 0x02131298);
    bool notExhale = actions.Next(KirbyActions.Ability, 0, 0x0212F80C) == KirbyActions.Ability;
    actions.Reset();
    bool landed = actions.Next(KirbyActions.Landing, 0, 0x021310D0) == KirbyActions.Landing &&
                  actions.Next(KirbyActions.Ability, 0, KirbyMoves.Exhale) == KirbyActions.Float;
    Check(exhale && notExhale && landed, "The exhale is its own move, after a float or on landing; another move after a float is not one");
    actions.Reset();
    bool wade = actions.Next(KirbyActions.Walk, 240, KirbyMoves.Wade) == SpeedMatch.Wade &&
                actions.Next(KirbyActions.Walk, 352, KirbyMoves.Walk) == KirbyActions.Walk &&
                actions.Next(KirbyActions.Walk, 352, KirbyMoves.MouthfulWalk) == KirbyActions.Walk;
    Check(wade && SpeedMatch.Classify(SpeedMatch.Wade) == SpeedMatch.Motion.None && !SpeedMatch.Brakes(SpeedMatch.Wade, 0, 240) &&
          SpeedMatch.Move(SpeedMatch.Wade, 1, 240, 300, 720, 300.9375, 720, MFlat) == (300.9375, 720),
        "Walking on the bottom of water keeps the original wade, unscaled and unbraked; dry walks stay walks");
    actions.Reset();
    actions.Next(KirbyActions.Float);
    Check(actions.Next(KirbyActions.Ability) == KirbyActions.Float && actions.Next(KirbyActions.Walk, 240) == KirbyActions.Walk,
        "Without a move (the helper, an older bridge) the exhale follows a float and the walk is a walk");
}
{
    // #166: a copy attack press uses the copy's item once the original starts one of the copy's moves.
    const uint Sword = 0x0212F7EC, Cutter = 0x02129DE8;
    uint Move(uint table, int index) => table + (uint)index * 24;
    Check(KirbyMoves.IsCopyMove(Move(Sword, 3), Sword) && KirbyMoves.IsCopyMove(Move(0x02130B44, 1), 0x02130B44) &&
          !KirbyMoves.IsCopyMove(0x021314D8, Sword) && !KirbyMoves.IsCopyMove(KirbyMoves.Exhale, Sword) &&
          !KirbyMoves.IsCopyMove(Move(Sword, 3), 0) && !KirbyMoves.IsCopyMove(Move(Sword, 3) + 4, Sword) &&
          !KirbyMoves.IsCopyMove(Sword - 24, Sword) && !KirbyMoves.IsCopyMove(KirbyMoves.Shared, 0x02130B44),
        "Copy moves are the copy's own table entries, below the moves every form shares");
    // Replays one ability's measured frames: press frames and (frame, move) changes.
    int Uses(uint table, int[] presses, (int Frame, uint Move)[] changes, int frames)
    {
        var tracker = new CopyAttackPresses();
        uint move = 0x021314D8;
        int uses = 0;
        for (int f = 0, c = 0; f < frames; f++)
        {
            while (c < changes.Length && changes[c].Frame == f) move = changes[c++].Move;
            if (tracker.Next(presses.Contains(f), move, table)) uses++;
        }
        return uses;
    }
    int[] rapid = [0, 8, 16, 24, 32, 40, 48, 56];
    // Sword, B every 8 frames: three slashes (the second and third start 5 and 7 frames after a press).
    Check(Uses(Sword, rapid, [(0, Move(Sword, 3)), (13, Move(Sword, 4)), (39, Move(Sword, 5)), (93, 0x021314D8)], 120) == 3,
        "Eight Sword presses that make a three-slash combo use the item three times");
    // Sword, B every 16 frames as the live affix probe presses it: nine slashes in three combos, the
    // third slash of each 10 frames after its press, and a new combo straight from the last slash.
    int[] pulse = [.. Enumerable.Range(0, 15).Select(i => 8 + i * 16)];
    Check(Uses(Sword, pulse, [(8, Move(Sword, 3)), (24, Move(Sword, 4)), (50, Move(Sword, 5)), (88, Move(Sword, 3)), (104, Move(Sword, 4)),
        (130, Move(Sword, 5)), (168, Move(Sword, 3)), (184, Move(Sword, 4)), (210, Move(Sword, 5))], 240) == 9,
        "Fifteen Sword presses, one every 16 frames, use the item once for each of the nine slashes they start");
    Check(Uses(Sword, [25], [(24, Move(Sword, 4)), (50, Move(Sword, 5))], 60) == 1, "A press made during a slash waits for the next one");
    // Fighter's jabs stay in one move however often B is pressed.
    const uint Fighter = 0x0212AD20;
    Check(Uses(Fighter, rapid, [(1, Move(Fighter, 3)), (107, 0x021314D8)], 120) == 1, "Fighter's repeated jabs, one move, use it once");
    // Cutter: a throw per press from a stand, the catch (index 11) without one.
    Check(Uses(Cutter, [0, 24, 48], [(0, Move(Cutter, 3)), (16, 0x021314D8), (24, Move(Cutter, 3)), (40, 0x021314D8), (48, Move(Cutter, 3)),
        (64, 0x021314D8), (77, Move(Cutter, 11)), (85, 0x021314D8)], 120) == 3, "Each Cutter throw uses it; the catch that follows by itself does not");
    // Beam, B every 8 frames: two whips, each a wind-up then the whip; the presses in between do nothing.
    const uint Beam = 0x0212A818;
    Check(Uses(Beam, rapid, [(0, Move(Beam, 7)), (1, Move(Beam, 3)), (37, 0x021314D8), (40, Move(Beam, 7)), (41, Move(Beam, 3)), (77, 0x021314D8)], 120) == 2,
        "Beam's wind-up and whip are one use, and a press that did nothing does not add one");
    // Jet, B every 8 frames: a burst and its follow-up for each press from a stand.
    const uint Jet = 0x0212EF6C;
    Check(Uses(Jet, rapid, [(0, Move(Jet, 3)), (1, Move(Jet, 4)), (19, 0x021314D8), (24, Move(Jet, 3)), (25, Move(Jet, 4)), (43, 0x021314D8),
        (48, Move(Jet, 3)), (49, Move(Jet, 4)), (67, 0x021314D8)], 120) == 3, "Each Jet burst is one use");
    // Presses that start nothing: in a recoil, during a change, in water (the water gun is shared).
    Check(Uses(Sword, [0, 20], [(0, 0x02130CF4), (30, 0x021314D8)], 60) == 0 &&
          Uses(Sword, [0], [(0, KirbyMoves.WaterGun), (30, 0x021314D8)], 60) == 0, "A press that starts no copy move uses nothing");
    // A move that starts after the window is not the press's.
    Check(Uses(Sword, [0], [(CopyAttackPresses.Window, Move(Sword, 3))], 30) == 0 &&
          Uses(Sword, [0], [(CopyAttackPresses.Window - 1, Move(Sword, 3))], 30) == 1, "A press waits ten frames for its move");
    var waitingPresses = new CopyAttackPresses();
    waitingPresses.Next(true, 0x021314D8, Sword);
    waitingPresses.Drop();
    Check(!waitingPresses.Next(false, Move(Sword, 3), Sword), "Presses dropped for an F press start nothing");
}
var mAttack = SpeedMatch.Move(SpeedMatch.Carry, 1, 1024, 300, 720, 304, 720, MFlat);
Check(MNear(mAttack.X - 300, 4 * SpeedMatch.GroundScale) && mAttack.Feet == 720 && !SpeedMatch.Brakes(SpeedMatch.Carry, 0, 1024) &&
      SpeedMatch.Move(SpeedMatch.Carry, 1, -256, 300, 720, 299, 720, MFlat).X == 299,
    "A carrying move keeps its lead over the run whatever is held; motion back keeps its original speed");
var mHop = SpeedMatch.Move(SpeedMatch.Carry, 1, 384, 300, 720, 301.5, 719.1, MFlat);
Check(MNear(mHop.X - 300, 1.5 * SpeedMatch.GroundScale) && mHop.Feet == 719.1 &&
      MNear(SpeedMatch.Move(SpeedMatch.Carry, 1, 512, 420, 720, 422, 720, MWall).X, 432 - SpeedMatch.HalfWidth - SpeedMatch.Margin),
    "A carrying move keeps the original's hops and stops a pixel short of a wall");
Check(SpeedMatch.RiseVelocity(KirbyActions.Jump, -2682) == -2682 && SpeedMatch.RiseVelocity(KirbyActions.Jump, -744) == SpeedMatch.JumpRiseVelocity &&
      SpeedMatch.RiseVelocity(KirbyActions.Jump, -940) == -940 + SpeedMatch.JumpGravity && SpeedMatch.RiseVelocity(KirbyActions.Jump, 100) == 100 &&
      SpeedMatch.RiseVelocity(KirbyActions.Float, -546) == -546, "The jump keeps its take-off, then rises and peaks at the Beheaded's tempo");
{
    // Original rise after take-off: 26.8 px over four frames, then this.
    int vy = SpeedMatch.JumpBurstEnd, frames = 4; double rise = 10.477 + 7.953 + 5.430 + 2.906;
    while (true)
    {
        vy = SpeedMatch.RiseVelocity(KirbyActions.Jump, vy) + 44;
        if (vy >= 0) break;
        rise += -vy / 256.0; frames++;
    }
    Check(Math.Abs(rise - 49.9) < 0.5 && frames == 15, "A jump keeps Kirby's 50 px and peaks after 0.25 s");
}
{
    // The original walk cycle: eight poses of 2 and 3 frames, each counted down
    // to zero; the average cycle over ten cycles, in frames.
    double Cycle(double rate)
    {
        var pacer = new AnimationPacer();
        int[] durations = [2, 3, 2, 3, 2, 3, 2, 3];
        int pose = 0, frames = 0, poses = 0; byte count = (byte)(durations[0] - 1);
        while (poses < 80)
        {
            count = pacer.Next(rate, count);
            frames++;
            if (count == 0) { pose = (pose + 1) % 8; count = (byte)(durations[pose] - 1); poses++; }
            else count--;
        }
        return frames / 10.0;
    }
    Check(Cycle(1) == 20 && Math.Abs(Cycle(SpeedMatch.GroundScale) - 12) < 0.5 && Cycle(1.25) == 16 && Cycle(3) == 12,
        "The walk cycle plays faster in proportion to the matched speed, down to one frame a pose");
    var pacer = new AnimationPacer();
    Check(Enumerable.Range(0, 10).All(_ => pacer.Next(3, 0) == 0), "A pose about to change is never counted below zero");
}
}
{
    // Sub OAM entry 0 is hidden, 1 is a 32×32 OBJ elsewhere, 2 is the icon at (4, 4).
    byte[] iconOam = new byte[1024];
    void Obj(int index, ushort a0, ushort a1, ushort a2)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(iconOam.AsSpan(index * 8), a0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(iconOam.AsSpan(index * 8 + 2), a1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(iconOam.AsSpan(index * 8 + 4), a2);
    }
    for (int index = 3; index < 128; index++) Obj(index, 0x0200, 0, 0);
    Obj(0, 0x0204, 0x8004, 7);
    Obj(1, 0x0004, 0x8024, 9);
    Obj(2, 0x0004, 0x8004, (ushort)(8 << 12 | 52));
    const uint subDisplay = 0x40111710; // 1D OBJ mapping with a 64-byte tile boundary.
    Check(AbilityIcon.Find(subDisplay, iconOam) == new AbilityIcon.Sprite(52 * 64, 8, false, false),
        "Find the 32×32 icon OBJ at (4, 4) with its tile address and palette");
    Check(AbilityIcon.Find(subDisplay & ~0x10u, iconOam) is null, "Reject 2D OBJ mapping");
    Obj(2, 0x0004, 0x9004, (ushort)(8 << 12 | 52));
    Check(AbilityIcon.Find(subDisplay, iconOam)?.FlipX == true, "Report a horizontally flipped icon");
    Obj(2, 0x2004, 0x8004, 52);
    Check(AbilityIcon.Find(subDisplay, iconOam) is null, "Ignore an 8bpp OBJ");

    byte[] tiles = new byte[AbilityIcon.TileBytes], iconPalette = new byte[32], icon = new byte[AbilityIcon.Size * AbilityIcon.Size * 4];
    tiles[0] = 0x21;          // (0,0) colour 1, (1,0) colour 2.
    tiles[5 * 32 + 3] = 0x30; // Tile (1,1) row 0: (15,8) colour 3.
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(iconPalette.AsSpan(2), 0x001F);  // Red.
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(iconPalette.AsSpan(4), 0x7C00);  // Blue.
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(iconPalette.AsSpan(6), 0x03E0);  // Green.
    int Pixel(int x, int y) => (x + y * AbilityIcon.Size) * 4;
    AbilityIcon.Decode(new AbilityIcon.Sprite(0, 0, false, false), tiles, iconPalette, icon);
    Check(icon.AsSpan(Pixel(0, 0), 4).SequenceEqual(new byte[] { 255, 0, 0, 255 }) &&
          icon.AsSpan(Pixel(1, 0), 4).SequenceEqual(new byte[] { 0, 0, 255, 255 }) &&
          icon.AsSpan(Pixel(15, 8), 4).SequenceEqual(new byte[] { 0, 255, 0, 255 }), "Decode 1D 4bpp tiles and BGR555 colours");
    Check(icon.AsSpan(Pixel(2, 0), 4).SequenceEqual(new byte[4]), "Colour 0 is transparent");
    AbilityIcon.Decode(new AbilityIcon.Sprite(0, 0, true, true), tiles, iconPalette, icon);
    Check(icon.AsSpan(Pixel(31, 31), 4).SequenceEqual(new byte[] { 255, 0, 0, 255 }) &&
          icon.AsSpan(Pixel(16, 23), 4).SequenceEqual(new byte[] { 0, 255, 0, 255 }), "Apply both OBJ flips");
}
{
    // OAM is read over the original builder's range, screen x -63..256 and
    // y -63..192, and culled pieces come from the bridge (#67). Kirby at (128, 100).
    byte[] oam = new byte[1024], vram = new byte[256 * 1024], palette = new byte[512];
    byte[] rgba = new byte[Kirby.Runtime.NdsObjRenderer.CanvasWidth * Kirby.Runtime.NdsObjRenderer.CanvasHeight * 4];
    void Obj(int index, ushort a0, ushort a1, ushort a2)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(oam.AsSpan(index * 8), a0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(oam.AsSpan(index * 8 + 2), a1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(oam.AsSpan(index * 8 + 4), a2);
    }
    for (int index = 0; index < 128; index++) Obj(index, 0x0200, 0, 0);
    vram.AsSpan(0, 32).Fill(0x11); // Tile 0: colour 1 everywhere.
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(palette.AsSpan(32 + 2), 0x001F); // Bank 1: red.
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(palette.AsSpan(64 + 2), 0x7C00); // Bank 2: blue.
    int At(int screenX, int screenY) =>
        ((Kirby.Runtime.NdsObjRenderer.CenterY + screenY - 100) * Kirby.Runtime.NdsObjRenderer.CanvasWidth +
         Kirby.Runtime.NdsObjRenderer.CenterX + screenX - 128) * 4;
    bool Drawn(int screenX, int screenY = 100) => rgba[At(screenX, screenY) + 3] == 255;
    void Draw(params Kirby.Runtime.FarObject[] far) => Kirby.Runtime.NdsObjRenderer.Render(0x10, oam, vram, palette, rgba, 128, 100, far: far);
    Obj(0, 100, 256, 1 << 12);
    Draw();
    Check(Drawn(256) && Drawn(263), "OAM x 256, the builder's right limit, is drawn right of the DS screen, not wrapped");
    Obj(0, 100, 460, 1 << 12);
    Draw();
    Check(Drawn(-52) && Drawn(-45), "OAM x 460 is screen x -52");
    Obj(0, 0xF8, 0, 1 << 12);
    Draw();
    Check(!Drawn(0, -8), "An unused OAM entry (y 0xF8, attr1 0) is not drawn");
    Obj(0, 100, 200, 1 << 12);
    Draw(new Kirby.Runtime.FarObject { X = 600, Y = 100, Attr0 = 100, Attr1 = 600 & 511, Attr2 = 1 << 12, Order = 1 });
    Check(Drawn(600) && Drawn(607) && Drawn(200), "A culled piece at screen x 600 is drawn 472 px right of Kirby");
    Draw(new Kirby.Runtime.FarObject { X = 200, Y = 100, Attr0 = 100, Attr1 = 200, Attr2 = 2 << 12, Order = 0 });
    Check(rgba[At(200, 100) + 2] == 255 && rgba[At(200, 100)] == 0, "A culled piece written before OAM entry 0 draws in front of it");
    Draw(new Kirby.Runtime.FarObject { X = 200, Y = 100, Attr0 = 100, Attr1 = 200, Attr2 = 2 << 12, Order = 1 });
    Check(rgba[At(200, 100)] == 255 && rgba[At(200, 100) + 2] == 0, "A culled piece written after OAM entry 0 draws behind it");
}
{
    // The runtime keeps one canvas and clears only what the last drawing covered (#123).
    // Every frame of a changing scene must equal a drawing onto a fully cleared canvas.
    var random = new Random(123);
    byte[] oam = new byte[1024], vram = new byte[256 * 1024], palette = new byte[512];
    int size = Kirby.Runtime.NdsObjRenderer.CanvasWidth * Kirby.Runtime.NdsObjRenderer.CanvasHeight * 4;
    byte[] clearedCanvas = new byte[size], keptCanvas = new byte[size];
    random.NextBytes(vram); random.NextBytes(palette);
    var drawn = Kirby.Runtime.NdsObjRenderer.Area.Canvas;
    bool same = true, partial = false, affine = false, emptied = false;
    int drew = 0;
    for (int frame = 0; frame < 300 && same; frame++)
    {
        random.NextBytes(oam); // Also the affine matrices, which share OAM.
        for (int index = 0; index < 128; index++) oam.AsSpan(index * 8, 6).Clear();
        for (int index = 0; index < 128; index++) oam[index * 8 + 1] = 0x02; // Hidden.
        int pieces = frame % 7 == 0 ? 0 : random.Next(1, 14);
        for (int n = 0; n < pieces; n++)
        {
            int index = random.Next(128);
            ushort a0 = (ushort)(random.Next(256) | (random.Next(4) == 0 ? 0x100 | random.Next(2) << 9 : 0) |
                (random.Next(8) == 0 ? 0x400 : 0) | random.Next(3) << 14);
            ushort a1 = (ushort)(random.Next(512) | random.Next(32) << 9 | random.Next(4) << 14);
            ushort a2 = (ushort)(random.Next(1024) | random.Next(4) << 10 | random.Next(8) << 12);
            affine |= (a0 & 0x100) != 0;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(oam.AsSpan(index * 8), a0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(oam.AsSpan(index * 8 + 2), a1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(oam.AsSpan(index * 8 + 4), a2);
        }
        var far = new Kirby.Runtime.FarObject[frame % 5 == 0 ? random.Next(1, 6) : 0];
        for (int n = 0; n < far.Length; n++)
            far[n] = new() { X = (short)random.Next(-700, 900), Y = (short)random.Next(-400, 500), Attr0 = (ushort)(random.Next(3) << 14),
                Attr1 = (ushort)(random.Next(4) << 14 | random.Next(4) << 12), Attr2 = (ushort)(random.Next(1024) | random.Next(8) << 12),
                Order = (ushort)random.Next(129) };
        int x = random.Next(-40, 300), y = random.Next(-40, 230);
        uint display = (uint)(random.Next(2) << 4 | random.Next(4) << 20);
        var expected = Kirby.Runtime.NdsObjRenderer.Render(display, oam, vram, palette, clearedCanvas, x, y, far: far);
        var expectedBody = (Kirby.Runtime.NdsObjRenderer.LastBodyPixels, Kirby.Runtime.NdsObjRenderer.LastBodyBounds);
        var actual = Kirby.Runtime.NdsObjRenderer.Render(display, oam, vram, palette, keptCanvas.AsSpan(), ref drawn, x, y, far: far);
        same = expected == actual && expectedBody == (Kirby.Runtime.NdsObjRenderer.LastBodyPixels, Kirby.Runtime.NdsObjRenderer.LastBodyBounds) &&
            clearedCanvas.AsSpan().SequenceEqual(keptCanvas);
        partial |= !drawn.IsEmpty && (drawn.Right - drawn.Left) * (drawn.Bottom - drawn.Top) < size / 16;
        emptied |= drawn.IsEmpty;
        if (clearedCanvas.AsSpan().ContainsAnyExcept((byte)0)) drew++;
    }
    Check(same, "A kept canvas cleared only where it was drawn equals a fully cleared one on every frame");
    Check(drew > 200 && partial && affine && emptied, "The keptCanvas-canvas comparison covers drawn, partly covered, affine and empty frames");
}
{
    // Same IDs and order as KirbyNdsProvider.Abilities (original abilities 2..25).
    string[] abilityIds = ["Cutter", "Mirror", "Beam", "Fighter", "Suplex", "YoYo", "Wheel", "Bomb", "Ice", "Fire", "Plasma", "Ninja",
        "Stone", "Wing", "Jet", "Copy", "Sword", "Hammer", "Parasol", "Paint", "Mike", "Crash", "Cook", "Sleep"];
    Check(abilityIds[HostStrikes.Suplex - 2] == "Suplex", "HostStrikes uses Suplex's original ability ID");
    Check(abilityIds.All(AbilityDescriptions.Summaries.ContainsKey) && AbilityDescriptions.Summaries.Count == abilityIds.Length,
        "Every copy ability has exactly one description");
    Check(AbilityDescriptions.Summaries.Values.All(text => text.Length is > 20 and <= 110),
        "Descriptions stay short enough for the pause card");
    // The host's ui.Text treats braces, @ and angle brackets as markup.
    Check(AbilityDescriptions.Summaries.Values.Append(AbilityDescriptions.Controls).All(text => text.IndexOfAny(['{', '}', '@', '<', '>']) < 0),
        "Descriptions contain no host text markup");
    Check(AbilityDescriptions.Describe("Fire") == AbilityDescriptions.Summaries["Fire"] + "\n" + AbilityDescriptions.Controls,
        "A description ends with the shared controls line");
    Check(AbilityDescriptions.Describe("Unknown") == AbilityDescriptions.Controls, "An unknown ability still shows the controls");
}
{
    // Kirby ability items (#55).
    Check(AbilityItems.ItemId("Fire") == "KirbyAbilityFire" && AbilityItems.AbilityOf("KirbyAbilityFire") == "Fire",
        "Ability item IDs map to their ability and back");
    Check(AbilityItems.AbilityOf("StartSword") is null && AbilityItems.AbilityOf("KirbyAbility") is null &&
          AbilityItems.AbilityOf(null) is null, "Other item IDs are not ability items");
    Check(AbilityItems.DisplayName("YoYo") == "Yo-Yo" && AbilityItems.DisplayName("Fire") == "Fire", "Yo-Yo keeps its hyphen");
    Check(AbilityItems.Replaces(weapon: true, skill: false, shield: false, abilityItem: false), "A floor sword or bow becomes an ability item");
    Check(AbilityItems.Replaces(weapon: false, skill: true, shield: false, abilityItem: false), "A floor skill becomes an ability item");
    Check(!AbilityItems.Replaces(weapon: true, skill: false, shield: true, abilityItem: false), "A floor shield stays");
    Check(!AbilityItems.Replaces(weapon: false, skill: false, shield: false, abilityItem: false), "Amulets, food and other items stay");
    Check(!AbilityItems.Replaces(weapon: true, skill: false, shield: false, abilityItem: true), "An ability item is never replaced again");

    // Host rule, checked against host cards: "Rusty Sword I+" shows "+33% damage".
    Check(AbilityItems.InfoLine(AbilityItems.HostDamageBonus(1, 1, false)) == "+33% copy damage", "Level I+ gives +33%");
    Check(AbilityItems.HostDamageBonus(1, 0, false) == 0 && AbilityItems.InfoLine(0) == "Copy ability", "A level I base item adds nothing");
    Check(AbilityItems.InfoLine(AbilityItems.HostDamageBonus(2, 0, false)) == "+4% copy damage", "Level II adds a little");
    Check(AbilityItems.InfoLine(AbilityItems.HostDamageBonus(3, 0, false)) == "+33% copy damage", "Level III adds a stat bonus");
    Check(AbilityItems.HostDamageBonus(2, 2, false) > AbilityItems.HostDamageBonus(2, 1, false) &&
          AbilityItems.HostDamageBonus(2, 1, false) > AbilityItems.HostDamageBonus(2, 0, false), "Each + raises the bonus");
    Check(Math.Abs(AbilityItems.HostDamageBonus(1, 0, true) - 1.25) < 1e-9 && AbilityItems.HostDamageBonus(1, 3, true) == AbilityItems.HostDamageBonus(1, 0, true),
        "Legendary is level + 6 regardless of quality");
    Check(AbilityItems.Slot([SlotContent.Other, SlotContent.Empty]) == 0, "The copy replaces an unusable host weapon first");
    Check(AbilityItems.Slot([SlotContent.Empty, SlotContent.Other]) == 1, "An unusable weapon is replaced before an empty slot is used");
    Check(AbilityItems.Slot([SlotContent.Shield, SlotContent.Empty], retainedShieldSlot: 0) == 1, "The copy takes the empty slot beside a shield");
    Check(AbilityItems.Slot([SlotContent.Empty, SlotContent.Ability]) == 1, "The slot already holding the copy's item is kept");
    Check(AbilityItems.Slot([SlotContent.Shield, SlotContent.Shield], retainedShieldSlot: 1) == 0, "A second shield yields its slot");
    Check(AbilityItems.Slot([SlotContent.Shield], retainedShieldSlot: 0) == -1, "The retained shield is never replaced");
    Check(AbilityItems.SpareShields([SlotContent.Shield, SlotContent.Shield], retainedShieldSlot: 1).SequenceEqual([0]), "A newly taken second shield replaces the one Kirby had");
    Check(AbilityItems.SpareShields([SlotContent.Shield, SlotContent.Shield], retainedShieldSlot: 0).SequenceEqual([1]), "The retained shield stays whichever slot it is in");
    Check(AbilityItems.SpareShields([SlotContent.Shield, SlotContent.Shield]).SequenceEqual([1]), "With no retained shield the first one stays");
    Check(AbilityItems.SpareShields([SlotContent.Shield, SlotContent.Empty], retainedShieldSlot: 0).Count == 0 &&
          AbilityItems.SpareShields([SlotContent.Ability, SlotContent.Shield], retainedShieldSlot: 1).Count == 0 &&
          AbilityItems.SpareShields([SlotContent.Other, SlotContent.Empty]).Count == 0, "One shield, or none, is left alone");

    object first = new(), second = new(), third = new();
    var mouth = new MouthfulItems<object>();
    mouth.Add(first, "Fire");
    mouth.Add(first, "Fire");
    mouth.Add(second, "Ice");
    Check(mouth.Count == 2, "An item enters the mouth once");
    var (taken, returned) = mouth.Resolve("Ice");
    Check(ReferenceEquals(taken, second) && returned.Count == 1 && ReferenceEquals(returned[0], first) && mouth.Count == 0,
        "The swallow takes the item of the copied ability; the others go back");
    mouth.Add(first, "Fire");
    mouth.Add(third, "Fire");
    (taken, returned) = mouth.Resolve("Fire");
    Check(ReferenceEquals(taken, first) && returned.Count == 1 && ReferenceEquals(returned[0], third), "Of two equal items the first is taken");
    mouth.Add(first, "Fire");
    (taken, returned) = mouth.Resolve(null);
    Check(taken is null && returned.Count == 1, "A mouthful lost without a copy returns every item");
    mouth.Add(first, "Fire");
    mouth.Observe(mouthFull: false, baseForm: true);
    Check(!mouth.Emptied, "The mouth is not empty before the original mouthful is seen");
    mouth.Observe(mouthFull: true, baseForm: true);
    mouth.Observe(mouthFull: false, baseForm: false);
    Check(!mouth.Emptied, "A copied Kirby has swallowed, not spat");
    mouth.Observe(mouthFull: false, baseForm: true);
    Check(mouth.Emptied, "The mouth emptying in base form is a spit");
    mouth.Resolve(null);
    mouth.Observe(mouthFull: false, baseForm: true);
    Check(!mouth.Emptied && mouth.Count == 0, "Settling clears the spit state");
}
{
    // A dropped ability star keeps its item for 7 seconds (#68).
    Check(AbilityStar.Shortened(480) == 420 && AbilityStar.Shortened(479) == 419, "A new star loses 60 of its original 480 frames");
    Check(AbilityStar.Shortened(420) is null && AbilityStar.Shortened(1) is null, "A shortened star is never shortened again");
    const uint StarAt = 0x02049FFC, OtherAt = 0x0204A098;
    const byte Sword = 18, Fire = 11;
    static StarSighting Seen(uint address, byte ability, int countdown, byte state = 1) => new(address, ability, countdown, state);
    object item = new();

    var stars = new StarItems<object>();
    stars.Observe([Seen(StarAt, Sword, 480)]);
    Check(stars.NewStars.Count == 1 && stars.NewStars[0].Countdown == 480, "A new star is reported with its countdown");
    Check(stars.Lose(item, Sword) == StarAt && ReferenceEquals(stars.ItemOf(StarAt), item) && stars.Carried == 1,
        "The lost copy's item joins the star dropped with it");
    stars.Observe([Seen(StarAt, Sword, 419)]);
    Check(stars.NewStars.Count == 0 && stars.Carried == 1, "The shortened star is the same star");
    for (int countdown = 418; countdown >= 1; countdown--) stars.Observe([Seen(StarAt, Sword, countdown)]);
    Check(stars.Carried == 1 && stars.Gone.Count == 0, "The star carries its item to its last frame");
    stars.Observe([]);
    Check(stars.Gone.Count == 1 && ReferenceEquals(stars.Gone[0], item) && stars.Carried == 0, "A star that runs out takes its item with it");

    stars = new StarItems<object>();
    Check(stars.Lose(item, Sword) is null && stars.Waiting == 1, "An item lost before its star appears waits for it");
    stars.Observe([Seen(StarAt, Sword, 480)]);
    Check(stars.Paired.Count == 1 && stars.Paired[0].Star == StarAt && stars.Waiting == 0, "The star appearing next frame takes the waiting item");
    stars.Observe([Seen(StarAt, Sword, 300, AbilityStar.Caught)]);
    Check(stars.Caught.Count == 0 && stars.Lose(new object(), Sword) is null, "A caught star is not over while its record lasts, and takes no other item");
    stars.Observe([]);
    Check(stars.Caught.Count == 1 && ReferenceEquals(stars.Caught[0], item) && stars.Gone.Count == 0,
        "The original inhale catching the star hands its item over");

    stars = new StarItems<object>();
    stars.Observe([Seen(StarAt, Sword, 420)]);
    stars.Lose(item, Sword);
    for (int i = 0; i < 3; i++) stars.Observe([]);
    stars.Observe([Seen(StarAt, Sword, 400)]);
    Check(ReferenceEquals(stars.ItemOf(StarAt), item) && stars.Gone.Count == 0, "A star missing from the records for a few frames keeps its item");
    int missing = 0;
    while (stars.Gone.Count == 0 && missing < 50) { stars.Observe([]); missing++; }
    Check(missing == StarItems<object>.MissingFrames + 1, "A star missing for longer is gone with its item");

    stars = new StarItems<object>();
    stars.Observe([Seen(OtherAt, Fire, 480)]);
    stars.Lose(item, Sword);
    int waited = 0;
    while (stars.Gone.Count == 0 && waited < 50) { stars.Observe([Seen(OtherAt, Fire, 479 - waited)]); waited++; }
    Check(waited == StarItems<object>.PairFrames && stars.ItemOf(OtherAt) is null,
        "With no star of its copy the item is gone; another copy's star never takes it");

    stars = new StarItems<object>();
    for (int i = 0; i < 30; i++) stars.Observe([Seen(StarAt, Sword, 420 - i)]);
    Check(stars.Lose(item, Sword) is null, "A star dropped long before the loss does not take the item");

    stars = new StarItems<object>();
    stars.Observe([Seen(StarAt, Sword, 480)]);
    stars.Lose(item, Sword);
    stars.Observe([Seen(StarAt, Fire, 480)]);
    Check(stars.Gone.Count == 1 && stars.ItemOf(StarAt) is null && stars.NewStars.Count == 1, "A new star at a freed address is not the old one");
    stars.Lose(item, Fire);
    stars.Clear();
    Check(stars.Carried == 0 && stars.Waiting == 0, "Clearing forgets every star and item");
}

// Copy critical hits (#88).
{
    var crit = new CopyCritical();
    Check(crit.Chance == 0 && crit.DamageMultiplier == 1.5 && !crit.Roll(() => 0), "Kirby starts with no crit chance and a ×1.5 crit");
    crit.BaseChance = 0.25;
    Check(crit.Roll(() => 0.24) && !crit.Roll(() => 0.25), "A roll below the chance is a crit");
    crit.AddChance(0.1, 2); crit.AddChance(0.1, 5);
    Check(Math.Abs(crit.Chance - 0.45) < 1e-12, "Timed chance bonuses add up");
    crit.Advance(2);
    Check(Math.Abs(crit.Chance - 0.35) < 1e-12, "A timed bonus ends when its time is up");
    crit.Advance(10);
    Check(Math.Abs(crit.Chance - 0.25) < 1e-12, "Only the base chance remains after every bonus ended");
    crit.AddChance(2, 1);
    Check(crit.Chance == 1, "Crit chance is capped at 100%");
    crit.Reset(); crit.BaseChance = 0;
    crit.AddChance(-1, 1); crit.AddChance(0.5, 0); crit.AddChance(double.NaN, 1);
    Check(crit.Chance == 0, "Invalid bonuses are ignored");
    crit.Guarantee();
    Check(crit.Roll(() => 0.99) && crit.Roll(() => 0.99), "A sure crit stays until a hit spends it");
    crit.Spend();
    Check(!crit.Roll(() => 0.99), "A spent sure crit is gone");
    crit.AddDamage(0.5, 3);
    Check(crit.DamageMultiplier == 2 && crit.Factor(true) == 2 && crit.Factor(false) == 1, "A damage bonus raises the crit factor only");
    crit.Advance(3);
    Check(crit.DamageMultiplier == 1.5, "A damage bonus ends on time");
    crit.BaseDamageMultiplier = 0.2;
    Check(crit.DamageMultiplier == 1, "A crit never lowers damage");

    // A crit tick folds into the pairing multiplier, so lethality stays in agreement.
    foreach (int tier in new[] { 1, 3, 7 })
    foreach (double factor in new[] { 1.0, 1.5, 2.0 })
    {
        double m = ColorlessDamage.Multiplier(tier) * 3.5 * factor;
        for (int life = 1; life <= 300; life += 7)
        {
            int hp = ColorlessDamage.SourceHp(life, m, 32760);
            for (int hit = 1; hit <= hp + 2; hit++)
            {
                int dealt = ColorlessDamage.HostDamage(hit, hp, life, m);
                if ((hit >= hp) != (dealt >= life)) throw new Exception($"Crit pairing broke: tier {tier} ×{factor} life {life} hit {hit}");
            }
        }
    }
    Check(true, "Source and host agree on lethal hits for crit ticks");
    Check(ColorlessDamage.HostDamage(10, 100, 500, 3.5 * 1.5) == 52, "A ×1.5 crit truncates 10 × 5.25 to 52");
}

// Kirby versions of the cooldown mutations (#89).
{
    var r = new KirbyRules(["fire", "sword", "ice"], n => 0);
    var m = r.Mutations;
    m.CopyKill(); m.Parry(); m.Copied(); m.CriticalLanded();
    Check(r.Critical.Chance == 0 && !r.Critical.Guaranteed, "Without the mutations nothing grants crit chance");
    m.SetOwned(KirbyMutation.KillerInstinct);
    for (int i = 0; i < 7; i++) m.CopyKill();
    Check(Math.Abs(r.Critical.Chance - 0.25) < 1e-12, "Killer Instinct stacks to its cap");
    r.Critical.Advance(6.1);
    Check(r.Critical.Chance == 0, "Killer Instinct bonuses end on time");
    m.CopyKill(); r.Critical.Advance(3); m.CopyKill();
    r.Critical.Advance(3.05);
    Check(Math.Abs(r.Critical.Chance - 0.05) < 1e-12, "Each Killer Instinct stack ends on its own time");
    r.Critical.Reset();
    m.SetOwned(KirbyMutation.BlindFaith);
    m.Parry();
    Check(r.Critical.Guaranteed && r.Critical.Roll(() => 0.99), "A parry makes the next copy attack a sure crit");
    r.Critical.Advance(8.1);
    Check(!r.Critical.Guaranteed, "A sure crit from a parry runs out");
    m.Parry(); r.Critical.Spend();
    Check(!r.Critical.Guaranteed, "A landed hit spends the parry's sure crit");
    m.SetOwned(KirbyMutation.None);
    m.Parry();
    Check(!r.Critical.Guaranteed, "A parry gives nothing without Blind Faith");
    m.SetOwned(KirbyMutation.HeartOfIce);
    Check(m.TargetChance(true) == 0.25 && m.TargetChance(false) == 0, "Heart of Ice only helps against a controlled, close target");
    Check(r.Critical.Roll(() => 0.2, m.TargetChance(true)) && !r.Critical.Roll(() => 0.2, m.TargetChance(false)),
        "Heart of Ice raises one target's chance, not Kirby's");
    m.SetOwned(KirbyMutation.None);
    Check(m.TargetChance(true) == 0, "No Heart of Ice, no bonus");
    m.SetOwned(KirbyMutation.HuntersInstinct);
    r.SetSourceAbility("fire");
    Check(Math.Abs(r.Critical.Chance - 0.20) < 1e-12, "Copying an ability gives Hunter's Instinct chance");
    r.SetSourceAbility("fire");
    r.Critical.Advance(10.1);
    r.SetSourceAbility("fire");
    Check(r.Critical.Chance == 0, "The same ability seen again is not a new copy");
    r.SetSourceAbility("ice");
    Check(Math.Abs(r.Critical.Chance - 0.20) < 1e-12, "Copying another ability counts again");
    r.SetSourceAbility("ice"); r.SetSourceAbility("sword");
    Check(Math.Abs(r.Critical.Chance - 0.20) < 1e-12, "Hunter's Instinct does not stack");
    r.Critical.Reset();
    m.SetOwned(KirbyMutation.MasterOfArmsInstinct);
    Check(Math.Abs(r.Critical.Chance - 0.10) < 1e-12, "Master of Arms Instinct gives chance while owned");
    for (int i = 0; i < 5; i++) m.CriticalLanded();
    Check(Math.Abs(r.Critical.DamageMultiplier - 2.25) < 1e-12, "Crits stack Master of Arms damage to its cap");
    m.SetOwned(KirbyMutation.None);
    Check(r.Critical.Chance == 0, "Losing the mutation ends its chance");
    r.Mutations.SetOwned(KirbyMutation.HeartOfIce | KirbyMutation.BlindFaith);
    r.Reset();
    Check(r.Mutations.Owned == KirbyMutation.None && r.Critical.Chance == 0, "A new run forgets the mutations");

    var tuning = new MutationTuning();
    Check(MutationDescriptions.Describe("P_CDR_Kill", tuning) == "Copy-attack kills give +5% critical chance for 6 sec. Stacks up to 5 times.", "Killer Instinct text");
    Check(MutationDescriptions.Describe("P_CDR_locked", tuning)!.StartsWith("+25% critical chance against slowed"), "Heart of Ice text");
    Check(MutationDescriptions.Describe("P_CDR_Parry", new MutationTuning { BlindFaithSeconds = 2.5 })!.Contains("within 2.5 sec"), "Texts use the live numbers");
    Check(MutationDescriptions.Describe("P_CDR_Distance", tuning) is not null && MutationDescriptions.Describe("P_CDR_Crit", tuning) is not null &&
        MutationDescriptions.Describe("P_Bleed", tuning) is null && MutationDescriptions.Describe(null, tuning) is null, "Only the cooldown mutations are rewritten");
    var timed = new CopyCritical();
    timed.AddChance(0.1, 5, "a", 2); timed.AddChance(0.1, 3, "a", 2); timed.Advance(1); timed.AddChance(0.3, 5, "a", 2);
    Check(Math.Abs(timed.Chance - 0.4) < 1e-12, "A full stack replaces the bonus with the least time left");
}
// Copy-attack wording and the pairing with a host mutation factor (#91).
{
    Check(CopyAttackWording.Rewrite("Melee attacks inflict bleeding (18 DPS for 1.5 sec).") == "Copy attacks inflict bleeding (18 DPS for 1.5 sec).", "Open Wounds text");
    Check(CopyAttackWording.Rewrite("Your first melee attack on an enemy inflicts +100 damage.") == "Your first copy attack on an enemy inflicts +100 damage.", "Initiative text");
    Check(CopyAttackWording.Rewrite("Melee kills make you invisible for 1 sec.") == "Copy-attack kills make you invisible for 1 sec.", "Predator text");
    Check(CopyAttackWording.Rewrite("Killing an enemy with a melee attack reduces the damage you take.") == "Killing an enemy with a copy attack reduces the damage you take.", "Berserker text");
    Check(CopyAttackWording.Rewrite("Close-ranged ranged attacks inflict 30% bonus damage.") == "Copy attacks at close range inflict 30% bonus damage.", "Point Blank text");
    Check(CopyAttackWording.Rewrite("+25% damage if there are no enemies near you.") is null && CopyAttackWording.Rewrite("") is null && CopyAttackWording.Rewrite(null) is null,
        "Texts without those words are left alone");
    // A host mutation factor folds into the pairing multiplier like a crit factor.
    foreach (double bonus in new[] { 1.02, 1.25, 1.6, 2.3 })
    foreach (double crit in new[] { 1.0, 1.5 })
    {
        double m = ColorlessDamage.Multiplier(3) * 3.5 * bonus * crit;
        for (int life = 1; life <= 400; life += 11)
        {
            int hp = ColorlessDamage.SourceHp(life, m, 32760);
            for (int hit = 1; hit <= hp + 2; hit++)
                if ((hit >= hp) != (ColorlessDamage.HostDamage(hit, hp, life, m) >= life))
                    throw new Exception($"Mutation pairing broke: ×{bonus} crit ×{crit} life {life} hit {hit}");
        }
    }
    Check(true, "Source and host agree on lethal hits with a host mutation factor");
    Check(ColorlessDamage.HostDamage(18, 100, 500, 3.5 * 1.6) == 100, "Vengeance's ×1.6 truncates 18 × 5.6 to 100");
}

// Mutations that cannot work for Kirby are not offered (#90).
{
    Check(MutationMenu.Hidden.Count == 16, "Sixteen mutations that cannot work for Kirby are hidden");
    foreach (var id in new[] { "P_DmgPlantedArrow", "P_AmmoOnHit", "P_Backpack_Melee", "P_Backpack_Ranged", "P_Backpack_Shield",
                 "P_AttackSpeed_Combo", "P_DmgSkillRanged", "P_DmgSkl", "P_Ammo", "P_DeployedDmg", "P_DeathBomb", "P_ShareDamage",
                 "P_Hot", "P_QuickHeal", "P_CorruptedHealing", "P_CursedFlask" })
        Check(MutationMenu.IsHidden(id) && MutationMenu.Hidden[id].Length > 10, $"{id} is hidden with a reason");
    Check(!MutationMenu.IsHidden("P_Bleed") && !MutationMenu.IsHidden("P_NoMobAround") && !MutationMenu.IsHidden("P_HealOnKill") && !MutationMenu.IsHidden("P_DmgNearRanged") &&
        !MutationMenu.IsHidden("P_Health") && !MutationMenu.IsHidden("P_HealOnParry") && !MutationMenu.IsHidden(null) && !MutationMenu.IsHidden(""), "Other mutations stay in the menu");
    Check(!MutationMenu.IsHidden("p_ammo"), "Mutation IDs are matched exactly");
}
// The Collector hides entries that do nothing for Kirby and still counts them for its gates (#99).
{
    foreach (var group in new[] { 0, 1, 3, 4, 5 })
        Check(CollectorMenu.Hides("AnyBlueprint", group), $"Weapon and skill group {group} ({CollectorMenu.HiddenGroups[group]}) is hidden");
    Check(!CollectorMenu.Hides("StartShield", 6) && !CollectorMenu.Hides("RandomShield", 9) &&
        !CollectorMenu.Hides("ArmoryUnlock", 9) && !CollectorMenu.Hides("P_Bleed", 12), "Shields, the starter shield, the armory and usable mutations stay");
    Check(new[] { "Flask1", "Flask2", "Flask3", "Flask4" }.All(id => CollectorMenu.Hides(id, 9)) &&
        CollectorMenu.Hides("P_Hot", 12) && CollectorMenu.Hides("P_CursedFlask", 12), "Health flask upgrades and flask mutations are hidden (#101)");
    Check(CollectorMenu.Hides("RandomBow", 9) && CollectorMenu.Hides("RandomCC", 9) && CollectorMenu.Hides("BackpackUnlock", 9),
        "Random Starter Bow, Random Melee Weapon and the backpack are hidden");
    Check(CollectorMenu.HiddenUpgrades.Values.All(reason => reason.Length > 10), "Every hidden upgrade has a reason");
    Check(CollectorMenu.Hides("P_Ammo", 12) && CollectorMenu.Hides("P_Backpack_Shield", 12), "Mutations the mutation menu hides are hidden here too");
    Check(!CollectorMenu.Hides(null, 4) && !CollectorMenu.Hides("", 4) && !CollectorMenu.Hides("randombow", 9), "IDs are matched exactly and must be present");
    var found = new[]
    {
        ("Bow1", 5, 25, false),          // hidden, locked: counts
        ("Bow2", 5, 25, true),           // hidden, unlocked: the host already counts it
        ("BaseBow", 5, 0, false),        // no cell cost: the host never counts these
        ("RandomBow", 9, 65, false),     // hidden upgrade, locked: counts
        ("ArmoryUnlock", 9, 50, false),  // kept and locked: still needs buying
        ("P_Bleed", 12, 30, false),
    };
    Check(CollectorMenu.CountUnlocked(7, found) == 9, "Found, locked, hidden entries with a cell cost count as unlocked");
    Check(CollectorMenu.CountUnlocked(0, []) == 0, "Nothing found adds nothing");
    Check(CollectorMenu.MeetsPrerequisite("Flask1", 9, false) && CollectorMenu.MeetsPrerequisite("ArmoryUnlock", 9, true) &&
        !CollectorMenu.MeetsPrerequisite("ArmoryUnlock", 9, false), "A hidden prerequisite counts as owned; a kept one needs buying (#101)");
}
// Kirby has no health flask; a fountain heals half his maximum health (#101).
{
    Check(Healing.FountainHeal(76) == 38 && Healing.FountainHeal(77) == 39, "A fountain heals half of maximum health, rounded up");
    Check(Healing.FountainHeal(1) == 1 && Healing.FountainHeal(0) == 0 && Healing.FountainHeal(-5) == 0, "Tiny and empty health pools stay in range");
}
// Limited-use abilities live in the F slot and are never swallowed (#102).
{
    Check(LimitedAbilities.Ids.SequenceEqual(["Paint", "Mike", "Crash", "Cook"]) && !LimitedAbilities.IsLimited("Sleep") &&
        !LimitedAbilities.IsLimited("crash") && !LimitedAbilities.IsLimited(null), "Paint, Mike, Crash and Cook are the limited-use abilities");
    Check(LimitedAbilities.Uses("Mike") == 3 && LimitedAbilities.Uses("Crash") == 1, "Mike keeps three shouts, the others one use");
    var slot = new FSlot();
    Check(slot.IsEmpty && slot.Take("Crash") is null && slot.Ability == "Crash" && slot.Uses == 1, "Taking into an empty slot");
    Check(slot.Take("Mike") == "Crash" && slot.Ability == "Mike" && slot.Uses == 3, "A new limited ability replaces the stored one");
    Check(!slot.Spend() && !slot.Spend() && slot.Spend() && slot.IsEmpty && slot.Uses == 0, "Mike empties the slot after its third shout");
    try { slot.Take("Sword"); throw new Exception("Sword accepted"); } catch (ArgumentException) { Check(slot.IsEmpty, "A swallowable ability never goes in the F slot"); }
    slot.Restore("Mike", 2);
    Check(slot.Ability == "Mike" && slot.Uses == 2, "A saved slot is restored");
    try { slot.Restore("Crash", 2); throw new Exception("Bad save accepted"); } catch (ArgumentException) { Check(true, "Saved uses beyond the ability's own are refused"); }
    var all = KirbyAbilityIdsForTests.All;
    var picked = new HashSet<string>();
    for (int i = 0; i < 20; i++) { int k = i; picked.Add(new KirbyRules(all, n => k % n).SelectCopyAbility()); }
    Check(picked.Count == 20 && !picked.Any(LimitedAbilities.IsLimited), "Enemies give every ability but the limited-use ones");
    var fRules = new KirbyRules(all, _ => 0);
    fRules.Stored.Take("Paint"); fRules.Reset();
    Check(fRules.Stored.IsEmpty, "A new run empties the F slot");
    Check(AbilityDescriptions.Describe("Crash").EndsWith(AbilityDescriptions.LimitedControls) &&
        AbilityDescriptions.Describe("Sword").EndsWith(AbilityDescriptions.Controls), "Limited items describe Interact and F");
}
// One F press through a simulated original machine (#102).
{
    // ROM model: a change loads for 3 frames; an attack held for its length reverts to base (1);
    // a hit's recoil (hurtAt, 20 frames) swallows attack presses; changes need a free Kirby.
    (FOutcome? Outcome, byte Ability, int Frames, int Presses, int Limits, int Requests) RunF(byte stored, byte main, int attackFrames,
        Func<int, bool> free, FSequence? sequence = null, byte start = 0, int stopAt = int.MaxValue, int hurtAt = -1, bool neverFires = false)
    {
        var f = sequence ?? new FSequence();
        if (sequence is null) f.Begin(stored, main);
        byte ability = start == 0 ? main : start; byte target = 0; int loading = 0, attack = start == stored ? attackFrames : -1, presses = 0, limits = 0, requests = 0;
        for (int frame = 0; frame < 3000; frame++)
        {
            if (frame == stopAt) return (null, ability, frame, presses, limits, requests);
            bool recoil = hurtAt >= 0 && frame >= hurtAt && frame < hurtAt + 20;
            bool isFree = free(frame) && attack < 0 && !recoil;
            var command = f.Advance(ability, loading > 0, isFree, a => { if (!isFree || loading > 0) return false; requests++; target = a; loading = 3; return true; });
            if (command.PressAttack) { presses++; if (attack < 0 && ability == stored && !recoil && !neverFires) attack = attackFrames; }
            if (command.LimitShouts) limits++;
            if (command.Finished is { } done) return (done, ability, frame, presses, limits, requests);
            if (loading > 0 && --loading == 0) ability = target;
            if (attack > 0 && --attack == 0) { ability = 1; attack = -1; }
        }
        return (null, ability, 3000, presses, limits, requests);
    }
    var crash = RunF(23, 18, 266, _ => true);
    Check(crash.Outcome == new FOutcome(true, true) && crash.Ability == 18, "Crash fires and the main Sword comes back");
    Check(crash.Presses == FSequence.FireFrames && crash.Limits == 0 && crash.Requests == 2, "One switch, one attack press, one restore");
    Check(crash.Frames < 266 + 20, "The press costs only a few frames beyond the original attack");
    var mike = RunF(22, 2, 121, _ => true);
    Check(mike.Outcome == new FOutcome(true, true) && mike.Limits == 1, "Mike is limited to one shout per press");
    var bare = RunF(21, 1, 404, _ => true);
    Check(bare.Outcome == new FOutcome(true, true) && bare.Ability == 1 && bare.Requests == 1, "Without a main copy Kirby simply stays in base form");
    var waited = RunF(24, 18, 445, frame => frame >= 10);
    Check(waited.Outcome == new FOutcome(true, true), "A press waits while Kirby finishes another action");
    var refused = RunF(24, 18, 445, _ => false);
    Check(refused.Outcome == new FOutcome(false, true) && refused.Frames == FSequence.WaitLimit - 1 && refused.Ability == 18,
        "A press Kirby cannot take is dropped without firing");
    var lost = RunF(23, 18, 266, frame => frame < 20);
    Check(lost.Outcome == new FOutcome(true, false), "A main copy that cannot come back is reported lost");
    var hurt = RunF(23, 18, 266, _ => true, hurtAt: 2);
    Check(hurt.Outcome == new FOutcome(true, true) && hurt.Ability == 18 && hurt.Presses > FSequence.FireFrames,
        "A press lost to a hit's recoil is pressed again once Kirby is free");
    var dud = RunF(23, 18, 266, _ => true, neverFires: true);
    Check(dud.Outcome == new FOutcome(false, true) && dud.Ability == 18 && dud.Presses == FSequence.FireFrames * (1 + FSequence.MaxRetries),
        "If the stored ability never fires, Kirby gets his main copy back and keeps the use");
    var f = new FSequence(); f.Begin(23, 18);
    var half = RunF(23, 18, 266, _ => true, f, stopAt: 100);
    var saved = f.Capture();
    Check(saved.Step == FStep.Attacking && saved.Stored == 23 && saved.Main == 18, "A press in progress is captured");
    var resumed = new FSequence(); resumed.Resume(saved);
    var after = RunF(23, 18, 100, _ => true, resumed, start: 23);
    Check(after.Outcome == new FOutcome(true, true) && after.Ability == 18, "A saved press resumes and restores the main copy");
    var waiting = new FSequence(); waiting.Resume(new FProgress(FStep.Waiting, 23, 18, false, false));
    Check(!waiting.Active && !new FSequence().Begin(1, 18) && !new FSequence().Begin(23, 0), "A waiting press is not resumed; invalid presses are refused");
}
Console.WriteLine($"PASS: {assertions} user-rule assertions. No claim of NDS physics or in-game verification.");
static class KirbyAbilityIdsForTests
{
    public static readonly string[] All = ["Cutter", "Mirror", "Beam", "Fighter", "Suplex", "YoYo", "Wheel", "Bomb", "Ice", "Fire", "Plasma", "Ninja",
        "Stone", "Wing", "Jet", "Copy", "Sword", "Hammer", "Parasol", "Paint", "Mike", "Crash", "Cook", "Sleep"];
}

