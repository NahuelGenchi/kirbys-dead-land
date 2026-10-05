using System.Diagnostics;
using System.Text;
using System.Text.Json;
using dc.en;
using dc.en.inter;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Lab-only checks for Kirby's roll and stomp mechanics in a generated level.
/// KIRBY_LAB_MOVES_SCAN=1 maps roll zones and breakables; KIRBY_LAB_MOVES_SETUP
/// = scenario@seconds teleports Kirby next to one (rollzone, air, breakable,
/// door, tile), optionally with a Zombie below (KIRBY_LAB_MOVES_TARGET=1 still, 2 active).
/// KIRBY_LAB_RUNE=1 grants the floor-breaking rune for the run. Inputs come
/// from the normal-binding repro plan; this probe only places and records.
/// </summary>
internal static unsafe class LabMovesProbe
{
    private static string? run;
    private static nint dumpedMap;
    private static int mapDumps;
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static double? began;
    private static bool scanned;
    private static readonly HashSet<string> placedSetups = [];
    private static uint lastFrame;
    private static Mob? target;
    private static int phaseCount;
    private static readonly JsonSerializerOptions Options = new()
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static void Install(string output)
    {
        run = output;
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_RUNE") == "1")
            dc.tool.Hook_ItemMetaManager.hasPermanentItem += (orig, meta, kind) =>
                kind?.ToString() == "BreakableGroundKey" || orig(meta, kind);
        Hook_BreakableGround.onDie += (orig, ground) => { Event("breakable-ground-broken", new { ground.cx, ground.cy, ground.cwid }); orig(ground); };
        Hook_BreakableGround.refuse += (orig, ground) => { Event("breakable-ground-refused", new { ground.cx, ground.cy }); orig(ground); };
        Hook_BreakableTile.onDie += (orig, tile) => { Event("breakable-tile-broken", new { tile.cx, tile.cy }); orig(tile); };
        Hook_Door.onDamage += (orig, door, attack) =>
        { Event("door-hit", new { door.cx, door.cy, attack.finalDmg, door.broken }); orig(door, attack); };
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_HERO_PHASES") == "1")
        {
            void Phase(string where)
            {
                var hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero is null || phaseCount > 400 || Elapsed < 1.5) return;
                phaseCount++;
                Event("phase", new { where, hero.cy, hero.yr, hero.dy, hero.radius, hero.hei, hero.gravity });
            }
            Hook_Hero.preUpdate += (orig, hero) => { Phase("pre-before"); orig(hero); Phase("pre-after"); };
            Hook_Hero.fixedUpdate += (orig, hero) => { Phase("fixed-before"); orig(hero); Phase("fixed-after"); };
            dc.pr.Hook_Level.resolveCircularCollisions += (orig, level) => { Phase("circ-before"); orig(level); Phase("circ-after"); };
            Hook_Hero.postUpdate += (orig, hero) => { Phase("post-before"); orig(hero); Phase("post-after"); };
        }
        Hook_PressurePlate.trigger += (orig, plate, by) =>
        { Event("plate-trigger", new { plate.cx, plate.cy, by = by?.GetType().FullName }); orig(plate, by); };
        Hook_PressurePlate.fixedUpdate += (orig, plate) =>
        {
            var hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero is not null && Math.Abs(hero.cx - plate.cx) <= 2 && Math.Abs(hero.cy - plate.cy) <= 2 && lastFrame % 20 == 0)
                Event("plate-seen", new { plate.cx, plate.cy, plateX = (plate.cx + plate.xr) * 24, plate.active,
                    cells = new[] { Cell(hero._level.map, plate.cx - 1, plate.cy), Cell(hero._level.map, plate.cx, plate.cy), Cell(hero._level.map, plate.cx + 1, plate.cy),
                        Cell(hero._level.map, plate.cx - 1, plate.cy + 1), Cell(hero._level.map, plate.cx, plate.cy + 1), Cell(hero._level.map, plate.cx + 1, plate.cy + 1) },
                    near = Entities<dc.Entity>(hero).Where(e => !e.destroyed && Math.Abs(e.cx - plate.cx) <= 2 && Math.Abs(e.cy - plate.cy) <= 2)
                        .Select(e => new { type = e.GetType().FullName, e.cx, e.cy, e.xr, e.yr, e.radius, e.hei }).ToArray(),
                    proxies = Enumerable.Range(1, 31).Select(slot => KirbyMod.Instance!.NativeProvider!.Machine!.Proxy((uint)slot))
                        .Where(p => p.Entity != 0).Select(p => new { p.Flags, x = p.XRaw / 65536.0, y = p.YRaw / 65536.0 }).ToArray(),
                    groundYr = hero._level.map.getGroundYr(hero.cx, hero.cy, HaxeProxy.Runtime.Ref<double>.In(hero.xr), HaxeProxy.Runtime.Ref<double>.In(hero.yr)),
                    hero = new { hero.cx, hero.cy, hero.xr, hero.yr, hero.dy, hero.dx, x = (hero.cx + hero.xr) * 24 } });
            orig(plate);
        };
        dc.tool.mainSkills.Hook_DiveAttack.onOwnerLand += (orig, dive, height) =>
        { Event("host-dive-land", new { height, active = dive.isActive(), targetLife = target?.life }); orig(dive, height); Event("host-dive-landed", new { targetLife = target?.life }); };
    }

    private static void Event(string name, object data) => File.AppendAllText(Path.Combine(run!, "moves-events.jsonl"),
        JsonSerializer.Serialize(new { elapsed = Elapsed, name, frame = KirbyMod.Instance?.NativeProvider?.Machine?.State.Frame, data }, Options) + "\n");

    private static double Elapsed => began is double at ? clock.Elapsed.TotalSeconds - at : -1;

    public static void Poll()
    {
        if (run is null) return;
        var provider = KirbyMod.Instance?.NativeProvider;
        var nds = provider?.Machine;
        var hero = ModCore.Modules.Game.Instance.HeroInstance;
        // The Beheaded (normal outfit) can be placed too, to measure host movement for comparison.
        bool kirby = nds is not null && KirbyMod.Instance?.ActiveSession is not null;
        if (hero is not { destroyed: false } || hero._level?.map is null || (!kirby && Environment.GetEnvironmentVariable("KIRBY_LAB_MOVES_ANY") != "1")) return;
        if (began is null && hero.awake && !hero._level.game.hasCinematic()) began = clock.Elapsed.TotalSeconds;
        if (began is null) return;
        var map = hero._level.map;
        if (dumpedMap != map.HashlinkPointer)
        {
            // Host collision grid of each level, for offline terrain checks:
            // i32 width, i32 height, then width*height i32 collision words.
            dumpedMap = map.HashlinkPointer;
            using var file = File.Create(Path.Combine(run, $"host-map-{++mapDumps}.bin"));
            using var writer = new BinaryWriter(file);
            writer.Write(map.wid); writer.Write(map.hei);
            for (int i = 0; i < map.wid * map.hei; i++) writer.Write(((int*)map.collisions.bytes)[i]);
        }
        if (!scanned && Elapsed >= 0.5 && Environment.GetEnvironmentVariable("KIRBY_LAB_MOVES_SCAN") == "1") { scanned = true; Scan(hero, map); }
        if (Environment.GetEnvironmentVariable("KIRBY_LAB_MOVES_SETUP") is { } setups)
            foreach (string setup in setups.Split(','))
                if (!placedSetups.Contains(setup) && setup.Split('@') is [var scenario, var at] && double.TryParse(at, out double time) && Elapsed >= time)
                {
                    placedSetups.Add(setup);
                    Place(hero, map, scenario);
                }
        if (!kirby) return;
        var state = nds!.State;
        if (state.Frame == lastFrame) return;
        lastFrame = state.Frame;
        var renderer = typeof(KirbyNdsProvider).GetField("renderer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(provider) as KirbySpriteRenderer;
        int diving = hero.affects.length > 11 && hero.affects.getDyn(11) is dc.hl.types.ArrayBase arr ? arr.length : -1;
        File.AppendAllText(Path.Combine(run, "moves-trace.jsonl"), JsonSerializer.Serialize(new
        {
            elapsed = Elapsed, frame = state.Frame, action = nds.Action,
            x = (hero.cx + hero.xr) * 24, feet = (hero.cy + hero.yr) * 24, state.VxRaw, state.VyRaw,
            stomping = provider!.Stomping, provider.SlideStrike, diving,
            ignore = hero.ignoreGround is { } ig ? ig.untilY : (int?)null,
            displayed = new[] { nds.DisplayedOrigin.X, nds.DisplayedOrigin.Y },
            current = new[] { (int)Math.Floor(state.X) - state.CameraX, (int)Math.Floor(state.Y) - state.CameraY },
            body = renderer is null ? null : new[] { renderer.BodyBounds.Left, renderer.BodyBounds.Top, renderer.BodyBounds.Right, renderer.BodyBounds.Bottom },
            bodyPixels = renderer?.BodyPixels, targetLife = target?.life, hero.life,
        }, Options) + "\n");
    }

    private static int Cell(dc.level.LevelMap map, int cx, int cy) =>
        cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei ? 3 : ((int*)map.collisions.bytes)[cy * map.wid + cx];

    private static char Glyph(int c) => (c & 64) != 0 ? 'R' : (c & 128) != 0 ? 'B' : (c & 512) != 0 ? '/' : (c & 8) != 0 ? '=' :
        (c & 3) != 0 ? '#' : (c & 16) != 0 ? 'H' : '.';

    private static IEnumerable<T> Entities<T>(Hero hero) where T : class
    {
        for (int i = 0; i < hero._level.entities.length; i++)
            if (hero._level.entities.getDyn(i) is T entity) yield return entity;
    }

    private static List<(int X0, int Y0, int X1, int Y1)> RollZones(dc.level.LevelMap map)
    {
        var seen = new HashSet<(int, int)>();
        var zones = new List<(int, int, int, int)>();
        for (int y = 0; y < map.hei; y++)
            for (int x = 0; x < map.wid; x++)
            {
                if ((Cell(map, x, y) & 64) == 0 || !seen.Add((x, y))) continue;
                var queue = new Queue<(int, int)>([(x, y)]);
                int x0 = x, y0 = y, x1 = x, y1 = y;
                while (queue.TryDequeue(out var p))
                {
                    (int px, int py) = p;
                    x0 = Math.Min(x0, px); x1 = Math.Max(x1, px); y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
                    foreach (var n in new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) })
                        if ((Cell(map, n.Item1, n.Item2) & 64) != 0 && seen.Add(n)) queue.Enqueue(n);
                }
                zones.Add((x0, y0, x1, y1));
            }
        return zones;
    }

    private static void Scan(Hero hero, dc.level.LevelMap map)
    {
        var zones = RollZones(map);
        var text = new StringBuilder();
        foreach (var (x0, y0, x1, y1) in zones.Take(40))
        {
            text.AppendLine($"roll zone x {x0}..{x1} y {y0}..{y1}");
            for (int y = y0 - 4; y <= y1 + 3; y++)
            {
                for (int x = x0 - 8; x <= x1 + 8; x++) text.Append(Glyph(Cell(map, x, y)));
                text.AppendLine();
            }
        }
        File.WriteAllText(Path.Combine(run!, "rollzones.txt"), text.ToString());
        File.WriteAllText(Path.Combine(run!, "level-features.json"), JsonSerializer.Serialize(new
        {
            hero = new { hero.cx, hero.cy }, map.wid, map.hei, rollZones = zones.Select(z => new[] { z.X0, z.Y0, z.X1, z.Y1 }),
            breakableGround = Entities<BreakableGround>(hero).Select(e => new { e.cx, e.cy, e.cwid, e.chei, e.destroyed }),
            breakableTiles = Entities<BreakableTile>(hero).Select(e => new { e.cx, e.cy, e.destroyed }),
            doors = Entities<Door>(hero).Select(e => new { e.cx, e.cy, e.broken, e._breakable, e.locked }),
            plates = Entities<PressurePlate>(hero).Select(e => new { e.cx, e.cy, e.xr, e.yr, type = e.GetType().FullName }),
            options = new { bind = dc.Main.Class.ME.options.diveAttackBind },
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Teleport(Hero hero, double x, double feet)
    {
        hero.setPosPixel(x, feet);
        if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
        hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
    }

    private static bool Open(dc.level.LevelMap map, int cx, int top, int bottom)
    {
        for (int y = top; y <= bottom; y++) if ((Cell(map, cx, y) & 0x2DB) != 0) return false;
        return true;
    }

    private static void Place(Hero hero, dc.level.LevelMap map, string scenario)
    {
        object? detail = null;
        switch (scenario)
        {
            case "rollzone":
                // Stand on the floor three cells before the first zone with a floor under it.
                foreach (var (x0, y0, x1, y1) in RollZones(map))
                {
                    if ((Cell(map, x0, y1 + 1) & 3) == 0) continue;
                    int start = x0 - 3;
                    if (!Open(map, start, y1 - 1, y1) || (Cell(map, start, y1 + 1) & 3) == 0) continue;
                    Teleport(hero, start * 24 + 12, (y1 + 1) * 24);
                    hero.dir = 1;
                    detail = new { zone = new[] { x0, y0, x1, y1 }, start };
                    break;
                }
                break;
            case "air":
            case "breakable":
            {
                int cx = hero.cx, ground = hero.cy + 1;
                if (scenario == "breakable" && Entities<BreakableGround>(hero).Where(e => !e.destroyed)
                        .OrderByDescending(e => Enumerable.Range(1, 6).TakeWhile(h => Open(map, e.cx + e.cwid / 2, e.cy - h, e.cy - 1)).Count())
                        .FirstOrDefault() is { } floor)
                { cx = floor.cx + floor.cwid / 2; ground = floor.cy; }
                else
                {
                    for (int d = 0; d < 30; d++)
                        foreach (int candidate in new[] { hero.cx + d, hero.cx - d })
                        {
                            int g = hero.cy + 1;
                            while (g < map.hei && (Cell(map, candidate, g) & 3) == 0) g++;
                            if (Open(map, candidate, g - 7, g - 1) && Open(map, candidate - 1, g - 7, g - 1) && Open(map, candidate + 1, g - 7, g - 1))
                            { cx = candidate; ground = g; d = 99; break; }
                        }
                }
                int height = 5;
                while (height > 2 && !(Open(map, cx, ground - height, ground - 1) && Open(map, cx - 1, ground - height, ground - 1) &&
                                       Open(map, cx + 1, ground - height, ground - 1))) height--;
                Teleport(hero, cx * 24 + 12, (ground - height + 1) * 24);
                // 1: a Zombie with its AI locked (a still body); 2: an active one that attacks.
                if (Environment.GetEnvironmentVariable("KIRBY_LAB_MOVES_TARGET") is "1" or "2")
                {
                    target = dc.en.mob.Zombie.Class.create(hero._level, cx + 1, ground - 1, 1, 1);
                    if (Environment.GetEnvironmentVariable("KIRBY_LAB_MOVES_TARGET") == "1") target.lockAiS(30);
                }
                detail = new { cx, ground, targetLife = target?.life };
                break;
            }
            case "flat":
            {
                // The left end of the longest open flat corridor: solid floor, four free cells above.
                int bestX = -1, bestY = -1, bestLength = 0;
                for (int y = 4; y < map.hei - 1; y++)
                {
                    int length = 0;
                    for (int x = 0; x < map.wid; x++)
                    {
                        bool open = (Cell(map, x, y + 1) & 3) != 0 && (Cell(map, x, y + 1) & 64) == 0 && Open(map, x, y - 3, y);
                        length = open ? length + 1 : 0;
                        if (length > bestLength) { bestLength = length; bestX = x - length + 1; bestY = y; }
                    }
                }
                if (bestLength >= 8)
                {
                    Teleport(hero, (bestX + 1) * 24 + 12, (bestY + 1) * 24);
                    hero.dir = 1;
                    detail = new { x = bestX, y = bestY, length = bestLength };
                }
                break;
            }
            case "high":
            {
                // The column with the tallest open drop onto solid floor.
                int bestX = -1, bestGround = -1, bestHeight = 0;
                for (int x = 1; x < map.wid - 1; x++)
                    for (int y = 1; y < map.hei - 1; y++)
                    {
                        if ((Cell(map, x, y) & 3) == 0 || (Cell(map, x, y - 1) & 0x2DB) != 0) continue;
                        int height = 0;
                        while (y - height - 1 > 0 && Open(map, x - 1, y - height - 1, y - height - 1) && Open(map, x, y - height - 1, y - height - 1) &&
                               Open(map, x + 1, y - height - 1, y - height - 1)) height++;
                        if (height > bestHeight) { bestHeight = height; bestX = x; bestGround = y; }
                    }
                if (bestHeight >= 6)
                {
                    int up = Math.Min(bestHeight - 1, 14);
                    Teleport(hero, bestX * 24 + 12, (bestGround - up) * 24);
                    detail = new { x = bestX, ground = bestGround, height = bestHeight, up };
                }
                break;
            }
            case "ladder":
            {
                // Stand at the foot of the first ladder or chain at least six cells tall.
                for (int x = 0; x < map.wid && detail is null; x++)
                    for (int y = 1; y < map.hei - 1; y++)
                    {
                        int c = Cell(map, x, y);
                        if ((c & 16) == 0 || (c & 3) != 0 || (Cell(map, x, y + 1) & 3) == 0) continue;
                        int height = 0;
                        while ((Cell(map, x, y - height) & 16) != 0 && (Cell(map, x, y - height) & 3) == 0) height++;
                        if (height < 6) continue;
                        Teleport(hero, x * 24 + 12, (y + 1) * 24);
                        detail = new { x, bottom = y, height };
                        break;
                    }
                break;
            }
            case "plate":
            {
                if (Entities<PressurePlate>(hero).FirstOrDefault(p => !p.destroyed) is { } plate)
                {
                    Teleport(hero, (plate.cx + plate.xr) * 24, (plate.cy + 1) * 24);
                    detail = new { plate.cx, plate.cy, plate.xr, plate.yr, type = plate.GetType().FullName };
                }
                break;
            }
            case "door":
            case "tile":
            {
                dc.Entity? found = scenario == "door"
                    ? Entities<Door>(hero).FirstOrDefault(d => !d.destroyed && !d.broken && !d.locked && d._breakable)
                    : Entities<BreakableTile>(hero).FirstOrDefault(t => !t.destroyed);
                if (found is not null)
                {
                    Teleport(hero, (found.cx - 3) * 24 + 12, (found.cy + 1) * 24);
                    hero.dir = 1;
                    detail = new { found.cx, found.cy };
                }
                break;
            }
        }
        Event("placed", new { scenario, detail, x = (hero.cx + hero.xr) * 24, feet = (hero.cy + hero.yr) * 24 });
    }
}
