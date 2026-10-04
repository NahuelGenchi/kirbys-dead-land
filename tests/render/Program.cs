using Kirby.Runtime;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length < 3) throw new ArgumentException("native-library ROM private-seed-state [frames=120] [buttons=0] [output-directory] [last-palette-bank=7]");
using var nds = new NdsRuntime(args[0], args[1], args[2]);
int frames = args.Length > 3 ? int.Parse(args[3]) : 120;
ushort buttons = args.Length > 4 ? ushort.Parse(args[4]) : (ushort)0;
int lastPaletteBank = args.Length > 6 ? int.Parse(args[6]) : 7;
if (lastPaletteBank is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(lastPaletteBank));
byte[] oam = new byte[1024], vram = new byte[256 * 1024], palette = new byte[512], display = new byte[4];
byte[] rgba = new byte[512 * 512 * 4], framebuffer = new byte[256 * 192 * 4];
int compared = 0, mismatched = 0, unsupported = 0;
int[] paletteObjects = new int[16], modes = new int[4];
int affineObjects = 0, flippedObjects = 0;
var records = new List<object>();
for (int frame = 0; frame < frames; frame++)
{
    Read(0x07000000, oam); Read(0x06400000, vram); Read(0x05000200, palette); Read(0x04000000, display);
    uint control = BinaryPrimitives.ReadUInt32LittleEndian(display);
    var result = NdsObjRenderer.Render(control, oam, vram, palette, rgba, 0, 0, lastPaletteBank);
    unsupported += result.Unsupported;
    for (int i = 0; i < 128; i++)
    {
        int a0 = BinaryPrimitives.ReadUInt16LittleEndian(oam.AsSpan(i * 8));
        int a1 = BinaryPrimitives.ReadUInt16LittleEndian(oam.AsSpan(i * 8 + 2));
        int a2 = BinaryPrimitives.ReadUInt16LittleEndian(oam.AsSpan(i * 8 + 4));
        if ((a0 & 255) >= 192 || (a1 & 511) >= 256 || (a0 & 0x300) == 0x200) continue;
        paletteObjects[a2 >> 12]++; modes[(a0 >> 10) & 3]++;
        if ((a0 & 0x100) != 0) affineObjects++;
        else if ((a1 & 0x3000) != 0) flippedObjects++;
    }
    // melon-rs publishes the just-finished scanout before the following VBlank
    // commits new OAM. The next source framebuffer corresponds to this snapshot.
    NdsState state = nds.State;
    nds.Step(buttons);
    unsafe { fixed (byte* p = framebuffer) { if (Native.Framebuffer(nds.Handle, p, (nuint)framebuffer.Length, false) != 0) throw new Exception("Framebuffer read failed"); } }
    int pixels = 0, misses = 0;
    for (int y = 0; y < 192; y++) for (int x = 0; x < 256; x++)
    {
        int from = ((y + 256) * 512 + x + 256) * 4;
        if (rgba[from + 3] == 0) continue;
        int to = (y * 256 + x) * 4;
        pixels++;
        // GPU scanout uses 5-bit <<3; uploaded host RGBA expands to 8 bits.
        if ((rgba[from] >> 3) != (framebuffer[to + 2] >> 3) ||
            (rgba[from + 1] >> 3) != (framebuffer[to + 1] >> 3) ||
            (rgba[from + 2] >> 3) != (framebuffer[to] >> 3)) misses++;
    }
    compared += pixels; mismatched += misses;
    records.Add(new { frame = state.Frame, pose = state.Graphic, ability = state.Ability, objects = result.Rendered, pixels, misses });
    if (args.Length > 5 && frame == frames - 1)
    {
        Directory.CreateDirectory(args[5]);
        File.WriteAllBytes(Path.Combine(args[5], "obj-rgba.bin"), rgba);
        File.WriteAllBytes(Path.Combine(args[5], "framebuffer-bgra.bin"), framebuffer);
        File.WriteAllBytes(Path.Combine(args[5], "oam.bin"), oam);
    }
}
var report = new { compared, mismatched, unsupported, paletteObjects, modes, affineObjects, flippedObjects, frames = records };
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 5) File.WriteAllText(Path.Combine(args[5], "report.json"), json);
Console.WriteLine(JsonSerializer.Serialize(new { compared, mismatched, unsupported, paletteObjects, modes, affineObjects, flippedObjects }));
if (compared == 0 || mismatched > 0 || unsupported > 0) Environment.ExitCode = 1;

void Read(uint address, byte[] data)
{
    unsafe { fixed (byte* p = data) { if (Native.Read(nds.Handle, address, p, (nuint)data.Length) != 0) throw new Exception("GPU read failed"); } }
}
static unsafe class Native
{
    [DllImport("kssu_bridge", EntryPoint = "kssu_read_gpu", CallingConvention = CallingConvention.Cdecl)] internal static extern int Read(nint h, uint address, byte* target, nuint length);
    [DllImport("kssu_bridge", EntryPoint = "kssu_framebuffer", CallingConvention = CallingConvention.Cdecl)] internal static extern int Framebuffer(nint h, byte* target, nuint length, [MarshalAs(UnmanagedType.I1)] bool bottom);
}
