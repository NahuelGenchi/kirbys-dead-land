using System.Text.Json;
using dc.tool;
using dc.tool.hero;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Holds the real binding of the host weapon in slot 0 (left-click by default,
/// the starting sword) through the production input path in base form. It must
/// reach the source as DS B, start the original inhale, and never let the host
/// weapon attack, even while the host reports its button down. Keys are
/// pressed in the host's key table, which <see cref="KirbyInput"/> reads (#176).
/// </summary>
internal static unsafe class LabWeaponAimProbe
{
    private static string? run;
    private static uint? start, aimStart;
    private static uint tick, lastReport;
    private static InventItem? item;
    private static int upKey = -1, weaponKey = -1;
    private static int maskedBefore, maskedAfter;
    private static int hostAttempts, hostAttacks, inhaleFrames, heldInputs;
    private static ushort weaponMask;
    private static bool restored, baseForm;
    private static bool upAlsoJump;
    // Dodge (action 1): a Shift key slides instead of guarding (#75), any other key guards.
    private static int dodgeKey = -1, guardKey = -1;
    private static bool shiftDodge, dodgeSlide;
    private static ushort guardMask;
    private static readonly Dictionary<int, ushort> directionMasks = [];
    private static readonly Dictionary<int, ushort> invertedMasks = [];
    private static double? moveStartX, moveStartY;
    private static double movedX, jumpRise;
    private static bool Held => aimStart is uint at && tick - at < 12;
    private static int Binding(ControllerAccess access, int action) => ((int*)access.parent.get_bindings().primary.bytes)[action];

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        dc.hxd.Hook__Key.isPressed += (orig, key) =>
            Held && aimStart is uint at && tick - at < 2 && key == weaponKey || orig(key);
        Hook_HeroWeaponsManager.hasButtonDownForWeapon += (orig, manager, weapon) =>
            Held && item?.permanentId == weapon.item.permanentId || orig(manager, weapon);
        Hook_HeroWeaponsManager.hasButtonPressedForWeapon += (orig, manager, weapon) =>
            Held && tick == aimStart && item?.permanentId == weapon.item.permanentId || orig(manager, weapon);
        Hook_HeroWeaponsManager.updateWeapons += (orig, manager) =>
        {
            bool inspect = Held && KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer == manager.hero.HashlinkPointer;
            if (inspect && Binding(manager.hero.controller, 10) == -1) maskedBefore++;
            orig(manager);
            if (inspect && Binding(manager.hero.controller, 10) == -1) maskedAfter++;
        };
        Hook_Weapon.tryToUse += (orig, weapon) =>
        {
            bool used = orig(weapon);
            if (item is not null && weapon.item.permanentId == item.permanentId)
            {
                hostAttempts++;
                if (used) hostAttacks++;
                File.AppendAllText(Path.Combine(run!, "weapon-aim-events.jsonl"), JsonSerializer.Serialize(new
                {
                    tick, used, weapon = weapon.GetType().FullName,
                    ability = KirbyMod.Instance?.ActiveSession?.Rules.AbilityId,
                }) + "\n");
            }
            return used;
        };
        KirbyInput.TestOverride = Input;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null) return 0;
        tick = machine.State.Frame;
        start ??= tick;
        var hero = session.Hero;
        if (Held)
        {
            heldInputs++;
            baseForm = session.Rules.IsBaseForm;
        }
        if (aimStart is uint since && tick - since < 60 && (machine.ActionFlags & 1) != 0) inhaleFrames++;
        if (tick - start < 120) return 0;
        // The new run's starting weapon, on the first weapon binding.
        item ??= hero.inventory.getEquippedWeaponOn(0);
        if (item is not null && aimStart is null && tick - start >= 180)
        {
            var overrideInput = KirbyInput.TestOverride;
            var options = dc.Main.Class.ME.options;
            bool originalInversion = options.invertPlayerMovements;
            var bindings = hero.controller.parent.get_bindings();
            int physicalUp = Binding(hero.controller, 10);
            upAlsoJump = new[] { bindings.primary, bindings.secondary, bindings.third }
                .Any(array => array.length > 0 && ((int*)array.bytes)[0] == physicalUp);
            try
            {
                KirbyInput.TestOverride = null;
                options.invertPlayerMovements = false;
                foreach (int action in new[] { 0, 1, 4, 5, 10, 11, 12, 13, 28 })
                {
                    directionMasks[action] = ReadWith(session, Binding(hero.controller, action));
                    if (action == 1) dodgeSlide = KirbyInput.SlideButton;
                }
                dodgeKey = Binding(hero.controller, 1);
                shiftDodge = dodgeKey >= 0 && (dodgeKey & 0xFF) == 16;
                guardKey = new[] { bindings.primary, bindings.secondary, bindings.third }
                    .Select(array => array.length > 1 ? ((int*)array.bytes)[1] : -1)
                    .FirstOrDefault(key => key >= 0 && (key & 0xFF) != 16, -1);
                if (guardKey >= 0) guardMask = ReadWith(session, guardKey);
                options.invertPlayerMovements = true;
                foreach (int action in new[] { 0, 1, 4, 5, 10, 11, 12, 13, 28 })
                    invertedMasks[action] = ReadWith(session, Binding(hero.controller, action));
            }
            finally
            {
                KirbyInput.TestOverride = overrideInput;
                options.invertPlayerMovements = originalInversion;
            }
            upKey = Binding(hero.controller, 10);
            weaponKey = Binding(hero.controller, 2);
            try
            {
                KirbyInput.TestOverride = null;
                options.invertPlayerMovements = false;
                weaponMask = ReadWith(session, weaponKey);
            }
            finally
            {
                KirbyInput.TestOverride = overrideInput;
                options.invertPlayerMovements = originalInversion;
            }
            aimStart = tick;
        }
        // Production physical input while the weapon key is held.
        LabSmokeCapture.HostKey(hero, 2, Held);
        if (Held) return null;
        uint elapsed = tick - start.Value;
        if (elapsed >= 260)
        {
            double hostX = (hero.cx + hero.xr) * 24, hostY = (hero.cy + hero.yr) * 24;
            moveStartX ??= hostX;
            moveStartY ??= hostY;
            movedX = Math.Max(movedX, hostX - moveStartX.Value);
            jumpRise = Math.Max(jumpRise, moveStartY.Value - hostY);
            LabSmokeCapture.HostKey(hero, 13, elapsed < 320);
            LabSmokeCapture.HostKey(hero, 0, elapsed is >= 340 and < 350);
            // Return null so the production physical-input path supplies the
            // actual source frames from these keys alone.
            return null;
        }
        return 0;
    }

    /// <summary>One production read with only this key down, its key-table entry restored after.</summary>
    private static ushort ReadWith(KirbySession session, int key)
    {
        if (key < 0) return KirbyInput.Read(session);
        int* state = LabSmokeCapture.HostKeyState(key);
        int before = *state;
        *state = Math.Max(1, dc.hxd.Timer.Class.frameCount);
        try { return KirbyInput.Read(session); }
        finally { *LabSmokeCapture.HostKeyState(key) = before; }
    }

    internal static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        if (run is null || session is null || tick == lastReport) return;
        lastReport = tick;
        if (aimStart is not null) restored |= Binding(session.Hero.controller, 10) == upKey;
        var bindings = session.Hero.controller.parent.get_bindings();
        File.WriteAllText(Path.Combine(run, "weapon-aim.json"), JsonSerializer.Serialize(new
        {
            tick, item = item?.getItemKind().ToString(), upKey, weaponKey, weaponMask, heldInputs, baseForm,
            inhaleFrames, hostAttempts, hostAttacks, maskedBefore, maskedAfter, restored,
            directionMasks, invertedMasks, upAlsoJump, dodgeKey, dodgeSlide, guardKey, guardMask,
            keyboardUpAndJump = new[] { bindings.primary, bindings.secondary, bindings.third }
                .Select(array => new { jump = ((int*)array.bytes)[0], up = ((int*)array.bytes)[10] }).ToArray(),
            movedX, jumpRise,
            available = KirbyInput.Available(session.Hero),
            inputState = new {
                session.Hero.awake, session.Hero.controller.manualLock,
                session.Hero.controller.parent.isLocked,
                session.Hero.controller.parent.suspendTimer,
                exclusiveId = session.Hero.controller.parent.exclusiveId?.ToString(),
                accessId = session.Hero.controller.id.ToString(),
                session.Hero.controller.parent.mode,
                cinematic = session.Hero._level.game.hasCinematic(),
            },
            passed = item is not null && weaponMask == 2 && heldInputs > 0 && baseForm && inhaleFrames > 0 && hostAttacks == 0 &&
                maskedBefore > 0 && maskedBefore == maskedAfter && restored &&
                directionMasks.GetValueOrDefault(10) == (upAlsoJump ? 65 : 64) && directionMasks.GetValueOrDefault(11) == 32 &&
                directionMasks.GetValueOrDefault(12) == 128 && directionMasks.GetValueOrDefault(13) == 16 &&
                directionMasks.GetValueOrDefault(0) == 1 && directionMasks.GetValueOrDefault(1) == (shiftDodge ? 0 : 512) &&
                dodgeSlide == shiftDodge && (guardKey < 0 || guardMask == 512) &&
                directionMasks.GetValueOrDefault(4) == 2 && directionMasks.GetValueOrDefault(5) == 4 &&
                directionMasks.GetValueOrDefault(28) == 1024 && movedX > 12 && jumpRise > 5 &&
                invertedMasks.GetValueOrDefault(10) == (upAlsoJump ? 129 : 128) && invertedMasks.GetValueOrDefault(11) == 16 &&
                invertedMasks.GetValueOrDefault(12) == 64 && invertedMasks.GetValueOrDefault(13) == 32 &&
                new[] { 0, 1, 4, 5, 28 }.All(action => invertedMasks.GetValueOrDefault(action) == directionMasks.GetValueOrDefault(action)),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
