using dc.en;
using dc.hl.types;
using dc.tool;
using dc.tool.hero;
using ModCore.Utilities;
using Kirby.Core;
using System.Globalization;
using HashlinkNET.Native.Impl;

namespace Kirby.Runtime;

/// <summary>Reads the user's existing Dead Cells bindings and translates them to DS buttons.</summary>
internal static unsafe class KirbyInput
{
    public static Func<KirbySession, ushort?>? TestOverride { get; set; }
    private sealed record Suppression(ControllerAccess Access, ArrayBytes_Int[] Arrays, int[] Saved, Suppression? Parent);
    private static Suppression? suppressed;
    private static ControllerAccess? directionReader;
    private static ArrayBytes_Int? noSticks;
    // Action IDs confirmed against this host's Options.configController.
    private static readonly int[] movementActions = [0, 1, 4, 5, 10, 11, 12, 13, 28];
    private static readonly int[] directionActions = [10, 11, 12, 13];
    public static void Install()
    {
        Hook_ControllerAccess.getMovementSticks += (orig, access, alternate) =>
            suppressed?.Access.HashlinkPointer == access.HashlinkPointer && directionReader?.HashlinkPointer != access.HashlinkPointer
                ? noSticks ??= ArrayUtils.CreateInt() : orig(access, alternate);
        Hook_HeroWeaponsManager.updateWeapon += (orig, manager, slot) =>
            WithHostDirections(manager.hero, () => orig(manager, slot));
    }
    public static bool Available(Hero hero)
    {
        ControllerAccess access = hero.controller;
        Controller controller = access.parent;
        // The host stores an absolute sys_time deadline, including after it expires.
        return hero.life > 0 && hero.awake && !access.manualLock && !controller.isLocked && Lib_std.sys_time() >= controller.suspendTimer &&
            (controller.exclusiveId is null || controller.exclusiveId.ToString() == access.id.ToString()) &&
            !hero._level.game.hasCinematic();
    }
    private static int Get(ArrayBytes_Int? a, int index) => a is not null && (uint)index < (uint)a.length
        ? ((int*)a.bytes)[index] : -1;
    public static bool Down(ControllerAccess access, int action)
    {
        Controller c = access.parent;
        BindingProfiles b = c.get_bindings();
        if ((c.mode & Controller.Class.ENABLE_KEY) != 0)
            foreach (var a in new[] { b.primary, b.secondary, b.third })
            {
                int key = Get(a, action);
                if (key >= 0 && dc.hxd.Key.Class.isDown(key)) return true;
            }
        if ((c.mode & Controller.Class.ENABLE_PAD) != 0)
            foreach (var a in new[] { b.padA, b.padB, b.padC })
            {
                int key = Get(a, action);
                if (key >= 0 && c.padIsDown(key)) return true;
            }
        return false;
    }
    public static ushort Read(KirbySession session)
    {
        Hero h = session.Hero;
        if (TestOverride?.Invoke(session) is ushort test) return WithShieldGuard(session, test);
        if (!Available(h)) return 0;
        var c = h.controller;
        ushort keys = 0;
        if (Down(c, 0)) keys |= 1; // DS A: original jump, flight, slide combinations.
        if (Down(c, 1)) keys |= 512; // DS L: original guard.
        if (Down(c, 4)) keys |= 2; // Skill 1: original inhale/ability/spit.
        if (Down(c, 5)) keys |= 4; // DS SELECT: original copy discard, verified against the ROM.
        if (Down(c, 28)) keys |= 1024; // DS X: create/return the original CPU helper.
        if (Down(c, 11)) keys |= 32;
        if (Down(c, 13)) keys |= 16;
        if (Down(c, 10)) keys |= 64;
        if (Down(c, 12)) keys |= 128;
        // Original DS directions are digital. Honour the selected movement stick
        // and existing dead zone when turning host analog input into DS directions.
        if (c.parent.gc?.device is { } device && (c.parent.mode & Controller.Class.ENABLE_PAD) != 0)
        {
            for (int direction = 0; direction < 2; direction++)
            {
                var sticks = c.getMovementSticks(direction == 1);
                for (int i = 0; i < sticks.length; i++)
                {
                    int axis = Get(sticks, i);
                    int mapped = Get(dc.libs.heaps.GamePad.Class.MAPPING, axis);
                    if (mapped < 0 || mapped >= device.values.length) continue;
                    double? configuredDeadZone = axis >= 24 ? c.rightDeadZone : c.leftDeadZone;
                    double dead = configuredDeadZone is double configured ? Math.Clamp(configured, 0, 1) : c.parent.gc.deadZone;
                    double value = ((double*)device.values.bytes)[mapped];
                    if (direction == 0) { if (value < -dead) keys |= 32; else if (value > dead) keys |= 16; }
                    // Host GamePad movement Y is positive upward.
                    else { if (value < -dead) keys |= 128; else if (value > dead) keys |= 64; }
                }
            }
        }
        if (dc.Main.Class.ME.options.invertPlayerMovements)
        {
            ushort directions = (ushort)(keys & 240);
            keys = (ushort)((keys & ~240) | ((directions & 80) << 1) | ((directions & 160) >> 1));
        }
        if (!session.Rules.IsBaseForm)
            for (int slot = 0; slot < h.inventory.nbWeapons; slot++)
            {
                var item = h.inventory.getEquippedWeaponOn(slot);
                if (item?.hasTag("Shield".AsHaxeString()) == true) continue;
                if (Down(c, slot + 2)) keys |= 2;
            }
        return WithShieldGuard(session, keys);
    }
    private static ushort WithShieldGuard(KirbySession session, ushort keys)
    {
        var hero = session.Hero;
        for (int slot = 0; slot < hero.inventory.nbWeapons; slot++)
        {
            var item = hero.inventory.getEquippedWeaponOn(slot);
            if (item?.hasTag("Shield".AsHaxeString()) == true &&
                session.Rules.CanUse(HostWeaponKind.Shield, item.permanentId.ToString(CultureInfo.InvariantCulture)) &&
                Down(hero.controller, slot + 2)) keys |= 512;
        }
        // The source guard handles native body contact while the same held
        // shield independently follows the host's real parry/counter path.
        return keys;
    }
    public static void WithoutHostMovement(Hero hero, Action update)
    {
        var profile = hero.controller.parent.get_bindings();
        var arrays = new[] { profile.primary, profile.secondary, profile.third, profile.padA, profile.padB, profile.padC };
        int[] saved = new int[arrays.Length * movementActions.Length];
        int at = 0;
        foreach (var array in arrays)
            foreach (int action in movementActions)
            {
                saved[at++] = Get(array, action);
                if ((uint)action < (uint)array.length) ((int*)array.bytes)[action] = -1;
            }
        var previous = suppressed;
        suppressed = new(hero.controller, arrays, saved, previous);
        try { update(); }
        finally
        {
            suppressed = previous; at = 0;
            foreach (var array in arrays)
                foreach (int action in movementActions)
                {
                    if ((uint)action < (uint)array.length) ((int*)array.bytes)[action] = saved[at];
                    at++;
                }
        }
    }

    /// <summary>Let host weapons aim without enabling the host character controller.</summary>
    internal static void WithHostDirections(Hero hero, Action readDirections)
    {
        var context = suppressed;
        if (context is null || context.Access.HashlinkPointer != hero.controller.HashlinkPointer)
        { readDirections(); return; }
        // A nested Hero hook may have captured an already masked profile.
        // The outermost matching scope owns the real user bindings.
        while (context.Parent?.Access.HashlinkPointer == context.Access.HashlinkPointer)
            context = context.Parent;
        Span<int> masked = stackalloc int[context.Arrays.Length * directionActions.Length];
        int at = 0;
        for (int arrayIndex = 0; arrayIndex < context.Arrays.Length; arrayIndex++)
        {
            var array = context.Arrays[arrayIndex];
            foreach (int action in directionActions)
            {
                masked[at++] = Get(array, action);
                if ((uint)action < (uint)array.length)
                    ((int*)array.bytes)[action] = context.Saved[arrayIndex * movementActions.Length + Array.IndexOf(movementActions, action)];
            }
        }
        var priorReader = directionReader;
        directionReader = hero.controller;
        try { readDirections(); }
        finally
        {
            directionReader = priorReader;
            at = 0;
            foreach (var array in context.Arrays)
                foreach (int action in directionActions)
                {
                    if ((uint)action < (uint)array.length) ((int*)array.bytes)[action] = masked[at];
                    at++;
                }
        }
    }
}
