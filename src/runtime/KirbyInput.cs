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
    /// <summary>True when the last horizontal input was only a light analog tilt.</summary>
    public static bool AnalogWalk { get; private set; }
    /// <summary>The host's dedicated dive-attack action is held (used when the player binds the dive to it).</summary>
    public static bool DiveButton { get; private set; }
    /// <summary>
    /// Jump is held only through keys or buttons that are also bound to Up (by
    /// default W and the Up arrow). The host lets such a key climb a ladder
    /// or chain instead of jumping.
    /// </summary>
    public static bool JumpFromUpOnly { get; private set; }
    /// <summary>A Shift key bound to Dodge is held: Kirby's original slide instead of the guard.</summary>
    public static bool SlideButton { get; private set; }
    /// <summary>
    /// Controller bindings follow Kirby's controller layout (#130): an 8-way
    /// stick, Dodge slides, Skill 1 guards and Skill 2 discards only when held.
    /// False reads the controller exactly like the keyboard.
    /// </summary>
    public static bool ControllerLayout { get; set; } = true;
    private const double DiscardHold = 0.3;
    private static readonly HeldButton discard = new(DiscardHold);
    private const double AnalogDashTilt = 0.75;
    private sealed record Suppression(ControllerAccess Access, ArrayBytes_Int[] Arrays, int[] Saved, Suppression? Parent);
    private static Suppression? suppressed;
    private static ControllerAccess? directionReader;
    private static ArrayBytes_Int? noSticks;
    // Action IDs confirmed against this host's Options.configController. 36 is
    // the dedicated dive attack: Kirby's stomp starts the host dive itself.
    private static readonly int[] movementActions = [0, 1, 4, 5, 10, 11, 12, 13, 28, 36];
    private const int DiveAction = 36;
    private const int DodgeAction = 1;
    private const int HealAction = 7;
    // hxd.Key.SHIFT; the left and right keys add a location flag above the low byte.
    private const int ShiftKey = 16;
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
    public static bool Down(ControllerAccess access, int action) => KeyDown(access, action) || PadDown(access, action);
    private static bool KeyDown(ControllerAccess access, int action)
    {
        Controller c = access.parent;
        BindingProfiles b = c.get_bindings();
        if ((c.mode & Controller.Class.ENABLE_KEY) != 0)
            foreach (var a in new[] { b.primary, b.secondary, b.third })
            {
                int key = Get(a, action);
                if (key >= 0 && dc.hxd.Key.Class.isDown(key)) return true;
            }
        return false;
    }
    private static bool PadDown(ControllerAccess access, int action)
    {
        Controller c = access.parent;
        BindingProfiles b = c.get_bindings();
        if ((c.mode & Controller.Class.ENABLE_PAD) != 0)
            foreach (var a in new[] { b.padA, b.padB, b.padC })
            {
                int key = Get(a, action);
                if (key >= 0 && c.padIsDown(key)) return true;
            }
        return false;
    }
    /// <summary>
    /// Dodge splits by key: a Shift key slides (Kirby's roll), and every other
    /// key bound to Dodge (Ctrl by default) guards. The controller's Dodge
    /// button slides in the controller layout and guards without it.
    /// </summary>
    private static (bool Guard, bool Slide) DodgeDown(ControllerAccess access)
    {
        Controller c = access.parent;
        BindingProfiles b = c.get_bindings();
        bool guard = false, slide = false;
        if ((c.mode & Controller.Class.ENABLE_KEY) != 0)
            foreach (var a in new[] { b.primary, b.secondary, b.third })
            {
                int key = Get(a, DodgeAction);
                if (key < 0 || !dc.hxd.Key.Class.isDown(key)) continue;
                if ((key & 0xFF) == ShiftKey) slide = true; else guard = true;
            }
        if ((c.mode & Controller.Class.ENABLE_PAD) != 0)
            foreach (var a in new[] { b.padA, b.padB, b.padC })
            {
                int key = Get(a, DodgeAction);
                if (key < 0 || !c.padIsDown(key)) continue;
                if (ControllerLayout) slide = true; else guard = true;
            }
        return (guard, slide);
    }
    private static bool JumpOnlyFromUp(ControllerAccess access)
    {
        Controller c = access.parent;
        BindingProfiles b = c.get_bindings();
        bool any = false;
        if ((c.mode & Controller.Class.ENABLE_KEY) != 0)
        {
            ArrayBytes_Int[] keys = [b.primary, b.secondary, b.third];
            foreach (var array in keys)
            {
                int key = Get(array, 0);
                if (key < 0 || !dc.hxd.Key.Class.isDown(key)) continue;
                if (!keys.Any(other => Get(other, 10) == key)) return false;
                any = true;
            }
        }
        if ((c.mode & Controller.Class.ENABLE_PAD) != 0)
        {
            ArrayBytes_Int[] buttons = [b.padA, b.padB, b.padC];
            foreach (var array in buttons)
            {
                int button = Get(array, 0);
                if (button < 0 || !c.padIsDown(button)) continue;
                if (!buttons.Any(other => Get(other, 10) == button)) return false;
                any = true;
            }
        }
        return any;
    }
    public static ushort Read(KirbySession session)
    {
        Hero h = session.Hero;
        AnalogWalk = false;
        DiveButton = false;
        JumpFromUpOnly = false;
        SlideButton = false;
        // The host Heal binding (F by default) fires the stored limited-use ability (#102).
        KirbyFSlot.Held = Available(h) && Down(h.controller, HealAction);
        if (TestOverride?.Invoke(session) is ushort test) return WithShieldGuard(session, test);
        if (!Available(h)) return 0;
        var c = h.controller;
        DiveButton = Down(c, DiveAction);
        ushort keys = 0;
        if (Down(c, 0)) keys |= 1; // DS A: original jump, flight, slide combinations.
        (bool guard, bool slide) = DodgeDown(c);
        if (guard) keys |= 512; // DS L: original guard.
        SlideButton = slide; // Down + A on the ground: original slide (KirbyNdsProvider.Step).
        if (KeyDown(c, 4)) keys |= 2; // Skill 1: original inhale/ability/spit.
        // The weapon buttons already attack on a controller: its Skill 1 guards.
        if (PadDown(c, 4)) keys |= (ushort)(ControllerLayout ? 512 : 2);
        // DS SELECT: original copy discard, verified against the ROM. A controller's Skill 2 must be held.
        bool padDiscard = PadDown(c, 5);
        if (KeyDown(c, 5) || (ControllerLayout ? discard.Update(padDiscard, Lib_std.sys_time()) : padDiscard)) keys |= 4;
        if (Down(c, 28)) keys |= 1024; // DS X: create/return the original CPU helper.
        if (Down(c, 11)) keys |= 32;
        if (Down(c, 13)) keys |= 16;
        bool digitalHorizontal = (keys & 48) != 0;
        double stickX = 0;
        if (Down(c, 10)) keys |= 64;
        if (Down(c, 12)) keys |= 128;
        // Original DS directions are digital. Honour the selected movement stick
        // and existing dead zone when turning host analog input into DS directions.
        if (c.parent.gc?.device is { } device && (c.parent.mode & Controller.Class.ENABLE_PAD) != 0)
        {
            // The strongest tilt and its dead zone on each axis; host GamePad movement Y is positive upward.
            Span<double> tilt = stackalloc double[2], deadZone = stackalloc double[2];
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
                    if (Math.Abs(value) <= dead || Math.Abs(value) <= Math.Abs(tilt[direction])) continue;
                    tilt[direction] = value; deadZone[direction] = dead;
                }
            }
            ushort stick = PadStick.Directions(tilt[0], tilt[1], deadZone[0], deadZone[1], ControllerLayout);
            keys |= stick;
            if ((stick & 48) != 0) stickX = Math.Abs(tilt[0]);
        }
        AnalogWalk = !digitalHorizontal && stickX > 0 && stickX < AnalogDashTilt;
        JumpFromUpOnly = (keys & 1) != 0 && JumpOnlyFromUp(c);
        if (dc.Main.Class.ME.options.invertPlayerMovements)
        {
            ushort directions = (ushort)(keys & 240);
            keys = (ushort)((keys & ~240) | ((directions & 80) << 1) | ((directions & 160) >> 1));
        }
        // Every non-shield weapon button (left-click by default) is DS B in
        // both forms: original inhale in base form, the copy attack while copied.
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
