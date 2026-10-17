using System.Text.Json;
using dc.en;
using dc.tool;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in check of the weapon handler (#188): equips a real shield, holds its
/// binding through the host's own key table, and Left on top of it for a while.
/// The handler that lets a charging weapon read the real direction bindings must
/// be off before the press, on while the shield is held, see the host read the
/// real direction keys while the shield charges, and come off after the release.
/// </summary>
internal static unsafe class LabShieldHoldProbe
{
    private const int HoldFrames = 120, LeftFrom = 40, LeftTo = 80;
    private static string run = "";
    private static uint? start, setupAt, pressAt, releaseAt, relaxedAt;
    private static uint tick, lastReport;
    private static InventItem? shield;
    private static BaseShield? hostShield;
    private static int shieldAction = -1;
    private static readonly HashSet<int> directionKeys = [];
    private static bool? heldBeforePress, heldAfterRelax;
    private static int heldFrames, pressedFrames, chargingFrames, chargingHeldFrames, directionReads, ownerTurned;
    private static readonly List<string> events = [];

    private static void Stage(string phase)
    {
        events.Add($"{tick}: {phase}");
        System.IO.File.AppendAllText(Path.Combine(run, "shield-events.txt"), $"{tick}: {phase}\n");
    }

    public static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        // A direction key read from inside a host weapon's update: masked bindings ask for key -1.
        dc.hxd.Hook__Key.isDown += (orig, key) =>
        {
            if (KirbyInput.ReadingHostDirections && directionKeys.Contains(key)) directionReads++;
            return orig(key);
        };
        Hook_Weapon.onOwnerTurned += (orig, weapon) =>
        {
            if (shield is not null && weapon.item?.permanentId == shield.permanentId) ownerTurned++;
            orig(weapon);
        };
        KirbyInput.TestOverride = Input;
    }

    private static int Binding(Hero hero, int action)
    {
        var bindings = hero.controller.parent.get_bindings().primary;
        return (uint)action < (uint)bindings.length ? ((int*)bindings.bytes)[action] : -1;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        tick = machine.State.Frame;
        start ??= tick;
        var hero = session.Hero;
        if (tick - start.Value < 180) return 0;
        if (shield is null)
        {
            setupAt = tick;
            shield = new InventItem(new InventItemKind.Weapon("StartShield".AsHaxeString()));
            hero.inventory.add(shield);
            if (hero.weaponsManager is dc.tool.hero.weap.BeheadedWeaponsManager manager)
                manager.onEquippedItemsUpdated(Ref<bool>.In(false));
            Stage("Shield added through the normal inventory path");
            return 0;
        }
        if (pressAt is null)
        {
            if (tick < setupAt + 30) return 0;
            hostShield = hero.weaponsManager.findItemInMain(shield) as BaseShield;
            for (int slot = 0; slot < hero.inventory.nbWeapons; slot++)
                if (hero.inventory.getEquippedWeaponOn(slot)?.permanentId == shield.permanentId) shieldAction = slot + 2;
            foreach (int action in new[] { 10, 11, 12, 13 })
                if (Binding(hero, action) is >= 0 and var key) directionKeys.Add(key);
            heldBeforePress = KirbyInput.WeaponUpdateHeld;
            pressAt = tick;
            Stage($"Shield found={hostShield is not null} action={shieldAction}; handler before the press={heldBeforePress}");
        }
        uint held = tick - pressAt.Value;
        if (shieldAction >= 0 && held < HoldFrames)
        {
            LabSmokeCapture.HostKey(hero, shieldAction, true);
            LabSmokeCapture.HostKey(hero, 11, held is >= LeftFrom and < LeftTo);
            pressedFrames++;
            // The production reader turns the held keys into the original's buttons.
            return null;
        }
        if (releaseAt is null)
        {
            releaseAt = tick;
            if (shieldAction >= 0) LabSmokeCapture.HostKey(hero, shieldAction, false);
            LabSmokeCapture.HostKey(hero, 11, false);
            Stage("Shield and Left released");
        }
        return 0;
    }

    internal static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        if (run.Length == 0 || session is null || pressAt is null || tick == lastReport) return;
        lastReport = tick;
        bool handler = KirbyInput.WeaponUpdateHeld;
        bool charging = hostShield?.isCharging() == true;
        if (releaseAt is null)
        {
            if (handler) heldFrames++;
            if (charging) { chargingFrames++; if (handler) chargingHeldFrames++; }
        }
        else if (relaxedAt is null && !handler)
        {
            relaxedAt = tick;
            Stage($"Handler off {tick - releaseAt.Value} frames after the release");
        }
        else if (relaxedAt is uint off && tick - off == 60) heldAfterRelax = handler;
        System.IO.File.WriteAllText(Path.Combine(run, "shield.json"), JsonSerializer.Serialize(new
        {
            tick, shieldFound = hostShield is not null, shieldAction, directionKeys,
            heldBeforePress, pressedFrames, heldFrames, chargingFrames, chargingHeldFrames,
            directionReads, ownerTurned,
            releasedAfter = releaseAt - pressAt, relaxedAfterRelease = relaxedAt - releaseAt, heldAfterRelax,
            retainedShield = session.Rules.RetainedShieldId,
            passed = hostShield is not null && heldBeforePress == false && pressedFrames > 0 && heldFrames >= pressedFrames - 1 &&
                (chargingFrames == 0 || chargingHeldFrames == chargingFrames && directionReads > 0) &&
                relaxedAt is not null && heldAfterRelax == false,
            events,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
