using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kirby.Runtime;

/// <summary>
/// The native libraries the runtime calls, by the names each system gives them
/// (#244). P/Invokes name a library by one of the short names below, and one
/// resolver for the assembly maps it to this system's file: the bridge, which
/// <see cref="NdsRuntime.Open"/> loads from the mod's folder, and the game's own
/// SDL2 and OpenGL, already loaded in its process.
/// </summary>
internal static class NativeLibraries
{
    public const string Bridge = "kssu_bridge", Sdl = "SDL2", OpenGl = "GL";

    /// <summary>The bridge's file in the mod's folder, as tools/nds_build.py names it for this system.</summary>
    public static string BridgeFile => OperatingSystem.IsWindows() ? "kssu_bridge.dll" : "libkssu_bridge.so";

    /// <summary>The loaded bridge, once <see cref="NdsRuntime.Open"/> has loaded it.</summary>
    internal static nint BridgeHandle { get; set; }

    /// <summary>This system's file for a short name, or null for a name the runtime does not map.</summary>
    internal static string? File(string name, bool windows) => name switch
    {
        Sdl => windows ? "SDL2.dll" : "libSDL2-2.0.so.0",
        OpenGl => windows ? "opengl32.dll" : "libGL.so.1",
        _ => null,
    };

    // Runs when the assembly loads, before any of its P/Invokes is bound.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => NativeLibrary.SetDllImportResolver(typeof(NativeLibraries).Assembly, Resolve);

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name == Bridge) return BridgeHandle;
        string? file = File(name, OperatingSystem.IsWindows());
        return file is not null && NativeLibrary.TryLoad(file, assembly, searchPath, out nint handle) ? handle : 0;
    }
}
