using Hashlink.Proxy.Clousre;
using ModCore.Modules;
using ModCore.Utilities;
using HaxeString = dc.String;
using HaxeBytes = dc.haxe.io.Bytes;
using LocalFile = System.IO.File;

namespace Kirby.Runtime;

/// <summary>Opt-in local save sandbox for the development launcher. Steam authentication is untouched.</summary>
internal static class LabIsolation
{
    private static string? saveRoot;

    public static void Install()
    {
        string? requested = Environment.GetEnvironmentVariable("KIRBY_LAB_SAVE_ROOT");
        if (string.IsNullOrWhiteSpace(requested)) return;
        string lab = Environment.GetEnvironmentVariable("DCCM_OverridePath_GAME_ROOT")
            ?? throw new InvalidOperationException("Lab save isolation requires an explicit game root.");
        string root = Path.GetFullPath(requested);
        if (root != Path.GetFullPath(Path.Combine(lab, "save")) || !Directory.Exists(root))
            throw new InvalidOperationException("Lab save directory must be the isolated game's save directory.");
        saveRoot = root;
        LabSmokeCapture.Install();
        dc.tool.Hook__File.exists += (_, name) => LocalFile.Exists(Resolve(name));
        dc.tool.Hook__File.getBytes += (_, name) => Read(name);
        dc.tool.Hook__File.saveBytes += (_, name, bytes) => Write(name, bytes);
        dc.tool.Hook__File.delete += (_, name) => LocalFile.Delete(Resolve(name));
        // These queries normally inspect Steam Cloud. A lab session instead uses
        // only its local directory, without changing the user's Steam settings.
        var hooks = HashlinkHooks.Instance;
        hooks.CreateHook("steam.$Cloud", "isEnabled", (HashlinkClosure _) => false);
        hooks.CreateHook("steam.$Cloud", "isEnabledForApp", (HashlinkClosure _) => false);
        hooks.CreateHook("steam.$Cloud", "exists", (HashlinkClosure _, HaxeString name) => false);
        hooks.CreateHook("steam.$Cloud", "read", (HashlinkClosure _, HaxeString name) => (HaxeBytes)null!);
        hooks.CreateHook("steam.$Cloud", "write", (HashlinkClosure _, HaxeString name, HaxeBytes data) => false);
        hooks.CreateHook("steam.$Cloud", "delete", (HashlinkClosure _, HaxeString name) => false);
        hooks.CreateHook("steam.$Cloud", "enable", (HashlinkClosure _, bool enabled) => { });
        // Configure the game's separate backup/Cloud-status code as well.
        dc.Hook_Main.createOptions += (orig, main) =>
        {
            dc.tool.File.Class.PATH = root.AsHaxeString();
            orig(main);
        };
        LocalFile.WriteAllText(Path.Combine(root, "steam_cloud.dat"), "0");
        LocalFile.WriteAllText(Path.Combine(lab, "save-isolation.ready"), root + "\n");
    }

    private static string Resolve(HaxeString name)
    {
        string relative = name.ToString();
        if (Path.IsPathRooted(relative) || relative.Contains('/') || relative.Contains('\\') || relative is "." or "..")
            throw new InvalidDataException("Unexpected save filename: " + relative);
        return Path.Combine(saveRoot!, relative);
    }

    private static HaxeBytes Read(HaxeString name)
    {
        string path = Resolve(name);
        return LocalFile.Exists(path) ? dc.hxd.File.Class.getBytes(path.AsHaxeString()) : null!;
    }

    private static void Write(HaxeString name, HaxeBytes bytes)
    {
        string path = Resolve(name);
        dc.hxd.File.Class.saveBytes(path.AsHaxeString(), bytes);
    }
}
