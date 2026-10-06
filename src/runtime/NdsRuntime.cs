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
    private static delegate* unmanaged[Cdecl]<nint, int, int, int> movePlayer;
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
        LabActivationTrace.Mark("Native Open begin");
        handle = Native.Open(Path.GetFullPath(rom), Path.GetFullPath(state));
        LabActivationTrace.Mark("Native Open returned");
        if (handle == 0) throw new InvalidOperationException("Original-ROM machine could not load its private seed state.");
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
        }
        if (firstStep) { LabActivationTrace.Mark("First native Step complete"); firstStep = false; }
    }
    public NdsState State { get { Check(); Result(Native.State(handle, out NdsState state)); return state; } }
    public int ReadHits(Span<NdsHit> hits)
    {
        Check(); fixed (NdsHit* ptr = hits) { int count = Native.Hits(handle, ptr, (nuint)hits.Length); Result(count); return count; }
    }
    public void Read(uint address, Span<byte> destination)
    {
        Check(); if (destination.IsEmpty) return;
        fixed (byte* ptr = destination) Result(Native.Read(handle, address, ptr, (nuint)destination.Length));
    }
    public void Write(uint address, ReadOnlySpan<byte> source)
    {
        Check(); if (source.IsEmpty) return;
        fixed (byte* ptr = source) Result(Native.Write(handle, address, ptr, (nuint)source.Length));
    }
    public int ActionFlags { get { Check(); int value = Native.ActionFlags(handle); Result(value); return value; } }
    public int Health { get { Check(); int value = Native.Health(handle); Result(value); return value; } }
    public int MaxHealth { get { Check(); int value = Native.MaxHealth(handle); Result(value); return value; } }
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
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NdsHit
{
    public uint Address, Owner, Slot;
    public fixed short Bounds[4];
    public uint Definition;
    public fixed ushort Words[14];
}
