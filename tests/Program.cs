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
foreach (double m in new[] { 3.5, 3.5 * 1.7, 3.5 * 2 * Math.Pow(1.15, 3), 3.5 * 3.2 * 2 * Math.Pow(1.15, 8) })
for (int hostLife = 1; hostLife <= 400; hostLife++)
{
    int sourceHp = ColorlessDamage.SourceHp(hostLife, m, 32760);
    for (int hit = 1; hit <= sourceHp + 2; hit++)
        if ((hit >= sourceHp) != (ColorlessDamage.HostDamage(hit, sourceHp, hostLife, m) >= hostLife))
            throw new Exception($"Combined lethality differs at x{m}, life {hostLife}, hit {hit}");
}
Check(true, "Lethality agrees with unit scale, item bonus and colorless tier combined");

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

ushort[] Feed(DashAssist assist, params (ushort Mask, bool Enabled, int Vx, int Vy)[] frames) =>
    frames.Select(f => assist.Filter(f.Mask, f.Enabled, f.Vx, f.Vy)).ToArray();
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
Check(HostStrikes.Slide(KirbyActions.Slide, -704) && !HostStrikes.Slide(KirbyActions.Slide, 384) && !HostStrikes.Slide(KirbyActions.Dash, 704),
    "Only the slide's fast part strikes like a roll");
Check(HostStrikes.Grab(HostStrikes.Suplex, SpeedMatch.Carry, -1766) && !HostStrikes.Grab(HostStrikes.Suplex, SpeedMatch.Carry, 300) &&
      !HostStrikes.Grab(HostStrikes.Suplex, KirbyActions.Dash, 528) && !HostStrikes.Grab(18, SpeedMatch.Carry, 1766),
    "Only Suplex's fast grab lunge bursts doors");
var stompRun = new StompController();
Check(!stompRun.TryStart(KirbyActions.Standing, 480), "A grounded Kirby cannot stomp");
Check(stompRun.TryStart(KirbyActions.Fall, 400) && stompRun.Active, "A falling Kirby stomps");
Check(!stompRun.TryStart(KirbyActions.Fall, 400), "A stomp cannot restart while active");
Check(stompRun.Mask(1 | 128) == 0, "Original buttons are released during a falling stomp");
Check(stompRun.Descent(100) == StompController.ExtraDescent, "Full extra descent in open air");
Check(stompRun.Descent(8) == 3.5 && stompRun.Descent(4) == 0, "Descent leaves the original a full fall step to make the floor contact");
Check(Math.Abs(3.5 + StompController.ExtraDescent - 15) < 1e-9, "The stomp falls 15 px a frame, the Beheaded's dive");
Check(stompRun.Advance(landed: false, interrupted: false) == StompController.Outcome.None && stompRun.Active, "Still diving");
Check(Math.Abs(stompRun.Height(472) - 3) < 1e-9, "Fall height is reported in host cells");
Check(stompRun.Advance(landed: true, interrupted: false) == StompController.Outcome.Landed && !stompRun.Active, "Landing ends the stomp");
Check(stompRun.Descent(100) == 0 && stompRun.Mask(1) == 1, "An idle stomp leaves descent and buttons alone");
var floatStomp = new StompController();
Check(floatStomp.TryStart(KirbyActions.Float, 300), "A floating Kirby stomps");
Check(floatStomp.Mask(1) == StompController.ButtonB && floatStomp.Mask(1) == 0, "A floating Kirby exhales once, then falls");
Check(floatStomp.Advance(false, interrupted: true) == StompController.Outcome.Cancelled && !floatStomp.Active, "A hit cancels the stomp");
var chainStomp = new StompController();
Check(KirbyActions.CanStomp(KirbyActions.Climb) && chainStomp.TryStart(KirbyActions.Climb, 300), "Kirby stomps from a ladder or chain");
Check(chainStomp.Mask(128 | 1) == StompController.ButtonA && chainStomp.Mask(128 | 1) == 0, "The original A kicks him off once, then buttons are released");
var fallStomp = new StompController();
fallStomp.TryStart(KirbyActions.Fall, 300);
Check(fallStomp.Mask(1) == 0, "A falling Kirby needs no releasing press");
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
Console.WriteLine($"PASS: {assertions} user-rule assertions. No claim of NDS physics or in-game verification.");
