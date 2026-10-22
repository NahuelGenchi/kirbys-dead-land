using System.Runtime.InteropServices;

namespace Kirby.Runtime;

/// <summary>Plays original machine stereo PCM through an independent SDL3 audio stream.</summary>
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
    // SDL_INIT_AUDIO, SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK and SDL_AUDIO_S16LE.
    private const uint InitAudio = 0x10, DefaultPlayback = 0xFFFFFFFF, SignedLittle16 = 0x8010;
    private nint stream;
    private bool initialized, disposed;
    public string? DeviceError { get; private set; }
    public bool DeviceAvailable => stream != 0;
    public long SubmittedFrames { get; private set; }
    public uint QueuedFrames => Queued() / 4;

    public NdsAudio(NdsRuntime nds)
    {
        PrepareSource(nds);
        try
        {
            if (!Sdl.Init(InitAudio)) { DeviceError = Error(); return; }
            initialized = true;
            // The stream takes the original format and converts it to the device's.
            AudioSpec original = new() { Format = SignedLittle16, Channels = 2, Frequency = SampleRate };
            stream = Sdl.Open(DefaultPlayback, ref original, 0, 0);
            if (stream == 0) { DeviceError = Error(); return; }
            // The device opens paused.
            if (!Sdl.Resume(stream)) { DeviceError = Error(); Sdl.Destroy(stream); stream = 0; }
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
        if (count == 0 || stream == 0) return;
        gain = double.IsFinite(gain) ? Math.Clamp(gain, 0, 1) : 0;
        if (gain != 1)
            for (int i = 0; i < count; i++) samples[i] = (short)Math.Round(samples[i] * gain);
        LabPcmObserver?.Invoke(samples, count);
        // A host pause or stall must not leave old actions sounding much later.
        if (Queued() > SampleRate * 4 / 10) Sdl.Clear(stream);
        uint queued = Queued() / 4;
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
            if (!Sdl.Put(stream, pointer, count * sizeof(short)))
                throw new InvalidOperationException("Original audio queue failed: " + Error());
        SubmittedFrames += count / 2;
    }

    public void Pause()
    {
        if (stream != 0) Sdl.Clear(stream);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Destroying the stream also closes its device.
        if (stream != 0) { Sdl.Destroy(stream); stream = 0; }
        if (initialized) { Sdl.Quit(InitAudio); initialized = false; }
    }

    /// <summary>Bytes of original PCM the device has not taken yet.</summary>
    private uint Queued() => stream == 0 ? 0 : (uint)Math.Max(0, Sdl.Queued(stream));
    private static string Error() => Marshal.PtrToStringUTF8(Sdl.GetError()) ?? "Unknown SDL audio error.";
    [DllImport("kssu_bridge", EntryPoint = "kssu_read_audio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeRead(nint handle, short* destination, nuint capacity);
    [StructLayout(LayoutKind.Sequential)]
    private struct AudioSpec
    {
        public uint Format;
        public int Channels, Frequency;
    }
    private static class Sdl
    {
        // The game's own SDL3, which DCCM loads into its process (NativeLibraries maps the name).
        private const string Library = NativeLibraries.Sdl;
        [DllImport(Library, EntryPoint = "SDL_InitSubSystem", CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool Init(uint flags);
        [DllImport(Library, EntryPoint = "SDL_QuitSubSystem", CallingConvention = CallingConvention.Cdecl)] internal static extern void Quit(uint flags);
        [DllImport(Library, EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)] internal static extern nint GetError();
        [DllImport(Library, EntryPoint = "SDL_OpenAudioDeviceStream", CallingConvention = CallingConvention.Cdecl)] internal static extern nint Open(uint device, ref AudioSpec spec, nint callback, nint userData);
        [DllImport(Library, EntryPoint = "SDL_ResumeAudioStreamDevice", CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool Resume(nint stream);
        [DllImport(Library, EntryPoint = "SDL_DestroyAudioStream", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(nint stream);
        [DllImport(Library, EntryPoint = "SDL_PutAudioStreamData", CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool Put(nint stream, void* data, int length);
        [DllImport(Library, EntryPoint = "SDL_GetAudioStreamQueued", CallingConvention = CallingConvention.Cdecl)] internal static extern int Queued(nint stream);
        [DllImport(Library, EntryPoint = "SDL_ClearAudioStream", CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool Clear(nint stream);
    }
}
