using System.Text.Json;
using dc.en;
using dc.en.mob;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check: a real Zombie stands in a host pool next to the dry bank
/// where Kirby is placed, its AI held so it stays in the water. The original
/// Waddle Dee proxy used to be defeated by source water, which pinned the host
/// enemy and ignored inhale and attacks. The probe records whether the source
/// claims the Zombie before any input, then holds the original inhale until it
/// is consumed and swallows it with DOWN.
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

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyInput.TestOverride = Input;
    }

    private static int Cell(dc.level.LevelMap map, int cx, int cy) =>
        cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei ? 3 : ((int*)map.collisions.bytes)[cy * map.wid + cx];

    private static bool Water(dc.level.LevelMap map, int cx, int cy) =>
        (Cell(map, cx, cy) & 3) == 0 && map.getRoomAt(cx, cy) is not null && map.getWaterSurfaceY(cx, cy) > 0;

    private static bool Floor(dc.level.LevelMap map, int cx, int cy) => (Cell(map, cx, cy + 1) & 3) != 0;

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
            if (!Place(hero)) { finishedAt = frame; return 0; }
            placedAt = frame;
            kirbyLifeBefore = hero.life;
            return 0;
        }
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
