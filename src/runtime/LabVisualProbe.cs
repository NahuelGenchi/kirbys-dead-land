using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kirby.Runtime;

/// <summary>Opt-in private visual fixture; never installed during normal play.</summary>
internal static unsafe class LabVisualProbe
{
    private static readonly byte[] abilities = [23, 21, 22];
    private static readonly string[] names = ["crash", "paint", "mike"];
    private static readonly uint[] top = new uint[NdsBackgroundEffectDecoder.PixelCount];
    private static readonly uint[] metadata = new uint[NdsBackgroundEffectDecoder.PixelCount];
    private static readonly uint[] palette = new uint[NdsBackgroundEffectDecoder.PixelCount];
    private static string? run, seed;
    private static uint? start;
    private static uint lastPoll = uint.MaxValue;
    private static int current;
    private static bool fed;
    public static bool ActiveEffect { get; private set; }

    public static void Install(string output)
    {
        run = output;
        seed = Path.Combine(output, "visual-start.state");
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyInput.TestOverride = Input;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null || current >= abilities.Length) return 0;
        if (start is null)
        {
            machine.SaveState(seed!);
            start = machine.State.Frame;
        }
        uint elapsed = machine.State.Frame - start.Value;
        if (elapsed >= 500)
        {
            current++;
            if (current >= abilities.Length) return 0;
            // Each fixture restores the same unmodified base source snapshot.
            // The ability itself is obtained through the original inhale,
            // supplied swallowed content and DOWN copy-loader sequence below.
            machine.LoadState(seed!);
            start = machine.State.Frame;
            elapsed = 0;
            fed = false;
        }
        if (elapsed is >= 60 and < 80 && !fed)
        {
            bool accepted = machine.Feed(abilities[current]);
            fed = true;
            File.AppendAllText(Path.Combine(run!, "visual-actions.jsonl"), JsonSerializer.Serialize(new
            { action = "feed", name = names[current], frame = machine.State.Frame, accepted }) + "\n");
        }
        return elapsed switch
        {
            >= 30 and < 60 => 2,
            >= 80 and < 84 => 128,
            >= 200 and < 240 => 2,
            _ => 0,
        };
    }

    public static void Poll()
    {
        if (run is null) return;
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null || machine.State.Frame == lastPoll) return;
        lastPoll = machine.State.Frame;
        NdsBackgroundEffect effect;
        if (NdsBackgroundEffectDecoder.Undrawn(machine.MainFrame) is { } undrawn) effect = undrawn;
        else
        {
            Read(machine, top, 0); Read(machine, metadata, 2); Read(machine, palette, 4);
            effect = NdsBackgroundEffectDecoder.Decode(machine.SourceBgPalette, palette, top, metadata);
        }
        ActiveEffect = effect.Active;
        var hero = KirbyMod.Instance?.ActiveSession?.Hero;
        File.AppendAllText(Path.Combine(run, "visual-effects.jsonl"), JsonSerializer.Serialize(new
        {
            frame = lastPoll, test = current < names.Length ? names[current] : "done",
            elapsed = start.HasValue ? lastPoll - start.Value : 0,
            ability = machine.State.Ability, effect,
            actorLayer = hero?.spr?.parent?.getChildIndex(hero.spr),
        }) + "\n");
    }

    private static void Read(NdsRuntime machine, uint[] values, uint plane)
    {
        fixed (uint* pointer = values)
            if (ReadLayers(machine.Handle, pointer, (nuint)values.Length, plane) != 0)
                throw new InvalidOperationException("Visual fixture source layer read failed.");
    }

    [DllImport("kssu_bridge", EntryPoint = "kssu_read_layers", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ReadLayers(nint handle, uint* destination, nuint count, uint plane);
}
