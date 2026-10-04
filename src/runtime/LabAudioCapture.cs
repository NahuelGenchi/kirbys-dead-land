using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kirby.Runtime;

/// <summary>Records only the original PCM submitted by this mod during an opt-in take.</summary>
internal static class LabAudioCapture
{
    private static readonly object gate = new();
    private static FileStream? output;

    internal static void Install(string run, Func<long> elapsedMilliseconds)
    {
        output = new FileStream(Path.Combine(run, "source-audio.s16le"), FileMode.Create,
            FileAccess.Write, FileShare.Read);
        NdsAudio.LabPcmObserver = (samples, count) =>
        {
            lock (gate)
            {
                if (output is null) return;
                long offset = output.Position;
                output.Write(MemoryMarshal.AsBytes(samples.AsSpan(0, count)));
                output.Flush();
                File.AppendAllText(Path.Combine(run, "audio-chunks.jsonl"), JsonSerializer.Serialize(new
                {
                    offset, bytes = count * sizeof(short), elapsedMs = elapsedMilliseconds(),
                    rate = NdsAudio.SampleRate, channels = 2,
                    sourceFrame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame,
                }) + "\n");
            }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (gate)
            {
                NdsAudio.LabPcmObserver = null;
                output?.Dispose(); output = null;
            }
        };
    }
}
