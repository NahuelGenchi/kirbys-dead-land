using System.Diagnostics;
using System.Text.Json;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in cost breakdown of the mod's per-frame work (<c>KIRBY_LAB_PERF=1</c> in
/// an offscreen lab run). It times the stages of a host frame while Kirby is
/// stepped normally, counts the managed bytes each one allocates, and records
/// the host frame intervals and collections, in <c>perf.json</c> in the run
/// folder. Normal play pays one static test per mark and writes nothing.
/// </summary>
internal static class LabPerfProbe
{
    internal enum Section
    {
        Step, Terrain, Prepare, Native, Complete, Render, Background, Read, Decode, Upload, Equipment, Items, Hud, Audio, Count,
    }

    internal readonly record struct Mark(long Ticks, long Bytes);

    internal static bool Enabled { get; private set; }
    private static string? path;
    private static readonly long[] ticks = new long[(int)Section.Count], bytes = new long[(int)Section.Count];
    private static readonly int[] calls = new int[(int)Section.Count];
    private static readonly List<float> intervals = [];
    private static long lastFrame, firstBytes, lastBytes, written;
    private static bool active, wasActive;
    private static int steps, textures;
    private static long enemyHooks;
    private static readonly int[] collections = new int[3];
    private static TimeSpan pause;

    internal static void Install()
    {
        string? run = Environment.GetEnvironmentVariable("KIRBY_LAB_RUN_ROOT");
        if (run is null || !Directory.Exists(run) || Environment.GetEnvironmentVariable("KIRBY_LAB_PLAY") == "1" ||
            Environment.GetEnvironmentVariable("KIRBY_LAB_PERF") != "1") return;
        path = Path.Combine(run, "perf.json");
        Enabled = true;
    }

    internal static Mark Start() => Enabled ? new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

    internal static void Stop(Section section, Mark mark)
    {
        if (!Enabled) return;
        ticks[(int)section] += Stopwatch.GetTimestamp() - mark.Ticks;
        bytes[(int)section] += GC.GetAllocatedBytesForCurrentThread() - mark.Bytes;
        calls[(int)section]++;
    }

    /// <summary>This host frame stepped Kirby normally (not a load, pause, cinematic or death).</summary>
    internal static void Stepped(int count)
    {
        if (!Enabled) return;
        active = true;
        steps += count;
    }

    /// <summary>The body texture was redrawn and uploaded.</summary>
    internal static void Uploaded() { if (Enabled) textures++; }

    /// <summary>A held-entity hook ran (see <see cref="KirbyEnemyHooks"/>).</summary>
    internal static void EnemyHook() { if (Enabled) enemyHooks++; }

    /// <summary>Once per host frame, after the mod's work for it.</summary>
    internal static void Frame()
    {
        if (!Enabled) return;
        long now = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        if (active && wasActive)
        {
            if (intervals.Count == 0)
            {
                firstBytes = lastBytes;
                for (int i = 0; i < 3; i++) collections[i] = GC.CollectionCount(i);
                pause = GC.GetTotalPauseDuration();
                enemyHooks = 0;
            }
            if (intervals.Count < 1_000_000) intervals.Add((float)((now - lastFrame) * 1000.0 / Stopwatch.Frequency));
        }
        wasActive = active; active = false; lastFrame = now; lastBytes = allocated;
        // The run is ended by a signal, so the report is rewritten every two seconds.
        if (intervals.Count > 0 && now - written > 2 * Stopwatch.Frequency) { written = now; Write(allocated); }
    }

    private static void Write(long allocated)
    {
        float[] sorted = [.. intervals];
        Array.Sort(sorted);
        double At(double share) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * share))];
        double ms = 1000.0 / Stopwatch.Frequency, total = 0;
        foreach (float value in sorted) total += value;
        var sections = new Dictionary<string, object>();
        for (int i = 0; i < (int)Section.Count; i++)
            sections[((Section)i).ToString()] = new
            {
                calls = calls[i],
                totalMs = Math.Round(ticks[i] * ms, 3),
                meanMs = calls[i] == 0 ? 0 : Math.Round(ticks[i] * ms / calls[i], 4),
                bytesPerCall = calls[i] == 0 ? 0 : bytes[i] / calls[i],
            };
        var report = new
        {
            frames = sorted.Length,
            seconds = Math.Round(total / 1000, 2),
            hostFps = Math.Round(sorted.Length * 1000 / total, 1),
            frameMs = new { mean = Math.Round(total / sorted.Length, 3), p50 = At(.5), p95 = At(.95), p99 = At(.99), p999 = At(.999), max = sorted[^1] },
            framesOver20Ms = sorted.Count(v => v > 20),
            framesOver33Ms = sorted.Count(v => v > 33.4),
            originalSteps = steps,
            textureUploads = textures,
            enemyHookCallsPerFrame = Math.Round(enemyHooks / (double)sorted.Length, 1),
            managedBytesPerFrame = (allocated - firstBytes) / sorted.Length,
            collections = new[] { GC.CollectionCount(0) - collections[0], GC.CollectionCount(1) - collections[1], GC.CollectionCount(2) - collections[2] },
            collectionPauseMs = Math.Round((GC.GetTotalPauseDuration() - pause).TotalMilliseconds, 2),
            sections,
        };
        File.WriteAllText(path!, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
