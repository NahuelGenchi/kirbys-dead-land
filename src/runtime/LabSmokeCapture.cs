using System.Runtime.InteropServices;
using System.Diagnostics;
using ModCore.Utilities;
using System.Text;
using System.Text.Json;
using LocalFile = System.IO.File;

namespace Kirby.Runtime;

/// <summary>Private framebuffer capture in the opt-in offscreen development lab.</summary>
internal static class LabSmokeCapture
{
    private static int frames;
    private static string? run;
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static long lastCapture;
    private static long lastSourceDump;
    private static bool started;
    private static uint? sourceStart;
    private static uint? lastSource;
    private static uint? checkpointStart;
    private static bool savedCheckpoint;
    private static bool savedRenderTree;
    private static int captureFps;
    private static readonly HashSet<(int Width, int Height)> queriedHurtboxes = [];
    [DllImport("kssu_bridge", EntryPoint = "kssu_save_state", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SaveNdsState(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libGL.so.1")] private static extern void glGetIntegerv(uint name, [Out] int[] value);
    [DllImport("libGL.so.1")] private static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, [Out] byte[] pixels);
    [DllImport("libGL.so.1")] private static extern void glPixelStorei(uint name, int value);

    public static void Install()
    {
        run = Environment.GetEnvironmentVariable("KIRBY_LAB_RUN_ROOT");
        if (run is null || !Directory.Exists(run)) return;
        string? requestedOutfit = Environment.GetEnvironmentVariable("KIRBY_LAB_OUTFIT");
        if (!string.IsNullOrEmpty(requestedOutfit))
            dc.Hook_User.newGame += (orig, user, seed, level, custom, scoring, mode) =>
            {
                user.heroSkin = requestedOutfit.AsHaxeString();
                orig(user, seed, level, custom, scoring, mode);
            };
        // Interactive play keeps only outfit selection. It must not capture a
        // growing framebuffer trace or inject smoke-test actions.
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_PLAY") == "1") return;
        if (int.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_CAPTURE_FPS"), out int requestedFps))
            captureFps = Math.Clamp(requestedFps, 1, 30);
        if (captureFps > 0) LabAudioCapture.Install(run, () => clock.ElapsedMilliseconds);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "movement")
        {
            dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
            KirbyInput.TestOverride = session =>
            {
                var machine = KirbyMod.Instance?.NativeProvider?.Machine;
                if (machine is null) return 0;
                uint tick = machine.State.Frame;
                sourceStart ??= tick;
                uint elapsed = tick - sourceStart.Value;
                return elapsed switch
                {
                    >= 180 and < 300 => 16,
                    >= 360 and < 372 => 1,
                    >= 540 and < 660 => 2,
                    >= 660 and < 780 => 128,
                    _ => 0,
                };
            };
        }
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "combat")
            LabCombatProbe.Install(run);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "ability")
            LabAbilityProbe.Install(run);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "cook")
            LabCookProbe.Install(run);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "visual")
            LabVisualProbe.Install(run);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "breakable")
            LabBreakableProbe.Install(run);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") is "boss" or "crowd")
            LabBossCrowdProbe.Install(run, Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN")!);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "aim")
            LabWeaponAimProbe.Install(run);
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "repro")
            LabReproProbe.Install(run);
        dc.Hook_Main.update += (orig, main) =>
        {
            orig(main);
            frames++;
            LabCombatProbe.Poll();
            LabAbilityProbe.Poll();
            LabCookProbe.Poll();
            LabVisualProbe.Poll();
            LabBreakableProbe.Poll();
            LabBossCrowdProbe.Poll();
            LabWeaponAimProbe.Poll();
            LabReproProbe.Poll();
            var source = KirbyMod.Instance?.NativeProvider?.Machine;
            if (source is not null && source.State.Frame != lastSource)
            {
                var state = source.State;
                checkpointStart ??= state.Frame;
                lastSource = state.Frame;
                var hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero is { destroyed: false, life: > 0, awake: true } && hero._level is not null &&
                    Environment.GetEnvironmentVariable("KIRBY_LAB_INPUT_PLAN") == "movement")
                    ProbeHurtbox(hero, source);
                if (!savedRenderTree && Environment.GetEnvironmentVariable("KIRBY_LAB_RENDER_TREE") == "1" &&
                    hero?.spr?.parent is not null && hero._level?.scroller is { } scroller)
                {
                    savedRenderTree = true;
                    LocalFile.WriteAllText(Path.Combine(run, "render-tree.json"), JsonSerializer.Serialize(new
                    {
                        sprite = DescribeObject(hero.spr, 0),
                        parent = DescribeObject(hero.spr.parent, 1),
                        grandparent = DescribeObject(hero.spr.parent.parent, 2),
                        scroller = DescribeObject(scroller, 2),
                    }, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
                    }));
                }
                LocalFile.AppendAllText(Path.Combine(run, "source-trace.jsonl"), JsonSerializer.Serialize(new
                {
                    mainFrames = frames,
                    elapsedMs = clock.ElapsedMilliseconds,
                    ndsFrame = state.Frame,
                    sourceX = state.X,
                    sourceY = state.Y,
                    vx = state.VxRaw,
                    vy = state.VyRaw,
                    ability = state.Ability,
                    abilityId = KirbyMod.Instance?.ActiveSession?.Rules.AbilityId,
                    retainedShieldId = KirbyMod.Instance?.ActiveSession?.Rules.RetainedShieldId,
                    sourceHealth = source.Health,
                    hostLife = hero?.life,
                    hostHeight = hero?.hei,
                    hostRadius = hero?.radius,
                    hostRectangle = hero?.useRectHitbox,
                    hurtbox = KirbyHurtbox.Diagnostics,
                    animation = state.Animation,
                    graphic = state.Graphic,
                    flags = state.Flags,
                    facing = state.FacingRaw,
                    hostX = hero is null ? 0 : (hero.cx + hero.xr) * 24,
                    hostY = hero is null ? 0 : (hero.cy + hero.yr) * 24,
                    awake = hero?.awake,
                    gameTimeRunning = hero?._level?.game?.isGameTimeRunning(),
                    tutorial = hero?._level?.game?.user?.tutorial?.current?.ToString(),
                }) + "\n");
                if (!savedCheckpoint && int.TryParse(Environment.GetEnvironmentVariable("KIRBY_LAB_SAVE_AFTER"), out int saveAfter) &&
                    state.Frame - checkpointStart.Value >= saveAfter && main.writeSave())
                {
                    savedCheckpoint = true;
                    var rules = KirbyMod.Instance?.ActiveSession?.Rules;
                    LocalFile.WriteAllText(Path.Combine(run, "checkpoint.json"), JsonSerializer.Serialize(new
                    {
                        ndsFrame = state.Frame, sourceX = state.X, sourceY = state.Y,
                        sourceAbility = state.Ability, ability = rules?.AbilityId, shield = rules?.RetainedShieldId,
                    }));
                }
            }
            if (!started && Environment.GetEnvironmentVariable("KIRBY_LAB_START_GAME") == "1" &&
                clock.ElapsedMilliseconds > 5000 && main.curProcess is dc.pr.TitleScreen title && !main.isLoading)
            {
                started = true;
                main.options.showModsUI = false;
                title.startNewGame(false);
            }
            if (!started && Environment.GetEnvironmentVariable("KIRBY_LAB_RESUME_GAME") == "1" &&
                clock.ElapsedMilliseconds > 5000 && main.curProcess is dc.pr.TitleScreen && !main.isLoading)
            {
                started = true;
                main.launchGame(new dc.LaunchMode.LoadSave(), null, null);
            }
            var captureHero = ModCore.Modules.Game.Instance.HeroInstance;
            bool gameplay = source is not null && captureHero is { destroyed: false, life: > 0, awake: true } &&
                captureHero._level is not null && !captureHero._level.game.hasCinematic();
            int interval = captureFps > 0 && gameplay ? 1000 / captureFps : LabVisualProbe.ActiveEffect ? 100 : 2000;
            if (clock.ElapsedMilliseconds - lastCapture < interval) return;
            lastCapture = clock.ElapsedMilliseconds;
            LocalFile.WriteAllText(Path.Combine(run, "frames.json"), JsonSerializer.Serialize(new
            {
                frames,
                process = main.curProcess?.GetType().FullName,
                hero = ModCore.Modules.Game.Instance.HeroInstance is not null,
                loading = main.isLoading,
                outfit = main.user?.heroSkin?.ToString(),
            }));
            Capture(Path.Combine(run, $"frame-{frames:000000}.ppm"));
            if (captureFps > 0)
                LocalFile.AppendAllText(Path.Combine(run, "captures.jsonl"), JsonSerializer.Serialize(new
                {
                    file = $"frame-{frames:000000}.png", elapsedMs = lastCapture,
                    gameplay, sourceFrame = source?.State.Frame,
                }) + "\n");
            if (source is not null && clock.ElapsedMilliseconds - lastSourceDump >= 2000)
            {
                lastSourceDump = clock.ElapsedMilliseconds;
                SaveNdsState(source.Handle, Path.Combine(run, "latest-nds.state"));
                byte[] ram = new byte[4 * 1024 * 1024];
                source.Read(0x02000000, ram);
                LocalFile.WriteAllBytes(Path.Combine(run, "latest-nds.ram"), ram);
            }
        };
    }

    private static unsafe void ProbeHurtbox(dc.en.Hero hero, NdsRuntime machine)
    {
        if (hero._level.game.hasCinematic()) return;
        var state = machine.State;
        Span<NdsHit> hits = stackalloc NdsHit[512];
        int count = machine.ReadHits(hits);
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int i = 0; i < count; i++)
        {
            ref var hit = ref hits[i];
            if (hit.Slot != 14 || hit.Owner != state.Player) continue;
            left = Math.Min(left, hit.Bounds[0]); top = Math.Min(top, hit.Bounds[1]);
            right = Math.Max(right, hit.Bounds[2]); bottom = Math.Max(bottom, hit.Bounds[3]);
        }
        if (left >= right || top >= bottom || !queriedHurtboxes.Add((right - left, bottom - top))) return;
        var before = (hero.cx, hero.cy, hero.xr, hero.yr, hero.hei, hero.radius, hero.useRectHitbox);
        double ox = (hero.cx + hero.xr) * 24 - state.X;
        double oy = (hero.cy + hero.yr) * 24 - 14.5 - state.Y;
        bool Touches(double x, double y)
        {
            var candidates = new dc.hl.types.ArrayObj();
            var helper = new dc.tool.TargetHelper(candidates);
            helper.add(hero);
            var area = new dc.tool.Area(0.25, 0.25);
            area.setAbsolutePos(x, y);
            helper.filterByArea(area);
            return helper.hasRemaining();
        }
        bool inside = Touches(ox + (left + right) * 0.5, oy + (top + bottom) * 0.5);
        bool above = Touches(ox + (left + right) * 0.5, oy + top - 2);
        bool beside = Touches(ox + right + 2, oy + (top + bottom) * 0.5);
        bool restored = before == (hero.cx, hero.cy, hero.xr, hero.yr, hero.hei, hero.radius, hero.useRectHitbox);
        LocalFile.AppendAllText(Path.Combine(run!, "hurtbox-queries.jsonl"), JsonSerializer.Serialize(new
        {
            frame = state.Frame, width = right - left, height = bottom - top,
            inside, above, beside, restored, passed = inside && !above && !beside && restored,
        }) + "\n");
    }

    private static object? DescribeObject(dc.h2d.Object? node, int depth)
    {
        if (node is null) return null;
        var children = new List<object?>();
        if (depth > 0)
            for (int i = 0; i < node.children.length; i++)
                if (node.children.getDyn(i) is dc.h2d.Object child)
                    children.Add(new { index = i, node = DescribeObject(child, depth - 1) });
        var layerIndexes = new List<int>();
        if (node is dc.h2d.Layers layers)
            for (int i = 0; i < layers.layersIndexes.length; i++)
                layerIndexes.Add((int)layers.layersIndexes.getDyn(i));
        return new
        {
            type = node.GetType().FullName, name = node.name?.ToString(),
            node.x, node.y, node.scaleX, node.scaleY, node.visible, node.alpha,
            filter = node.filter?.GetType().FullName,
            childCount = node.children.length, layerIndexes, children,
        };
    }

    private static void Capture(string path)
    {
        int[] viewport = new int[4];
        glGetIntegerv(0x0BA2, viewport);
        int width = viewport[2], height = viewport[3];
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return;
        byte[] pixels = new byte[checked(width * height * 3)];
        int[] alignment = new int[1];
        glGetIntegerv(0x0D05, alignment);
        glPixelStorei(0x0D05, 1);
        glReadPixels(0, 0, width, height, 0x1907, 0x1401, pixels);
        glPixelStorei(0x0D05, alignment[0]);
        using var output = LocalFile.Create(path);
        output.Write(Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        for (int y = height - 1; y >= 0; y--)
            output.Write(pixels.AsSpan(y * width * 3, width * 3));
    }
}
