using System.Runtime.InteropServices;

namespace Kirby.Runtime;

/// <summary>Plays original machine stereo PCM through an independent SDL queue.</summary>
public sealed unsafe class NdsAudio : IDisposable
{
    public const int SampleRate = 48000;
    internal static Action<short[], int>? LabPcmObserver { get; set; }
    // SDAT names player 0 "BGM_Player"; its NitroSystem player record is
    // 0x0205EBC4 + 0x20. Other players, including jump/ability SFX, are retained.
    public const uint SourceMusicVolume = 0x0205EBE4;
    private readonly short[] samples = new short[65536];
    private readonly short[] adjusted = new short[65536 + 1024];
    // Vsync-locked stepping runs the original machine at the display rate,
    // within 3% of the DS's. Keep the queue near one device period by dropping
    // or repeating one stereo frame in a hundred: inaudible, and without the
    // periodic clear that the slow drift would otherwise eventually trigger.
    private const uint HighWater = 4096, LowWater = 512;
    private const int AdjustInterval = 100;
    private uint device;
    private bool initialized, disposed;
    public string? DeviceError { get; private set; }
    public bool DeviceAvailable => device != 0;
    public long SubmittedFrames { get; private set; }
    public uint QueuedFrames => device == 0 ? 0 : Sdl.Queued(device) / 4;

    public NdsAudio(NdsRuntime nds)
    {
        PrepareSource(nds);
        try
        {
            if (Sdl.Init(0x10) != 0) { DeviceError = Error(); return; }
            initialized = true;
            AudioSpec wanted = new() { Frequency = SampleRate, Format = 0x8010, Channels = 2, Samples = 1024 };
            device = Sdl.Open(null, 0, ref wanted, out AudioSpec actual, 0);
            if (device == 0) { DeviceError = Error(); return; }
            if (actual.Frequency != SampleRate || actual.Format != wanted.Format || actual.Channels != 2)
            {
                Sdl.Close(device); device = 0;
                DeviceError = "SDL did not provide the original stereo PCM format.";
                return;
            }
            Sdl.Pause(device, 0);
        }
        catch (DllNotFoundException ex) { DeviceError = ex.Message; }
        catch (EntryPointNotFoundException ex) { DeviceError = ex.Message; }
    }

    /// <summary>Apply before stepping so the original driver mutes only music.</summary>
    public static void PrepareSource(NdsRuntime nds) => nds.Write(SourceMusicVolume, [0]);

    /// <summary>Drain once after source stepping. Gain follows host effect volume.</summary>
    public void Pump(NdsRuntime nds, double gain = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int count;
        fixed (short* pointer = samples) count = NativeRead(nds.Handle, pointer, (nuint)samples.Length);
        if (count < 0 || count > samples.Length || (count & 1) != 0)
            throw new InvalidOperationException($"Invalid original audio sample count: {count}.");
        if (count == 0 || device == 0) return;
        gain = double.IsFinite(gain) ? Math.Clamp(gain, 0, 1) : 0;
        if (gain != 1)
            for (int i = 0; i < count; i++) samples[i] = (short)Math.Round(samples[i] * gain);
        LabPcmObserver?.Invoke(samples, count);
        // A host pause or stall must not leave old actions sounding much later.
        if (Sdl.Queued(device) > SampleRate * 4 / 10) Sdl.Clear(device);
        uint queued = Sdl.Queued(device) / 4;
        int frames = count / 2;
        short[] source = samples;
        if (queued > HighWater || (queued < LowWater && SubmittedFrames > 0))
        {
            bool drop = queued > HighWater;
            int written = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                bool marked = frame % AdjustInterval == AdjustInterval - 1;
                if (drop && marked) continue;
                adjusted[written++] = samples[frame * 2];
                adjusted[written++] = samples[frame * 2 + 1];
                if (!drop && marked)
                {
                    adjusted[written++] = samples[frame * 2];
                    adjusted[written++] = samples[frame * 2 + 1];
                }
            }
            source = adjusted;
            count = written;
        }
        fixed (short* pointer = source)
            if (Sdl.Queue(device, pointer, (uint)(count * sizeof(short))) != 0)
                throw new InvalidOperationException("Original audio queue failed: " + Error());
        SubmittedFrames += count / 2;
    }

    public void Pause()
    {
        if (device != 0) Sdl.Clear(device);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (device != 0) { Sdl.Clear(device); Sdl.Close(device); device = 0; }
        if (initialized) { Sdl.Quit(0x10); initialized = false; }
    }

    private static string Error() => Marshal.PtrToStringUTF8(Sdl.GetError()) ?? "Unknown SDL audio error.";
    [DllImport("kssu_bridge", EntryPoint = "kssu_read_audio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeRead(nint handle, short* destination, nuint capacity);
    [StructLayout(LayoutKind.Sequential)]
    private struct AudioSpec
    {
        public int Frequency;
        public ushort Format;
        public byte Channels, Silence;
        public ushort Samples, Padding;
        public uint Size;
        public nint Callback, UserData;
    }
    private static class Sdl
    {
        private const string Library = "libSDL2-2.0.so.0";
        [DllImport(Library, EntryPoint = "SDL_InitSubSystem", CallingConvention = CallingConvention.Cdecl)] internal static extern int Init(uint flags);
        [DllImport(Library, EntryPoint = "SDL_QuitSubSystem", CallingConvention = CallingConvention.Cdecl)] internal static extern void Quit(uint flags);
        [DllImport(Library, EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)] internal static extern nint GetError();
        [DllImport(Library, EntryPoint = "SDL_OpenAudioDevice", CallingConvention = CallingConvention.Cdecl)] internal static extern uint Open([MarshalAs(UnmanagedType.LPUTF8Str)] string? name, int capture, ref AudioSpec desired, out AudioSpec actual, int allowedChanges);
        [DllImport(Library, EntryPoint = "SDL_CloseAudioDevice", CallingConvention = CallingConvention.Cdecl)] internal static extern void Close(uint device);
        [DllImport(Library, EntryPoint = "SDL_PauseAudioDevice", CallingConvention = CallingConvention.Cdecl)] internal static extern void Pause(uint device, int pause);
        [DllImport(Library, EntryPoint = "SDL_QueueAudio", CallingConvention = CallingConvention.Cdecl)] internal static extern int Queue(uint device, void* data, uint length);
        [DllImport(Library, EntryPoint = "SDL_GetQueuedAudioSize", CallingConvention = CallingConvention.Cdecl)] internal static extern uint Queued(uint device);
        [DllImport(Library, EntryPoint = "SDL_ClearQueuedAudio", CallingConvention = CallingConvention.Cdecl)] internal static extern void Clear(uint device);
    }
}
