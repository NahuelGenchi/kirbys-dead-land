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
    // updateWeapon reads the direction bindings only while its weapon charges. In a
    // Kirby run only the retained shield reaches the host's weapon use (TryUseWeapon),
    // from its binding or the assist mode's auto-hit, so the handler that lets it read
    // them is installed while one of those may start a charge or one lasts (#188).
    private static bool weaponHook;
    private static int weaponIdle;
    // Host frames the handler stays after the shield binding was last down.
    private const int WeaponLinger = 30;
    public static void Install()
    {
        Hook_ControllerAccess.getMovementSticks += (orig, access, alternate) =>
            suppressed?.Access.HashlinkPointer == access.HashlinkPointer && directionReader?.HashlinkPointer != access.HashlinkPointer
                ? noSticks ??= ArrayUtils.CreateInt() : orig(access, alternate);
    }
    /// <summary>Whether the weapon handler is installed now (lab report).</summary>
    internal static bool WeaponUpdateHeld => weaponHook;
    /// <summary>A host weapon reads the real direction bindings now (lab report).</summary>
    internal static bool ReadingHostDirections => directionReader is not null;
    private static void HoldWeaponUpdate()
    {
        weaponIdle = 0;
        if (weaponHook) return;
        DccmDetours.Attach("tool.hero.HeroWeaponsManager.updateWeapon");
        Hook_HeroWeaponsManager.updateWeapon += UpdateWeapon;
        weaponHook = true;
    }
    /// <summary>
    /// Remove the weapon handler once the shield binding has not been down for a
    /// while and no weapon of the hero still charges, and DCCM's detour with it (#178).
    /// Called once between host frames.
    /// </summary>
    internal static void RelaxWeaponUpdate(Hero? hero)
    {
        weaponIdle = Math.Min(weaponIdle + 1, WeaponLinger + 1);
        if (!weaponHook || weaponIdle <= WeaponLinger || hero is not null && Charging(hero)) return;
        Hook_HeroWeaponsManager.updateWeapon -= UpdateWeapon;
        DccmDetours.Detach("tool.hero.HeroWeaponsManager.updateWeapon");
        weaponHook = false;
    }
    private static bool Charging(Hero hero)
    {
        var weapons = hero.weaponsManager?.mainWeapons;
        if (weapons is null) return false;
        for (int i = 0; i < weapons.length; i++)
            if (weapons.getDyn(i) is Weapon weapon && weapon.isCharging()) return true;
        return false;
    }
    private static void UpdateWeapon(Hook_HeroWeaponsManager.orig_updateWeapon orig, HeroWeaponsManager manager, int slot) =>
        WithHostDirections(manager.hero, () => orig(manager, slot));
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

    /// <summary>
    /// The binding arrays, input modes and key states one read uses, taken from
    /// the host once (#176). Asking the host again for each action made about
    /// half a kilobyte of managed garbage per action: a static host function
    /// such as <c>Key.isDown</c> is a new delegate on every access.
    /// </summary>
    private readonly struct Bindings
    {
        private readonly Controller controller;
        // Keyboard bindings (primary, secondary, third), then controller ones (A, B, C).
        private readonly ArrayBytes_Int? key0, key1, key2, pad0, pad1, pad2;
        // hxd.Key.keyPressed: hxd.Key.isDown(code) is keyPressed[code] > 0, 0 past its end.
        private readonly ArrayBytes_Int? pressed;
        public readonly bool Keys, Pad;

        public Bindings(ControllerAccess access)
        {
            controller = access.parent;
            BindingProfiles b = controller.get_bindings();
            int mode = controller.mode;
            Keys = (mode & Controller.Class.ENABLE_KEY) != 0;
            Pad = (mode & Controller.Class.ENABLE_PAD) != 0;
            key0 = b.primary; key1 = b.secondary; key2 = b.third;
            pad0 = b.padA; pad1 = b.padB; pad2 = b.padC;
            pressed = Keys ? dc.hxd.Key.Class.keyPressed : null;
        }

        public bool IsDown(int key) => Get(pressed, key) > 0;

        public int KeyOf(int profile, int action) => Get(profile switch { 0 => key0, 1 => key1, _ => key2 }, action);
        public int ButtonOf(int profile, int action) => Get(profile switch { 0 => pad0, 1 => pad1, _ => pad2 }, action);
        public bool PadIsDown(int button) => controller.padIsDown(button);

        public bool Down(int action) => KeyDown(action) || PadDown(action);
        public bool KeyDown(int action)
        {
            if (Keys)
                for (int profile = 0; profile < 3; profile++)
                {
                    int key = KeyOf(profile, action);
                    if (key >= 0 && IsDown(key)) return true;
                }
            return false;
        }
        public bool PadDown(int action)
        {
            if (Pad)
                for (int profile = 0; profile < 3; profile++)
                {
                    int button = ButtonOf(profile, action);
                    if (button >= 0 && controller.padIsDown(button)) return true;
                }
            return false;
        }
    }

    /// <summary>
    /// Dodge splits by key: a Shift key slides (Kirby's roll), and every other
    /// key bound to Dodge (Ctrl by default) guards. The controller's Dodge
    /// button slides in the controller layout and guards without it.
    /// </summary>
    private static (bool Guard, bool Slide) DodgeDown(in Bindings input)
    {
        bool guard = false, slide = false;
        if (input.Keys)
            for (int profile = 0; profile < 3; profile++)
            {
                int key = input.KeyOf(profile, DodgeAction);
                if (key < 0 || !input.IsDown(key)) continue;
                if ((key & 0xFF) == ShiftKey) slide = true; else guard = true;
            }
        if (input.Pad)
            for (int profile = 0; profile < 3; profile++)
            {
                int button = input.ButtonOf(profile, DodgeAction);
                if (button < 0 || !input.PadIsDown(button)) continue;
                if (ControllerLayout) slide = true; else guard = true;
            }
        return (guard, slide);
    }
    private static bool JumpOnlyFromUp(in Bindings input)
    {
        bool any = false;
        if (input.Keys)
            for (int profile = 0; profile < 3; profile++)
            {
                int key = input.KeyOf(profile, 0);
                if (key < 0 || !input.IsDown(key)) continue;
                if (!(input.KeyOf(0, 10) == key || input.KeyOf(1, 10) == key || input.KeyOf(2, 10) == key)) return false;
                any = true;
            }
        if (input.Pad)
            for (int profile = 0; profile < 3; profile++)
            {
                int button = input.ButtonOf(profile, 0);
                if (button < 0 || !input.PadIsDown(button)) continue;
                if (!(input.ButtonOf(0, 10) == button || input.ButtonOf(1, 10) == button || input.ButtonOf(2, 10) == button)) return false;
                any = true;
            }
        return any;
    }
    public static ushort Read(KirbySession session)
    {
        var mark = LabPerfProbe.Start();
        ushort keys = ReadKeys(session);
        LabPerfProbe.Stop(LabPerfProbe.Section.Input, mark);
        return keys;
    }
    private static ushort ReadKeys(KirbySession session)
    {
        Hero h = session.Hero;
        AnalogWalk = false;
        DiveButton = false;
        JumpFromUpOnly = false;
        SlideButton = false;
        bool available = Available(h);
        var c = h.controller;
        Bindings input = available ? new Bindings(c) : default;
        if (dc.Main.Class.ME.options.assistMode.modeAuto) HoldWeaponUpdate();
        // The host Heal binding (F by default) fires the stored limited-use ability (#102).
        KirbyFSlot.Held = available && input.Down(HealAction);
        if (TestOverride?.Invoke(session) is ushort test) return WithShieldGuard(session, test, available ? input : null);
        if (!available) return 0;
        DiveButton = input.Down(DiveAction);
        ushort keys = 0;
        if (input.Down(0)) keys |= 1; // DS A: original jump, flight, slide combinations.
        (bool guard, bool slide) = DodgeDown(input);
        if (guard) keys |= 512; // DS L: original guard.
        SlideButton = slide; // Down + A on the ground: original slide (KirbyNdsProvider.Step).
        if (input.KeyDown(4)) keys |= 2; // Skill 1: original inhale/ability/spit.
        // The weapon buttons already attack on a controller: its Skill 1 guards.
        if (input.PadDown(4)) keys |= (ushort)(ControllerLayout ? 512 : 2);
        // DS SELECT: original copy discard, verified against the ROM. A controller's Skill 2 must be held.
        bool padDiscard = input.PadDown(5);
        if (input.KeyDown(5) || (ControllerLayout ? discard.Update(padDiscard, Lib_std.sys_time()) : padDiscard)) keys |= 4;
        if (input.Down(28)) keys |= 1024; // DS X: create/return the original CPU helper.
        if (input.Down(11)) keys |= 32;
        if (input.Down(13)) keys |= 16;
        bool digitalHorizontal = (keys & 48) != 0;
        double stickX = 0;
        if (input.Down(10)) keys |= 64;
        if (input.Down(12)) keys |= 128;
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
        JumpFromUpOnly = (keys & 1) != 0 && JumpOnlyFromUp(input);
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
            if (item?.hasTag(ShieldTag) == true) continue;
            if (input.Down(slot + 2)) keys |= 2;
        }
        return WithShieldGuard(session, keys, input);
    }
    private static dc.String? shieldTag;
    // Built once: each new host string is a host allocation and a managed wrapper.
    private static dc.String ShieldTag => shieldTag ??= "Shield".AsHaxeString();
    private static ushort WithShieldGuard(KirbySession session, ushort keys, Bindings? input)
    {
        var hero = session.Hero;
        for (int slot = 0; slot < hero.inventory.nbWeapons; slot++)
        {
            var item = hero.inventory.getEquippedWeaponOn(slot);
            if (item?.hasTag(ShieldTag) == true &&
                session.Rules.CanUse(HostWeaponKind.Shield, item.permanentId.ToString(CultureInfo.InvariantCulture)) &&
                (input ?? new Bindings(hero.controller)).Down(slot + 2))
            {
                keys |= 512;
                HoldWeaponUpdate();
            }
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
