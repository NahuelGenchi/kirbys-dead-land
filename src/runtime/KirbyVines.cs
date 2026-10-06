using dc.en.inter;
using dc.level;

namespace Kirby.Runtime;

/// <summary>
/// Vines grown with the Vine rune (en.inter.VineLadder) write no collision
/// bits: VineLadder.buildLadder only adds level spots. Every cell of the vine
/// gets spot 0, where the Beheaded climbs, and spot 18; a rising vine adds
/// spot 18 above the block it stops under, and a hanging vine marks the solid
/// ledge it hangs from. Remember each built vine's column so the terrain
/// export can find those cells without reading every spot each frame.
/// </summary>
internal static class KirbyVines
{
    public enum Cell : byte { None, Ladder, Top }
    private const int ClimbSpot = 1 << 0, VineSpot = 1 << 18;
    private static nint level;
    private static readonly HashSet<(int X, int Y)> cells = [];

    public static void Install() =>
        Hook_VineLadder.buildLadder += (orig, vine) => { orig(vine); Record(vine._level.map, vine.cx); };

    private static void Record(LevelMap map, int cx)
    {
        if (map.HashlinkPointer != level) { cells.Clear(); level = map.HashlinkPointer; }
        // All of a vine's cells are in its own column.
        for (int cy = 0; cy < map.hei; cy++)
            if (Classify(map, cx, cy) != Cell.None) cells.Add((cx, cy));
    }

    /// <summary>
    /// A vine cell is a ladder for the original; a solid one, and the block a
    /// rising vine stops under, are a ladder top. The spots are read again
    /// here, so a stale record never adds a ladder.
    /// </summary>
    public static Cell At(LevelMap map, int cx, int cy) =>
        cells.Count == 0 || map.HashlinkPointer != level || !cells.Contains((cx, cy)) ? Cell.None : Classify(map, cx, cy);

    private static Cell Classify(LevelMap map, int cx, int cy)
    {
        const int Vine = ClimbSpot | VineSpot;
        if ((Spots(map, cx, cy) & Vine) == Vine) return Cell.Ladder;
        return (Spots(map, cx, cy - 1) & VineSpot) != 0 && (Spots(map, cx, cy + 1) & Vine) == Vine ? Cell.Top : Cell.None;
    }

    private static int Spots(LevelMap map, int cx, int cy)
    {
        var spots = map.fastSpots;
        if (spots is null || cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei) return 0;
        int index = cy * map.wid + cx;
        return index < spots.length && spots.getDyn(index) is SpotFlags flags ? flags.low : 0;
    }
}
