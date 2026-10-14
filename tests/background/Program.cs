using Kirby.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length < 5) throw new ArgumentException("native-library ROM source-ready-state frames private-report-path");
using var nds = new NdsRuntime(args[0], args[1], args[2]);
uint[][] capture = Enumerable.Range(0, 5).Select(_ => new uint[NdsBackgroundEffectDecoder.PixelCount]).ToArray();
byte[] screen = new byte[NdsBackgroundEffectDecoder.PixelCount * 4];
uint[] drawn = new uint[NdsBackgroundEffectDecoder.PixelCount];
long compared = 0, mismatches = 0;
int unsupported = 0, active = 0;
var frames = new List<object>();
var effects = new List<NdsBackgroundEffect>();
var fullTime = new System.Diagnostics.Stopwatch();
var liveTime = new System.Diagnostics.Stopwatch();
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
    // final framebuffer, including exact melonDS RGB6-to-BGRA expansion. A step
    // ends as VBlank begins (#134): the capture is of the 192 lines just drawn,
    // and the emulator presents them when their frame ends, inside the next step.
    if (frame > 0) for (int i = 0; i < drawn.Length; i++) for (int c = 0; c < 3; c++)
    {
        int six = (int)((drawn[i] >> (c * 8)) & 63);
        int eight = (six << 2) | (six >> 4);
        if (screen[i * 4 + 2 - c] != eight) mismatches++;
        compared++;
    }
    capture[3].CopyTo(drawn, 0);
    // Timed as the runtime does it: three planes read, then decoded.
    fullTime.Start();
    var effect = Effect(false);
    fullTime.Stop();
    if (!effect.Supported) unsupported++;
    if (effect.Active) active++;
    effects.Add(effect);
    frames.Add(new { frame, nds = nds.State.Frame, effect });
}

// The live runtime leaves idle scanlines of the upper screen undrawn (#148) and
// takes the effect of an undrawn frame from the bridge's word for it. Replayed
// from the same state, every frame must give the effect the full capture gave.
nds.LoadState(args[2]);
nds.SkipIdleScreen(true);
int idle = 0, partial = 0, differences = 0;
for (int frame = 0; frame < effects.Count; frame++)
{
    nds.Step((ushort)(frame < 40 ? 2 : 0));
    int state = nds.MainFrame;
    if (state == 1) idle++;
    if (state == 2) partial++;
    liveTime.Start();
    var effect = Effect(true);
    liveTime.Stop();
    if (effect != effects[frame]) differences++;
}
double fullMs = fullTime.Elapsed.TotalMilliseconds / effects.Count, liveMs = liveTime.Elapsed.TotalMilliseconds / effects.Count;
var report = new { compared, mismatches, active, unsupported, idle, partial, differences, fullMs, liveMs, frames };
File.WriteAllText(args[4], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { compared, mismatches, active, unsupported, idle, partial, differences, fullMs, liveMs }));
if (mismatches != 0 || unsupported != 0 || differences != 0) Environment.ExitCode = 1;

// The effect of the frame just stepped, as NdsBackgroundEffects reads it.
NdsBackgroundEffect Effect(bool live)
{
    if (live && NdsBackgroundEffectDecoder.Undrawn(nds.MainFrame) is { } undrawn) return undrawn;
    unsafe
    {
        foreach (uint plane in (uint[])[0, 2, 4]) fixed (uint* pointer = capture[plane])
            if (Native.Layers(nds.Handle, pointer, (nuint)capture[plane].Length, plane) != 0) throw new Exception("Layer capture read failed.");
    }
    return NdsBackgroundEffectDecoder.Decode(nds.SourceBgPalette, capture[4], capture[0], capture[2]);
}

static unsafe class Native
{
    [DllImport("kssu_bridge", EntryPoint = "kssu_read_layers", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Layers(nint h, uint* destination, nuint count, uint plane);
    [DllImport("kssu_bridge", EntryPoint = "kssu_framebuffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Framebuffer(nint h, byte* target, nuint length, [MarshalAs(UnmanagedType.I1)] bool bottom);
}
