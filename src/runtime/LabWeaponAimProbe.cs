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
/// weapon attack, even while the host reports its button down.
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
    private static int mappingKey = -1;
    private static readonly Dictionary<int, ushort> directionMasks = [];
    private static readonly Dictionary<int, ushort> invertedMasks = [];
    private static double? moveStartX, moveStartY;
    private static double movedX, jumpRise;
    // Movement sticks the host lists for the hero (#185): inside Kirby's update
    // (most seen), while a weapon reads its aim, and between frames.
    private static int sticksMasked = -1, sticksAim = -1, sticksBetween = -1;
    private static bool Held => aimStart is uint at && tick - at < 12;
    private static int Binding(ControllerAccess access, int action) => ((int*)access.parent.get_bindings().primary.bytes)[action];

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        dc.hxd.Hook__Key.isDown += (orig, key) => key >= 0 && key == mappingKey ||
            Held && key == weaponKey || orig(key);
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
            if (inspect)
            {
                var hero = manager.hero;
                sticksMasked = Math.Max(sticksMasked, hero.controller.getMovementSticks(false).length);
                KirbyInput.WithHostDirections(hero, () => sticksAim = hero.controller.getMovementSticks(false).length);
            }
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
                    mappingKey = Binding(hero.controller, action);
                    directionMasks[action] = KirbyInput.Read(session);
                }
                options.invertPlayerMovements = true;
                foreach (int action in new[] { 0, 1, 4, 5, 10, 11, 12, 13, 28 })
                {
                    mappingKey = Binding(hero.controller, action);
                    invertedMasks[action] = KirbyInput.Read(session);
                }
            }
            finally
            {
                mappingKey = -1;
                KirbyInput.TestOverride = overrideInput;
                options.invertPlayerMovements = originalInversion;
            }
            upKey = Binding(hero.controller, 10);
            weaponKey = Binding(hero.controller, 2);
            try
            {
                KirbyInput.TestOverride = null;
                options.invertPlayerMovements = false;
                mappingKey = weaponKey;
                weaponMask = KirbyInput.Read(session);
            }
            finally
            {
                mappingKey = -1;
                KirbyInput.TestOverride = overrideInput;
                options.invertPlayerMovements = originalInversion;
            }
            aimStart = tick;
        }
        // Production physical input while the weapon key is held.
        if (Held) return null;
        uint elapsed = tick - start.Value;
        if (elapsed >= 260)
        {
            double hostX = (hero.cx + hero.xr) * 24, hostY = (hero.cy + hero.yr) * 24;
            moveStartX ??= hostX;
            moveStartY ??= hostY;
            movedX = Math.Max(movedX, hostX - moveStartX.Value);
            jumpRise = Math.Max(jumpRise, moveStartY.Value - hostY);
            mappingKey = elapsed switch
            {
                < 320 => Binding(hero.controller, 13),
                >= 340 and < 350 => Binding(hero.controller, 0),
                _ => -1,
            };
            // Return null so the production physical-input path supplies the
            // actual source frames, using only this isolated virtual key.
            return null;
        }
        return 0;
    }

    internal static void Poll()
    {
        var session = KirbyMod.Instance?.ActiveSession;
        if (run is null || session is null || tick == lastReport) return;
        lastReport = tick;
        if (aimStart is not null) restored |= Binding(session.Hero.controller, 10) == upKey;
        sticksBetween = session.Hero.controller.getMovementSticks(false).length;
        var options = dc.Main.Class.ME.options;
        var bindings = session.Hero.controller.parent.get_bindings();
        File.WriteAllText(Path.Combine(run, "weapon-aim.json"), JsonSerializer.Serialize(new
        {
            tick, item = item?.getItemKind().ToString(), upKey, weaponKey, weaponMask, heldInputs, baseForm,
            inhaleFrames, hostAttempts, hostAttacks, maskedBefore, maskedAfter, restored,
            directionMasks, invertedMasks, upAlsoJump,
            keyboardUpAndJump = new[] { bindings.primary, bindings.secondary, bindings.third }
                .Select(array => new { jump = ((int*)array.bytes)[0], up = ((int*)array.bytes)[10] }).ToArray(),
            movedX, jumpRise,
            sticksMasked, sticksAim, sticksBetween,
            stickFunctions = new { left = options.lstickFunction, right = options.rstickFunction },
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
                directionMasks.GetValueOrDefault(0) == 1 && directionMasks.GetValueOrDefault(1) == 512 &&
                directionMasks.GetValueOrDefault(4) == 2 && directionMasks.GetValueOrDefault(5) == 4 &&
                directionMasks.GetValueOrDefault(28) == 1024 && movedX > 12 && jumpRise > 5 &&
                sticksMasked == 0 && sticksBetween > 0 && sticksAim == sticksBetween &&
                invertedMasks.GetValueOrDefault(10) == (upAlsoJump ? 129 : 128) && invertedMasks.GetValueOrDefault(11) == 16 &&
                invertedMasks.GetValueOrDefault(12) == 64 && invertedMasks.GetValueOrDefault(13) == 32 &&
                new[] { 0, 1, 4, 5, 28 }.All(action => invertedMasks.GetValueOrDefault(action) == directionMasks.GetValueOrDefault(action)),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
