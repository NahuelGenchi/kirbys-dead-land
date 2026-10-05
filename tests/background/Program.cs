using Kirby.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length < 5) throw new ArgumentException("native-library ROM source-ready-state frames private-report-path");
using var nds = new NdsRuntime(args[0], args[1], args[2]);
uint[][] capture = Enumerable.Range(0, 5).Select(_ => new uint[NdsBackgroundEffectDecoder.PixelCount]).ToArray();
byte[] screen = new byte[NdsBackgroundEffectDecoder.PixelCount * 4];
long compared = 0, mismatches = 0;
int unsupported = 0, active = 0;
var frames = new List<object>();
for (int frame = 0; frame < int.Parse(args[3]); frame++)
{
    nds.Step((ushort)(frame < 40 ? 2 : 0));
    unsafe
    {
        for (uint plane = 0; plane < 5; plane++) fixed (uint* pointer = capture[plane])
            if (Native.Layers(nds.Handle, pointer, (nuint)capture[plane].Length, plane) != 0) throw new Exception("Layer capture read failed.");
        fixed (byte* pointer = screen)
            if (Native.Framebuffer(nds.Handle, pointer, (nuint)screen.Length, false) != 0) throw new Exception("Original framebuffer read failed.");
    }
    // Validate the captured compositor output against the independent original
    // final framebuffer, including exact melonDS RGB6-to-BGRA expansion.
    for (int i = 0; i < capture[3].Length; i++) for (int c = 0; c < 3; c++)
    {
        int six = (int)((capture[3][i] >> (c * 8)) & 63);
        int eight = (six << 2) | (six >> 4);
        if (screen[i * 4 + 2 - c] != eight) mismatches++;
        compared++;
    }
    var effect = NdsBackgroundEffectDecoder.Decode(nds.SourceBgPalette, capture[4], capture[0], capture[2]);
    if (!effect.Supported) unsupported++;
    if (effect.Active) active++;
    frames.Add(new { frame, nds = nds.State.Frame, effect });
}
var report = new { compared, mismatches, active, unsupported, frames };
File.WriteAllText(args[4], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { compared, mismatches, active, unsupported }));
if (mismatches != 0 || unsupported != 0) Environment.ExitCode = 1;

static unsafe class Native
{
    [DllImport("kssu_bridge", EntryPoint = "kssu_read_layers", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Layers(nint h, uint* destination, nuint count, uint plane);
    [DllImport("kssu_bridge", EntryPoint = "kssu_framebuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Framebuffer(nint h, byte* target, nuint length, [MarshalAs(UnmanagedType.I1)] bool bottom);
}
