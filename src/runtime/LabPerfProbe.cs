using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using dc.en;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in cost breakdown of a host frame (<c>KIRBY_LAB_PERF=1</c> in an offscreen
/// lab run), written to <c>perf.json</c> and <c>perf-frames.json</c> in the run
/// folder. It counts the host frames in which the hero plays: frames that
/// stepped Kirby normally, or, in a run without Kirby, frames in which the
/// Beheaded is awake in a running level. For each it records the interval,
/// the time and managed bytes of the mod's stages, texture uploads, the .NET
/// and HashLink collections, and the lab's own captures, and lists every
/// frame over 20 ms with what happened in it. <c>KIRBY_LAB_PERF_HOST=1</c> also
/// times the host functions a frame is made of (each called about once a
/// frame, so the hooks add about 20 µs a frame). <c>KIRBY_LAB_PERF=quiet</c>
/// keeps the plans but drops the lab's trace, captures and machine dumps, so
/// the figures are those of a played frame. <c>KIRBY_LAB_PERF_ALLOC=1</c> adds
/// the runtime's allocation samples by type, and <c>KIRBY_LAB_PERF_HOOKS=1</c> the
/// calls of each hooked host function per frame. From Kirby's first activation
/// until <see cref="ArrivalPlayed"/> counted frames later, it also writes every
/// host frame with the time the .NET JIT took on the game thread in it, by
/// stage and by host phase (<c>perf-arrival.json</c>, #184). Normal play pays
/// one static test per mark and writes nothing.
/// </summary>
internal static unsafe class LabPerfProbe
{
    internal enum Section
    {
        Step, Terrain, Prepare, Native, Complete, Render, Background, Read, Decode, Upload, Equipment, Items, Hud, Audio, Lab, Input, Sync, Activate, Count,
    }

    /// <summary>Host functions timed with <c>KIRBY_LAB_PERF_HOST=1</c>; nested ones are inclusive.</summary>
    internal enum Phase
    {
        // hxd.System.mainLoop: events, Boot.mainLoop (update and render), present.
        Loop, Events, Update, Level, LevelPost, Render, Sync, Draw, Present, TextureUpload, Count,
    }

    /// <summary>A stage's start: the clock, the thread's managed bytes and, during the arrival, its JIT time.</summary>
    internal readonly record struct Mark(long Ticks, long Bytes, long Jit = 0);

    internal static bool Enabled { get; private set; }
    /// <summary>The lab skips its own trace, captures and dumps (<c>KIRBY_LAB_PERF=quiet</c>).</summary>
    internal static bool Quiet { get; private set; }
    /// <summary>
    /// A lab baseline with none of Kirby's hooks: DCCM, the host and the lab's
    /// own probes only (<c>KIRBY_LAB_BARE=1</c>, never in play).
    /// </summary>
    internal static bool Bare { get; } = Environment.GetEnvironmentVariable("KIRBY_LAB_BARE") == "1" &&
        Environment.GetEnvironmentVariable("KIRBY_LAB_RUN_ROOT") is not null &&
        Environment.GetEnvironmentVariable("KIRBY_LAB_PLAY") != "1";
    private static string? path, framesPath;
    private static readonly long[] ticks = new long[(int)Section.Count], bytes = new long[(int)Section.Count];
    private static readonly int[] calls = new int[(int)Section.Count];
    // This frame's section ticks, for the frame's own record.
    private static readonly long[] frameTicks = new long[(int)Section.Count];
    private static long lastFrame, firstBytes, lastBytes, firstAllBytes, written;
    // Managed bytes of the probe's own reports and of the lab's captures, left out of the per-frame figures.
    private static long probeBytes, firstProbeBytes, firstLabBytes;
    private static bool active, wasActive, labSpike, wasLabSpike;
    private static int steps, frameSteps, textures, frameUploads;
    private static long enemyHooks;
    private static readonly int[] collections = new int[3];
    private static TimeSpan pause;

    // One row per counted frame: interval, the mod's work, its original frame
    // and upload, each host phase, the collections that ended in it and the
    // lab's own spikes.
    private enum Col { Interval, Mod, Native, Upload, Steps, NetGen, NetPause, HlMarks, HlKb, Lab, LabMs, Phases, Count = Phases + (int)Phase.Count }
    private const int Capacity = 1 << 17;
    private static readonly float[] rows = new float[Capacity * (int)Col.Count];
    private static int count;

    // .NET collections, one entry each.
    private readonly record struct NetGc(int Frame, long Index, int Generation, double PauseMs, long PromotedBytes, long HeapBytes, bool Compacted, bool Concurrent);
    private static readonly List<NetGc> netGcs = [];
    private static int lastNetCount;

    // HashLink GC: hl_gc_stats from libhl, and the mark count from its gc_stats
    // record when the loaded libhl is the DCCM 35.13.6 build it was read from.
    [DllImport("libhl.so")] private static extern void hl_gc_stats(double* totalAllocated, double* allocationCount, double* currentMemory);
    [DllImport("libhl.so")] private static extern void hl_gc_major();
    private const string KnownLibhl = "26aaa1b33cbb623d6ad9ce6029a75a2c20be5aba800e36ab3944d905eef23cce";
    private const long GcStatsFromExport = 0xa0cc0 - 0x1d4d0, MarkCountOffset = 0x4c;
    private static int* markCount;
    private static bool hlStats;
    private static double hlFirstBytes, hlFirstAllocs, hlLastBytes, hlMemory;
    private static int hlFirstMarks, hlLastMarks;
    private static readonly List<double> forcedMajors = [];
    private static bool gcProbe;

    // Host phases.
    private static readonly long[] phaseTicks = new long[(int)Phase.Count], phaseBytes = new long[(int)Phase.Count];
    private static readonly int[] phaseCalls = new int[(int)Phase.Count], phaseDepth = new int[(int)Phase.Count];
    private static readonly long[] framePhase = new long[(int)Phase.Count];
    private static bool hostPhases;

    // The arrival (#184): one row per host frame from the first activation, with
    // the JIT time of the game thread (in it, in each stage and in each phase)
    // and the number of methods compiled so far by all threads, which numbers the
    // lines of a DOTNET_JitStdOutFile summary.
    private enum Arr
    {
        Interval, State, Row, Steps, Lab, Jit, JitMethods, AllJit, AllMethods, Kb, NetGcs, Sections,
        SectionJit = Sections + (int)Section.Count, Phases = SectionJit + (int)Section.Count,
        PhaseJit = Phases + (int)Phase.Count, Count = PhaseJit + (int)Phase.Count,
    }
    /// <summary>Counted frames the arrival record keeps after the first one (about five seconds).</summary>
    internal const int ArrivalPlayed = 600;
    private const int ArrivalCapacity = 6000;
    private static float[]? arrival;
    private static string? arrivalPath;
    private static int arrivalFrames = -1;
    // Marks read the JIT clock until the arrival record is complete.
    private static bool tracking;
    private static long jitAtFrame, allJitAtFrame, methodsAtFrame, allMethodsAtFrame;
    private static readonly long[] frameJit = new long[(int)Section.Count], framePhaseJit = new long[(int)Phase.Count];
    private static string? renderer;
    private static double firstLoad = -1, maxLoad;
    private static AllocationListener? allocationSamples;
    private static HookCounter? hookCalls;
    [DllImport("libc")] private static extern int gettid();

    [DllImport("libGL.so.1")] private static extern nint glGetString(uint name);

    internal static void Install()
    {
        string? run = Environment.GetEnvironmentVariable("KIRBY_LAB_RUN_ROOT");
        string? mode = Environment.GetEnvironmentVariable("KIRBY_LAB_PERF");
        if (run is null || !Directory.Exists(run) || Environment.GetEnvironmentVariable("KIRBY_LAB_PLAY") == "1" ||
            mode is not ("1" or "quiet")) return;
        // It reads /proc, HashLink's libhl.so and the thread's Linux ID (#244).
        if (!OperatingSystem.IsLinux())
        {
            File.WriteAllText(Path.Combine(run, "perf.json"), "{\"unsupported\": \"The performance probe runs on Linux only.\"}\n");
            return;
        }
        Quiet = mode == "quiet";
        path = Path.Combine(run, "perf.json");
        framesPath = Path.Combine(run, "perf-frames.json");
        Enabled = true;
        lastNetCount = GC.CollectionCount(0);
        if (!Bare)
        {
            arrivalPath = Path.Combine(run, "perf-arrival.json");
            arrival = new float[ArrivalCapacity * (int)Arr.Count];
            tracking = true;
        }
        InstallHashlinkStats();
        gcProbe = Environment.GetEnvironmentVariable("KIRBY_LAB_PERF_GCPROBE") == "1";
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_PERF_HOST") == "1") InstallPhases();
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_PERF_ALLOC") == "1") allocationSamples = new AllocationListener();
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_PERF_HOOKS") == "1") hookCalls = HookCounter.Install();
    }

    private static void InstallHashlinkStats()
    {
        try
        {
            double a, b, c;
            hl_gc_stats(&a, &b, &c);
            hlStats = true;
            LabActivationTrace.HashlinkGc = HashlinkGc;
            string? file = File.ReadLines("/proc/self/maps").Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(f => f.Length >= 6 && f[5].EndsWith("/libhl.so", StringComparison.Ordinal)).Select(f => f[5]).FirstOrDefault();
            if (file is null || !NativeLibrary.TryLoad(file, out nint handle) ||
                !NativeLibrary.TryGetExport(handle, "hl_gc_stats", out nint export)) return;
            using var stream = File.OpenRead(file);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != KnownLibhl) return;
            markCount = (int*)(export + GcStatsFromExport + MarkCountOffset);
        }
        catch (Exception) when (!hlStats) { }
    }

    /// <summary>HashLink's collections so far and its memory, for the lab's activation trace (#193).</summary>
    private static string HashlinkGc()
    {
        double allocated = 0, allocations = 0, memory = 0;
        hl_gc_stats(&allocated, &allocations, &memory);
        return $" (HashLink: {(markCount is null ? -1 : *markCount)} collections, {allocated / 1048576:0} MB allocated, {memory / 1048576:0} MB)";
    }

    private static void ReadHashlink(out double allocated, out double allocations, out int marks)
    {
        double a = 0, b = 0, c = 0;
        if (hlStats) hl_gc_stats(&a, &b, &c);
        allocated = a; allocations = b; hlMemory = c;
        marks = markCount is null ? 0 : *markCount;
    }

    internal static Mark Start() => Enabled
        ? new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(), tracking ? JitTicks() : 0) : default;

    internal static void Stop(Section section, Mark mark)
    {
        if (!Enabled) return;
        long elapsed = Stopwatch.GetTimestamp() - mark.Ticks;
        ticks[(int)section] += elapsed;
        frameTicks[(int)section] += elapsed;
        bytes[(int)section] += GC.GetAllocatedBytesForCurrentThread() - mark.Bytes;
        calls[(int)section]++;
        if (tracking) frameJit[(int)section] += JitTicks() - mark.Jit;
    }

    // The time the game thread has spent in the JIT, in TimeSpan ticks.
    private static long JitTicks() => JitInfo.GetCompilationTime(currentThread: true).Ticks;

    /// <summary>Kirby is about to activate: the arrival record starts with this frame (the first activation only).</summary>
    internal static void Activated()
    {
        if (tracking && arrivalFrames < 0) arrivalFrames = 0;
    }

    /// <summary>This host frame stepped Kirby normally (not a load, pause, cinematic or death).</summary>
    internal static void Stepped(int count)
    {
        if (!Enabled) return;
        active = true;
        steps += count;
        frameSteps += count;
    }

    /// <summary>
    /// A frame without a Kirby session counts when the host hero plays: the
    /// Beheaded baseline of the same plans.
    /// </summary>
    internal static void HostFrame(Hero? hero)
    {
        if (!Enabled || hero is null || hero.destroyed || hero.life <= 0 || !hero.awake || hero._level is not { } level) return;
        if (level.paused || level.game is not { } game || game.paused || game.hasCinematic()) return;
        active = true;
    }

    /// <summary>The body texture was redrawn and uploaded.</summary>
    internal static void Uploaded() { if (Enabled) { textures++; frameUploads++; } }

    /// <summary>A held-entity hook ran (see <see cref="KirbyEnemyHooks"/>).</summary>
    internal static void EnemyHook() { if (Enabled) enemyHooks++; }

    /// <summary>The lab captured the framebuffer or saved the machine in this frame; the frame is reported apart.</summary>
    internal static void LabSpike() { if (Enabled) labSpike = true; }

    /// <summary>Once per host frame, after the mod's work for it.</summary>
    internal static void Frame()
    {
        if (!Enabled) return;
        long now = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        ReadHashlink(out double hlBytes, out double hlAllocs, out int marks);
        int netCount = GC.CollectionCount(0);
        bool counted = active && wasActive && count < Capacity;
        if (counted)
        {
            if (count == 0)
            {
                firstBytes = lastBytes;
                firstAllBytes = GC.GetTotalAllocatedBytes(false);
                firstProbeBytes = probeBytes; firstLabBytes = bytes[(int)Section.Lab];
                for (int i = 0; i < 3; i++) collections[i] = GC.CollectionCount(i);
                pause = GC.GetTotalPauseDuration();
                enemyHooks = 0;
                hlFirstBytes = hlLastBytes; hlFirstAllocs = hlAllocs; hlFirstMarks = hlLastMarks;
                netGcs.Clear();
                Array.Clear(phaseTicks); Array.Clear(phaseBytes); Array.Clear(phaseCalls);
                renderer = Marshal.PtrToStringUTF8(glGetString(0x1F01)) + " / " + Marshal.PtrToStringUTF8(glGetString(0x1F02));
                allocationSamples?.Start(gettid());
                hookCalls?.Start();
            }
            double ms = 1000.0 / Stopwatch.Frequency;
            var row = rows.AsSpan(count * (int)Col.Count, (int)Col.Count);
            row[(int)Col.Interval] = (float)((now - lastFrame) * ms);
            long mod = frameTicks[(int)Section.Step] + frameTicks[(int)Section.Render] + frameTicks[(int)Section.Items] +
                frameTicks[(int)Section.Hud] + frameTicks[(int)Section.Audio];
            row[(int)Col.Mod] = (float)(mod * ms);
            row[(int)Col.Native] = (float)(frameTicks[(int)Section.Native] * ms);
            row[(int)Col.Upload] = (float)(frameTicks[(int)Section.Upload] * ms);
            row[(int)Col.Steps] = frameSteps;
            row[(int)Col.NetGen] = -1;
            if (netCount != lastNetCount)
            {
                var info = GC.GetGCMemoryInfo(GCKind.Any);
                double paused = 0;
                foreach (var span in info.PauseDurations) paused += span.TotalMilliseconds;
                netGcs.Add(new(count, info.Index, info.Generation, paused, info.PromotedBytes, info.HeapSizeBytes, info.Compacted, info.Concurrent));
                row[(int)Col.NetGen] = info.Generation;
                row[(int)Col.NetPause] = (float)paused;
            }
            row[(int)Col.HlMarks] = marks - hlLastMarks;
            row[(int)Col.HlKb] = (float)((hlBytes - hlLastBytes) / 1024);
            row[(int)Col.Lab] = wasLabSpike || labSpike ? 1 : 0;
            row[(int)Col.LabMs] = (float)(frameTicks[(int)Section.Lab] * ms);
            for (int i = 0; i < (int)Phase.Count; i++) row[(int)Col.Phases + i] = (float)(framePhase[i] * ms);
            if (gcProbe && count == 1500) ForceMajors(row);
            count++;
        }
        bool arrivalDone = tracking && arrivalFrames >= 0 && RecordArrival(now, allocated, netCount, counted);
        wasActive = active; active = false; lastFrame = now; lastBytes = allocated;
        // A capture's frame ends with it; its time lands in the interval of the next frame too.
        wasLabSpike = labSpike; labSpike = false;
        lastNetCount = netCount; hlLastBytes = hlBytes; hlLastMarks = marks;
        frameSteps = 0; frameUploads = 0;
        Array.Clear(frameTicks); Array.Clear(framePhase);
        Array.Clear(frameJit); Array.Clear(framePhaseJit);
        if (arrivalDone)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            WriteArrival();
            probeBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            labSpike = true;
        }
        // The run is ended by a signal, so the report is rewritten every two seconds.
        if (count > 0 && now - written > 2 * Stopwatch.Frequency)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Write(allocated, hlBytes, hlAllocs, marks);
            probeBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            written = Stopwatch.GetTimestamp();
            // Writing the report is the probe's own work: keep it out of the next interval.
            labSpike = true;
        }
        // The probe's own reports compile code too; that JIT time stays out of the next frame's record.
        if (tracking)
        {
            jitAtFrame = JitTicks(); allJitAtFrame = JitInfo.GetCompilationTime(false).Ticks;
            methodsAtFrame = JitInfo.GetCompiledMethodCount(true); allMethodsAtFrame = JitInfo.GetCompiledMethodCount(false);
        }
    }

    /// <summary>
    /// Adds this frame to the arrival record; true when the record is complete
    /// and tracking stops.
    /// </summary>
    private static bool RecordArrival(long now, long allocated, int netCount, bool counted)
    {
        long allJit = JitInfo.GetCompilationTime(false).Ticks;
        double ms = 1000.0 / Stopwatch.Frequency;
        const float TicksPerMs = 10_000f;
        var a = arrival!.AsSpan(arrivalFrames++ * (int)Arr.Count, (int)Arr.Count);
        a[(int)Arr.Interval] = (float)((now - lastFrame) * ms);
        a[(int)Arr.State] = counted ? 2 : active ? 1 : 0;
        a[(int)Arr.Row] = counted ? count - 1 : -1;
        a[(int)Arr.Steps] = frameSteps;
        a[(int)Arr.Lab] = wasLabSpike || labSpike ? 1 : 0;
        a[(int)Arr.Jit] = (JitTicks() - jitAtFrame) / TicksPerMs;
        a[(int)Arr.JitMethods] = JitInfo.GetCompiledMethodCount(true) - methodsAtFrame;
        a[(int)Arr.AllJit] = (allJit - allJitAtFrame) / TicksPerMs;
        a[(int)Arr.AllMethods] = JitInfo.GetCompiledMethodCount(false);
        a[(int)Arr.Kb] = (allocated - lastBytes) / 1024f;
        a[(int)Arr.NetGcs] = netCount - lastNetCount;
        for (int i = 0; i < (int)Section.Count; i++)
        {
            a[(int)Arr.Sections + i] = (float)(frameTicks[i] * ms);
            a[(int)Arr.SectionJit + i] = frameJit[i] / TicksPerMs;
        }
        // The host phases that ended since the last mark. The mark is taken inside the host's
        // update, so the update in a row is the previous frame's.
        for (int i = 0; i < (int)Phase.Count; i++)
        {
            a[(int)Arr.Phases + i] = (float)(framePhase[i] * ms);
            a[(int)Arr.PhaseJit + i] = framePhaseJit[i] / TicksPerMs;
        }
        if (!(counted && count > ArrivalPlayed) && arrivalFrames < ArrivalCapacity) return false;
        tracking = false;
        return true;
    }

    private static void WriteArrival()
    {
        var columns = new List<string> { "ms", "state", "row", "steps", "lab", "jitMs", "jitMethods", "allJitMs", "allMethods", "kb", "netGcs" };
        string[] sections = Enum.GetNames<Section>()[..(int)Section.Count], phases = Enum.GetNames<Phase>()[..(int)Phase.Count];
        columns.AddRange(sections.Select(s => s + "Ms"));
        columns.AddRange(sections.Select(s => s + "JitMs"));
        columns.AddRange(phases.Select(p => "phase" + p + "Ms"));
        columns.AddRange(phases.Select(p => "phase" + p + "JitMs"));
        using var writer = new StreamWriter(arrivalPath!);
        // state: 0 no normal step (loading, cinematic), 1 stepped but not yet counted, 2 counted as perf.json's row.
        writer.Write($"{{\"hostPhases\":{(hostPhases ? "true" : "false")},\"columns\":[{string.Join(',', columns.Select(c => $"\"{c}\""))}],\"rows\":[");
        for (int i = 0; i < arrivalFrames; i++)
        {
            if (i > 0) writer.Write(',');
            writer.Write('[');
            for (int c = 0; c < (int)Arr.Count; c++)
            {
                if (c > 0) writer.Write(',');
                writer.Write(arrival![i * (int)Arr.Count + c].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            writer.Write(']');
        }
        writer.Write("]}");
    }

    /// <summary>
    /// With <c>KIRBY_LAB_PERF_GCPROBE=1</c>, time three full HashLink
    /// collections once, at the 1,500th counted frame (marked as a lab frame).
    /// </summary>
    private static void ForceMajors(Span<float> row)
    {
        for (int i = 0; i < 3; i++)
        {
            long start = Stopwatch.GetTimestamp();
            hl_gc_major();
            forcedMajors.Add(Math.Round((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency, 3));
        }
        row[(int)Col.Lab] = 1;
        labSpike = true;
    }

    private static void InstallPhases()
    {
        hostPhases = true;
        dc.hxd.Hook__System.mainLoop += orig => { Enter(Phase.Loop, out var m); try { return orig(); } finally { Leave(Phase.Loop, m); } };
        dc.sdl.Hook__Sdl.processEvents += (orig, onEvent) => { Enter(Phase.Events, out var m); try { return orig(onEvent); } finally { Leave(Phase.Events, m); } };
        dc.Hook_Boot.update += (orig, boot, dt) => { Enter(Phase.Update, out var m); try { orig(boot, dt); } finally { Leave(Phase.Update, m); } };
        dc.pr.Hook_Level.update += (orig, level) => { Enter(Phase.Level, out var m); try { orig(level); } finally { Leave(Phase.Level, m); } };
        dc.pr.Hook_Level.postUpdate += (orig, level) => { Enter(Phase.LevelPost, out var m); try { orig(level); } finally { Leave(Phase.LevelPost, m); } };
        dc.h3d.Hook_Engine.render += (orig, engine, app) => { Enter(Phase.Render, out var m); try { return orig(engine, app); } finally { Leave(Phase.Render, m); } };
        dc.h2d.Hook_Scene.sync += (orig, scene, context) => { Enter(Phase.Sync, out var m); try { orig(scene, context); } finally { Leave(Phase.Sync, m); } };
        dc.h2d.Hook_RenderContext.drawScene += (orig, context) => { Enter(Phase.Draw, out var m); try { orig(context); } finally { Leave(Phase.Draw, m); } };
        dc.h3d.impl.Hook_GlDriver.present += (orig, driver) => { Enter(Phase.Present, out var m); try { orig(driver); } finally { Leave(Phase.Present, m); } };
        dc.h3d.mat.Hook_Texture.uploadPixels += (orig, texture, pixels, mip, layer) =>
        { Enter(Phase.TextureUpload, out var m); try { orig(texture, pixels, mip, layer); } finally { Leave(Phase.TextureUpload, m); } };
        dc.h3d.mat.Hook_Texture.uploadBitmap += (orig, texture, bitmap, mip, layer) =>
        { Enter(Phase.TextureUpload, out var m); try { orig(texture, bitmap, mip, layer); } finally { Leave(Phase.TextureUpload, m); } };
    }

    private static void Enter(Phase phase, out Mark mark)
    {
        mark = phaseDepth[(int)phase]++ == 0
            ? new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(), tracking ? JitTicks() : 0) : default;
    }

    private static void Leave(Phase phase, Mark mark)
    {
        if (--phaseDepth[(int)phase] != 0) return;
        long elapsed = Stopwatch.GetTimestamp() - mark.Ticks;
        phaseTicks[(int)phase] += elapsed;
        framePhase[(int)phase] += elapsed;
        phaseBytes[(int)phase] += GC.GetAllocatedBytesForCurrentThread() - mark.Bytes;
        phaseCalls[(int)phase]++;
        if (tracking) framePhaseJit[(int)phase] += JitTicks() - mark.Jit;
    }

    private static void Write(long allocated, double hlBytes, double hlAllocs, int marks)
    {
        int n = count, cols = (int)Col.Count;
        // The machine's own load: a busy desktop slows every frame.
        if (double.TryParse(File.ReadAllText("/proc/loadavg").Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture, out double load))
        {
            if (firstLoad < 0) firstLoad = load;
            maxLoad = Math.Max(maxLoad, load);
        }
        double total = 0;
        // Without the frames of lab captures, and with the lab plans' own per-frame work taken out.
        var clean = new List<float>(n); var withoutLabWork = new List<float>(n);
        for (int i = 0; i < n; i++)
        {
            float v = rows[i * cols + (int)Col.Interval];
            total += v;
            if (rows[i * cols + (int)Col.Lab] != 0) continue;
            clean.Add(v);
            withoutLabWork.Add(v - rows[i * cols + (int)Col.LabMs]);
        }
        object Stats(List<float> values)
        {
            if (values.Count == 0) return new { frames = 0 };
            float[] sorted = [.. values];
            Array.Sort(sorted);
            double sum = 0;
            foreach (float v in sorted) sum += v;
            double At(double share) => Math.Round(sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * share))], 3);
            return new
            {
                frames = sorted.Length,
                mean = Math.Round(sum / sorted.Length, 3), p50 = At(.5), p95 = At(.95), p99 = At(.99), p999 = At(.999),
                max = Math.Round(sorted[^1], 3),
                over20Ms = sorted.Count(v => v > 20), over33Ms = sorted.Count(v => v > 33.4),
            };
        }
        var all = new List<float>(n);
        for (int i = 0; i < n; i++) all.Add(rows[i * cols + (int)Col.Interval]);
        double ms = 1000.0 / Stopwatch.Frequency;
        var sections = new Dictionary<string, object>();
        for (int i = 0; i < (int)Section.Count; i++)
            sections[((Section)i).ToString()] = new
            {
                calls = calls[i],
                totalMs = Math.Round(ticks[i] * ms, 3),
                meanMs = calls[i] == 0 ? 0 : Math.Round(ticks[i] * ms / calls[i], 4),
                bytesPerCall = calls[i] == 0 ? 0 : bytes[i] / calls[i],
            };
        Dictionary<string, object>? phases = null;
        if (hostPhases)
        {
            phases = [];
            for (int p = 0; p < (int)Phase.Count; p++)
            {
                var values = new List<float>(n);
                for (int i = 0; i < n; i++) if (rows[i * cols + (int)Col.Lab] == 0) values.Add(rows[i * cols + (int)Col.Phases + p]);
                values.Sort();
                double sum = 0;
                foreach (float v in values) sum += v;
                phases[((Phase)p).ToString()] = new
                {
                    calls = phaseCalls[p],
                    perFrameMs = values.Count == 0 ? 0 : Math.Round(sum / values.Count, 4),
                    p50Ms = values.Count == 0 ? 0 : Math.Round(values[values.Count / 2], 4),
                    p99Ms = values.Count == 0 ? 0 : Math.Round(values[Math.Min(values.Count - 1, (int)(values.Count * .99))], 4),
                    bytesPerFrame = n == 0 ? 0 : phaseBytes[p] / n,
                };
            }
        }
        var slow = new List<object>();
        for (int i = 0; i < n && slow.Count < 400; i++)
        {
            var r = rows.AsSpan(i * cols, cols);
            if (r[(int)Col.Interval] <= 20) continue;
            Dictionary<string, double>? frame = null;
            if (hostPhases)
            {
                frame = [];
                for (int p = 0; p < (int)Phase.Count; p++) frame[((Phase)p).ToString()] = Math.Round(r[(int)Col.Phases + p], 3);
            }
            slow.Add(new
            {
                frame = i, ms = Math.Round(r[(int)Col.Interval], 3), mod = Math.Round(r[(int)Col.Mod], 3),
                native = Math.Round(r[(int)Col.Native], 3), upload = Math.Round(r[(int)Col.Upload], 3), steps = (int)r[(int)Col.Steps],
                netGc = (int)r[(int)Col.NetGen], netPauseMs = Math.Round(r[(int)Col.NetPause], 3),
                hlMarks = (int)r[(int)Col.HlMarks], hlKb = Math.Round(r[(int)Col.HlKb], 1), lab = r[(int)Col.Lab] != 0,
                labMs = Math.Round(r[(int)Col.LabMs], 3), phases = frame,
            });
        }
        // Frames that ended a HashLink mark or a .NET collection, against the others.
        var hlFrames = new List<float>(); var netFrames = new List<float>(); var quiet = new List<float>();
        for (int i = 0; i < n; i++)
        {
            var r = rows.AsSpan(i * cols, cols);
            if (r[(int)Col.Lab] != 0) continue;
            if (r[(int)Col.HlMarks] > 0) hlFrames.Add(r[(int)Col.Interval]);
            if (r[(int)Col.NetGen] >= 0) netFrames.Add(r[(int)Col.Interval]);
            if (r[(int)Col.HlMarks] == 0 && r[(int)Col.NetGen] < 0) quiet.Add(r[(int)Col.Interval]);
        }
        var histogram = new int[61];
        foreach (float v in clean) histogram[Math.Min(60, (int)v)]++;
        var report = new
        {
            mode = steps > 0 ? "kirby" : Bare ? "bare" : "host",
            quiet = Quiet,
            renderer,
            loadAverage = new { first = firstLoad, max = maxLoad },
            frames = n,
            seconds = Math.Round(total / 1000, 2),
            hostFps = Math.Round(n * 1000 / total, 1),
            frameMs = Stats(all),
            withoutLabFrames = Stats(clean),
            withoutLabWork = Stats(withoutLabWork),
            framesOver20Ms = all.Count(v => v > 20),
            framesOver33Ms = all.Count(v => v > 33.4),
            labFrames = n - clean.Count,
            originalSteps = steps,
            textureUploads = textures,
            enemyHookCallsPerFrame = Math.Round(enemyHooks / (double)Math.Max(1, n), 1),
            // All managed allocation on the game thread, as before #174, and without the lab's captures and this probe.
            managedBytesPerFrame = (allocated - firstBytes) / Math.Max(1, n),
            managedBytesPerFrameWithoutLab = (allocated - firstBytes - (bytes[(int)Section.Lab] - firstLabBytes) - (probeBytes - firstProbeBytes)) / Math.Max(1, n),
            managedBytesPerFrameAllThreads = (GC.GetTotalAllocatedBytes(false) - firstAllBytes - (probeBytes - firstProbeBytes)) / Math.Max(1, n),
            collections = new[] { GC.CollectionCount(0) - collections[0], GC.CollectionCount(1) - collections[1], GC.CollectionCount(2) - collections[2] },
            collectionPauseMs = Math.Round((GC.GetTotalPauseDuration() - pause).TotalMilliseconds, 2),
            netGc = new
            {
                server = GCSettings.IsServerGC,
                latency = GCSettings.LatencyMode.ToString(),
                concurrent = AppContext.GetData("System.GC.Concurrent")?.ToString(),
                frameMs = Stats(netFrames),
                events = netGcs.Select(g => new
                {
                    frame = g.Frame, index = g.Index, gen = g.Generation, pauseMs = Math.Round(g.PauseMs, 3),
                    promotedKb = g.PromotedBytes / 1024, heapMb = Math.Round(g.HeapBytes / 1048576.0, 1), g.Compacted, g.Concurrent,
                }),
            },
            hashlinkGc = new
            {
                available = hlStats,
                markCountRead = markCount is not null,
                bytesPerFrame = Math.Round((hlBytes - hlFirstBytes) / Math.Max(1, n)),
                allocationsPerFrame = Math.Round((hlAllocs - hlFirstAllocs) / Math.Max(1, n), 1),
                heapMb = Math.Round(hlMemory / 1048576.0, 1),
                marks = marks - hlFirstMarks,
                frameMs = Stats(hlFrames),
                forcedMajorMs = forcedMajors,
            },
            quietFrameMs = Stats(quiet),
            allocationSamples = allocationSamples?.Report(),
            hookCallsPerFrame = hookCalls?.Report(n),
            histogramMs = histogram,
            sections,
            hostPhases = phases,
            slowFrames = slow,
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path!, JsonSerializer.Serialize(report, options));
        // The frame series for charts: [interval, mod, native, steps, net gen, HL marks, lab] per frame.
        using var writer = new StreamWriter(framesPath!);
        writer.Write("{\"columns\":[\"ms\",\"mod\",\"native\",\"steps\",\"netGc\",\"hlMarks\",\"lab\",\"labMs\"],\"rows\":[");
        for (int i = 0; i < n; i++)
        {
            var r = rows.AsSpan(i * cols, cols);
            if (i > 0) writer.Write(',');
            writer.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[{r[(int)Col.Interval]:0.###},{r[(int)Col.Mod]:0.###},{r[(int)Col.Native]:0.###},{(int)r[(int)Col.Steps]},{(int)r[(int)Col.NetGen]},{(int)r[(int)Col.HlMarks]},{(int)r[(int)Col.Lab]},{r[(int)Col.LabMs]:0.###}]"));
        }
        writer.Write("]}");
    }

    /// <summary>
    /// The runtime's allocation samples (about one per 100 KB allocated), by
    /// type and by whether the game thread made them.
    /// </summary>
    /// <summary>
    /// Counts the calls of each hooked host function. Every call that reaches a
    /// handler passes through DCCM's <c>HashlinkHookManager.HookEntry</c>, one
    /// manager per function; a Harmony prefix (DCCM ships HarmonyX) counts them.
    /// Calls that take DCCM's managed path with no handler left are not counted.
    /// </summary>
    private sealed class HookCounter
    {
        // Calls per hook manager, and those of them made with no handler left
        // (DCCM keeps such a function on its managed path after the last -=).
        private static readonly Dictionary<object, (int Calls, int Idle)> counts = new(ReferenceEqualityComparer.Instance);
        private static bool counting;
        private static System.Reflection.FieldInfo? function, handlers;

        internal static HookCounter? Install()
        {
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var manager = typeof(ModCore.Modules.HashlinkHooks).Assembly.GetType("ModCore.Hooks.HashlinkHookManager");
            var entry = manager?.GetMethod("HookEntry", any);
            var harmonyAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "0Harmony");
            var harmonyType = harmonyAssembly?.GetType("HarmonyLib.Harmony");
            var methodType = harmonyAssembly?.GetType("HarmonyLib.HarmonyMethod");
            var patch = harmonyType?.GetMethods().FirstOrDefault(m => m.Name == "Patch" && m.GetParameters() is { Length: >= 2 } p &&
                p[0].ParameterType == typeof(System.Reflection.MethodBase));
            if (manager is null || entry is null || harmonyType is null || methodType is null || patch is null) return null;
            function = manager.GetField("function", any);
            handlers = manager.GetField("hooks", any);
            object harmony = Activator.CreateInstance(harmonyType, "kirby.lab.hookcalls")!;
            object prefix = Activator.CreateInstance(methodType, typeof(HookCounter).GetMethod(nameof(Prefix), any)!)!;
            var args = new object?[patch.GetParameters().Length];
            args[0] = entry; args[1] = prefix;
            patch.Invoke(harmony, args);
            return new HookCounter();
        }

        private static void Prefix(object __instance)
        {
            if (!counting) return;
            counts.TryGetValue(__instance, out var c);
            bool idle = handlers?.GetValue(__instance) is System.Collections.ICollection { Count: 0 };
            counts[__instance] = (c.Calls + 1, c.Idle + (idle ? 1 : 0));
        }

        internal void Start() { counts.Clear(); counting = true; }

        internal object Report(int frames) => counts
            .Select(kv => (Name: Name(kv.Key), PerFrame: kv.Value.Calls / (double)Math.Max(1, frames),
                IdlePerFrame: kv.Value.Idle / (double)Math.Max(1, frames)))
            .OrderByDescending(x => x.PerFrame)
            .Select(x => new { function = x.Name, perFrame = Math.Round(x.PerFrame, 2), idlePerFrame = Math.Round(x.IdlePerFrame, 2) })
            .ToArray();

        private static string Name(object manager) =>
            function?.GetValue(manager) is Hashlink.Reflection.Members.HashlinkFunction f
                ? $"{f.DeclaringType}.{f.Name}" : manager.ToString() ?? "?";
    }

    private sealed class AllocationListener : EventListener
    {
        private readonly object gate = new();
        private readonly Dictionary<(string Type, bool Game), (long Bytes, int Samples)> samples = [];
        private volatile bool counting;
        private int gameThread;

        internal void Start(int thread)
        {
            lock (gate) { samples.Clear(); gameThread = thread; counting = true; }
        }

        protected override void OnEventSourceCreated(EventSource source)
        {
            // The GC keyword at the verbose level carries GCAllocationTick.
            if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)1);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (!counting || e.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true ||
                e.Payload is null || e.PayloadNames is null) return;
            int type = e.PayloadNames.IndexOf("TypeName"), amount = e.PayloadNames.IndexOf("AllocationAmount64");
            if (amount < 0) amount = e.PayloadNames.IndexOf("AllocationAmount");
            if (type < 0 || amount < 0) return;
            var key = (e.Payload[type] as string ?? "?", e.OSThreadId == gameThread);
            long size = Convert.ToInt64(e.Payload[amount]);
            lock (gate)
            {
                samples.TryGetValue(key, out var total);
                samples[key] = (total.Bytes + size, total.Samples + 1);
            }
        }

        internal object Report()
        {
            lock (gate)
                return samples.OrderByDescending(entry => entry.Value.Bytes).Take(60).Select(entry => new
                {
                    type = entry.Key.Type, gameThread = entry.Key.Game, kb = entry.Value.Bytes / 1024, samples = entry.Value.Samples,
                }).ToArray();
        }
    }
}
