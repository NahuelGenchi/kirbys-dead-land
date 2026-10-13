using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kirby.Runtime;

/// <summary>Thread-affine owner of one isolated original-ROM machine.</summary>
public sealed unsafe class NdsRuntime : IDisposable
{
    public const string RomSha256 = "b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027";
    private readonly int thread = Environment.CurrentManagedThreadId;
    private nint handle;
    private static nint library;
    private static delegate* unmanaged[Cdecl]<nint, int, int, int> movePlayer, moveHelper;
    private static delegate* unmanaged[Cdecl]<nint, NdsHelper*, int> helperState;
    private static delegate* unmanaged[Cdecl]<nint, uint, int> helperOwns;
    private static delegate* unmanaged[Cdecl]<nint, byte, int> setAbility, setMikeShouts, mainScreenOnly;
    private static delegate* unmanaged[Cdecl]<nint, int> abilityChangePending, unfinishedPasses;
    private bool firstStep = true, firstWorld = true;
    // Kirby's original screen position after the latest source frame, taken
    // before the next World call rebases his coordinates.
    private (int X, int Y) steppedOrigin;
    public byte[] SourceBgPalette { get; } = new byte[512];
    /// <summary>
    /// Kirby's original screen position for the OBJ attributes currently in
    /// OAM. The game builds OAM during a frame and the hardware shows it from
    /// the next one, so after a source frame OAM describes the previous
    /// frame's positions. Pairing it with the newest state shifted Kirby by
    /// a whole terrain cell whenever the host window rebased under him.
    /// </summary>
    public (int X, int Y) DisplayedOrigin { get; private set; }
    /// <summary>The original controller's action code (see <see cref="Kirby.Core.KirbyActions"/>).</summary>
    public ushort Action
    {
        get
        {
            Span<byte> value = stackalloc byte[2];
            Read(0x020BAF80, value);
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(value);
        }
    }
    public NdsRuntime(string nativeLibrary, string rom, string state)
    {
        using var stream = System.IO.File.OpenRead(rom);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), RomSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ROM does not match Kirby Super Star Ultra USA revision 0.");
        LabActivationTrace.Mark("ROM hash verified");
        if (library == 0)
        {
            library = NativeLibrary.Load(Path.GetFullPath(nativeLibrary));
            NativeLibrary.SetDllImportResolver(typeof(NdsRuntime).Assembly,
                (name, _, _) => name == "kssu_bridge" ? library : 0);
        }
        if (Native.ProxyCapacity() != 31)
            throw new InvalidOperationException("The native bridge does not match this runtime. Rebuild and stage both components together.");
        if (movePlayer == null && NativeLibrary.TryGetExport(library, "kssu_move_player", out nint move))
            movePlayer = (delegate* unmanaged[Cdecl]<nint, int, int, int>)move;
        if (moveHelper == null && NativeLibrary.TryGetExport(library, "kssu_move_helper", out nint helperMove) &&
            NativeLibrary.TryGetExport(library, "kssu_helper_state", out nint helperRead))
        {
            moveHelper = (delegate* unmanaged[Cdecl]<nint, int, int, int>)helperMove;
            helperState = (delegate* unmanaged[Cdecl]<nint, NdsHelper*, int>)helperRead;
        }
        if (helperOwns == null && NativeLibrary.TryGetExport(library, "kssu_helper_owns", out nint ownsHelper))
            helperOwns = (delegate* unmanaged[Cdecl]<nint, uint, int>)ownsHelper;
        if (setAbility == null && NativeLibrary.TryGetExport(library, "kssu_set_ability", out nint set) &&
            NativeLibrary.TryGetExport(library, "kssu_ability_change_pending", out nint pending) &&
            NativeLibrary.TryGetExport(library, "kssu_set_mike_shouts", out nint shouts))
        {
            setAbility = (delegate* unmanaged[Cdecl]<nint, byte, int>)set;
            abilityChangePending = (delegate* unmanaged[Cdecl]<nint, int>)pending;
            setMikeShouts = (delegate* unmanaged[Cdecl]<nint, byte, int>)shouts;
        }
        if (mainScreenOnly == null && NativeLibrary.TryGetExport(library, "kssu_main_screen_only", out nint mainOnly))
            mainScreenOnly = (delegate* unmanaged[Cdecl]<nint, byte, int>)mainOnly;
        if (unfinishedPasses == null && NativeLibrary.TryGetExport(library, "kssu_unfinished_passes", out nint unfinished))
            unfinishedPasses = (delegate* unmanaged[Cdecl]<nint, int>)unfinished;
        LabActivationTrace.Mark("Native Open begin");
        handle = Native.Open(Path.GetFullPath(rom), Path.GetFullPath(state));
        LabActivationTrace.Mark("Native Open returned");
        if (handle == 0) throw new InvalidOperationException("Original-ROM machine could not load its private seed state.");
        // The mod never shows the lower screen, so its picture is not drawn.
        // The machine and the upper screen are unaffected. This only saves
        // time: an older bridge without the switch, or a refusal, draws both.
        if (mainScreenOnly != null) mainScreenOnly(handle, 1);
        fixed (byte* pointer = SourceBgPalette)
            Result(Native.ReadGpu(handle, 0x05000000, pointer, (nuint)SourceBgPalette.Length));
        LabActivationTrace.Mark("Source BG palette read");
        ResetOrigin();
    }
    private static (int X, int Y) ScreenOrigin(in NdsState state) =>
        ((int)Math.Floor(state.X) - state.CameraX, (int)Math.Floor(state.Y) - state.CameraY);
    private void ResetOrigin() => DisplayedOrigin = steppedOrigin = ScreenOrigin(State);
    public nint Handle { get { Check(); return handle; } }
    private void Check()
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
        if (Environment.CurrentManagedThreadId != thread)
            throw new InvalidOperationException("NDS operations must run on the owning game thread.");
    }
    private static void Result(int status)
    {
        if (status < 0) throw new InvalidOperationException($"NDS bridge returned {status}.");
    }
    public void Step(ushort buttons, uint frames = 1)
    {
        Check();
        if (firstStep) LabActivationTrace.Mark("First native Step begin");
        // One frame at a time, so the OAM origin is known after every frame.
        for (uint i = 0; i < frames; i++)
        {
            Result(Native.Step(handle, buttons, 1));
            DisplayedOrigin = steppedOrigin;
            steppedOrigin = ScreenOrigin(State);
            int unfinished = UnfinishedPasses;
            PassUnfinished = unfinished != countedPasses;
            countedPasses = unfinished;
        }
        if (firstStep) { LabActivationTrace.Mark("First native Step complete"); firstStep = false; }
    }
    public NdsState State { get { Check(); Result(Native.State(handle, out NdsState state)); return state; } }
    /// <summary>
    /// Source frames so far that ended while the original was inside a logic
    /// pass: its actors already moved, its sprites not yet built. The bridge
    /// ends each frame at VBlank so that none does; such a frame can draw Kirby
    /// a terrain cell off when the window moves under it (#134). Zero with an
    /// older bridge, which cannot tell.
    /// </summary>
    public int UnfinishedPasses
    {
        get { Check(); if (unfinishedPasses == null) return 0; int value = unfinishedPasses(handle); Result(value); return value; }
    }
    private int countedPasses;
    /// <summary>
    /// The last frame ended inside a logic pass: the original takes longer than
    /// a frame over it, as it does when it loads an ability or a helper. Kirby
    /// and the terrain window must stay where they are until it has finished.
    /// </summary>
    public bool PassUnfinished { get; private set; }
    public int ReadHits(Span<NdsHit> hits)
    {
        Check(); fixed (NdsHit* ptr = hits) { int count = Native.Hits(handle, ptr, (nuint)hits.Length); Result(count); return count; }
    }
    public void Read(uint address, Span<byte> destination)
    {
        Check(); if (destination.IsEmpty) return;
        fixed (byte* ptr = destination) Result(Native.Read(handle, address, ptr, (nuint)destination.Length));
    }
    /// <summary>Read GPU registers, palette RAM, VRAM or OAM through the bridge's checked GPU range.</summary>
    public void ReadGpu(uint address, Span<byte> destination)
    {
        Check(); if (destination.IsEmpty) return;
        fixed (byte* ptr = destination) Result(Native.ReadGpu(handle, address, ptr, (nuint)destination.Length));
    }
    /// <summary>
    /// Copy the OBJ pieces the original OAM builder culled from the OAM the
    /// hardware shows now (see <see cref="FarObject"/>). Returns the count.
    /// </summary>
    public int ReadFarObjects(Span<FarObject> destination)
    {
        Check();
        fixed (FarObject* ptr = destination)
        {
            int count = Native.FarObjects(handle, ptr, (nuint)destination.Length);
            Result(count);
            return count;
        }
    }
    public void Write(uint address, ReadOnlySpan<byte> source)
    {
        Check(); if (source.IsEmpty) return;
        fixed (byte* ptr = source) Result(Native.Write(handle, address, ptr, (nuint)source.Length));
    }
    public int ActionFlags { get { Check(); int value = Native.ActionFlags(handle); Result(value); return value; } }
    /// <summary>Kirby's original HP; -2 while the original has no player health group (callers skip that frame).</summary>
    public int Health { get { Check(); int value = Native.Health(handle); if (value != -2) Result(value); return value; } }
    public int MaxHealth { get { Check(); int value = Native.MaxHealth(handle); if (value != -2) Result(value); return value; } }
    public void SetHealth(ushort current, ushort maximum) { Check(); Result(Native.SetHealth(handle, current, maximum)); }
    public void Hurt(ushort damage, int direction) { Check(); Result(Native.Hurt(handle, damage, direction)); }
    public bool Owns(uint entity) { Check(); int value = Native.Owns(handle, entity); Result(value); return value != 0; }
    public void SetProxySize(uint slot, ushort width, ushort height)
    { Check(); Result(Native.ProxySize(handle, slot, width, height)); }
    public void SetProxyOptions(uint slot, byte ability, bool boss)
    { Check(); Result(Native.ProxyOptions(handle, slot, ability, boss)); }
    public void SetProxyContact(uint slot, bool enabled)
    { Check(); Result(Native.ProxyContact(handle, slot, enabled)); }
    public void DetachProxy(uint slot)
    { Check(); Result(Native.ProxyDetach(handle, slot)); }
    public int CleanupRoom()
    { Check(); int count = Native.RoomCleanup(handle); Result(count); return count; }
    public bool Feed(byte ability) { Check(); return Native.Feed(handle, ability) == 0; }
    public void SetSwallowedAbility(byte ability) { Check(); Result(Native.SetSwallowedAbility(handle, ability)); }
    public void SetProxy(uint slot, int xRaw, int yRaw, int hp, bool active = true)
    { Check(); Result(Native.SetProxy(handle, slot, xRaw, yRaw, hp, active)); }
    public NdsProxy Proxy(uint slot)
    { Check(); Result(Native.Proxy(handle, slot, out var proxy)); return proxy; }
    public void SaveState(string path) { Check(); Result(Native.SaveState(handle, Path.GetFullPath(path))); }
    public void LoadState(string path) { Check(); Result(Native.LoadState(handle, Path.GetFullPath(path))); ResetOrigin(); }
    public void World(ReadOnlySpan<ushort> cells, int xRaw, int yRaw)
    {
        Check();
        if (firstWorld) LabActivationTrace.Mark("First native World begin");
        fixed (ushort* ptr = cells) Result(Native.World(handle, ptr, (nuint)cells.Length, xRaw, yRaw));
        if (firstWorld) { LabActivationTrace.Mark("First native World complete"); firstWorld = false; }
    }
    /// <summary>
    /// The bridge can move Kirby alone. An older bridge only has <see cref="World"/>,
    /// which moves every original object with him.
    /// </summary>
    public static bool CanMovePlayer => movePlayer != null;
    /// <summary>
    /// Move only Kirby by a 16.16 delta in the current window, with his cached
    /// terrain probes; thrown objects, effects, the helper and enemies stay where
    /// the original left them, and the source camera follows him by itself.
    /// </summary>
    public void MovePlayer(int dxRaw, int dyRaw)
    {
        Check();
        if (movePlayer == null) throw new NotSupportedException("The native bridge cannot move Kirby alone. Rebuild and stage it with this runtime.");
        Result(movePlayer(handle, dxRaw, dyRaw));
    }
    /// <summary>The bridge can read and move the original helper (#105).</summary>
    public static bool CanMoveHelper => moveHelper != null && helperState != null;
    /// <summary>The original CPU helper's position, velocity and action; false while Kirby has none (or an older bridge).</summary>
    public bool TryHelper(out NdsHelper helper)
    {
        Check();
        helper = default;
        if (helperState == null) return false;
        fixed (NdsHelper* ptr = &helper)
        {
            int result = helperState(handle, ptr);
            if (result == -2) return false;
            Result(result);
        }
        return true;
    }
    /// <summary>
    /// Move only the original helper by a 16.16 delta, with its cached terrain
    /// probes (see <see cref="MovePlayer"/>); false while Kirby has none.
    /// </summary>
    public bool MoveHelper(int dxRaw, int dyRaw)
    {
        Check();
        if (moveHelper == null) return false;
        int result = moveHelper(handle, dxRaw, dyRaw);
        if (result == -2) return false;
        Result(result);
        return true;
    }
    /// <summary>The bridge can tell the original helper's objects from Kirby's (#128).</summary>
    public static bool CanTellHelper => helperOwns != null;
    /// <summary>
    /// The object is the original CPU helper or descends from it. False while Kirby
    /// has no helper, and always with an older bridge (its hits then count as Kirby's).
    /// </summary>
    public bool HelperOwns(uint entity)
    {
        Check();
        if (helperOwns == null) return false;
        int result = helperOwns(handle, entity);
        Result(result);
        return result != 0;
    }
    /// <summary>The bridge can change Kirby's copy through the original ability routine (#102).</summary>
    public static bool CanChangeAbility => setAbility != null;
    /// <summary>
    /// Ask the original ability routine for this copy byte (1 is base form) on the
    /// next player update. False while the original would refuse it (Kirby is not
    /// standing, walking, airborne or swimming) or a change is still loading.
    /// </summary>
    public bool RequestAbility(byte ability)
    {
        Check();
        if (setAbility == null) return false;
        int result = setAbility(handle, ability);
        if (result is -2 or -3) return false;
        Result(result);
        return true;
    }
    /// <summary>A requested ability change has not returned yet (it loads graphics for 2–5 frames).</summary>
    public bool AbilityChangePending
    {
        get { Check(); if (abilityChangePending == null) return false; int value = abilityChangePending(handle); Result(value); return value != 0; }
    }
    /// <summary>Set Mike's remaining shouts before a shout; false unless Kirby holds Mike.</summary>
    public bool SetMikeShouts(byte shouts)
    {
        Check();
        return setMikeShouts != null && setMikeShouts(handle, shouts) == 0;
    }
    public void Dispose()
    {
        if (handle == 0) return; Check(); Native.Close(handle); handle = 0;
    }
    private static class Native
    {
        [DllImport("kssu_bridge", EntryPoint = "kssu_proxy_capacity", CallingConvention = CallingConvention.Cdecl)] internal static extern uint ProxyCapacity();
        [DllImport("kssu_bridge", EntryPoint = "kssu_player_owns", CallingConvention = CallingConvention.Cdecl)] internal static extern int Owns(nint h, uint entity);
        [DllImport("kssu_bridge", EntryPoint = "kssu_health", CallingConvention = CallingConvention.Cdecl)] internal static extern int Health(nint h);
        [DllImport("kssu_bridge", EntryPoint = "kssu_max_health", CallingConvention = CallingConvention.Cdecl)] internal static extern int MaxHealth(nint h);
        [DllImport("kssu_bridge", EntryPoint = "kssu_set_health", CallingConvention = CallingConvention.Cdecl)] internal static extern int SetHealth(nint h, ushort current, ushort maximum);
        [DllImport("kssu_bridge", EntryPoint = "kssu_hurt", CallingConvention = CallingConvention.Cdecl)] internal static extern int Hurt(nint h, ushort damage, int direction);
        [DllImport("kssu_bridge", EntryPoint = "kssu_read_gpu", CallingConvention = CallingConvention.Cdecl)] internal static extern int ReadGpu(nint h, uint address, byte* data, nuint length);
        [DllImport("kssu_bridge", EntryPoint = "kssu_far_objs", CallingConvention = CallingConvention.Cdecl)] internal static extern int FarObjects(nint h, FarObject* objects, nuint capacity);
        [DllImport("kssu_bridge", EntryPoint = "kssu_proxy_size", CallingConvention = CallingConvention.Cdecl)] internal static extern int ProxySize(nint h, uint slot, ushort width, ushort height);
        [DllImport("kssu_bridge", EntryPoint = "kssu_proxy_options", CallingConvention = CallingConvention.Cdecl)] internal static extern int ProxyOptions(nint h, uint slot, byte ability, [MarshalAs(UnmanagedType.I1)] bool boss);
        [DllImport("kssu_bridge", EntryPoint = "kssu_proxy_contact", CallingConvention = CallingConvention.Cdecl)] internal static extern int ProxyContact(nint h, uint slot, [MarshalAs(UnmanagedType.I1)] bool enabled);
        [DllImport("kssu_bridge", EntryPoint = "kssu_proxy_detach", CallingConvention = CallingConvention.Cdecl)] internal static extern int ProxyDetach(nint h, uint slot);
        [DllImport("kssu_bridge", EntryPoint = "kssu_room_cleanup", CallingConvention = CallingConvention.Cdecl)] internal static extern int RoomCleanup(nint h);
        [DllImport("kssu_bridge", EntryPoint = "kssu_set_swallowed_ability", CallingConvention = CallingConvention.Cdecl)] internal static extern int SetSwallowedAbility(nint h, byte ability);
        [DllImport("kssu_bridge", EntryPoint = "kssu_proxy_set", CallingConvention = CallingConvention.Cdecl)] internal static extern int SetProxy(nint h, uint slot, int x, int y, int hp, [MarshalAs(UnmanagedType.I1)] bool active);
        [DllImport("kssu_bridge", EntryPoint = "kssu_proxy_state", CallingConvention = CallingConvention.Cdecl)] internal static extern int Proxy(nint h, uint slot, out NdsProxy proxy);
        [DllImport("kssu_bridge", EntryPoint = "kssu_save_state", CallingConvention = CallingConvention.Cdecl)] internal static extern int SaveState(nint h, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport("kssu_bridge", EntryPoint = "kssu_load_state", CallingConvention = CallingConvention.Cdecl)] internal static extern int LoadState(nint h, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport("kssu_bridge", EntryPoint = "kssu_action_flags", CallingConvention = CallingConvention.Cdecl)] internal static extern int ActionFlags(nint h);
        [DllImport("kssu_bridge", EntryPoint = "kssu_feed", CallingConvention = CallingConvention.Cdecl)] internal static extern int Feed(nint h, byte ability);
        [DllImport("kssu_bridge", EntryPoint = "kssu_world", CallingConvention = CallingConvention.Cdecl)] internal static extern int World(nint h, ushort* cells, nuint count, int xRaw, int yRaw);
        [DllImport("kssu_bridge", EntryPoint = "kssu_open", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Open([MarshalAs(UnmanagedType.LPUTF8Str)] string rom, [MarshalAs(UnmanagedType.LPUTF8Str)] string state);
        [DllImport("kssu_bridge", EntryPoint = "kssu_close", CallingConvention = CallingConvention.Cdecl)] internal static extern void Close(nint h);
        [DllImport("kssu_bridge", EntryPoint = "kssu_step", CallingConvention = CallingConvention.Cdecl)] internal static extern int Step(nint h, ushort mask, uint frames);
        [DllImport("kssu_bridge", EntryPoint = "kssu_state", CallingConvention = CallingConvention.Cdecl)] internal static extern int State(nint h, out NdsState state);
        [DllImport("kssu_bridge", EntryPoint = "kssu_hits", CallingConvention = CallingConvention.Cdecl)] internal static extern int Hits(nint h, NdsHit* hits, nuint count);
        [DllImport("kssu_bridge", EntryPoint = "kssu_read", CallingConvention = CallingConvention.Cdecl)] internal static extern int Read(nint h, uint address, byte* data, nuint length);
        [DllImport("kssu_bridge", EntryPoint = "kssu_write", CallingConvention = CallingConvention.Cdecl)] internal static extern int Write(nint h, uint address, byte* data, nuint length);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct NdsProxy
{
    public uint Entity, Flags;
    public int XRaw, YRaw, Hp;
    public uint NormalUpdate;
    public readonly bool Controlled => (Flags & 2) != 0;
    public readonly bool Swallowed => (Flags & 4) != 0;
    public readonly bool Gone => (Flags & 8) != 0;
    public readonly bool Pending => (Flags & 16) != 0;
    /// <summary>The original inhale is pulling it (not a hit recoil, which also sets <see cref="Controlled"/>).</summary>
    public readonly bool Inhaling => (Flags & 32) != 0;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NdsState
{
    public uint Frame, Player;
    public int XRaw, YRaw;
    public short VxRaw, VyRaw, CameraX, CameraY;
    public byte Ability, Kind, Flags, FacingRaw;
    public uint Graphic, Animation;
    public fixed uint StateWords[16];
    public readonly double X => XRaw / 65536.0;
    public readonly double Y => YRaw / 65536.0;
    /// <summary>
    /// The original facing: bit 31 of the player word at +0x6C. FacingRaw
    /// (+0x15) does not change with facing; its 0x10 bit hides the actor.
    /// </summary>
    public readonly bool FacesLeft => (StateWords[11] & 0x8000_0000u) != 0;
}

/// <summary>The original CPU helper (the DS X button's partner), in source coordinates (16.16).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NdsHelper
{
    public int XRaw, YRaw;
    public short VxRaw, VyRaw;
    /// <summary>The helper controller's action code, in Kirby's codes (<see cref="Kirby.Core.KirbyActions"/>).</summary>
    public ushort Action;
    private ushort reserved;
    public readonly double X => XRaw / 65536.0;
    public readonly double Y => YRaw / 65536.0;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NdsHit
{
    public uint Address, Owner, Slot;
    public fixed short Bounds[4];
    public uint Definition;
    public fixed ushort Words[14];
}
