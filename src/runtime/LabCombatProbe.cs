using System.Globalization;
using System.Reflection;
using Path = System.IO.Path;
using System.Text.Json;
using dc.en;
using dc.en.mob;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using dc.tool.weap;
using HaxeProxy.Runtime;
using Kirby.Core;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>Opt-in integration probe using real host enemies and the normal shield path.</summary>
internal static unsafe class LabCombatProbe
{
    private static string run = "";
    private static uint? start, evolvedAt;
    private static Mob? victim, attacker;
    private static InventItem? shield;
    private static BaseShield? hostShield;
    private static uint lastReportFrame;
    private static bool setupAttempted;
    private static bool equipped, parryTried, parryStarted, countered, attackSent;
    private static bool? useAccepted;
    private static int? absorbedUid;
    private static uint lastTick;
    private static uint setupAt;
    private static uint? parryAt;
    private static string? copiedAbility, abilityAtParry;
    private static byte? copiedSourceAbility;
    private static int shieldAction = -1;
    // The source guard (DS L) that KirbyInput adds while the shield key is held at the parry.
    private static bool sourceGuardAtParry;
    private static readonly FieldInfo InputField = typeof(KirbyNdsProvider).GetField("input", BindingFlags.NonPublic | BindingFlags.Instance)!;
    // KIRBY_LAB_COMBAT_ELITE=1 makes the swallowed Zombie an Elite and records
    // the copy damage that the second Zombie takes from each original hit.
    private static readonly bool eliteVictim = Environment.GetEnvironmentVariable("KIRBY_LAB_COMBAT_ELITE") == "1";
    private static bool? empoweredAfterSwallow;
    private static readonly List<int> attackerHits = [];
    private static void Stage(string phase) => System.IO.File.AppendAllText(Path.Combine(run, "combat-events.txt"), $"{lastTick}: {phase}\n");

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        Hook_BaseShield.onShieldStartParry += (orig, self) =>
        {
            orig(self);
            if (shield is not null && self.item.permanentId == shield.permanentId) parryStarted = true;
        };
        Hook_BaseShield.onShieldCounterSuccessful += (orig, self, attack, flag) =>
        {
            orig(self, attack, flag);
            if (shield is not null && self.item.permanentId == shield.permanentId) countered = true;
        };
        Hook_HeroWeaponsManager.hasButtonDownForWeapon += (orig, manager, weapon) => ShieldHeld(weapon) || orig(manager, weapon);
        Hook_HeroWeaponsManager.hasButtonPressedForWeapon += (orig, manager, weapon) =>
            (ShieldHeld(weapon) && lastTick == parryAt) || orig(manager, weapon);
        Hook_Mob.applyAttackResult += (orig, mob, attack) =>
        {
            int before = mob.life;
            orig(mob, attack);
            if (attacker is not null && mob.HashlinkPointer == attacker.HashlinkPointer && before > mob.life)
                attackerHits.Add(before - mob.life);
        };
        KirbyInput.TestOverride = Input;
    }

    private static bool ShieldHeld(Weapon weapon) => parryAt is uint at && lastTick - at < 3 &&
        shield is not null && weapon.item.permanentId == shield.permanentId;

    private static Mob Spawn(Hero hero, double dx)
    {
        // The factory also initializes movement/skills. A raw constructor leaves
        // the enemy's movement controller null at its first update.
        var mob = Zombie.Class.create(hero._level, hero.cx + 2, hero.cy, 1, 1);
        mob.setPosPixel((hero.cx + hero.xr) * 24 + dx, (hero.cy + hero.yr) * 24);
        if (mob.yr == 0) { mob.cy--; mob.yr = 1; }
        return mob;
    }

    private static ushort? Input(KirbySession session)
    {
        ushort? keys = Decide(session);
        // The shield key is held in the host's key table, which KirbyInput's
        // shield guard reads (#176); the host's weapon queries are hooked above.
        LabSmokeCapture.HostKey(session.Hero, shieldAction, parryAt is uint at && lastTick - at < 3);
        return keys;
    }

    private static ushort? Decide(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        uint tick = machine.State.Frame;
        start ??= tick;
        uint elapsed = tick - start.Value;
        lastTick = tick;
        var hero = session.Hero;
        if (elapsed < 180) return 0;
        if (!setupAttempted)
        {
            setupAttempted = true;
            setupAt = tick;
            Stage("Creating shield");
            shield = new InventItem(new InventItemKind.Weapon("StartShield".AsHaxeString()));
            Stage("Adding shield through normal inventory path");
            hero.inventory.add(shield);
            if (hero.weaponsManager is dc.tool.hero.weap.BeheadedWeaponsManager manager)
                manager.onEquippedItemsUpdated(Ref<bool>.In(false));
            Stage("Shield added; allowing host equipment update");
            return 0;
        }
        if (victim is null && tick > setupAt + 2 && shield is not null)
        {
            hostShield = hero.weaponsManager.findItemInMain(shield) as BaseShield;
            equipped = hostShield is not null;
            for (int slot = 0; slot < hero.inventory.nbWeapons; slot++)
                if (hero.inventory.getEquippedWeaponOn(slot)?.permanentId == shield.permanentId) shieldAction = slot + 2;
            Stage($"Shield weapon found={equipped}; creating Zombie");
            victim = Spawn(hero, 36);
            if (eliteVictim) victim.setElite(true); // No Elite skills: keep the swallow deterministic.
            absorbedUid = victim.__uid;
            Stage($"Zombie created uid={absorbedUid}");
        }
        if (victim is null) return 0;
        if (!victim.destroyed && victim.life > 0 && evolvedAt is null)
            return 2; // Original inhale, until the host enemy is actually consumed.
        if (evolvedAt is uint shown && Environment.GetEnvironmentVariable("KIRBY_LAB_CAPTURE_FPS") is not null &&
            tick - shown >= 240)
            return (tick - shown) switch
            {
                >= 240 and < 360 => 32,
                >= 400 and < 412 => 1,
                >= 480 and < 600 => 16,
                >= 650 and < 710 => 64,
                >= 720 and < 810 => 32,
                >= 850 and < 1000 => 2,
                >= 1000 and < 1060 => 128,
                _ => 0,
            };
        copiedAbility ??= session.Rules.AbilityId;
        if (machine.State.Ability == 1)
            return evolvedAt is null ? (ushort)128 : (ushort)0; // Original swallow/transform action.
        evolvedAt ??= tick;
        copiedSourceAbility ??= machine.State.Ability;
        empoweredAfterSwallow ??= session.Rules.AbilityEmpowered;
        uint abilityFrames = tick - evolvedAt.Value;
        if (abilityFrames < 5) return 0;
        if (!parryTried && hostShield is not null)
        {
            parryTried = true;
            attacker = Spawn(hero, 36);
            parryAt = tick;
            abilityAtParry = session.Rules.AbilityId;
            useAccepted = hostShield.tryToUse();
            Stage($"Shield use accepted={useAccepted} while ability={abilityAtParry}");
        }
        if (abilityFrames is >= 60 and < 150) return 2; // Actual original ability after shield test.
        return 0;
    }

    public static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run.Length == 0 || session is null || machine is null) return;
        if (machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        if (parryAt is uint at && lastTick - at < 3 && KirbyMod.Instance?.NativeProvider is { } provider &&
            InputField.GetValue(provider) is ushort input && (input & 512) != 0) sourceGuardAtParry = true;
        if (parryStarted && !attackSent && attacker is not null)
        {
            attackSent = true;
            var attack = AttackUtils.Class.createFromMob(attacker, 1, null);
            AttackUtils.Class.hit(attack, session.Hero);
        }
        string? shieldId = shield?.permanentId.ToString(CultureInfo.InvariantCulture);
        System.IO.File.WriteAllText(Path.Combine(run, "combat.json"), JsonSerializer.Serialize(new
        {
            frame = lastTick,
            elapsedSourceFrames = start is null ? 0 : machine.State.Frame - start.Value,
            equipped,
            absorbedUid,
            eliteVictim,
            victimElite = victim?.elite,
            empoweredAfterSwallow,
            empowered = session.Rules.AbilityEmpowered,
            damageMultiplier = 1 + (KirbyAbilityItems.SlotItem(session.Hero, session.Rules.AbilityId)?.getDamageBonus() ?? 0),
            attackerHits,
            enemyConsumed = victim is not null && (victim.destroyed || victim.life <= 0),
            selectedAbility = session.Rules.AbilityId,
            sourceAbility = machine.State.Ability,
            copiedAbility,
            copiedSourceAbility,
            abilityAtParry,
            shieldId,
            retainedShieldId = session.Rules.RetainedShieldId,
            sameShieldAllowed = shieldId is not null && session.Rules.CanUse(HostWeaponKind.Shield, shieldId),
            parryTried,
            useAccepted,
            sourceGuardAtParry,
            parryStarted,
            attackSent,
            countered,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
