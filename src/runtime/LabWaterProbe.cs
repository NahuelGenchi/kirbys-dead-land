using System.Text.Json;
using dc.en;
using dc.en.mob;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Path = System.IO.Path;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check: a real Zombie stands in a host pool next to the dry bank
/// where Kirby is placed, its AI held so it stays in the water. The original
/// Waddle Dee proxy used to be defeated by source water, which pinned the host
/// enemy and ignored inhale and attacks. The probe records whether the source
/// claims the Zombie before any input, then holds the original inhale until it
/// is consumed and swallows it with DOWN.
/// <para>
/// With <c>KIRBY_LAB_WATER_LEVEL</c> the new run starts in that level. With
/// <c>KIRBY_LAB_WATER_WADE=1</c> the probe wades instead (#144): Kirby is put on
/// the floor in the middle of the widest stretch of water (deep water if the
/// level has any), stands, walks right and left, jumps and walks again, and
/// the original's action codes are counted for each step and listed in order
/// (<c>0815</c> is swimming). The running moves are listed too, and the host
/// distance of each source frame Kirby wades along the floor (#166). The plan is short because a boss that wakes up
/// attacks about eight seconds later, and it stops if Kirby dies. Both modes
/// record how deep every host water column is.
/// </para>
/// </summary>
internal static unsafe class LabWaterProbe
{
    private static string? run;
    private static uint? start, placedAt, inhaleAt, consumedAt, swallowAt, finishedAt;
    private static Zombie? zombie;
    private static int towardPool;
    private static object? site;
    private static bool searched, timedOut, mouthfulAtConsumption, leftWaterBeforeInhale;
    private static int waterCells, poolFloorCells;
    private static string[]? surroundings;
    private static int framesControlledBeforeInhale, zombieLifeBeforeInhale, zombieLifeAtEnd, kirbyLifeBefore, kirbyLifeAfter;
    private static byte sourceAbilityAfter;
    private static byte? firstCopyAfterSwallow;
    private static uint? firstCopyFrame;
    private static string? copyAbilityAfter;
    private static uint lastReportFrame;
    private static readonly string? level = Environment.GetEnvironmentVariable("KIRBY_LAB_WATER_LEVEL");
    private static readonly bool wade = Environment.GetEnvironmentVariable("KIRBY_LAB_WATER_WADE") == "1";
    private static object? depths, wadeSite;
    private static nint wadeHero;
    private static bool wadeInterrupted;
    // Step, the source frame it ends at and the DS keys held (A 1, right 16, left 32).
    private static readonly (string Name, uint Until, ushort Keys)[] WadePlan =
        [("stand", 60, 0), ("walk", 120, 16), ("walk", 180, 32), ("jump", 192, 1), ("jump", 300, 0), ("walkAfterJump", 360, 16)];
    private static readonly SortedDictionary<string, SortedDictionary<string, int>> wadeActions = [];
    private static readonly List<string> wadeOrder = [];
    // #166: each change of the running move with its frame, and the host distance
    // of each source frame spent in the wade (060B under 0x02131160) after another.
    private static readonly List<string> wadeMoves = [];
    private static readonly List<double> wadeSteps = [];
    private static double? lastWadeX;
    private static uint lastWadeFrame;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        if (level is not null)
            dc.Hook_User.newGame += (orig, user, seed, info, custom, scoring, mode) =>
                orig(user, seed, ((HaxeProxyBase)dc.Data.Class.level.resolve(level.AsHaxeString(), false)).ToVirtual<LevelInfo>(),
                    custom, scoring, mode);
        KirbyInput.TestOverride = Input;
    }

    private static int Cell(dc.level.LevelMap map, int cx, int cy) =>
        cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei ? 3 : ((int*)map.collisions.bytes)[cy * map.wid + cx];

    /// <summary>A free cell with host water in it, however shallow.</summary>
    private static bool HostWater(dc.level.LevelMap map, int cx, int cy) =>
        (Cell(map, cx, cy) & 3) == 0 && map.getRoomAt(cx, cy) is not null && map.getWaterSurfaceY(cx, cy) > 0;

    /// <summary>A cell the runtime exports as original water.</summary>
    private static bool Water(dc.level.LevelMap map, int cx, int cy) =>
        HostWater(map, cx, cy) && Kirby.Core.SpeedMatch.Submerged(map.getWaterSurfaceY(cx, cy), cy);

    private static bool Floor(dc.level.LevelMap map, int cx, int cy) => (Cell(map, cx, cy + 1) & 3) != 0;

    /// <summary>
    /// Host water columns by depth in pixels (surface to the first solid cell
    /// below), and how many of their cells the runtime exports as original water.
    /// </summary>
    private static object Survey(dc.level.LevelMap map)
    {
        var columns = new SortedDictionary<double, int>();
        int hostCells = 0, exported = 0;
        for (int y = 1; y < map.hei; y++)
        for (int x = 0; x < map.wid; x++)
        {
            if (!HostWater(map, x, y)) continue;
            hostCells++;
            if (Water(map, x, y)) exported++;
            if (HostWater(map, x, y - 1)) continue;
            int floor = y;
            while (floor < map.hei && (Cell(map, x, floor) & 3) == 0) floor++;
            double depth = Math.Round(floor * 24 - map.getWaterSurfaceY(x, y), 1);
            columns[depth] = columns.GetValueOrDefault(depth) + 1;
        }
        return new { hostCells, exported, columnsByDepth = columns.ToDictionary(pair => pair.Key.ToString("0.#"), pair => pair.Value) };
    }

    /// <summary>
    /// The floor in the middle of the widest stretch of water with two free
    /// cells above it: exported water if the level has any, otherwise host water.
    /// </summary>
    private static bool Wade(Hero hero)
    {
        var map = hero._level.map;
        (int X, int Y, int Width) best = default;
        foreach (bool deep in new[] { true, false })
        {
            for (int y = 3; y < map.hei - 1; y++)
            for (int x = 0, run = 0; x < map.wid; x++)
            {
                bool open = (deep ? Water(map, x, y) : HostWater(map, x, y)) && Floor(map, x, y) &&
                    (Cell(map, x, y - 1) & 3) == 0 && (Cell(map, x, y - 2) & 3) == 0;
                run = open ? run + 1 : 0;
                if (run > best.Width) best = (x - run / 2, y, run);
            }
            if (best.Width > 0) break;
        }
        if (best.Width == 0) return false;
        hero.setPosPixel(best.X * 24 + 12, (best.Y + 1) * 24);
        if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
        hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
        wadeHero = hero.HashlinkPointer;
        double surface = map.getWaterSurfaceY(best.X, best.Y);
        wadeSite = new { cell = new[] { best.X, best.Y }, width = best.Width, surface, floor = (best.Y + 1) * 24,
            depth = (best.Y + 1) * 24 - surface, exported = Water(map, best.X, best.Y) };
        return true;
    }

    private static ushort WadeInput(NdsRuntime machine, Hero hero, uint frame)
    {
        // A death starts a new run with another hero somewhere else.
        if (hero.HashlinkPointer != wadeHero || hero.life <= 0) { wadeInterrupted = true; finishedAt = frame; return 0; }
        uint t = frame - placedAt!.Value;
        if (frame != lastWadeFrame)
        {
            lastWadeFrame = frame;
            machine.TryMove(out NdsMove move);
            string moveText = $"{move.Action:X4}/{move.Descriptor:X8}/{move.Carry}";
            if (wadeMoves.Count == 0 || !wadeMoves[^1].EndsWith(moveText)) wadeMoves.Add($"{t} {moveText}");
            double x = (hero.cx + hero.xr) * 24;
            bool wading = move.Action == Kirby.Core.KirbyActions.Walk && move.Descriptor == Kirby.Core.KirbyMoves.Wade;
            if (wading && lastWadeX is { } last) wadeSteps.Add(Math.Round(x - last, 4));
            lastWadeX = wading ? x : null;
        }
        foreach (var (name, until, keys) in WadePlan)
        {
            if (t >= until) continue;
            if (!wadeActions.TryGetValue(name, out var counts)) wadeActions[name] = counts = [];
            string action = machine.Action.ToString("X4");
            counts[action] = counts.GetValueOrDefault(action) + 1;
            if (wadeOrder.Count == 0 || !wadeOrder[^1].EndsWith(action)) wadeOrder.Add($"{t} {action}");
            return keys;
        }
        finishedAt = frame;
        return 0;
    }

    /// <summary>
    /// A pool floor cell beside a dry bank cell with a floor and two free cells
    /// above it (the bank top may be up to three cells higher than the pool floor).
    /// </summary>
    private static bool Place(Hero hero)
    {
        var map = hero._level.map;
        for (int y = 2; y < map.hei - 1; y++)
        for (int x = 2; x < map.wid - 2; x++)
        {
            if (!Water(map, x, y)) continue;
            waterCells++;
            surroundings ??= Enumerable.Range(y - 5, 8).Select(row => new string(Enumerable.Range(x - 8, 24).Select(col =>
                Water(map, col, row) ? '~' : (Cell(map, col, row) & 3) != 0 ? '#' : (Cell(map, col, row) & 8) != 0 ? '=' :
                (Cell(map, col, row) & 16) != 0 ? 'H' : '.').ToArray()) + $" {row}").Prepend($"from x={x - 8}").ToArray();
            if (!Floor(map, x, y)) continue;
            poolFloorCells++;
            foreach (int dir in new[] { 1, -1 })
            {
                int edge = x + dir;
                if (Water(map, edge, y)) continue;
                // Two cells into a wider pool, otherwise the cell beside the bank.
                int pool = Water(map, x - dir, y) && Floor(map, x - dir, y) ? x - dir : x;
                for (int bank = y; bank >= y - 3; bank--)
                {
                    if ((Cell(map, edge, bank) & 3) != 0 || Water(map, edge, bank) || !Floor(map, edge, bank) ||
                        (Cell(map, edge, bank - 1) & 3) != 0 || (Cell(map, edge, bank - 2) & 3) != 0) continue;
                    hero.setPosPixel(edge * 24 + 12, (bank + 1) * 24);
                    if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
                    hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
                    zombie = Zombie.Class.create(hero._level, pool, y, 1, 1);
                    zombie.setPosPixel(pool * 24 + 12, (y + 1) * 24);
                    if (zombie.yr == 0) { zombie.cy--; zombie.yr = 1; }
                    towardPool = -dir;
                    site = new { pool = new[] { pool, y }, bank = new[] { edge, bank }, bankAboveWaterFloor = y - bank };
                    return true;
                }
            }
        }
        return false;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        var provider = KirbyMod.Instance?.NativeProvider;
        if (machine is null || provider is null || run is null) return 0;
        Hero hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        if (!hero.awake || hero._level.game.hasCinematic() || frame - start.Value < 180 || finishedAt is not null) return 0;
        if (!searched)
        {
            searched = true;
            depths = Survey(hero._level.map);
            if (wade ? !Wade(hero) : !Place(hero)) { finishedAt = frame; return 0; }
            placedAt = frame;
            kirbyLifeBefore = hero.life;
            return 0;
        }
        if (wadeSite is not null) return WadeInput(machine, hero, frame);
        if (zombie is null) return 0;
        uint t = frame - placedAt!.Value;
        if (!zombie.destroyed && zombie.life > 0)
        {
            zombie.lockAiS(1);
            if (inhaleAt is null && !Water(hero._level.map, zombie.cx, zombie.cy)) leftWaterBeforeInhale = true;
        }
        if (inhaleAt is null)
        {
            // Observe without input: a source-defeated proxy claimed the Zombie at once.
            if (provider.ControlsEnemy(zombie)) framesControlledBeforeInhale++;
            if (t < 120) return 0;
            zombieLifeBeforeInhale = zombie.life;
            inhaleAt = frame;
        }
        uint inhaling = frame - inhaleAt.Value;
        if (consumedAt is null)
        {
            if (zombie.destroyed || zombie.life <= 0)
            {
                consumedAt = frame;
                mouthfulAtConsumption = (machine.ActionFlags & 2) != 0;
                return 0;
            }
            if (inhaling > 600) { timedOut = true; Finish(session, hero, frame); return 0; }
            // Face the pool with a short tap, then hold the original inhale.
            if (inhaling < 3) return towardPool > 0 ? (ushort)16 : (ushort)32;
            return inhaling < 12 ? (ushort)0 : (ushort)2;
        }
        uint after = frame - consumedAt.Value;
        if (after < 30) return 0;
        swallowAt ??= frame;
        // Several frames, because a host frame may advance zero source frames.
        // Host hits can knock the copy loose before the end, so keep the first one.
        if (firstCopyAfterSwallow is null && machine.State.Ability > 1)
            (firstCopyAfterSwallow, firstCopyFrame) = (machine.State.Ability, frame);
        if (frame - swallowAt.Value < 4) return 128;
        if (frame - swallowAt.Value < 240) return 0;
        Finish(session, hero, frame);
        return 0;
    }

    private static void Finish(KirbySession session, Hero hero, uint frame)
    {
        var machine = KirbyMod.Instance!.NativeProvider!.Machine!;
        zombieLifeAtEnd = zombie is null || zombie.destroyed ? 0 : zombie.life;
        sourceAbilityAfter = machine.State.Ability;
        copyAbilityAfter = session.Rules.AbilityId;
        kirbyLifeAfter = hero.life;
        finishedAt = frame;
    }

    public static void Poll()
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (run is null || machine is null || machine.State.Frame == lastReportFrame) return;
        lastReportFrame = machine.State.Frame;
        File.WriteAllText(Path.Combine(run, "water.json"), JsonSerializer.Serialize(new
        {
            frame = lastReportFrame,
            searched, waterCells, poolFloorCells, surroundings,
            depths, wadeSite, wadeActions, wadeOrder, wadeInterrupted, wadeMoves,
            wadeSpeed = wadeSteps.Count == 0 ? null : new
            {
                frames = wadeSteps.Count,
                medianPxPerFrame = wadeSteps.Select(Math.Abs).Order().ElementAt(wadeSteps.Count / 2),
                maxPxPerFrame = wadeSteps.Max(Math.Abs),
            },
            site,
            placedAt, inhaleAt, consumedAt, swallowAt, finishedAt, timedOut,
            framesControlledBeforeInhale,
            leftWaterBeforeInhale,
            zombieLifeBeforeInhale,
            mouthfulAtConsumption,
            zombieLifeAtEnd,
            firstCopyAfterSwallow, firstCopyFrame,
            sourceAbilityAfter,
            copyAbilityAfter,
            kirbyLifeBefore,
            kirbyLifeAfter,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
