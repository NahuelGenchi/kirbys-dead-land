using Kirby.Runtime;
using System.Runtime.InteropServices;

if (args.Length != 3) throw new ArgumentException("native-library ROM private-seed-state");
using var nds = new NdsRuntime(args[0], args[1], args[2]);
NdsAudio.PrepareSource(nds);
ushort[] terrain = new ushort[900];
for (int y = 7; y < 9; y++) for (int x = 0; x < 100; x++) terrain[y * 100 + x] = 3;
nds.World(terrain, nds.State.XRaw, nds.State.YRaw);
short[] samples = new short[65536];
long sampleCount = 0;
int idlePeak = 0, jumpPeak = 0, landingPeak = 0, settledPeak = 0;
for (int frame = 0; frame < 180; frame++)
{
    // Exercise the same per-frame world/audio preparation as the host provider.
    nds.World(terrain, nds.State.XRaw, nds.State.YRaw);
    NdsAudio.PrepareSource(nds);
    nds.Step(frame == 60 ? (ushort)1 : (ushort)0);
    int count;
    unsafe { fixed (short* pointer = samples) count = Native.Read(nds.Handle, pointer, (nuint)samples.Length); }
    if (count < 0 || count > samples.Length || (count & 1) != 0) throw new Exception("Audio ABI returned invalid stereo sample count");
    sampleCount += count;
    int peak = 0;
    for (int i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs((int)samples[i]));
    if (frame >= 10 && frame < 60) idlePeak = Math.Max(idlePeak, peak);
    if (frame >= 60 && frame < 90) jumpPeak = Math.Max(jumpPeak, peak);
    if (frame >= 90 && frame < 120) landingPeak = Math.Max(landingPeak, peak);
    if (frame >= 120) settledPeak = Math.Max(settledPeak, peak);
}
if (idlePeak != 0 || settledPeak != 0 || jumpPeak == 0 || landingPeak == 0)
    throw new Exception($"Music/SFX isolation differs: idle {idlePeak}, jump {jumpPeak}, landing {landingPeak}, settled {settledPeak}");
if (sampleCount < 288000 || sampleCount > 290000) throw new Exception("Original 48kHz audio rate differs");
using (var audio = new NdsAudio(nds))
{
    if (!audio.DeviceAvailable) throw new Exception("SDL queued device unavailable: " + audio.DeviceError);
    for (int frame = 0; frame < 60; frame++)
    {
        nds.World(terrain, nds.State.XRaw, nds.State.YRaw);
        NdsAudio.PrepareSource(nds);
        nds.Step(frame == 0 ? (ushort)1 : (ushort)0);
        audio.Pump(nds, .5);
    }
    if (audio.SubmittedFrames < 48000 || audio.QueuedFrames == 0 || audio.QueuedFrames > 6000)
        throw new Exception("SDL queue is empty, unbounded or sample rate differs");
    audio.Pause();
    if (audio.QueuedFrames != 0) throw new Exception("Pause did not discard queued effects");
}
Console.WriteLine($"PASS original stereo PCM: {sampleCount} samples; music-muted idle/settled silence; jump peak {jumpPeak}, landing peak {landingPeak}; SDL queue, volume path, pause and disposal.");

static unsafe class Native
{
    [DllImport("kssu_bridge", EntryPoint = "kssu_read_audio", CallingConvention = CallingConvention.Cdecl)] internal static extern int Read(nint handle, short* destination, nuint capacity);
}
